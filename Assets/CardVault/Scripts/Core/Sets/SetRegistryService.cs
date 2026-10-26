using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;

namespace CardVault.Core.Sets
{
    public sealed class ParallelProgress
    {
        public string ParallelId;
        public string Name;
        public int Owned;
        public int Total;
        public bool IsSerialNumbered;
        public bool IsOneOfOne;
        public double Percent => Total == 0 ? 0 : 100.0 * Owned / Total;
    }

    /// <summary>
    /// Current-ownership view of a set. Base progress only counts base versions; parallels,
    /// numbered chases and graded progress are tracked separately so a 1/1 is never needed
    /// for an ordinary base set.
    /// </summary>
    public sealed class SetProgress
    {
        public string SetId;
        public int BaseTotal;
        public int BaseOwned;
        public List<string> OwnedBaseTemplateIds = new List<string>();
        public List<string> MissingBaseTemplateIds = new List<string>();
        public int RookiesTotal;
        public int RookiesOwned;
        public List<ParallelProgress> Parallels = new List<ParallelProgress>();
        public int GradedOwned;
        public int DuplicateCopies;
        /// <summary>Base entries ever collected, including ones since sold.</summary>
        public int EverCollected;
        public long? FirstCompletedAt;

        public double Percent => BaseTotal == 0 ? 0 : 100.0 * BaseOwned / BaseTotal;
        public bool IsCompleteNow => BaseTotal > 0 && BaseOwned == BaseTotal;
        public bool EverCompleted => FirstCompletedAt.HasValue;
    }

    /// <summary>Set registry service: checklist queries and progress from current ownership.</summary>
    public sealed class SetRegistryService
    {
        private readonly GameContext _ctx;

        public SetRegistryService(GameContext ctx)
        {
            _ctx = ctx;
        }

        public IEnumerable<SetDef> AllSets => _ctx.Content.Pack.Sets;

        public SetRecord RecordFor(string setId)
        {
            if (!_ctx.State.SetRewards.Sets.TryGetValue(setId, out var record))
            {
                record = new SetRecord { SetId = setId };
                _ctx.State.SetRewards.Sets[setId] = record;
            }
            return record;
        }

        public SetRecord ExistingRecord(string setId)
            => _ctx.State.SetRewards.Sets.TryGetValue(setId, out var r) ? r : null;

        private IEnumerable<CardInstance> PlayerCardsInSet(string setId)
            => _ctx.State.Cards.Values.Where(c => c.OwnerId == Owners.Player && c.OriginSetId == setId);

        public SetProgress Progress(string setId)
        {
            var set = _ctx.Content.Sets[setId];
            var checklist = _ctx.Content.CardsInSet(setId);
            var owned = PlayerCardsInSet(setId).ToList();

            var baseOwned = new HashSet<string>(owned.Where(c => c.ParallelId == ContentDatabase.BaseParallelId).Select(c => c.TemplateId));
            int baseCopies = owned.Count(c => c.ParallelId == ContentDatabase.BaseParallelId);
            var record = ExistingRecord(setId);

            var progress = new SetProgress
            {
                SetId = setId,
                BaseTotal = checklist.Count,
                BaseOwned = checklist.Count(t => baseOwned.Contains(t.Id)),
                OwnedBaseTemplateIds = checklist.Where(t => baseOwned.Contains(t.Id)).Select(t => t.Id).ToList(),
                MissingBaseTemplateIds = checklist.Where(t => !baseOwned.Contains(t.Id)).Select(t => t.Id).ToList(),
                RookiesTotal = checklist.Count(t => t.IsRookie),
                RookiesOwned = checklist.Count(t => t.IsRookie && baseOwned.Contains(t.Id)),
                GradedOwned = checklist.Count(t => owned.Any(c => c.TemplateId == t.Id && c.IsGraded)),
                DuplicateCopies = Math.Max(0, baseCopies - baseOwned.Count),
                EverCollected = record?.EverCollectedBase.Count ?? 0,
                FirstCompletedAt = record?.FirstCompletedAt,
            };

            foreach (var parallelId in set.ParallelIds)
            {
                var def = _ctx.Content.Parallel(parallelId);
                var have = new HashSet<string>(owned.Where(c => c.ParallelId == parallelId).Select(c => c.TemplateId));
                progress.Parallels.Add(new ParallelProgress
                {
                    ParallelId = parallelId,
                    Name = def.Name,
                    Owned = checklist.Count(t => have.Contains(t.Id)),
                    Total = checklist.Count,
                    IsSerialNumbered = def.IsSerialNumbered,
                    IsOneOfOne = def.IsOneOfOne,
                });
            }
            return progress;
        }

        /// <summary>Whether a milestone's condition holds for the player's current collection.</summary>
        public bool IsSatisfied(SetMilestoneDef milestone, SetProgress progress)
        {
            var set = _ctx.Content.Sets[progress.SetId];
            var checklist = _ctx.Content.CardsInSet(set.Id);
            var ownedBase = new HashSet<string>(progress.OwnedBaseTemplateIds);
            switch (milestone.Kind)
            {
                case SetMilestoneKind.BasePercent:
                    return progress.BaseTotal > 0 && progress.Percent + 1e-9 >= milestone.Threshold;
                case SetMilestoneKind.AllBase:
                    return progress.IsCompleteNow;
                case SetMilestoneKind.AllRookies:
                    return progress.RookiesTotal > 0 && progress.RookiesOwned == progress.RookiesTotal;
                case SetMilestoneKind.AllOfSubject:
                {
                    var subjectCards = checklist.Where(t => t.SubjectId == milestone.SubjectId).ToList();
                    return subjectCards.Count > 0 && subjectCards.All(t => ownedBase.Contains(t.Id));
                }
                case SetMilestoneKind.ParallelComplete:
                {
                    var p = progress.Parallels.FirstOrDefault(x => x.ParallelId == milestone.ParallelId);
                    return p != null && p.Total > 0 && p.Owned == p.Total;
                }
                case SetMilestoneKind.GradedComplete:
                {
                    var owned = PlayerCardsInSet(set.Id).Where(c => c.IsGraded && c.Grade.Value >= milestone.MinGrade)
                        .Select(c => c.TemplateId).ToHashSet();
                    return checklist.Count > 0 && checklist.All(t => owned.Contains(t.Id));
                }
                default:
                    return false;
            }
        }

        /// <summary>Owned copies (any parallel) of a checklist card, for the set detail screen.</summary>
        public IEnumerable<CardInstance> OwnedCopies(string templateId)
            => _ctx.State.Cards.Values.Where(c => c.OwnerId == Owners.Player && c.TemplateId == templateId);
    }
}
