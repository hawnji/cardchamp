using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.Rewards;
using CardVault.Core.Sets;
using CardVault.Core.Showcase;
using CardVault.Core.State;

namespace CardVault.Core.Progression
{
    public sealed class AchievementView
    {
        public AchievementDef Definition;
        public long Progress;
        public bool Unlocked;
        public long? UnlockedAt;
    }

    public sealed class CollectorProfile
    {
        public string Name;
        public int Rank;
        public string RankName;
        public long Xp;
        public long XpForCurrentRank;
        public long? XpForNextRank;
        public string TitleName;
        public IReadOnlyDictionary<StatKey, long> Stats;
    }

    /// <summary>
    /// Integrated collector progression. Applies every committed action to persistent stats
    /// exactly once (tracked by position in the append-only action log), then evaluates
    /// achievements, collector XP/rank and showcase upgrades. Rewards go through RewardService
    /// with stable keys, so nothing is granted twice.
    /// </summary>
    public sealed class ProgressionService
    {
        private readonly GameContext _ctx;
        private readonly RewardService _rewards;
        private readonly SetRewardService _setRewards;
        private readonly ShowcaseService _showcase;
        private readonly CardHistoryService _history;
        private readonly UnlockEvaluator _unlocks;
        private bool _processing;

        public ProgressionService(GameContext ctx, RewardService rewards, SetRewardService setRewards, ShowcaseService showcase,
            CardHistoryService history, UnlockEvaluator unlocks)
        {
            _ctx = ctx;
            _rewards = rewards;
            _setRewards = setRewards;
            _showcase = showcase;
            _history = history;
            _unlocks = unlocks;
            _ctx.Events.ActionRecorded += _ => CatchUp();
            _ctx.Events.XpAwarded += (xp, _) => AddXp(xp);
        }

        private ProgressionState State => _ctx.State.Progression;
        private XpConfig Xp => _ctx.Content.Pack.Xp;

        public long Stat(StatKey key) => _unlocks.Stat(key);

        private void Increment(StatKey key, long by = 1)
        {
            string k = key.ToString();
            State.Stats[k] = (State.Stats.TryGetValue(k, out var v) ? v : 0) + by;
        }

        private void SetStat(StatKey key, long value) => State.Stats[key.ToString()] = value;

        /// <summary>Applies all not-yet-processed actions. Safe to call any time; re-entrant calls are folded.</summary>
        public void CatchUp()
        {
            if (_processing) return;
            _processing = true;
            try
            {
                bool changed = false;
                // Rewards granted below can append new actions; keep going until the log is drained.
                while (State.ProcessedActionCount < _ctx.State.Actions.Count)
                {
                    var action = _ctx.State.Actions[State.ProcessedActionCount];
                    State.ProcessedActionCount++;
                    Apply(action);
                    changed = true;
                }
                if (changed) EvaluateUnlocks();
            }
            finally
            {
                _processing = false;
            }
        }

        private void Apply(GameAction a)
        {
            switch (a.Type)
            {
                case GameActionType.CardAcquired:
                    if (a.TemplateId != null && State.EverOwnedVariants.Add(a.TemplateId + "|" + a.ParallelId))
                    {
                        Increment(StatKey.UniqueCardsAcquired);
                        State.CollectorXp += Xp.PerNewUniqueCard;
                    }
                    if (a.IsSerialNumbered && a.CardInstanceId != null && State.DiscoveredRareInstances.Add(a.CardInstanceId))
                    {
                        Increment(StatKey.RareCardsDiscovered);
                        State.CollectorXp += Xp.PerRareDiscovery;
                        if (a.IsOneOfOne) Increment(StatKey.OneOfOnesDiscovered);
                    }
                    CheckHistoricalMilestone(a.CardInstanceId);
                    break;
                case GameActionType.PackOpened:
                    Increment(StatKey.PacksOpened);
                    State.CollectorXp += Xp.PerPackOpened;
                    if (a.IsVintage) Increment(StatKey.VintageProductsOpened);
                    break;
                case GameActionType.CardGraded:
                    Increment(StatKey.CardsGraded);
                    State.CollectorXp += Xp.PerCardGraded;
                    break;
                case GameActionType.TradeCompleted:
                    Increment(StatKey.NpcTradesCompleted);
                    State.CollectorXp += Xp.PerTrade;
                    break;
                case GameActionType.CardPurchased:
                    Increment(StatKey.NpcPurchases);
                    break;
                case GameActionType.CardSold:
                    Increment(StatKey.NpcSales);
                    break;
                case GameActionType.SetMilestoneReached:
                    Increment(StatKey.SetMilestonesReached);
                    State.CollectorXp += Xp.PerSetMilestone;
                    SetStat(StatKey.SetsCompleted, _setRewards.SetsCompleted);
                    break;
                case GameActionType.CardShowVisited:
                    Increment(StatKey.CardShowsVisited);
                    break;
                case GameActionType.CardShowChallengeCompleted:
                    Increment(StatKey.CardShowChallengesCompleted);
                    State.CollectorXp += Xp.PerChallenge;
                    break;
                case GameActionType.CardShowCompleted:
                    Increment(StatKey.CardShowsCompleted);
                    break;
                case GameActionType.ReleaseMilestone:
                    Increment(StatKey.ReleaseMilestones);
                    break;
            }
        }

