using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Economy;
using CardVault.Core.Game;
using CardVault.Core.Grading;
using CardVault.Core.History;
using CardVault.Core.Packs;
using CardVault.Core.State;

namespace CardVault.Core.Market
{
    /// <summary>
    /// NPC-only marketplace: daily NPC listings, vintage sealed listings, selling to NPCs,
    /// player listings bought by NPCs, and NPC trade offers.
    ///
    /// Every successful operation is one transaction that moves currency (ledger), moves
    /// ownership (inventory) and writes provenance (history) together. Validation happens
    /// before any mutation, so a failed operation leaves no trace.
    /// </summary>
    public sealed class MarketService
    {
        private readonly GameContext _ctx;
        private readonly CardInventoryService _inventory;
        private readonly SealedInventoryService _sealed;
        private readonly CardHistoryService _history;
        private readonly EconomyService _economy;
        private readonly PriceService _prices;
        private readonly CardCatalog _catalog;

        public MarketService(GameContext ctx, CardInventoryService inventory, SealedInventoryService sealedInventory,
            CardHistoryService history, EconomyService economy, PriceService prices, CardCatalog catalog)
        {
            _ctx = ctx;
            _inventory = inventory;
            _sealed = sealedInventory;
            _history = history;
            _economy = economy;
            _prices = prices;
            _catalog = catalog;
        }

        private MarketState Market => _ctx.State.Market;
        private MarketConfig Config => _ctx.Content.Pack.Market;

        public IEnumerable<MarketListing> ActiveListings
            => Market.Listings.Where(l => l.Status == ListingStatus.Active && l.SellerId != Owners.Player && IsVisible(l));

        public IEnumerable<MarketListing> ActiveCardListings => ActiveListings.Where(l => l.CardInstanceId != null);
        public IEnumerable<MarketListing> ActiveSealedListings => ActiveListings.Where(l => l.SealedInstanceId != null);
        public IEnumerable<MarketListing> VintageListings => ActiveListings.Where(l => l.Source == ListingSource.Vintage);
        public IEnumerable<MarketListing> PlayerListings => Market.Listings.Where(l => l.SellerId == Owners.Player && l.Status == ListingStatus.Active);
        public IEnumerable<TradeOffer> OpenTrades => Market.TradeOffers.Where(t => t.Status == TradeStatus.Open);

        public MarketListing Listing(string id) => Market.Listings.FirstOrDefault(l => l.ListingId == id);
        public TradeOffer Trade(string id) => Market.TradeOffers.FirstOrDefault(t => t.TradeId == id);

        private bool IsVisible(MarketListing l)
            => string.IsNullOrEmpty(l.RequiresUnlockId) || Market.UnlockedListingIds.Contains(l.RequiresUnlockId);

        // ================================================================ daily simulation

        /// <summary>Runs once per game day: expires stale offers, simulates NPC buyers, posts new stock.</summary>
        public void Tick()
        {
            long day = _ctx.Clock.Day;
            ExpireStale();
            if (day <= Market.LastRefreshDay) return;

            // Simulate each missed day at most once (bounded so long absences stay cheap).
            long firstDay = Math.Max(Market.LastRefreshDay + 1, day - 2);
            for (long d = firstDay; d <= day; d++)
            {
                SimulatePlayerListingSales();
            }
            Market.LastRefreshDay = day;
            PostDailyStock();
        }

        private void ExpireStale()
        {
            foreach (var l in Market.Listings.Where(l => l.Status == ListingStatus.Active && l.ExpiresAt > 0 && l.ExpiresAt <= _ctx.Now))
            {
                if (l.SellerId == Owners.Player) continue; // player listings stay until sold or cancelled
                l.Status = ListingStatus.Expired;
                var card = _inventory.Get(l.CardInstanceId);
                if (card != null && card.Status == CardStatus.Listed) card.Status = CardStatus.Available;
            }
            foreach (var t in Market.TradeOffers.Where(t => t.Status == TradeStatus.Open && t.ExpiresAt <= _ctx.Now))
                t.Status = TradeStatus.Expired;
        }

