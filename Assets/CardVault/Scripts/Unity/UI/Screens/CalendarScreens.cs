using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Calendar;
using CardVault.Core.Content;
using CardVault.Core.State;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    public sealed class ReleaseCalendarScreen : GameScreen
    {
        private bool _monthView;
        private int _category; // 0 all, 1 sports, 2 tcg
        private int _brand = -1;
        private int _universe = -1;
        private bool _watchOnly;
        private DateTime _month;
        private bool _monthInitialized;

        public override string Title => "Release Calendar";

        private CalendarFilter Filter()
        {
            var brands = Game.Content.Pack.Brands;
            var universes = Game.Content.Pack.Universes;
            return new CalendarFilter
            {
                Category = _category == 1 ? CardCategory.Sports : _category == 2 ? CardCategory.Tcg : (CardCategory?)null,
                BrandId = _brand >= 0 && _brand < brands.Count ? brands[_brand].Id : null,
                UniverseId = _universe >= 0 && _universe < universes.Count ? universes[_universe].Id : null,
                WatchlistOnly = _watchOnly,
            };
        }

        public override void Build(VisualElement root)
        {
            if (!_monthInitialized)
            {
                var now = Game.Clock.CalendarNow;
                _month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                _monthInitialized = true;
            }

            root.Add(Ui.Tabs(new[] { "List", "Month" }, _monthView ? 1 : 0, i => { _monthView = i == 1; Shell.Refresh(); }));
            root.Add(Ui.Tabs(new[] { "All", "Sports", "TCG" }, _category, i => { _category = i; Shell.Refresh(); }));

            var brands = Game.Content.Pack.Brands;
            var universes = Game.Content.Pack.Universes;
            root.Add(Ui.Row(
                Ui.Button("Brand: " + (_brand >= 0 ? brands[_brand].Name : "All"), () =>
                {
                    _brand = _brand + 1 >= brands.Count ? -1 : _brand + 1;
                    Shell.Refresh();
                }, Ui.ButtonKind.Secondary),
                Ui.Spacer(16),
                Ui.Button("Universe: " + (_universe >= 0 ? universes[_universe].Name : "All"), () =>
                {
                    _universe = _universe + 1 >= universes.Count ? -1 : _universe + 1;
                    Shell.Refresh();
                }, Ui.ButtonKind.Secondary)));
            root.Add(Ui.Button(_watchOnly ? "Showing watchlist only" : "Show watchlist only", () => { _watchOnly = !_watchOnly; Shell.Refresh(); },
                _watchOnly ? Ui.ButtonKind.Primary : Ui.ButtonKind.Ghost));

            if (_monthView) BuildMonth(root);
            else BuildList(root);
        }

        private void BuildList(VisualElement root)
        {
            var f = Filter();
            Section(root, "Upcoming", Game.Calendar.Upcoming(f), r => "Releases " + Fmt.Date(Game, r.ReleaseAt));
            Section(root, "On shelves now", Game.Calendar.Current(f), r => ReleaseCalendarService.Label(Game.Calendar.StatusOf(r)));
            Section(root, "Recently released", Game.Calendar.RecentlyReleased(f), r => "Released " + Fmt.Date(Game, r.ReleaseAt));
            Section(root, "Retiring soon", Game.Calendar.Retiring(f), r => "Retires " + Fmt.Date(Game, r.RetireAt));

            root.Add(Ui.SectionHeader("Upcoming card shows"));
            var shows = Game.Calendar.UpcomingCardShows(30);
            if (shows.Count == 0) root.Add(Ui.Muted("No shows in the next 30 days."));
            foreach (var s in shows.Take(8))
            {
                var id = s.Id;
                root.Add(Ui.ListRow(s.Name, Fmt.Date(Game, s.Time) + " to " + Fmt.Date(Game, s.EndTime), s.StatusLabel,
                    () => Shell.Push(new CardShowDetailScreen(id)), Theme.Info));
            }

            Section(root, "Seasonal and anniversary", Game.Calendar.Seasonal(f), r => ReleaseCalendarService.Label(Game.Calendar.StatusOf(r)));
            Section(root, "Vintage market events", Game.Calendar.VintageEvents(f), r => ReleaseCalendarService.Label(Game.Calendar.StatusOf(r)));
            Section(root, "Historical archive", Game.Calendar.Archive(f), r => "Retired " + Fmt.Date(Game, r.RetireAt));
        }

        private void Section(VisualElement root, string title, IReadOnlyList<ReleaseDef> releases, Func<ReleaseDef, string> subtitle)
        {
            root.Add(Ui.SectionHeader(title));
            if (releases.Count == 0)
            {
                root.Add(Ui.Muted("Nothing here."));
                return;
            }
            foreach (var r in releases)
            {
                var id = r.Id;
                root.Add(Ui.ListRow(r.Name, ReleaseCalendarService.Label(r.Category) + " · " + subtitle(r),
                    Game.Calendar.IsWatched(r.Id) ? "Watching" : ReleaseCalendarService.Label(Game.Calendar.StatusOf(r)),
                    () => Shell.Push(new ReleaseDetailScreen(id))));
            }
        }

        private void BuildMonth(VisualElement root)
        {
            root.Add(Ui.Row(
                Ui.Button("Prev", () => { _month = _month.AddMonths(-1); Shell.Refresh(); }, Ui.ButtonKind.Secondary),
                Ui.Spacer(),
                Ui.Title(_month.ToString("MMMM yyyy")),
                Ui.Spacer(),
                Ui.Button("Next", () => { _month = _month.AddMonths(1); Shell.Refresh(); }, Ui.ButtonKind.Secondary)));

            var items = Game.Calendar.Month(_month.Year, _month.Month, Filter());
            var byDay = items.GroupBy(i => Game.Clock.ToCalendarDate(i.Time).Day).ToDictionary(g => g.Key, g => g.ToList());

            var header = Ui.Row();
            foreach (var d in new[] { "M", "T", "W", "T", "F", "S", "S" })
            {
                var l = Ui.Muted(d);
                l.style.width = Length.Percent(14.28f);
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                header.Add(l);
            }
            root.Add(header);

            var grid = Ui.Grid(14.28f);
            int offset = ((int)_month.DayOfWeek + 6) % 7;
            for (int i = 0; i < offset; i++) Ui.AddToGrid(grid, Ui.Spacer(90));
            int days = DateTime.DaysInMonth(_month.Year, _month.Month);
            var today = Game.Clock.CalendarNow.Date;
            for (int day = 1; day <= days; day++)
            {
                var cell = Ui.Column(Ui.Text(day.ToString(), Theme.FontSmall, bold: true));
                cell.style.height = 90;
                cell.style.alignItems = Align.Center;
                Theme.Round(cell, 12);
                cell.style.backgroundColor = new DateTime(_month.Year, _month.Month, day) == today ? Theme.AccentDark : Theme.Surface;
                if (byDay.TryGetValue(day, out var list))
                {
                    var dots = Ui.Row();
                    foreach (var item in list.Take(3))
                    {
                        var dot = new VisualElement();
                        dot.style.width = 14;
                        dot.style.height = 14;
                        Theme.Round(dot, 7);
                        dot.style.marginRight = 4;
                        dot.style.backgroundColor = item.Kind == CalendarItemKind.CardShow ? Theme.Info
                            : item.Kind == CalendarItemKind.Retirement ? Theme.Negative : Theme.Accent;
                        dots.Add(dot);
                    }
                    cell.Add(dots);
                }
                Ui.AddToGrid(grid, cell);
            }
            root.Add(grid);
            root.Add(Ui.Muted("Gold: release · Red: retirement · Blue: card show"));

            root.Add(Ui.SectionHeader("This month"));
            if (items.Count == 0) root.Add(Ui.Muted("Nothing scheduled."));
            foreach (var item in items)
            {
                var i = item;
                root.Add(Ui.ListRow(i.Name, Fmt.Date(Game, i.Time) + " · " + i.CategoryLabel, i.StatusLabel, () =>
                {
                    if (i.Kind == CalendarItemKind.CardShow) Shell.Push(new CardShowDetailScreen(i.Id));
                    else Shell.Push(new ReleaseDetailScreen(i.Id));
                }, i.Kind == CalendarItemKind.CardShow ? Theme.Info : i.Kind == CalendarItemKind.Retirement ? Theme.Negative : Theme.Accent));
            }
        }
    }

    public sealed class ReleaseDetailScreen : GameScreen
    {
        private readonly string _releaseId;

        public ReleaseDetailScreen(string releaseId)
        {
            _releaseId = releaseId;
        }

        public override string Title => "Release";

        public override void Build(VisualElement root)
        {
            var r = Game.Calendar.Release(_releaseId);
            if (r == null)
            {
                root.Add(Ui.Muted("This release is no longer listed."));
                return;
            }
            var status = Game.Calendar.StatusOf(r);
            var set = Game.Content.Sets[r.SetId];
            var brand = Game.Content.Brands[r.BrandId];

            root.Add(Ui.Panel(
                Ui.Row(Ui.Badge(ReleaseCalendarService.Label(status), Theme.Accent), Ui.Badge(ReleaseCalendarService.Label(r.Category), Theme.Info)),
                Ui.Spacer(8),
                Ui.Title(r.Name),
                Ui.Muted(brand.Name + " · " + set.Name + (set.UniverseId != null ? " · " + Game.Content.Universes[set.UniverseId].Name : "")),
                Ui.Spacer(8),
                Ui.Text(r.Description, Theme.FontSmall),
                Ui.Spacer(8),
                Ui.Muted("Announced " + Fmt.Date(Game, r.AnnounceAt) + " · Release " + Fmt.Date(Game, r.ReleaseAt) +
                         (r.RetireAt > 0 ? " · Retires " + Fmt.Date(Game, r.RetireAt) : " · No retirement date")),
                r.Themes.Count > 0 ? Ui.Muted("Themes: " + string.Join(", ", r.Themes)) : null));

            bool watched = Game.Calendar.IsWatched(r.Id);
            root.Add(Ui.Button(watched ? "Remove from watchlist" : "Add to watchlist", () =>
                Shell.Run(() => watched ? Game.Calendar.Unwatch(r.Id) : Game.Calendar.Watch(r.Id),
                    watched ? "Removed from watchlist" : "You'll get an in-game reminder"), watched ? Ui.ButtonKind.Secondary : Ui.ButtonKind.Ghost));

            if (r.Products.Count > 0)
            {
                root.Add(Ui.SectionHeader("Products"));
                bool purchasable = ReleaseCalendarService.IsPurchasable(status);
                foreach (var p in Game.Calendar.Products(r))
                {
                    var view = p;
                    root.Add(Ui.Panel(
                        Ui.Text(p.Product.Name, Theme.FontBody, bold: true),
                        Ui.Muted(Fmt.Credits(p.Product.PriceCredits) + " · " + (status >= ReleaseStatus.Available ? p.Remaining + " of " + p.InitialStock + " left" : p.InitialStock + " allocated")),
                        purchasable
                            ? Ui.Button("Buy now", () => Shell.Run(() => Game.Shop.Buy(r.Id, view.ProductId).AsResult(), view.Product.Name + " added to My Sealed"),
                                enabled: view.Remaining > 0)
                            : null));
                }
            }

            if (status == ReleaseStatus.Retired || status == ReleaseStatus.Archived)
            {
                int vintage = Game.Market.VintageListings.Count(l => l.SealedInstanceId != null &&
                    r.Products.Any(p => p.ProductId == Game.Sealed.Get(l.SealedInstanceId).ProductId));
                root.Add(Ui.SectionHeader("Vintage market"));
                root.Add(Ui.Muted(vintage > 0
                    ? vintage + " sealed item" + (vintage == 1 ? "" : "s") + " from this release are listed by vintage dealers."
                    : "No sealed product from this release is listed right now."));
                root.Add(Ui.Button("Open vintage listings", () => Shell.SwitchTab(1), Ui.ButtonKind.Secondary));
            }

            if (r.ChaseTemplateIds.Count > 0)
            {
                root.Add(Ui.SectionHeader("Chase cards"));
                foreach (var t in r.ChaseTemplateIds.Select(id => Game.Content.Cards[id]))
                    root.Add(Ui.ListRow(t.Name + " #" + t.Number, t.Rarity + (t.IsRookie ? " · Rookie" : "") + (t.IsAutograph ? " · Autograph" : ""), null, null));
            }

            if (r.EventIds.Count > 0)
            {
                root.Add(Ui.SectionHeader("Linked card shows"));
                foreach (var eventId in r.EventIds)
                {
                    var occ = Game.CardShows.Active().FirstOrDefault(o => o.Definition.Id == eventId)
                              ?? Game.CardShows.Upcoming(60).FirstOrDefault(o => o.Definition.Id == eventId);
                    var def = Game.Content.CardShows[eventId];
                    if (occ == null) root.Add(Ui.ListRow(def.Name, "No date scheduled", null, null));
                    else
                    {
                        var id = occ.OccurrenceId;
                        root.Add(Ui.ListRow(def.Name, Fmt.Date(Game, occ.StartsAt), Game.CardShows.StatusOf(occ).ToString(),
                            () => Shell.Push(new CardShowDetailScreen(id)), Theme.Info));
                    }
                }
            }

            root.Add(Ui.Button("Inspect the " + set.Name + " checklist", () => Shell.Push(new SetDetailScreen(set.Id)), Ui.ButtonKind.Secondary));
        }
    }
}
