using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.Rewards;
using CardVault.Core.State;

namespace CardVault.Core.Sets
{
    public sealed class MilestoneView
    {
        public SetMilestoneDef Definition;
        public MilestoneRecord Record;
        public bool Achieved => Record != null;
        public bool Claimable => Record != null && !Record.Claimed;
        public bool Claimed => Record != null && Record.Claimed;
    }

    /// <summary>
    /// Set completion rewards. Milestones are detected from committed acquisitions/grades,
    /// recorded permanently (selling a card never un-completes anything), and their reward is
    /// claimable exactly once through RewardService.
    /// </summary>
    public sealed class SetRewardService
    {
        private readonly GameContext _ctx;
        private readonly SetRegistryService _registry;
        private readonly RewardService _rewards;
        private readonly CardHistoryService _history;

        public SetRewardService(GameContext ctx, SetRegistryService registry, RewardService rewards, CardHistoryService history)
        {
            _ctx = ctx;
            _registry = registry;
            _rewards = rewards;
            _history = history;
            _ctx.Events.ActionRecorded += OnAction;
        }

        public static string GrantKey(string setId, string milestoneId) => "set:" + setId + ":" + milestoneId;

        private void OnAction(GameAction action)
        {
            if (action.SetId == null) return;
            if (action.Type != GameActionType.CardAcquired && action.Type != GameActionType.CardGraded) return;
            Evaluate(action.SetId, action.CardInstanceId);
        }

        /// <summary>Checks every set (used after load so content updates and migrations catch up).</summary>
        public void EvaluateAll()
        {
            foreach (var set in _ctx.Content.Pack.Sets) Evaluate(set.Id, null);
        }

        /// <summary>Records newly reached milestones. Returns the milestones reached by this call.</summary>
        public IReadOnlyList<SetMilestoneDef> Evaluate(string setId, string triggerCardId)
        {
            var reached = new List<SetMilestoneDef>();
            if (!_ctx.Content.Sets.TryGetValue(setId, out var set)) return reached;

            var progress = _registry.Progress(setId);
            var record = _registry.RecordFor(setId);
            foreach (var id in progress.OwnedBaseTemplateIds) record.EverCollectedBase.Add(id);

            var newlyReached = set.Milestones
                .Where(m => !record.Milestones.ContainsKey(m.Id) && _registry.IsSatisfied(m, progress))
                .ToList();
            if (newlyReached.Count == 0) return reached;

            var trigger = triggerCardId != null && _ctx.State.Cards.TryGetValue(triggerCardId, out var c) && c.OwnerId == Owners.Player ? c : null;

            using (var tx = _ctx.Begin("set-milestones:" + setId + ":" + _ctx.State.Ids.NextSequence))
            {
                foreach (var milestone in newlyReached)
                {
                    record.Milestones[milestone.Id] = new MilestoneRecord
                    {
                        MilestoneId = milestone.Id,
                        AchievedAt = _ctx.Now,
                        TriggerCardInstanceId = trigger?.InstanceId,
                    };
                    if (milestone.Kind == SetMilestoneKind.AllBase && !record.FirstCompletedAt.HasValue)
                        record.FirstCompletedAt = _ctx.Now;

                    var m = milestone;
                    _ctx.RecordAction(tx, GameActionType.SetMilestoneReached, a =>
                    {
                        a.SetId = setId;
                        a.BrandId = set.BrandId;
                        a.Category = set.Category;
                        a.IsVintage = set.IsVintage;
                        a.RefId = m.Id;
                        a.CardInstanceId = trigger?.InstanceId;
                    });

                    _ctx.Notify(NotificationKind.SetMilestone, set.Name + ": " + milestone.Name,
                        milestone.AutoGrant
                            ? "Reward added: " + RewardService.Describe(milestone.Reward, _ctx.Content)
                            : "Reward ready to claim: " + RewardService.Describe(milestone.Reward, _ctx.Content),
                        setId, GrantKey(setId, milestone.Id));
                    reached.Add(milestone);
                }

                // One provenance entry on the card that completed the milestone(s).
                if (trigger != null)
                {
                    string names = string.Join(", ", reached.Select(r => "\"" + r.Name + "\""));
                    _history.Append(tx, trigger.InstanceId, HistoryEventType.SetMilestone, e =>
                    {
                        e.SetId = setId;
                        e.Description = "Completed " + names + " in " + set.Name + ".";
                    });
                }
                tx.Commit();
            }

            foreach (var milestone in reached.Where(m => m.AutoGrant)) Claim(setId, milestone.Id);
            return reached;
        }

        public OpResult<RewardGrantRecord> Claim(string setId, string milestoneId)
        {
            var def = _ctx.Content.Milestone(setId, milestoneId);
            if (def == null) return OpResult<RewardGrantRecord>.Fail(OpError.NotFound, "Milestone not found.");
            var record = _registry.ExistingRecord(setId);
            if (record == null || !record.Milestones.TryGetValue(milestoneId, out var m))
                return OpResult<RewardGrantRecord>.Fail(OpError.NotEligible, "This milestone has not been reached yet.");
            if (m.Claimed) return OpResult<RewardGrantRecord>.Fail(OpError.AlreadyClaimed, "This reward has already been claimed.");

            var set = _ctx.Content.Sets[setId];
            var grant = _rewards.Grant(GrantKey(setId, milestoneId), def.Reward, set.Name + " - " + def.Name, setId: setId);
            if (!grant.Ok && grant.Error != OpError.AlreadyClaimed) return grant;

            // A prior grant record (e.g. from a crash between grant and flag) still counts as claimed.
            var granted = _rewards.Record(GrantKey(setId, milestoneId));
            m.Claimed = true;
            m.ClaimedAt = granted?.Time ?? _ctx.Now;
            m.ClaimTransactionId = granted?.TransactionId;
            return grant.Ok ? grant : OpResult<RewardGrantRecord>.Success(granted);
        }

        public IReadOnlyList<MilestoneView> Milestones(string setId)
        {
            var record = _registry.ExistingRecord(setId);
            return _ctx.Content.Sets[setId].Milestones.Select(m => new MilestoneView
            {
                Definition = m,
                Record = record != null && record.Milestones.TryGetValue(m.Id, out var r) ? r : null,
            }).ToList();
        }

        public int ClaimableCount => _ctx.Content.Pack.Sets.Sum(s => Milestones(s.Id).Count(v => v.Claimable));

        public int SetsCompleted => _ctx.State.SetRewards.Sets.Values.Count(r => r.FirstCompletedAt.HasValue);

        public bool IsCompleted(string setId) => _registry.ExistingRecord(setId)?.FirstCompletedAt.HasValue ?? false;

        /// <summary>Sets whose first completion the UI has not celebrated yet.</summary>
        public IEnumerable<string> PendingCelebrations
            => _ctx.State.SetRewards.Sets.Values.Where(r => r.FirstCompletedAt.HasValue && !r.CompletionCelebrated).Select(r => r.SetId);

        public void MarkCelebrated(string setId)
        {
            var r = _registry.ExistingRecord(setId);
            if (r != null) r.CompletionCelebrated = true;
        }
    }
}