        private void PostDailyStock()
        {
            var npcs = _ctx.Content.Pack.Npcs;
            if (npcs.Count == 0) return;

            using (var tx = _ctx.Begin())
            {
                long expires = _ctx.Now + Config.ListingLifetimeDays * GameClock.SecondsPerDay;

                for (int i = 0; i < Config.DailyListingCount; i++)
                {
                    var npc = _ctx.Rng.Pick(npcs);
                    var set = PickSetFor(npc, vintage: false);
                    if (set == null) continue;
                    var card = SeedNpcCard(tx, npc, set);
                    if (card == null) continue;
                    PostListing(tx, npc, card, null, ListingSource.NpcDaily, expires);
                }

                // NPCs relist cards they hold (including ones bought from the player), which keeps
                // ownership chains moving and lets the player buy a card back.
                var held = _ctx.State.Cards.Values
                    .Where(c => Owners.IsNpc(c.OwnerId) && c.Status == CardStatus.Available && c.Tradable)
                    .OrderBy(c => c.InstanceId, StringComparer.Ordinal).ToList();
                for (int i = 0; i < Math.Min(3, held.Count); i++)
                {
                    var card = held[_ctx.Rng.Range(0, held.Count)];
                    if (card.Status != CardStatus.Available) continue;
                    var npc = _ctx.Content.Npcs[Owners.NpcIdOf(card.OwnerId)];
                    PostListing(tx, npc, card, null, ListingSource.NpcDaily, expires);
                }

                var vintageProducts = _ctx.Content.Pack.Products.Where(p => p.IsVintage).ToList();
                var vintageDealers = npcs.Where(n => n.DealsVintage).ToList();
                if (vintageDealers.Count == 0) vintageDealers = npcs;
                for (int i = 0; i < Config.VintageListingCount && vintageProducts.Count > 0; i++)
                {
                    var npc = _ctx.Rng.Pick(vintageDealers);
                    if (_ctx.Rng.Chance(0.5))
                    {
                        var product = _ctx.Rng.Pick(vintageProducts);
                        var item = _sealed.Create(tx, product.Id, Owners.Npc(npc.Id), "market:vintage");
                        PostListing(tx, npc, null, item, ListingSource.Vintage, expires);
                    }
                    else
                    {
                        var set = PickSetFor(npc, vintage: true);
                        if (set == null) continue;
                        var card = SeedNpcCard(tx, npc, set, preferGraded: true);
                        if (card != null) PostListing(tx, npc, card, null, ListingSource.Vintage, expires);
                    }
                }

                PostTradeOffers(tx, npcs);
                tx.Commit();
            }
        }

        private SetDef PickSetFor(NpcDef npc, bool vintage)
        {
            var sets = _ctx.Content.Pack.Sets.Where(s => s.IsVintage == vintage &&
                (npc.FocusCategory == null || s.Category == npc.FocusCategory.Value)).ToList();
            if (sets.Count == 0) sets = _ctx.Content.Pack.Sets.Where(s => s.IsVintage == vintage).ToList();
            return sets.Count == 0 ? null : _ctx.Rng.Pick(sets);
        }

        /// <summary>Creates a card held by an NPC dealer, with full provenance.</summary>
        public CardInstance SeedNpcCard(Transaction tx, NpcDef npc, SetDef set, bool preferGraded = false, string eventId = null)
        {
            var cards = _ctx.Content.CardsInSet(set.Id);
            if (cards.Count == 0) return null;
            var template = _ctx.Rng.Pick(cards);

            string parallelId = ContentDatabase.BaseParallelId;
            if (set.ParallelIds.Count > 0 && _ctx.Rng.Chance(0.25))
            {
                var p = _ctx.Rng.Pick(set.ParallelIds);
                // Dealers never stock one-of-ones from thin air; those come from packs and events.
                if (!_ctx.Content.Parallel(p).IsOneOfOne && _inventory.HasFreeSerial(template.Id, p)) parallelId = p;
            }

            double condition = set.IsVintage ? _ctx.Rng.Range(4.0, 9.0) : _ctx.Rng.Range(6.5, 10.0);
            double? grade = null;
            if (preferGraded ? _ctx.Rng.Chance(0.6) : _ctx.Rng.Chance(0.1))
                grade = GradingService.RollGrade(_ctx.Rng, condition, _ctx.Content.Pack.Grading);

            var card = _inventory.Create(tx, new CardCreationRequest
            {
                TemplateId = template.Id,
                ParallelId = parallelId,
                OwnerId = Owners.Npc(npc.Id),
                Condition = condition,
                Grade = grade,
                OriginRef = "market:" + npc.Id,
                EventId = eventId,
            });
            if (card == null) return null;
            _history.Append(tx, card.InstanceId, HistoryEventType.Acquired, e =>
            {
                e.PreviousOwnerId = Owners.Unallocated;
                e.NewOwnerId = card.OwnerId;
                e.NewCondition = card.Condition;
                e.NewGrade = card.Grade;
                e.SetId = set.Id;
                e.EventId = eventId;
            });
            return card;
        }

