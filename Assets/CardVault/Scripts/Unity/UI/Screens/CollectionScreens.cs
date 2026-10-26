using System.Linq;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Rewards;
using CardVault.Core.State;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    public sealed class CollectionScreen : GameScreen
    {
        private static readonly string[] Tabs = { "Inventory", "Sets", "Showcase" };
        private readonly CardGrid _grid = new CardGrid();
        private int _tab;
        private CardFilter _filter;
        private int _page;

        public override string Title => "Collection";

        public override void Teardown() => _grid.Release();

        public override void Build(VisualElement root)
        {
            root.Add(Ui.Tabs(Tabs, _tab, i =>
            {
                if (i == 2) Shell.Push(new ShowcaseScreen());
                else
                {
                    _tab = i;
                    Shell.Refresh();
                }
            }));
            if (_tab == 1)
            {
                SetListScreen.BuildList(Shell, root);
                return;
            }

            var cards = CardFilters.Sorted(Game, Game.Inventory.PlayerCards.Where(c => CardFilters.Matches(Game, c, _filter))).ToList();
            root.Add(Ui.Muted(Game.Inventory.PlayerCardCount + " cards · " + Game.Sealed.PlayerUnopened.Count() + " sealed products"));
            root.Add(Ui.Tabs(CardFilters.All.Select(CardFilters.Label).ToList(), (int)_filter, i =>
            {
                _filter = (CardFilter)i;
                _page = 0;
                Shell.Refresh();
            }));
            _grid.Build(Shell, root, cards, _page, p => { _page = p; Shell.Refresh(); }, c => Shell.Push(new CardDetailScreen(c.InstanceId)));
        }
    }

    public sealed class SetListScreen : GameScreen
    {
        public override string Title => "Set Registry";

        public override void Build(VisualElement root) => BuildList(Shell, root);

        public static void BuildList(AppShell shell, VisualElement root)
        {
            var game = shell.Game;
            root.Add(Ui.Muted(game.SetRewards.SetsCompleted + " sets completed · " + game.SetRewards.ClaimableCount + " rewards to claim"));
            foreach (var set in game.Sets.AllSets)
            {
                var progress = game.Sets.Progress(set.Id);
                int claimable = game.SetRewards.Milestones(set.Id).Count(m => m.Claimable);
                var id = set.Id;
                string subtitle = game.Content.Brands[set.BrandId].Name + " · " + set.Year + (set.IsVintage ? " · Vintage" : "") +
                                  " · " + progress.BaseOwned + "/" + progress.BaseTotal + (claimable > 0 ? " · " + claimable + " to claim" : "");
                var row = Ui.ListRow(set.Name, subtitle, progress.EverCompleted ? "Completed" : Fmt.Percent(progress.Percent),
                    () => shell.Push(new SetDetailScreen(id)), progress.EverCompleted ? Theme.Accent : (Color?)null);
                row.Add(Ui.ProgressBar(progress.Percent / 100.0, height: 12));
                root.Add(row);
            }
        }
    }

    public sealed class SetDetailScreen : GameScreen
    {
        private readonly string _setId;
        private readonly CardViewPool _pool = new CardViewPool();
        private bool _showChecklist;

        public SetDetailScreen(string setId)
        {
            _setId = setId;
        }

        public override string Title => Game.Content.Sets[_setId].Name;

        public override void Teardown() => _pool.ReleaseAll();

        public override void Build(VisualElement root)
        {
            var set = Game.Content.Sets[_setId];
            var progress = Game.Sets.Progress(_setId);
            var brand = Game.Content.Brands[set.BrandId];

            var hero = Ui.Column();
            hero.style.height = 360;
            hero.style.justifyContent = Justify.FlexEnd;
            Theme.Round(hero, Theme.Radius);
            Theme.Padding(hero, 24, 32);
            hero.style.marginBottom = 20;
            var art = ArtCache.Get("sets/" + set.ArtKey);
            if (art != null) hero.style.backgroundImage = new StyleBackground(art);
            hero.style.backgroundColor = set.Category == CardCategory.Sports ? Theme.Hex("#1F3A5F") : Theme.Hex("#3D2A5C");
            if (set.IsVintage) hero.style.backgroundColor = Theme.Hex("#5A4632");
            if (progress.EverCompleted) hero.Add(Ui.Badge("COMPLETED SET", Theme.Accent));
            hero.Add(Ui.Text(set.Name, Theme.FontHero, bold: true));
            hero.Add(Ui.Muted(brand.Name + " · " + set.Year + (set.IsVintage ? " · Vintage" : "")));
            root.Add(hero);

            root.Add(Ui.Panel(
                Ui.Row(Ui.Text(Fmt.Percent(progress.Percent), Theme.FontHero, Theme.Accent, bold: true), Ui.Spacer(),
                    Ui.Column(Ui.Text(progress.BaseOwned + " collected", Theme.FontBody, bold: true),
                        Ui.Muted(progress.MissingBaseTemplateIds.Count + " missing · " + progress.DuplicateCopies + " duplicates"))),
                Ui.Spacer(12),
                Ui.ProgressBar(progress.Percent / 100.0),
                progress.FirstCompletedAt.HasValue ? Ui.Muted("First completed " + Fmt.Date(Game, progress.FirstCompletedAt.Value) +
                    (progress.IsCompleteNow ? "" : " · you have since parted with some cards")) : null,
                Ui.Muted(set.Description ?? "")));

            if (progress.RookiesTotal > 0)
                root.Add(Ui.Muted("Rookies: " + progress.RookiesOwned + "/" + progress.RookiesTotal + " · Graded: " + progress.GradedOwned + "/" + progress.BaseTotal));
            foreach (var p in progress.Parallels)
                root.Add(Ui.Muted(p.Name + (p.IsSerialNumbered ? (p.IsOneOfOne ? " (1/1 chase)" : " (numbered)") : "") + ": " + p.Owned + "/" + p.Total));

            root.Add(Ui.SectionHeader("Milestone rewards"));
            foreach (var m in Game.SetRewards.Milestones(_setId))
            {
                var milestone = m;
                string state = m.Claimed ? "Claimed " + Fmt.Date(Game, m.Record.ClaimedAt) : m.Achieved ? "Ready to claim" : "Locked";
                var panel = Ui.Panel(
                    Ui.Row(Ui.Text(m.Definition.Name, Theme.FontBody, bold: true), Ui.Spacer(), Ui.Muted(state)),
                    Ui.Muted(RewardService.Describe(m.Definition.Reward, Game.Content)));
                if (m.Claimable)
                    panel.Add(Ui.Button("Claim reward", () => Shell.Run(() => Game.SetRewards.Claim(_setId, milestone.Definition.Id).AsResult(),
                        "Claimed: " + RewardService.Describe(milestone.Definition.Reward, Game.Content))));
                root.Add(panel);
            }

            root.Add(Ui.Button(_showChecklist ? "Hide full collection" : "Inspect full collection", () =>
            {
                _showChecklist = !_showChecklist;
                Shell.Refresh();
            }, Ui.ButtonKind.Secondary));

            if (!_showChecklist) return;
            var owned = progress.OwnedBaseTemplateIds.ToHashSet();
            var grid = Ui.Grid(33.3f);
            foreach (var t in Game.Content.CardsInSet(_setId))
            {
                var copies = Game.Sets.OwnedCopies(t.Id).OrderByDescending(c => c.ParallelId == ContentDatabase.BaseParallelId).ToList();
                if (copies.Count > 0)
                {
                    var view = _pool.Get();
                    view.Bind(Game, copies[0], 300, Shell.ReducedEffects);
                    var id = copies[0].InstanceId;
                    view.OnTap(() => Shell.Push(new CardDetailScreen(id)));
                    if (!owned.Contains(t.Id)) view.style.opacity = 0.7f;
                    else view.style.opacity = 1f;
                    Ui.AddToGrid(grid, view);
                }
                else
                {
                    var missing = Ui.Column(Ui.Text("#" + t.Number, Theme.FontTitle, Theme.TextMuted, bold: true), Ui.Muted(t.Name), Ui.Muted("Missing"));
                    missing.style.height = 420;
                    missing.style.justifyContent = Justify.Center;
                    missing.style.alignItems = Align.Center;
                    Theme.Round(missing, 18);
                    Theme.BorderColor(missing, Theme.Border, 3);
                    Ui.AddToGrid(grid, missing);
                }
            }
            root.Add(grid);
        }
    }

    public sealed class CardDetailScreen : GameScreen
    {
        private readonly string _cardId;
        private readonly CardViewPool _pool = new CardViewPool();
        private int _tab;

        public CardDetailScreen(string cardId)
        {
            _cardId = cardId;
        }

        public override string Title => "Card";

        public override void Teardown() => _pool.ReleaseAll();

        public override void Build(VisualElement root)
        {
            var card = Game.Inventory.Get(_cardId);
            if (card == null)
            {
                root.Add(Ui.Muted("Card not found."));
                return;
            }
            var t = Game.Content.Cards[card.TemplateId];
            var view = _pool.Get();
            view.Bind(Game, card, 620, Shell.ReducedEffects);
            view.style.alignSelf = Align.Center;
            root.Add(view);
            root.Add(Ui.Spacer(16));
            root.Add(Ui.Title(Game.Catalog.DisplayName(card)));
            root.Add(Ui.Tabs(new[] { "Details", "History" }, _tab, i => { _tab = i; Shell.Refresh(); }));
            if (_tab == 1) BuildHistory(root, card);
            else BuildDetails(root, card, t);
        }

        private void BuildDetails(VisualElement root, CardInstance card, CardTemplateDef t)
        {
            var set = Game.Content.Sets[t.SetId];
            var subject = Game.Catalog.SubjectOf(t.Id);
            var parallel = Game.Content.Parallel(card.ParallelId);
            var info = Ui.Panel(
                Ui.Muted(set.Name + " · " + Game.Content.Brands[set.BrandId].Name + " · " + set.Year),
                Ui.Muted("Rarity " + t.Rarity + (t.IsRookie ? " · Rookie" : "") + (t.IsAutograph ? " · Autograph" : "") + (t.IsAlternateArt ? " · Alternate art" : "")),
                Ui.Muted("Parallel: " + parallel.Name + (card.IsSerialNumbered ? " · Serial " + CardCatalog.SerialLabel(card) : "")),
                Ui.Muted(card.IsGraded ? "Grade " + card.Grade.Value.ToString("0.#") + " · cert " + card.GradingCertId : "Raw · condition " + card.Condition.ToString("0.0")),
                Ui.Muted("Estimated value " + Fmt.Credits(Game.Prices.EstimateValue(card))),
                Ui.Muted("Owner: " + Game.History.OwnerName(card.OwnerId) + (card.Status != CardStatus.Available ? " · " + card.Status : "")));
            if (subject != null)
            {
                string team = subject.TeamId != null && Game.Content.Teams.TryGetValue(subject.TeamId, out var tm) ? tm.City + " " + tm.Name : null;
                info.Add(Ui.Muted(subject.Name + (team != null ? " · " + team : "") + (subject.Position != null ? " · " + subject.Position : "") +
                                  (subject.Element != null ? " · " + subject.Element : "")));
                if (subject.Stats.Count > 0) info.Add(Ui.Muted(string.Join("   ", subject.Stats.Select(kv => kv.Key + " " + kv.Value))));
            }
            root.Add(info);

            if (card.OwnerId != Owners.Player)
            {
                root.Add(Ui.Muted("This card belongs to " + Game.History.OwnerName(card.OwnerId) + "."));
                return;
            }

            var loc = Game.Showcase.LocationOf(card.InstanceId);
            if (loc.HasValue)
                root.Add(Ui.Button("Remove from showcase", () => Shell.Run(() => Game.Showcase.RemoveItem(card.InstanceId), "Removed from showcase"), Ui.ButtonKind.Secondary));
            root.Add(Ui.Button("Feature in showcase", () => Shell.Run(() => Game.Showcase.SetFeatured(card.InstanceId), "Now featured on your pedestal")));

            if (!card.IsGraded && card.Status == CardStatus.Available)
            {
                var fee = Game.Content.Pack.Grading.FeeCredits;
                root.Add(Ui.Button("Submit for grading (" + Fmt.Credits(fee) + ")",
                    () => Shell.Run(() => Game.Grading.Submit(card.InstanceId).AsResult(), "Sent to the grader. Results in 2 days."), Ui.ButtonKind.Secondary));
            }

            if (card.Tradable && card.Status == CardStatus.Available)
            {
                var (npc, price) = Game.Market.QuoteSale(card);
                root.Add(Ui.Button("Sell to " + npc.Name + " for " + Fmt.Credits(price), () => ConfirmSale(card, npc.Name, price), Ui.ButtonKind.Danger));
                long ask = (long)(Game.Prices.EstimateValue(card) * 1.1);
                root.Add(Ui.Button("List on market for " + Fmt.Credits(ask), () =>
                    Shell.Run(() => Game.Market.CreatePlayerListing(card.InstanceId, ask).AsResult(), "Listed. NPC buyers check listings daily."), Ui.ButtonKind.Secondary));
            }
            else if (card.Status == CardStatus.Listed)
            {
                var listing = Game.Market.PlayerListings.FirstOrDefault(l => l.CardInstanceId == card.InstanceId);
                if (listing != null)
                    root.Add(Ui.Button("Cancel listing (" + Fmt.Credits(listing.PriceCredits) + ")",
                        () => Shell.Run(() => Game.Market.CancelPlayerListing(listing.ListingId), "Listing cancelled"), Ui.ButtonKind.Secondary));
            }
            else if (!card.Tradable)
            {
                root.Add(Ui.Muted("Commemorative card: it can be displayed but not sold or traded."));
            }
        }

        private void ConfirmSale(CardInstance card, string buyer, long price)
        {
            var body = Ui.Column(
                Ui.Text("Sell " + Game.Catalog.DisplayName(card) + " to " + buyer + " for " + Fmt.Credits(price) + "?"),
                Ui.Muted("The card leaves your collection and showcase. Its history stays with the card."),
                Ui.Button("Confirm sale", () =>
                {
                    Shell.CloseModal();
                    if (Shell.Run(() => Game.Market.SellToNpc(card.InstanceId).AsResult(), "Sold for " + Fmt.Credits(price))) Shell.Back();
                }, Ui.ButtonKind.Danger),
                Ui.Button("Keep it", Shell.CloseModal, Ui.ButtonKind.Secondary));
            Shell.ShowModal("Confirm sale", body);
        }

        private void BuildHistory(VisualElement root, CardInstance card)
        {
            var t = Game.Content.Cards[card.TemplateId];
            root.Add(Ui.Panel(
                Ui.Text("Provenance", Theme.FontBody, bold: true),
                Ui.Muted(card.IsSerialNumbered ? "Serial " + CardCatalog.SerialLabel(card) + " of the " + Game.Content.Parallel(card.ParallelId).Name + " run" : "Unnumbered card"),
                Ui.Muted("Current owner: " + Game.History.OwnerName(card.OwnerId)),
                Ui.Muted("Owners so far: " + Game.History.OwnershipChain(card.InstanceId).Count(o => o != Owners.Unallocated)),
                Ui.Muted("Simulated in-game provenance. Card Vault cards and owners are fictional.")));

            var timeline = Game.History.For(card.InstanceId);
            if (timeline.Count == 0) root.Add(Ui.Muted("No recorded history."));
            foreach (var e in timeline)
            {
                var row = Ui.Row();
                row.style.alignItems = Align.FlexStart;
                row.style.marginBottom = 18;
                var dot = new VisualElement();
                dot.style.width = 24;
                dot.style.height = 24;
                dot.style.marginTop = 10;
                dot.style.marginRight = 24;
                Theme.Round(dot, 12);
                dot.style.backgroundColor = ColorFor(e.Type);
                row.Add(dot);
                var col = Ui.Column(
                    Ui.Text(Label(e.Type), Theme.FontBody, bold: true),
                    Ui.Text(e.Description, Theme.FontSmall),
                    Ui.Muted(Fmt.Date(Game, e.Time) + (e.PriceCredits.HasValue && e.Type != HistoryEventType.GradingSubmitted ? " · " + Fmt.Credits(e.PriceCredits.Value) : "")));
                col.style.flexShrink = 1;
                row.Add(col);
                root.Add(row);
            }
        }

        private static string Label(HistoryEventType type)
        {
            switch (type)
            {
                case HistoryEventType.Created: return "Printed";
                case HistoryEventType.PackOpened: return "Pulled from a pack";
                case HistoryEventType.Acquired: return "Acquired";
                case HistoryEventType.NpcPurchase: return "Purchased";
                case HistoryEventType.NpcSale: return "Sold";
                case HistoryEventType.NpcTrade: return "Traded";
                case HistoryEventType.GradingSubmitted: return "Sent for grading";
                case HistoryEventType.GradeAwarded: return "Graded";
                case HistoryEventType.ListingCreated: return "Listed";
                case HistoryEventType.ListingCanceled: return "Listing withdrawn";
                case HistoryEventType.ListingSold: return "Listing sold";
                case HistoryEventType.EventAcquisition: return "Card show acquisition";
                case HistoryEventType.RewardGrant: return "Reward";
                case HistoryEventType.SetMilestone: return "Collection milestone";
                case HistoryEventType.CollectionAchievement: return "Storied card";
                case HistoryEventType.Correction: return "Correction";
                case HistoryEventType.MigrationBackfill: return "Record created";
                default: return type.ToString();
            }
        }

        private static Color ColorFor(HistoryEventType type)
        {
            switch (type)
            {
                case HistoryEventType.NpcSale:
                case HistoryEventType.ListingSold: return Theme.Negative;
                case HistoryEventType.NpcPurchase:
                case HistoryEventType.PackOpened:
                case HistoryEventType.EventAcquisition:
                case HistoryEventType.RewardGrant: return Theme.Positive;
                case HistoryEventType.GradeAwarded:
                case HistoryEventType.SetMilestone:
                case HistoryEventType.CollectionAchievement: return Theme.Accent;
                default: return Theme.Info;
            }
        }
    }
}
