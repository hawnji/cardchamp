using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Economy;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.State;

namespace CardVault.Core.Rewards
{
    /// <summary>
    /// The single path for granting rewards (set milestones, card show rewards, achievements,
    /// rank-ups). Each grant has a stable key such as "set:fh26:pct50"; a key can be granted
    /// exactly once per save, which is what makes repeated completion, replays and clock
    /// changes harmless.
    /// </summary>
    public sealed class RewardService
    {
        private readonly GameContext _ctx;
        private readonly CardInventoryService _inventory;
        private readonly CardHistoryService _history;
        private readonly EconomyService _economy;

        public RewardService(GameContext ctx, CardInventoryService inventory, CardHistoryService history, EconomyService economy)
        {
            _ctx = ctx;
            _inventory = inventory;
            _history = history;
            _economy = economy;
        }

        public bool IsGranted(string grantKey) => _ctx.State.Rewards.Grants.ContainsKey(grantKey);

        public RewardGrantRecord Record(string grantKey)
            => _ctx.State.Rewards.Grants.TryGetValue(grantKey, out var r) ? r : null;

        public bool IsCosmeticUnlocked(string cosmeticId) => _ctx.State.Profile.UnlockedCosmetics.Contains(cosmeticId);

        public IEnumerable<CosmeticDef> UnlockedCosmetics(CosmeticKind kind)
            => _ctx.Content.Pack.Cosmetics.Where(c => c.Kind == kind && IsCosmeticUnlocked(c.Id));

        /// <summary>Checks a reward can be delivered in full (e.g. serials still free) without granting it.</summary>
        public OpResult CanGrant(string grantKey, RewardDef reward)
        {
            if (IsGranted(grantKey)) return OpResult.Fail(OpError.AlreadyClaimed, "This reward has already been claimed.");
            foreach (var card in reward.Cards)
            {
                if (!_inventory.HasFreeSerial(card.TemplateId, card.ParallelId ?? ContentDatabase.BaseParallelId))
                    return OpResult.Fail(OpError.OutOfStock, "Every numbered copy of this reward card has been claimed.");
            }
            return OpResult.Success();
        }

        public OpResult<RewardGrantRecord> Grant(string grantKey, RewardDef reward, string source, string eventId = null, string setId = null)
        {
            var check = CanGrant(grantKey, reward);
            if (!check.Ok) return OpResult<RewardGrantRecord>.Fail(check.Error, check.Message);

            using (var tx = _ctx.Begin("reward:" + grantKey))
            {
                var record = new RewardGrantRecord
                {
                    GrantKey = grantKey,
                    TransactionId = tx.Id,
                    Time = _ctx.Now,
                    Source = source,
                    Credits = reward.Credits,
                    Tickets = reward.EventTickets,
                };

                if (reward.Credits != 0 || reward.EventTickets != 0)
                    _economy.Apply(tx, LedgerKind.Reward, reward.Credits, reward.EventTickets, grantKey, source);

                foreach (var cosmeticId in reward.CosmeticIds)
                {
                    if (_ctx.State.Profile.UnlockedCosmetics.Add(cosmeticId)) record.CosmeticIds.Add(cosmeticId);
                }

                foreach (var grant in reward.Cards)
                {
                    var card = _inventory.Create(tx, new CardCreationRequest
                    {
                        TemplateId = grant.TemplateId,
                        ParallelId = grant.ParallelId ?? ContentDatabase.BaseParallelId,
                        OwnerId = Owners.Player,
                        Condition = grant.Condition,
                        Tradable = grant.Tradable,
                        OriginRef = grantKey,
                        EventId = eventId,
                    });
                    _history.Append(tx, card.InstanceId, eventId != null ? HistoryEventType.EventAcquisition : HistoryEventType.RewardGrant, e =>
                    {
                        e.PreviousOwnerId = Owners.Unallocated;
                        e.NewOwnerId = Owners.Player;
                        e.EventId = eventId;
                        e.SetId = setId ?? card.OriginSetId;
                        e.Description = "Awarded: " + source + (grant.Tradable ? "." : " (commemorative, not tradable).");
                    });
                    _ctx.RecordCardAction(tx, GameActionType.CardAcquired, card, refId: grantKey);
                    record.CardInstanceIds.Add(card.InstanceId);
                }

                foreach (var unlock in reward.UnlockListingIds) _ctx.State.Market.UnlockedListingIds.Add(unlock);
                _ctx.State.Profile.BonusShowcaseSlots += reward.ShowcaseSlots;
                if (reward.CollectorXp > 0) _ctx.Events.QueueXp(tx, reward.CollectorXp, grantKey);

                _ctx.State.Rewards.Grants[grantKey] = record;
                tx.Commit();
                return OpResult<RewardGrantRecord>.Success(record);
            }
        }

        public static string Describe(RewardDef reward, ContentDatabase content)
        {
            var parts = new List<string>();
            if (reward.Credits > 0) parts.Add(reward.Credits.ToString("N0") + " credits");
            if (reward.EventTickets > 0) parts.Add(reward.EventTickets + (reward.EventTickets == 1 ? " event ticket" : " event tickets"));
            foreach (var c in reward.CosmeticIds)
                if (content.Cosmetics.TryGetValue(c, out var def)) parts.Add(def.Name);
            foreach (var card in reward.Cards)
            {
                string name = content.Cards[card.TemplateId].Name;
                if (card.ParallelId != null && card.ParallelId != ContentDatabase.BaseParallelId) name += " " + content.Parallel(card.ParallelId).Name;
                parts.Add(name + (card.Tradable ? "" : " (commemorative)"));
            }
            if (reward.UnlockListingIds.Count > 0) parts.Add("exclusive market access");
            if (reward.ShowcaseSlots > 0) parts.Add("+" + reward.ShowcaseSlots + " showcase slots");
            if (reward.CollectorXp > 0) parts.Add(reward.CollectorXp + " XP");
            return parts.Count == 0 ? "Prestige only" : string.Join(", ", parts);
        }
    }
}