        private MarketListing PostListing(Transaction tx, NpcDef npc, CardInstance card, SealedProductInstance item, ListingSource source, long expires)
        {
            long price;
            if (card != null) price = (long)Math.Round(_prices.EstimateValue(card) * npc.SellMarkup);
            else price = (long)Math.Round(_prices.EstimateProductValue(_ctx.Content.Products[item.ProductId], 1.6) * npc.SellMarkup);

            var listing = new MarketListing
            {
                ListingId = _ctx.Ids.Next(IdPrefixes.Listing),
                SellerId = Owners.Npc(npc.Id),
                CardInstanceId = card?.InstanceId,
                SealedInstanceId = item?.InstanceId,
                PriceCredits = Math.Max(1, price),
                CreatedAt = _ctx.Now,
                ExpiresAt = expires,
                Status = ListingStatus.Active,
                Source = source,
            };
            Market.Listings.Add(listing);
            if (card != null)
            {
                card.Status = CardStatus.Listed;
                _history.Append(tx, card.InstanceId, HistoryEventType.ListingCreated, e =>
                {
                    e.PreviousOwnerId = card.OwnerId;
                    e.PriceCredits = listing.PriceCredits;
                    e.Description = npc.Name + " listed this card for " + listing.PriceCredits.ToString("N0") + " credits.";
                });
            }
            return listing;
        }

        private void PostTradeOffers(Transaction tx, IReadOnlyList<NpcDef> npcs)
        {
            var playerCards = _inventory.PlayerCards
                .Where(c => c.Tradable && c.Status == CardStatus.Available)
                .OrderBy(c => c.InstanceId, StringComparer.Ordinal).ToList();
            if (playerCards.Count == 0) return;

            for (int i = 0; i < Config.DailyTradeOffers; i++)
            {
                var npc = _ctx.Rng.Pick(npcs);
                var wanted = _ctx.Rng.Pick(playerCards);
                if (Market.TradeOffers.Any(t => t.Status == TradeStatus.Open && t.RequestedCardIds.Contains(wanted.InstanceId))) continue;

                var set = PickSetFor(npc, vintage: _ctx.Rng.Chance(0.2));
                if (set == null) continue;
                var offered = SeedNpcCard(tx, npc, set);
                if (offered == null) continue;

                long diff = _prices.EstimateValue(wanted) - _prices.EstimateValue(offered);
                var offer = new TradeOffer
                {
                    TradeId = _ctx.Ids.Next(IdPrefixes.Trade),
                    NpcId = npc.Id,
                    OfferedCardIds = { offered.InstanceId },
                    RequestedCardIds = { wanted.InstanceId },
                    // NPCs top up roughly 60% of a value gap in the player's favour, never pay more.
                    CreditsToPlayer = diff > 0 ? (long)(diff * 0.6) : 0,
                    CreatedAt = _ctx.Now,
                    ExpiresAt = _ctx.Now + 2 * GameClock.SecondsPerDay,
                    Status = TradeStatus.Open,
                };
                Market.TradeOffers.Add(offer);
            }
        }

        private void SimulatePlayerListingSales()
        {
            foreach (var listing in PlayerListings.ToList())
            {
                var card = _inventory.Get(listing.CardInstanceId);
                if (card == null || card.OwnerId != Owners.Player) continue;
                long value = _prices.EstimateValue(card);
                double ratio = listing.PriceCredits / (double)Math.Max(1, value);
                if (ratio > Config.MaxSellPriceOverValue) continue;
                double chance = Config.PlayerListingSaleChancePerDay * Math.Max(0.1, 1.0 - (ratio - 0.8));
                if (!_ctx.Rng.Chance(Math.Min(0.95, chance))) continue;

                var buyer = _ctx.Rng.Pick(_ctx.Content.Pack.Npcs);
                CompletePlayerListingSale(listing, card, buyer);
            }
        }

