using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Calendar;
using CardVault.Core.CardShows;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Sets;
using CardVault.Core.State;

namespace CardVault.Core.Game
{
    public sealed class SetProgressSummary
    {
        public SetDef Set;
        public SetProgress Progress;
        public int ClaimableMilestones;
    }

    public sealed class HomeSummary
    {
        public ReleaseDef FeaturedRelease;
        public ReleaseStatus FeaturedStatus;
        public IReadOnlyList<ReleaseDef> UpcomingReleases;
        public IReadOnlyList<EventOccurrence> ActiveCardShows;
        public IReadOnlyList<SetProgressSummary> CollectionProgress;
        public IReadOnlyList<CardInstance> RecentlyAcquired;
        public int UnreadNotifications;
        public int ClaimableRewards;
    }

    /// <summary>Read-only aggregation for the Home screen. Holds no state of its own.</summary>
    public sealed class HomeSummaryService
    {
        private readonly GameContext _ctx;
        private readonly ReleaseCalendarService _calendar;
        private readonly CardShowService _shows;
        private readonly SetRegistryService _sets;
        private readonly SetRewardService _setRewards;

        public HomeSummaryService(GameContext ctx, ReleaseCalendarService calendar, CardShowService shows,
            SetRegistryService sets, SetRewardService setRewards)
        {
            _ctx = ctx;
            _calendar = calendar;
            _shows = shows;
            _sets = sets;
            _setRewards = setRewards;
        }

        public HomeSummary Build()
        {
            var current = _calendar.Current().Where(r => ReleaseCalendarService.IsPurchasable(_calendar.StatusOf(r))).ToList();
            var upcoming = _calendar.Upcoming();
            var featured = current.FirstOrDefault() ?? upcoming.FirstOrDefault();

            var progress = _ctx.Content.Pack.Sets
                .Select(s => new SetProgressSummary
                {
                    Set = s,
                    Progress = _sets.Progress(s.Id),
                    ClaimableMilestones = _setRewards.Milestones(s.Id).Count(m => m.Claimable),
                })
                .Where(p => p.Progress.BaseOwned > 0 || p.Progress.EverCompleted)
                .OrderByDescending(p => p.ClaimableMilestones)
                .ThenByDescending(p => p.Progress.Percent)
                .Take(4)
                .ToList();

            var recentIds = new List<string>();
            for (int i = _ctx.State.Actions.Count - 1; i >= 0 && recentIds.Count < 6; i--)
            {
                var a = _ctx.State.Actions[i];
                if (a.Type != GameActionType.CardAcquired || a.CardInstanceId == null || recentIds.Contains(a.CardInstanceId)) continue;
                if (_ctx.State.Cards.TryGetValue(a.CardInstanceId, out var c) && c.OwnerId == Owners.Player) recentIds.Add(a.CardInstanceId);
            }

            return new HomeSummary
            {
                FeaturedRelease = featured,
                FeaturedStatus = featured != null ? _calendar.StatusOf(featured) : ReleaseStatus.Hidden,
                UpcomingReleases = upcoming.Take(3).ToList(),
                ActiveCardShows = _shows.Active(),
                CollectionProgress = progress,
                RecentlyAcquired = recentIds.Select(id => _ctx.State.Cards[id]).ToList(),
                UnreadNotifications = _ctx.State.Notifications.Count(n => !n.Read),
                ClaimableRewards = _setRewards.ClaimableCount,
            };
        }
    }
}
