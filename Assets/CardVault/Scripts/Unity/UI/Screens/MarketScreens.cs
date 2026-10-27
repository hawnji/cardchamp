using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.State;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    /// <summary>NPC market: daily listings, selling, trade offers, vintage, own listings and history.</summary>
    public sealed class MarketScreen : GameScreen
    {
        private static readonly string[] Tabs = { "Listings", "Sell", "Trades", "Vintage", "My Listings", "History" };
        private int _tab;

        public override string Title => "Market";

        public override void Build(VisualElement root)
        {
            root.Add(Ui.Tabs(Tabs, _tab, i => { _tab = i; Shell.Refresh(); }));
            switch (_tab)
            {
                case 0: BuildListings(root, Game.Market.ActiveCardListings.Where(l => l.Source != ListingSource.Vintage)); break;
                case 1: BuildSell(root); break;
                case 2: BuildTrades(root); break;
                case 3: BuildVintage(root); break;
                case 4: BuildMine(root); break;
                default: BuildHistory(root); break;
            }
        }

        private void BuildListings(VisualElement root, IEnumerable<MarketListing> listings)
        {
            var list = listings.OrderByDescending(l => l.PriceCredits).ToList();
            if (list.Count == 0) root.Add(Ui.Muted("No cards listed right now. Vendors restock every game day."));
            foreach (var l in list) root.Add(CardListingRow(l));
        }

        private VisualElement CardListingRow(MarketListing l)
        {
            var listing = l;
            var card = Game.Inventory.Get(l.CardInstanceId);
            if (card == null) return Ui.Muted("Listing unavailable.");
            string subtitle = "Sold by " + Game.History.OwnerName(l.SellerId) +
                              " · est. " + Fmt.Credits(Game.Prices.EstimateValue(card)) +
                              (l.ExpiresAt > 0 ? " · until " + Fmt.Date(Game, l.ExpiresAt) : "");
            var row = Ui.ListRow(Game.Catalog.DisplayName(card), subtitle, Fmt.Credits(l.PriceCredits),
                () => Shell.Push(new CardDetailScreen(card.InstanceId)),
                card.IsSerialNumbered ? Theme.Accent : (UnityEngine.Color?)null);
            row.Add(Ui.Button("Buy", () => Shell.Run(() => Game.Market.Buy(listing.ListingId).AsResult(),
                Game.Catalog.DisplayName(card) + " added to your collection"), Ui.ButtonKind.Secondary));
            return row;
        }

        private void BuildSell(VisualElement root)
        {
            root.Add(Ui.Muted("Sell instantly to the best NPC offer, or list a card at your own price."));
            root.Add(Ui.Button("Choose a card to sell", PickCardToSell));
            var sellable = Game.Inventory.PlayerCards.Count(c => Game.Inventory.CanPlayerRelease(c).Ok);
            root.Add(Ui.Muted(sellable + " cards can be sold. Cards in grading or listed are excluded."));
        }

        private void PickCardToSell()
        {
            var source = Game.Inventory.PlayerCards.Where(c => Game.Inventory.CanPlayerRelease(c).Ok).ToList();
            if (source.Count == 0)
            {
                Shell.Toast("You have no cards available to sell.");
                return;
            }
            CardPicker.Show(Shell, "Sell a card", source, ShowSellOptions);
        }

        private void ShowSellOptions(CardInstance card)
        {
            var (npc, price) = Game.Market.QuoteSale(card);
            long estimate = Game.Prices.EstimateValue(card);
            long ask = System.Math.Max(1, (long)(estimate * 1.1));
            var body = Ui.Column(
                Ui.Text(Game.Catalog.DisplayName(card), Theme.FontBody, bold: true),
                Ui.Muted("Estimated value " + Fmt.Credits(estimate)),
                Ui.Spacer(12),
                Ui.Button(npc != null ? "Sell to " + npc.Name + " for " + Fmt.Credits(price) : "No buyer available",
                    () =>
                    {
                        Shell.CloseModal();
                        Shell.Run(() => Game.Market.SellToNpc(card.InstanceId).AsResult(), "Sold for " + Fmt.Credits(price));
                    }, enabled: npc != null),
                Ui.Button("List for " + Fmt.Credits(ask), () =>
                {
                    Shell.CloseModal();
                    Shell.Run(() => Game.Market.CreatePlayerListing(card.InstanceId, ask).AsResult(), "Listed. NPC buyers check listings daily.");
                }, Ui.ButtonKind.Secondary),
                Ui.Button("Cancel", Shell.CloseModal, Ui.ButtonKind.Ghost));
            Shell.ShowModal("Sell card", body);
        }

        private void BuildTrades(VisualElement root)
        {
            var trades = Game.Market.OpenTrades.OrderBy(t => t.ExpiresAt).ToList();
            if (trades.Count == 0) root.Add(Ui.Muted("No trade offers. NPC collectors send offers for cards they want."));
            foreach (var t in trades)
            {
                var trade = t;
                var npcName = Game.Content.Npcs.TryGetValue(t.NpcId, out var npc) ? npc.Name : t.NpcId;
                var panel = Ui.Panel(
                    Ui.Text(npcName + " offers a trade", Theme.FontBody, bold: true),
                    Ui.Muted("Expires " + Fmt.Date(Game, t.ExpiresAt)));
                panel.Add(Ui.SectionHeader("You receive"));
                foreach (var id in t.OfferedCardIds) panel.Add(CardLine(id));
                if (t.CreditsToPlayer > 0) panel.Add(Ui.Text("+ " + Fmt.Credits(t.CreditsToPlayer), Theme.FontSmall, Theme.Positive));
                panel.Add(Ui.SectionHeader("You give"));
                foreach (var id in t.RequestedCardIds) panel.Add(CardLine(id));
                panel.Add(Ui.Row(
                    Ui.Button("Accept", () => Shell.Run(() => Game.Market.AcceptTrade(trade.TradeId), "Trade completed")),
                    Ui.Button("Decline", () => Shell.Run(() => Game.Market.DeclineTrade(trade.TradeId), "Offer declined"), Ui.ButtonKind.Secondary)));
                root.Add(panel);
            }
        }

        private VisualElement CardLine(string cardId)
        {
            var card = Game.Inventory.Get(cardId);
            if (card == null) return Ui.Muted("Unknown card");
            return Ui.ListRow(Game.Catalog.DisplayName(card), "est. " + Fmt.Credits(Game.Prices.EstimateValue(card)), "View",
                () => Shell.Push(new CardDetailScreen(cardId)));
        }

        private void BuildVintage(VisualElement root)
        {
            root.Add(Ui.Muted("Cards and sealed product from retired and vintage sets."));
            var singles = Game.Market.ActiveCardListings.Where(l => l.Source == ListingSource.Vintage ||
                                                                   Game.Catalog.IsVintage(Game.Inventory.Get(l.CardInstanceId)?.TemplateId ?? "")).ToList();
            root.Add(Ui.SectionHeader("Vintage singles"));
            if (singles.Count == 0) root.Add(Ui.Muted("No vintage singles listed today."));
            foreach (var l in singles) root.Add(CardListingRow(l));

            int sealedCount = Game.Market.VintageListings.Count(l => l.SealedInstanceId != null);
            root.Add(Ui.SectionHeader("Vintage sealed"));
            root.Add(Ui.Button(sealedCount + " sealed listing" + (sealedCount == 1 ? "" : "s") + " in the Shop",
                () => Shell.SwitchTab(1), Ui.ButtonKind.Secondary));
        }

        private void BuildMine(VisualElement root)
        {
            var mine = Game.Market.PlayerListings.ToList();
            if (mine.Count == 0) root.Add(Ui.Muted("You have no active listings. List a card from the Sell tab or a card's detail page."));
            foreach (var l in mine)
            {
                var listing = l;
                var card = Game.Inventory.Get(l.CardInstanceId);
                var name = card != null ? Game.Catalog.DisplayName(card) : "Card";
                var row = Ui.ListRow(name, "Listed " + Fmt.Date(Game, l.CreatedAt), Fmt.Credits(l.PriceCredits),
                    () => Shell.Push(new CardDetailScreen(listing.CardInstanceId)));
                row.Add(Ui.Button("Cancel listing", () => Shell.Run(() => Game.Market.CancelPlayerListing(listing.ListingId), "Listing cancelled"),
                    Ui.ButtonKind.Secondary));
                root.Add(row);
            }
        }

        private void BuildHistory(VisualElement root)
        {
            var entries = Game.Market.RecentMarketHistory(50).ToList();
            if (entries.Count == 0) root.Add(Ui.Muted("No market activity yet."));
            foreach (var e in entries)
            {
                string delta = (e.CreditsDelta >= 0 ? "+" : "") + Fmt.Credits(e.CreditsDelta);
                var refId = e.RefId;
                bool isCard = refId != null && Game.Inventory.Get(refId) != null;
                root.Add(Ui.ListRow(e.Description ?? e.Kind.ToString(), Fmt.Date(Game, e.Time), delta,
                    isCard ? () => Shell.Push(new CardDetailScreen(refId)) : (System.Action)null,
                    e.CreditsDelta >= 0 ? Theme.Positive : Theme.Negative));
            }
        }
    }
}