        private void CompletePlayerListingSale(MarketListing listing, CardInstance card, NpcDef buyer)
        {
            using (var tx = _ctx.Begin())
            {
                string buyerId = Owners.Npc(buyer.Id);
                _economy.Apply(tx, LedgerKind.ListingSale, listing.PriceCredits, 0, listing.ListingId,
                    "Sold " + _catalog.DisplayName(card) + " to " + buyer.Name);
                _inventory.Transfer(tx, card, buyerId);
                listing.Status = ListingStatus.Sold;
                listing.BuyerId = buyerId;
                listing.ClosedTransactionId = tx.Id;
                _history.Append(tx, card.InstanceId, HistoryEventType.ListingSold, e =>
                {
                    e.PreviousOwnerId = Owners.Player;
                    e.NewOwnerId = buyerId;
                    e.PriceCredits = listing.PriceCredits;
                });
                _ctx.RecordCardAction(tx, GameActionType.CardSold, card, refId: listing.ListingId);
                _ctx.Notify(NotificationKind.Market, "Listing sold",
                    buyer.Name + " bought " + _catalog.DisplayName(card) + " for " + listing.PriceCredits.ToString("N0") + " credits.",
                    card.InstanceId, "sold:" + listing.ListingId);
                tx.Commit();
            }
        }

        // ================================================================ player actions

        public OpResult<MarketListing> Buy(string listingId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult<MarketListing>.Fail(OpError.AlreadyProcessed, "Purchase already completed.");

            var listing = Listing(listingId);
            if (listing == null) return OpResult<MarketListing>.Fail(OpError.NotFound, "Listing not found.");
            if (listing.Status != ListingStatus.Active) return OpResult<MarketListing>.Fail(OpError.NotAvailable, "This listing is no longer available.");
            if (listing.ExpiresAt > 0 && listing.ExpiresAt <= _ctx.Now) return OpResult<MarketListing>.Fail(OpError.NotAvailable, "This listing has expired.");
            if (listing.SellerId == Owners.Player) return OpResult<MarketListing>.Fail(OpError.InvalidArgument, "You cannot buy your own listing.");
            if (!IsVisible(listing)) return OpResult<MarketListing>.Fail(OpError.Locked, "This listing requires collector access.");
            var afford = _economy.CheckAfford(listing.PriceCredits);
            if (!afford.Ok) return OpResult<MarketListing>.Fail(afford.Error, afford.Message);

            CardInstance card = null;
            SealedProductInstance item = null;
            if (listing.CardInstanceId != null)
            {
                card = _inventory.Get(listing.CardInstanceId);
                if (card == null || card.OwnerId != listing.SellerId)
                    return OpResult<MarketListing>.Fail(OpError.NotAvailable, "The seller no longer has this card.");
            }
            else
            {
                item = _sealed.Get(listing.SealedInstanceId);
                if (item == null || item.Opened || item.OwnerId != listing.SellerId)
                    return OpResult<MarketListing>.Fail(OpError.NotAvailable, "The seller no longer has this product.");
            }

            using (var tx = _ctx.Begin(transactionId))
            {
                string sellerName = _history.OwnerName(listing.SellerId);
                if (card != null)
                {
                    _economy.Apply(tx, LedgerKind.CardPurchase, -listing.PriceCredits, 0, listing.ListingId,
                        "Bought " + _catalog.DisplayName(card) + " from " + sellerName);
                    _inventory.Transfer(tx, card, Owners.Player);
                    _history.Append(tx, card.InstanceId, HistoryEventType.NpcPurchase, e =>
                    {
                        e.PreviousOwnerId = listing.SellerId;
                        e.NewOwnerId = Owners.Player;
                        e.PriceCredits = listing.PriceCredits;
                        e.NewGrade = card.Grade;
                    });
                    _ctx.RecordCardAction(tx, GameActionType.CardPurchased, card, refId: listing.ListingId);
                    _ctx.RecordCardAction(tx, GameActionType.CardAcquired, card, refId: listing.ListingId);
                }
                else
                {
                    var product = _ctx.Content.Products[item.ProductId];
                    var set = _ctx.Content.Sets[product.SetId];
                    _economy.Apply(tx, LedgerKind.ProductPurchase, -listing.PriceCredits, 0, listing.ListingId,
                        "Bought " + product.Name + " from " + sellerName);
                    item.OwnerId = Owners.Player;
                    item.AcquiredAt = _ctx.Now;
                    _ctx.RecordAction(tx, GameActionType.ProductPurchased, a =>
                    {
                        a.ProductId = product.Id;
                        a.SetId = set.Id;
                        a.BrandId = set.BrandId;
                        a.IsVintage = product.IsVintage || set.IsVintage;
                        a.Category = set.Category;
                        a.RefId = listing.ListingId;
                    });
                }

                listing.Status = ListingStatus.Sold;
                listing.BuyerId = Owners.Player;
                listing.ClosedTransactionId = tx.Id;
                tx.Commit();
                return OpResult<MarketListing>.Success(listing);
            }
        }

