using System.Linq;
using CardVault.Core.Calendar;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    public sealed class HomeScreen : GameScreen
    {
        private readonly CardViewPool _pool = new CardViewPool();

        public override string Title => "Card Vault";

        public override void Teardown() => _pool.ReleaseAll();

        public override void Build(VisualElement root)
        {
            var home = Game.Home.Build();
            var profile = Game.Progression.Profile();

            root.Add(Ui.Panel(
                Ui.Text("Welcome back, " + profile.Name, Theme.FontTitle, bold: true),
                Ui.Muted("Rank " + profile.Rank + " · " + profile.RankName + (profile.TitleName != null ? " · " + profile.TitleName : "")),
                Ui.Spacer(12),
                Ui.ProgressBar(profile.XpForNextRank.HasValue
                    ? (profile.Xp - profile.XpForCurrentRank) / (double)(profile.XpForNextRank.Value - profile.XpForCurrentRank)
                    : 1.0)));

            if (home.ClaimableRewards > 0)
                root.Add(Ui.ListRow(home.ClaimableRewards + " collection reward" + (home.ClaimableRewards == 1 ? "" : "s") + " ready",
                    "Claim them from your set registry", "Claim", () => Shell.Push(new SetListScreen()), Theme.Positive));

            if (home.FeaturedRelease != null)
            {
                var r = home.FeaturedRelease;
                root.Add(Ui.SectionHeader("Featured release"));
                var panel = Ui.Panel(
                    Ui.Badge(ReleaseCalendarService.Label(home.FeaturedStatus), Theme.Accent),
                    Ui.Spacer(8),
                    Ui.Text(r.Name, Theme.FontTitle, bold: true),
                    Ui.Muted(ReleaseCalendarService.Label(r.Category) + " · " + Fmt.Date(Game, r.ReleaseAt)),
                    Ui.Text(r.Description, Theme.FontSmall),
                    Ui.Button(ReleaseCalendarService.IsPurchasable(home.FeaturedStatus) ? "Shop this release" : "View release",
                        () => Shell.Push(new ReleaseDetailScreen(r.Id))));
                root.Add(panel);
            }

            root.Add(Ui.SectionHeader("Active card shows"));
            if (home.ActiveCardShows.Count == 0) root.Add(Ui.Muted("No shows today. Check the calendar for the next one."));
            foreach (var occ in home.ActiveCardShows)
            {
                var o = occ;
                root.Add(Ui.ListRow(o.Definition.Name, "Closes " + Fmt.Date(Game, o.EndsAt), "Visit",
                    () => Shell.Push(new CardShowDetailScreen(o.OccurrenceId)), Theme.Info));
            }
            root.Add(Ui.Button("All card shows", () => Shell.Push(new CardShowsScreen()), Ui.ButtonKind.Secondary));

            root.Add(Ui.SectionHeader("Upcoming releases"));
            if (home.UpcomingReleases.Count == 0) root.Add(Ui.Muted("Nothing announced right now."));
            foreach (var r in home.UpcomingReleases)
            {
                var id = r.Id;
                root.Add(Ui.ListRow(r.Name, ReleaseCalendarService.Label(r.Category) + " · " + Fmt.Date(Game, r.ReleaseAt),
                    Game.Calendar.IsWatched(r.Id) ? "Watching" : null, () => Shell.Push(new ReleaseDetailScreen(id))));
            }
            root.Add(Ui.Button("Release calendar", () => Shell.Push(new ReleaseCalendarScreen()), Ui.ButtonKind.Secondary));

            root.Add(Ui.SectionHeader("Collection milestones"));
            if (home.CollectionProgress.Count == 0) root.Add(Ui.Muted("Open a pack to start your first set."));
            foreach (var p in home.CollectionProgress)
            {
                var setId = p.Set.Id;
                var row = Ui.ListRow(p.Set.Name, p.Progress.BaseOwned + " of " + p.Progress.BaseTotal + " base cards" +
                                                 (p.ClaimableMilestones > 0 ? " · reward ready" : ""),
                    Fmt.Percent(p.Progress.Percent), () => Shell.Push(new SetDetailScreen(setId)));
                row.Add(Ui.ProgressBar(p.Progress.Percent / 100.0, height: 12));
                root.Add(row);
            }

            root.Add(Ui.SectionHeader("Recently acquired"));
            if (home.RecentlyAcquired.Count == 0) root.Add(Ui.Muted("Your newest cards will appear here."));
            var grid = Ui.Grid(33.3f);
            foreach (var card in home.RecentlyAcquired)
            {
                var view = _pool.Get();
                view.Bind(Game, card, 300, Shell.ReducedEffects);
                var id = card.InstanceId;
                view.OnTap(() => Shell.Push(new CardDetailScreen(id)));
                Ui.AddToGrid(grid, view);
            }
            root.Add(grid);
        }
    }
}
