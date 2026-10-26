using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Calendar;
using CardVault.Core.Common;
using CardVault.Core.Packs;
using CardVault.Core.State;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    public sealed class ShopScreen : GameScreen
    {
        private static readonly string[] Tabs = { "New", "Vintage", "My Sealed", "Retiring" };
        private int _tab;

        public override string Title => "Shop";

        public override void Build(VisualElement root)
        {
            root.Add(Ui.Button("Release calendar", () => Shell.Push(new ReleaseCalendarScreen()), Ui.ButtonKind.Ghost));
            root.Add(Ui.Tabs(Tabs, _tab, i => { _tab = i; Shell.Refresh(); }));
            switch (_tab)
            {
                case 0: BuildNew(root); break;
                case 1: BuildVintage(root); break;
                case 2: BuildSealed(root); break;
                default: BuildRetiring(root); break;
            }
        }

        private void BuildNew(VisualElement root)
        {
            var offers = Game.Shop.Offers();
            if (offers.Count == 0) root.Add(Ui.Muted("No products are on sale right now. Check upcoming releases."));
            foreach (var group in offers.GroupBy(o => o.Release.Id))
            {
                var release = group.First().Release;
                root.Add(Ui.SectionHeader(release.Name));
                foreach (var offer in group) root.Add(OfferRow(offer));
            }
        }

        private VisualElement OfferRow(ShopOffer offer)
        {
            var o = offer;
            string retire = o.RetireAt > 0 ? " · retires " + Fmt.Date(Game, o.RetireAt) : "";
            var panel = Ui.Panel(
                Ui.Row(Ui.Badge(ReleaseCalendarService.Label(o.Status), o.Status == ReleaseStatus.LimitedStock ? Theme.Negative : Theme.Accent),
                    Ui.Muted(o.Remaining + " left" + retire)),
                Ui.Text(o.Product.Name, Theme.FontBody, bold: true),
                Ui.Muted(o.Product.Description ?? ""),
                Ui.Button("Buy for " + Fmt.Credits(o.Price), () => Shell.Run(() => Game.Shop.Buy(o.Release.Id, o.Product.Id).AsResult(),
                    o.Product.Name + " added to My Sealed"), enabled: ReleaseCalendarService.IsPurchasable(o.Status) && o.Remaining > 0));
            return panel;
        }

        private void BuildVintage(VisualElement root)
        {
            root.Add(Ui.Muted("Discontinued sealed product from retired releases. Supply is finite."));
            var listings = Game.Market.ActiveSealedListings.Where(l => l.Source == ListingSource.Vintage).ToList();
            if (listings.Count == 0) root.Add(Ui.Muted("No sealed vintage product listed today."));
            foreach (var l in listings)
            {
                var listing = l;
                var item = Game.Sealed.Get(l.SealedInstanceId);
                var product = Game.Content.Products[item.ProductId];
                root.Add(Ui.Panel(
                    Ui.Text(product.Name, Theme.FontBody, bold: true),
                    Ui.Muted("Offered by " + Game.History.OwnerName(l.SellerId)),
                    Ui.Button("Buy for " + Fmt.Credits(l.PriceCredits), () => Shell.Run(() => Game.Market.Buy(listing.ListingId).AsResult(),
                        product.Name + " added to My Sealed"))));
            }
            root.Add(Ui.Button("Vintage singles in the Market", () => Shell.SwitchTab(3), Ui.ButtonKind.Secondary));
        }

        private void BuildSealed(VisualElement root)
        {
            var items = Game.Sealed.PlayerUnopened.OrderBy(s => s.AcquiredAt).ToList();
            if (items.Count == 0) root.Add(Ui.Muted("You have no unopened product."));
            foreach (var group in items.GroupBy(s => s.ProductId))
            {
                var product = Game.Content.Products[group.Key];
                var first = group.First();
                root.Add(Ui.Panel(
                    Ui.Text(product.Name + " ×" + group.Count(), Theme.FontBody, bold: true),
                    Ui.Muted(product.IsContainer ? "Contains " + product.ContainsCount + " packs" : product.Description ?? ""),
                    Ui.Button(product.IsContainer ? "Open box" : "Open pack", () => OpenProduct(first.InstanceId))));
            }
        }

        private void OpenProduct(string sealedId)
        {
            var result = Game.Packs.Open(sealedId);
            if (!result.Ok)
            {
                Shell.Toast(result.Message);
                return;
            }
            Shell.AfterChange();
            if (result.Value.Cards.Count > 0) Shell.Push(new PackRevealScreen(result.Value));
            else Shell.Toast(result.Value.ContainedProducts.Count + " packs added to My Sealed");
        }

        private void BuildRetiring(VisualElement root)
        {
            var retiring = Game.Calendar.Retiring();
            if (retiring.Count == 0) root.Add(Ui.Muted("Nothing is retiring in the next two weeks."));
            foreach (var r in retiring)
            {
                var id = r.Id;
                root.Add(Ui.ListRow(r.Name, "Last day " + Fmt.Date(Game, r.RetireAt) + " · then moves to the vintage market", "View",
                    () => Shell.Push(new ReleaseDetailScreen(id)), Theme.Negative));
            }
        }
    }

    /// <summary>Reveals pulled cards one tap at a time; serial-numbered hits get a callout.</summary>
    public sealed class PackRevealScreen : GameScreen
    {
        private readonly PackOpeningResult _result;
        private readonly CardViewPool _pool = new CardViewPool();
        private int _revealed;

        public PackRevealScreen(PackOpeningResult result)
        {
            _result = result;
        }

        public override string Title => Game.Content.Products[_result.ProductId].Name;

        public override void Teardown() => _pool.ReleaseAll();

        public override void Build(VisualElement root)
        {
            if (_revealed < _result.Cards.Count)
            {
                var card = _result.Cards[_revealed];
                var view = _pool.Get();
                view.Bind(Game, card, 700, Shell.ReducedEffects);
                view.style.alignSelf = Align.Center;
                root.Add(view);
                root.Add(Ui.Spacer(16));
                if (card.IsSerialNumbered)
                    root.Add(Ui.Text(card.IsOneOfOne ? "ONE OF ONE!" : "Numbered " + Core.Cards.CardCatalog.SerialLabel(card), Theme.FontTitle, Theme.Accent, bold: true));
                root.Add(Ui.Text(Game.Catalog.DisplayName(card), Theme.FontBody, bold: true));
                root.Add(Ui.Muted("Estimated value " + Fmt.Credits(Game.Prices.EstimateValue(card)) + " · condition " + card.Condition.ToString("0.0")));
                root.Add(Ui.Button("Next card (" + (_revealed + 1) + "/" + _result.Cards.Count + ")", () => { _revealed++; Shell.Refresh(); }));
                root.Add(Ui.Button("Reveal all", () => { _revealed = _result.Cards.Count; Shell.Refresh(); }, Ui.ButtonKind.Secondary));
                return;
            }

            root.Add(Ui.Title("Your pulls"));
            var grid = Ui.Grid(33.3f);
            foreach (var card in _result.Cards)
            {
                var view = _pool.Get();
                view.Bind(Game, card, 300, Shell.ReducedEffects);
                var id = card.InstanceId;
                view.OnTap(() => Shell.Push(new CardDetailScreen(id)));
                Ui.AddToGrid(grid, view);
            }
            root.Add(grid);
            root.Add(Ui.Button("Done", Shell.Back));
        }
    }
}