        /// <summary>Best instant offer from any NPC for a player card.</summary>
        public (NpcDef npc, long price) QuoteSale(CardInstance card)
        {
            long value = _prices.EstimateValue(card);
            NpcDef best = null;
            double bestFactor = 0;
            var category = _catalog.CategoryOf(card.TemplateId);
            bool vintage = _catalog.IsVintage(card.TemplateId);
            foreach (var npc in _ctx.Content.Pack.Npcs)
            {
                double factor = npc.BuyFactor;
                if (npc.FocusCategory.HasValue && npc.FocusCategory.Value == category) factor += 0.05;
                if (vintage && npc.DealsVintage) factor += 0.05;
                if (factor > bestFactor || (factor == bestFactor && string.CompareOrdinal(npc.Id, best?.Id) < 0))
                {
                    best = npc;
                    bestFactor = factor;
                }
            }
            return (best, Math.Max(1, (long)Math.Round(value * bestFactor)));
        }

        public OpResult<long> SellToNpc(string cardInstanceId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult<long>.Fail(OpError.AlreadyProcessed, "Sale already completed.");
            var card = _inventory.Get(cardInstanceId);
            var check = _inventory.CanPlayerRelease(card);
            if (!check.Ok) return OpResult<long>.Fail(check.Error, check.Message);
            if (card.Status == CardStatus.Listed) return OpResult<long>.Fail(OpError.CardBusy, "Cancel the listing before selling this card.");
            var (npc, price) = QuoteSale(card);
            if (npc == null) return OpResult<long>.Fail(OpError.NotAvailable, "No dealer is buying right now.");

            using (var tx = _ctx.Begin(transactionId))
            {
                string buyerId = Owners.Npc(npc.Id);
                _economy.Apply(tx, LedgerKind.CardSale, price, 0, card.InstanceId, "Sold " + _catalog.DisplayName(card) + " to " + npc.Name);
                _inventory.Transfer(tx, card, buyerId);
                _history.Append(tx, card.InstanceId, HistoryEventType.NpcSale, e =>
                {
                    e.PreviousOwnerId = Owners.Player;
                    e.NewOwnerId = buyerId;
                    e.PriceCredits = price;
                    e.NewGrade = card.Grade;
                });
                _ctx.RecordCardAction(tx, GameActionType.CardSold, card, refId: npc.Id);
                tx.Commit();
                return OpResult<long>.Success(price);
            }
        }

        public OpResult<MarketListing> CreatePlayerListing(string cardInstanceId, long priceCredits, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult<MarketListing>.Fail(OpError.AlreadyProcessed, "Listing already created.");
            var card = _inventory.Get(cardInstanceId);
            var check = _inventory.CanPlayerRelease(card);
            if (!check.Ok) return OpResult<MarketListing>.Fail(check.Error, check.Message);
            if (card.Status == CardStatus.Listed) return OpResult<MarketListing>.Fail(OpError.CardBusy, "This card is already listed.");
            if (priceCredits <= 0) return OpResult<MarketListing>.Fail(OpError.InvalidArgument, "Enter a price above zero.");

            using (var tx = _ctx.Begin(transactionId))
            {
                var listing = new MarketListing
                {
                    ListingId = _ctx.Ids.Next(IdPrefixes.Listing),
                    SellerId = Owners.Player,
                    CardInstanceId = card.InstanceId,
                    PriceCredits = priceCredits,
                    CreatedAt = _ctx.Now,
                    Status = ListingStatus.Active,
                    Source = ListingSource.PlayerListing,
                };
                Market.Listings.Add(listing);
                card.Status = CardStatus.Listed;
                _history.Append(tx, card.InstanceId, HistoryEventType.ListingCreated, e =>
                {
                    e.PreviousOwnerId = Owners.Player;
                    e.PriceCredits = priceCredits;
                });
                tx.Commit();
                return OpResult<MarketListing>.Success(listing);
            }
        }