        /// <summary>A card the player holds after passing through three or more owners.</summary>
        private void CheckHistoricalMilestone(string cardId)
        {
            if (cardId == null || State.HistoricalMilestoneCards.Contains(cardId)) return;
            if (!_ctx.State.Cards.TryGetValue(cardId, out var card) || card.OwnerId != Owners.Player) return;
            var owners = _history.OwnershipChain(cardId).Where(o => o != Owners.Unallocated).Distinct().Count();
            if (owners < 3) return;
            State.HistoricalMilestoneCards.Add(cardId);
            Increment(StatKey.HistoricalMilestones);
            using (var tx = _ctx.Begin("historic:" + cardId))
            {
                _history.Append(tx, cardId, HistoryEventType.CollectionAchievement, e =>
                    e.Description = "Became a storied card: you are its owner number " + owners + ".");
                tx.Commit();
            }
        }

        private void AddXp(long xp)
        {
            State.CollectorXp += xp;
            EvaluateUnlocks();
        }

        private void EvaluateUnlocks()
        {
            // Achievements can grant XP and XP can unlock showcase slots, so iterate to a fixed point.
            for (int guard = 0; guard < 8; guard++)
            {
                bool changed = false;
                changed |= UpdateRank();
                changed |= UpdateShowcaseUpgrades();
                changed |= UpdateAchievements();
                if (!changed) break;
            }
        }

        private bool UpdateRank()
        {
            bool changed = false;
            foreach (var rank in _ctx.Content.Ranks)
            {
                if (State.CollectorXp < rank.XpRequired) break;
                if (rank.Level > State.CollectorRank)
                {
                    State.CollectorRank = rank.Level;
                    _ctx.Notify(NotificationKind.RankUp, "Collector rank " + rank.Level + ": " + rank.Name,
                        rank.Reward.IsEmpty ? "New showcase space may be available." : "Reward: " + RewardService.Describe(rank.Reward, _ctx.Content),
                        null, "rank:" + rank.Level);
                    changed = true;
                }
                if (State.RankRewardsGranted.Add(rank.Level))
                {
                    if (!rank.Reward.IsEmpty) _rewards.Grant("rank:" + rank.Level, rank.Reward, "Collector rank " + rank.Name);
                    changed = true;
                }
            }
            return changed;
        }

        private bool UpdateShowcaseUpgrades()
        {
            var fresh = _showcase.CollectNewUpgrades();
            if (fresh.Count == 0) return false;
            SetStat(StatKey.ShowcaseUpgradesUnlocked, _ctx.State.Showcase.CountedUpgrades.Count);
            foreach (var key in fresh)
                _ctx.Notify(NotificationKind.Achievement, "Showcase upgrade unlocked", DescribeUpgrade(key), null, "showcase:" + key);
            return true;
        }

        private string DescribeUpgrade(string key)
        {
            var parts = key.Split(':');
            var area = _showcase.Area(parts.Length > 1 ? parts[1] : "");
            if (area == null) return "New display space is available.";
            return parts[0] == "area" ? area.Name + " is now open." : "More slots in " + area.Name + ".";
        }

        private bool UpdateAchievements()
        {
            bool changed = false;
            foreach (var ach in _ctx.Content.Pack.Achievements)
            {
                if (State.Achievements.ContainsKey(ach.Id) || Stat(ach.Stat) < ach.Threshold) continue;
                State.Achievements[ach.Id] = _ctx.Now;
                changed = true;
                using (var tx = _ctx.Begin("achievement:" + ach.Id))
                {
                    _ctx.RecordAction(tx, GameActionType.AchievementUnlocked, a => a.RefId = ach.Id);
                    _ctx.Notify(NotificationKind.Achievement, "Achievement: " + ach.Name,
                        ach.Description + (ach.Reward.IsEmpty ? "" : " Reward: " + RewardService.Describe(ach.Reward, _ctx.Content)),
                        ach.Id, "ach:" + ach.Id);
                    tx.Commit();
                }
                if (!ach.Reward.IsEmpty) _rewards.Grant("ach:" + ach.Id, ach.Reward, "Achievement: " + ach.Name);
            }
            return changed;
        }

        // ================================================================ views

        public IReadOnlyList<AchievementView> Achievements()
            => _ctx.Content.Pack.Achievements.Select(a => new AchievementView
            {
                Definition = a,
                Progress = Math.Min(a.Threshold, Stat(a.Stat)),
                Unlocked = State.Achievements.ContainsKey(a.Id),
                UnlockedAt = State.Achievements.TryGetValue(a.Id, out var t) ? t : (long?)null,
            }).ToList();

        public CollectorProfile Profile()
        {
            var ranks = _ctx.Content.Ranks;
            var current = ranks.LastOrDefault(r => r.Level <= State.CollectorRank);
            var next = ranks.FirstOrDefault(r => r.Level == State.CollectorRank + 1);
            var titleId = _ctx.State.Profile.EquippedTitleId;
            return new CollectorProfile
            {
                Name = _ctx.State.Profile.CollectorName,
                Rank = State.CollectorRank,
                RankName = current?.Name ?? "Collector",
                Xp = State.CollectorXp,
                XpForCurrentRank = current?.XpRequired ?? 0,
                XpForNextRank = next?.XpRequired,
                TitleName = titleId != null && _ctx.Content.Cosmetics.TryGetValue(titleId, out var t) ? t.Name : null,
                Stats = Enum.GetValues(typeof(StatKey)).Cast<StatKey>().ToDictionary(k => k, Stat),
            };
        }
    }
}