        public OpResult CancelPlayerListing(string listingId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult.Fail(OpError.AlreadyProcessed, "Listing already cancelled.");
            var listing = Listing(listingId);
            if (listing == null) return OpResult.Fail(OpError.NotFound, "Listing not found.");
            if (listing.SellerId != Owners.Player) return OpResult.Fail(OpError.NotOwned, "This is not your listing.");
            if (listing.Status != ListingStatus.Active) return OpResult.Fail(OpError.InvalidState, "This listing is already closed.");

            using (var tx = _ctx.Begin(transactionId))
            {
                listing.Status = ListingStatus.Cancelled;
                listing.ClosedTransactionId = tx.Id;
                var card = _inventory.Get(listing.CardInstanceId);
                if (card != null)
                {
                    card.Status = CardStatus.Available;
                    _history.Append(tx, card.InstanceId, HistoryEventType.ListingCanceled, e => e.PreviousOwnerId = Owners.Player);
                }
                tx.Commit();
                return OpResult.Success();
            }
        }

        public OpResult AcceptTrade(string tradeId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult.Fail(OpError.AlreadyProcessed, "Trade already completed.");
            var trade = Trade(tradeId);
            if (trade == null) return OpResult.Fail(OpError.NotFound, "Trade offer not found.");
            if (trade.Status != TradeStatus.Open) return OpResult.Fail(OpError.NotAvailable, "This offer is no longer open.");
            if (trade.ExpiresAt <= _ctx.Now) return OpResult.Fail(OpError.NotAvailable, "This offer has expired.");

            string npcOwner = Owners.Npc(trade.NpcId);
            var requested = trade.RequestedCardIds.Select(_inventory.Get).ToList();
            foreach (var card in requested)
            {
                var check = _inventory.CanPlayerRelease(card);
                if (!check.Ok) return check;
                if (card.Status == CardStatus.Listed) return OpResult.Fail(OpError.CardBusy, "Cancel the listing for this card first.");
            }
            var offered = trade.OfferedCardIds.Select(_inventory.Get).ToList();
            if (offered.Any(c => c == null || c.OwnerId != npcOwner || c.Status != CardStatus.Available))
                return OpResult.Fail(OpError.NotAvailable, "The dealer no longer has the offered card.");
            if (trade.CreditsToPlayer < 0)
            {
                var afford = _economy.CheckAfford(-trade.CreditsToPlayer);
                if (!afford.Ok) return afford;
            }

            using (var tx = _ctx.Begin(transactionId))
            {
                string npcName = _history.OwnerName(npcOwner);
                foreach (var card in requested)
                {
                    _inventory.Transfer(tx, card, npcOwner);
                    _history.Append(tx, card.InstanceId, HistoryEventType.NpcTrade, e =>
                    {
                        e.PreviousOwnerId = Owners.Player;
                        e.NewOwnerId = npcOwner;
                        e.Description = "Traded to " + npcName + ".";
                    });
                }
                foreach (var card in offered)
                {
                    _inventory.Transfer(tx, card, Owners.Player);
                    _history.Append(tx, card.InstanceId, HistoryEventType.NpcTrade, e =>
                    {
                        e.PreviousOwnerId = npcOwner;
                        e.NewOwnerId = Owners.Player;
                        e.Description = "Received in a trade with " + npcName + ".";
                    });
                    _ctx.RecordCardAction(tx, GameActionType.CardAcquired, card, refId: trade.TradeId);
                }
                if (trade.CreditsToPlayer != 0)
                    _economy.Apply(tx, LedgerKind.TradeCash, trade.CreditsToPlayer, 0, trade.TradeId, "Trade with " + npcName);

                trade.Status = TradeStatus.Accepted;
                trade.ClosedTransactionId = tx.Id;
                _ctx.RecordAction(tx, GameActionType.TradeCompleted, a => a.RefId = trade.TradeId);
                tx.Commit();
                return OpResult.Success();
            }
        }

        public OpResult DeclineTrade(string tradeId)
        {
            var trade = Trade(tradeId);
            if (trade == null) return OpResult.Fail(OpError.NotFound, "Trade offer not found.");
            if (trade.Status != TradeStatus.Open) return OpResult.Fail(OpError.InvalidState, "This offer is already closed.");
            trade.Status = TradeStatus.Declined;
            return OpResult.Success();
        }

        /// <summary>Recent completed market transactions for the Market history view.</summary>
        public IEnumerable<LedgerEntry> RecentMarketHistory(int count)
            => _ctx.State.Ledger.Where(l => l.Kind == LedgerKind.CardPurchase || l.Kind == LedgerKind.CardSale ||
                                             l.Kind == LedgerKind.ListingSale || l.Kind == LedgerKind.TradeCash ||
                                             l.Kind == LedgerKind.ProductPurchase || l.Kind == LedgerKind.EventPurchase)
                .Reverse().Take(count);

        public void UnlockListingAccess(string unlockId) => Market.UnlockedListingIds.Add(unlockId);
    }
}
