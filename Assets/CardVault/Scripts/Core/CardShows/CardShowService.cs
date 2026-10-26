using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Calendar;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Economy;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.Packs;
using CardVault.Core.Rewards;
using CardVault.Core.State;

namespace CardVault.Core.CardShows
{
    public enum OccurrenceStatus { Upcoming, Active, Ended }

    public sealed class EventOccurrence
    {
        public CardShowEventDef Definition;
        public int Index;
        public long StartsAt;
        public long EndsAt;
        public string OccurrenceId => CardShowService.OccurrenceIdFor(Definition.Id, Index);
    }

    public sealed class ChallengeView
    {
        public ChallengeDef Definition;
        public int Progress;
        public int Target;
        public bool Completed;
    }

    public sealed class VendorStockView
    {
        public VendorStockDef Definition;
        public string DisplayName;
        public int Remaining;
        public long PriceCredits;
        public int PriceTickets;
        public bool Locked;
        /// <summary>The concrete card the vendor holds next (card stock only).</summary>
        public string NextCardInstanceId;
    }

    /// <summary>
    /// Card show events: deterministic schedules, limited vendor inventory, challenges verified
    /// against the persistent action log, event points, once-only reward redemption and an
    /// event history. Works entirely offline on the internal game clock.
    /// </summary>
    public sealed class CardShowService
    {
        /// <summary>Points can still be redeemed this long after a show closes.</summary>
        public const long RedemptionGraceSeconds = GameClock.SecondsPerDay;

        private readonly GameContext _ctx;
        private readonly CardInventoryService _inventory;
        private readonly SealedInventoryService _sealed;
        private readonly CardHistoryService _history;
        private readonly EconomyService _economy;
        private readonly RewardService _rewards;
        private readonly CardCatalog _catalog;

        public CardShowService(GameContext ctx, CardInventoryService inventory, SealedInventoryService sealedInventory,
            CardHistoryService history, EconomyService economy, RewardService rewards, CardCatalog catalog)
        {
            _ctx = ctx;
            _inventory = inventory;
            _sealed = sealedInventory;
            _history = history;
            _economy = economy;
            _rewards = rewards;
            _catalog = catalog;
            _ctx.Events.ActionRecorded += OnAction;
        }

        private CardShowState State => _ctx.State.CardShows;

        public static string OccurrenceIdFor(string eventId, int index) => eventId + "@" + index;

        // ================================================================ schedule

        public static long PeriodOf(ScheduleKind kind)
        {
            switch (kind)
            {
                case ScheduleKind.Daily: return GameClock.SecondsPerDay;
                case ScheduleKind.Weekly: return 7 * GameClock.SecondsPerDay;
                case ScheduleKind.Monthly: return 28 * GameClock.SecondsPerDay;
                default: return 0;
            }
        }

        /// <summary>Occurrences overlapping [from, to). Pure arithmetic on the schedule definition.</summary>
        public IEnumerable<EventOccurrence> OccurrencesBetween(long from, long to)
        {
            foreach (var def in _ctx.Content.Pack.CardShows)
            {
                var s = def.Schedule;
                long period = PeriodOf(s.Kind);
                if (period == 0)
                {
                    if (s.FirstStartAt < to && s.FirstStartAt + s.DurationSeconds > from)
                        yield return new EventOccurrence { Definition = def, Index = 0, StartsAt = s.FirstStartAt, EndsAt = s.FirstStartAt + s.DurationSeconds };
                    continue;
                }
                long firstIndex = Math.Max(0, (long)Math.Floor((from - s.DurationSeconds - s.FirstStartAt) / (double)period));
                for (long n = firstIndex; ; n++)
                {
                    if (s.MaxOccurrences > 0 && n >= s.MaxOccurrences) break;
                    long start = s.FirstStartAt + n * period;
                    if (start >= to) break;
                    long end = start + s.DurationSeconds;
                    if (end > from) yield return new EventOccurrence { Definition = def, Index = (int)n, StartsAt = start, EndsAt = end };
                }
            }
        }

        public EventOccurrence Occurrence(string occurrenceId)
        {
            int at = occurrenceId?.LastIndexOf('@') ?? -1;
            if (at <= 0 || !int.TryParse(occurrenceId.Substring(at + 1), out int index) || index < 0) return null;
            if (!_ctx.Content.CardShows.TryGetValue(occurrenceId.Substring(0, at), out var def)) return null;
            var s = def.Schedule;
            long period = PeriodOf(s.Kind);
            if (period == 0 && index != 0) return null;
            if (s.MaxOccurrences > 0 && index >= s.MaxOccurrences) return null;
            long start = s.FirstStartAt + index * period;
            return new EventOccurrence { Definition = def, Index = index, StartsAt = start, EndsAt = start + s.DurationSeconds };
        }

        public OccurrenceStatus StatusOf(EventOccurrence occ)
            => _ctx.Now < occ.StartsAt ? OccurrenceStatus.Upcoming : _ctx.Now < occ.EndsAt ? OccurrenceStatus.Active : OccurrenceStatus.Ended;

        public IReadOnlyList<EventOccurrence> Active()
            => OccurrencesBetween(_ctx.Now, _ctx.Now + 1).Where(o => StatusOf(o) == OccurrenceStatus.Active).OrderBy(o => o.EndsAt).ToList();

        public IReadOnlyList<EventOccurrence> Upcoming(int days = 14)
            => OccurrencesBetween(_ctx.Now + 1, _ctx.Now + days * GameClock.SecondsPerDay)
                .Where(o => StatusOf(o) == OccurrenceStatus.Upcoming).OrderBy(o => o.StartsAt).ToList();

        /// <summary>Finished shows the player attended, newest first.</summary>
        public IReadOnlyList<CardShowRecord> History => State.History.OrderByDescending(h => h.EndsAt).ToList();

        public EventParticipation Participation(string occurrenceId)
            => State.Occurrences.TryGetValue(occurrenceId ?? "", out var p) ? p : null;

        public IEnumerable<CalendarItem> CalendarItems(long from, long to)
            => OccurrencesBetween(from, to).Where(o => o.Definition.Category != CardShowCategory.DailyMiniEvent).Select(o => new CalendarItem
            {
                Kind = CalendarItemKind.CardShow,
                Id = o.OccurrenceId,
                Name = o.Definition.Name,
                Time = o.StartsAt,
                EndTime = o.EndsAt,
                StatusLabel = StatusOf(o).ToString(),
                CategoryLabel = "Card Show",
                Category = CategoryOfShow(o.Definition),
            });

        private CardCategory? CategoryOfShow(CardShowEventDef def)
        {
            var cats = def.FeaturedSetIds.Select(id => _ctx.Content.Sets[id].Category).Distinct().ToList();
            return cats.Count == 1 ? cats[0] : (CardCategory?)null;
        }

        // ================================================================ visiting & vendors

        public OpResult<EventParticipation> Visit(string occurrenceId)
        {
            var occ = Occurrence(occurrenceId);
            if (occ == null) return OpResult<EventParticipation>.Fail(OpError.NotFound, "Card show not found.");
            if (StatusOf(occ) != OccurrenceStatus.Active)
                return OpResult<EventParticipation>.Fail(OpError.NotAvailable, StatusOf(occ) == OccurrenceStatus.Upcoming ? "This show has not opened yet." : "This show has closed.");

            var p = Participation(occurrenceId);
            bool firstVisit = p == null || !p.Visited;
            using (var tx = _ctx.Begin())
            {
                if (p == null)
                {
                    p = new EventParticipation
                    {
                        OccurrenceId = occurrenceId,
                        EventId = occ.Definition.Id,
                        OccurrenceIndex = occ.Index,
                        StartsAt = occ.StartsAt,
                        EndsAt = occ.EndsAt,
                    };
                    State.Occurrences[occurrenceId] = p;
                }
                if (firstVisit)
                {
                    p.Visited = true;
                    p.FirstVisitedAt = _ctx.Now;
                    _ctx.RecordAction(tx, GameActionType.CardShowVisited, a => a.RefId = occurrenceId);
                }
                InitializeInventory(tx, occ, p);
                tx.Commit();
            }
            Evaluate(occ);
            return OpResult<EventParticipation>.Success(p);
        }

        /// <summary>Creates the show's finite stock once. Vendor cards are real NPC-owned instances.</summary>
        private void InitializeInventory(Transaction tx, EventOccurrence occ, EventParticipation p)
        {
            if (p.InventoryInitialized) return;
            p.InventoryInitialized = true;
            foreach (var vendor in occ.Definition.Vendors)
            foreach (var stock in vendor.Stock)
            {
                if (stock.ProductId != null)
                {
                    p.StockRemaining[stock.Id] = stock.Quantity;
                    continue;
                }
                var cards = new List<string>();
                for (int i = 0; i < stock.Quantity; i++)
                {
                    var card = _inventory.Create(tx, new CardCreationRequest
                    {
                        TemplateId = stock.TemplateId,
                        ParallelId = stock.ParallelId ?? ContentDatabase.BaseParallelId,
                        OwnerId = Owners.Npc(vendor.NpcId),
                        Condition = stock.Condition,
                        Grade = stock.Grade,
                        OriginRef = "show:" + occ.OccurrenceId,
                        EventId = occ.Definition.Id,
                    });
                    if (card == null) break; // numbered run exhausted: the vendor simply has fewer
                    _history.Append(tx, card.InstanceId, HistoryEventType.Acquired, e =>
                    {
                        e.PreviousOwnerId = Owners.Unallocated;
                        e.NewOwnerId = card.OwnerId;
                        e.EventId = occ.Definition.Id;
                        e.SetId = card.OriginSetId;
                        e.NewGrade = card.Grade;
                        e.Description = _history.OwnerName(card.OwnerId) + " brought this card to " + occ.Definition.Name + ".";
                    });
                    cards.Add(card.InstanceId);
                }
                p.VendorCardInstances[stock.Id] = cards;
                p.StockRemaining[stock.Id] = cards.Count;
            }
        }

        public IReadOnlyList<VendorStockView> VendorStock(string occurrenceId, string vendorId)
        {
            var occ = Occurrence(occurrenceId);
            var vendor = occ?.Definition.Vendors.FirstOrDefault(v => v.Id == vendorId);
            if (vendor == null) return Array.Empty<VendorStockView>();
            var p = Participation(occurrenceId);
            return vendor.Stock.Select(s => new VendorStockView
            {
                Definition = s,
                DisplayName = StockName(s),
                Remaining = p != null && p.StockRemaining.TryGetValue(s.Id, out var n) ? n : (p?.InventoryInitialized == true ? 0 : s.Quantity),
                PriceCredits = PriceFor(occ.Definition, s),
                PriceTickets = s.PriceTickets,
                Locked = !string.IsNullOrEmpty(s.RequiresUnlockId) && !_ctx.State.Market.UnlockedListingIds.Contains(s.RequiresUnlockId),
                NextCardInstanceId = p != null && p.VendorCardInstances.TryGetValue(s.Id, out var ids)
                    ? ids.FirstOrDefault(id => _inventory.IsOwnedBy(id, Owners.Npc(vendor.NpcId)))
                    : null,
            }).ToList();
        }

        private string StockName(VendorStockDef s)
        {
            if (s.ProductId != null) return _ctx.Content.Products[s.ProductId].Name;
            var t = _ctx.Content.Cards[s.TemplateId];
            string name = t.Name + " #" + t.Number;
            var parallel = _ctx.Content.Parallel(s.ParallelId);
            if (!parallel.IsBase) name += " " + parallel.Name + (parallel.IsSerialNumbered ? " /" + parallel.PrintRun : "");
            if (s.Grade.HasValue) name += " · Grade " + s.Grade.Value.ToString("0.#");
            return name;
        }

        public long PriceFor(CardShowEventDef def, VendorStockDef stock)
        {
            if (stock.PriceCredits <= 0) return 0;
            string setId = stock.ProductId != null ? _ctx.Content.Products[stock.ProductId].SetId : _ctx.Content.Cards[stock.TemplateId].SetId;
            double price = stock.PriceCredits * def.Pricing.VendorMarkup;
            if (def.FeaturedSetIds.Contains(setId)) price *= def.Pricing.FeaturedSetDiscount;
            return Math.Max(1, (long)Math.Round(price));
        }

        public OpResult BuyFromVendor(string occurrenceId, string vendorId, string stockId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult.Fail(OpError.AlreadyProcessed, "Purchase already completed.");
            var occ = Occurrence(occurrenceId);
            if (occ == null) return OpResult.Fail(OpError.NotFound, "Card show not found.");
            if (StatusOf(occ) != OccurrenceStatus.Active) return OpResult.Fail(OpError.NotAvailable, "Vendors are only open while the show runs.");
            var vendor = occ.Definition.Vendors.FirstOrDefault(v => v.Id == vendorId);
            var stock = vendor?.Stock.FirstOrDefault(s => s.Id == stockId);
            if (stock == null) return OpResult.Fail(OpError.NotFound, "Item not found.");
            if (!string.IsNullOrEmpty(stock.RequiresUnlockId) && !_ctx.State.Market.UnlockedListingIds.Contains(stock.RequiresUnlockId))
                return OpResult.Fail(OpError.Locked, "This item requires VIP access.");

            var p = Participation(occurrenceId);
            if (p == null || !p.Visited)
            {
                var visit = Visit(occurrenceId);
                if (!visit.Ok) return visit.AsResult();
                p = visit.Value;
            }
            if (!p.StockRemaining.TryGetValue(stockId, out int remaining) || remaining <= 0)
                return OpResult.Fail(OpError.OutOfStock, "Sold out.");

            long credits = PriceFor(occ.Definition, stock);
            var afford = _economy.CheckAfford(credits, stock.PriceTickets);
            if (!afford.Ok) return afford;

            string vendorOwner = Owners.Npc(vendor.NpcId);
            CardInstance card = null;
            if (stock.TemplateId != null)
            {
                string cardId = p.VendorCardInstances.TryGetValue(stockId, out var ids) ? ids.FirstOrDefault(id => _inventory.IsOwnedBy(id, vendorOwner)) : null;
                card = _inventory.Get(cardId);
                if (card == null) return OpResult.Fail(OpError.OutOfStock, "Sold out.");
            }

            using (var tx = _ctx.Begin(transactionId))
            {
                string vendorName = _history.OwnerName(vendorOwner);
                _economy.Apply(tx, LedgerKind.EventPurchase, -credits, -stock.PriceTickets, occurrenceId,
                    "Bought " + StockName(stock) + " from " + vendorName + " at " + occ.Definition.Name);
                p.StockRemaining[stockId] = remaining - 1;

                if (card != null)
                {
                    _inventory.Transfer(tx, card, Owners.Player);
                    _history.Append(tx, card.InstanceId, HistoryEventType.NpcPurchase, e =>
                    {
                        e.PreviousOwnerId = vendorOwner;
                        e.NewOwnerId = Owners.Player;
                        e.PriceCredits = credits;
                        e.EventId = occ.Definition.Id;
                        e.NewGrade = card.Grade;
                        e.Description = "Bought from " + vendorName + " at " + occ.Definition.Name +
                                        (credits > 0 ? " for " + credits.ToString("N0") + " credits" : "") +
                                        (stock.PriceTickets > 0 ? " for " + stock.PriceTickets + " event tickets" : "") + ".";
                    });
                    _ctx.RecordCardAction(tx, GameActionType.CardPurchased, card, refId: occurrenceId);
                    _ctx.RecordCardAction(tx, GameActionType.CardAcquired, card, refId: occurrenceId);
                }
                else
                {
                    var product = _ctx.Content.Products[stock.ProductId];
                    var set = _ctx.Content.Sets[product.SetId];
                    _sealed.Create(tx, product.Id, Owners.Player, "show:" + occurrenceId);
                    _ctx.RecordAction(tx, GameActionType.ProductPurchased, a =>
                    {
                        a.ProductId = product.Id;
                        a.SetId = set.Id;
                        a.BrandId = set.BrandId;
                        a.IsVintage = product.IsVintage || set.IsVintage;
                        a.Category = set.Category;
                        a.RefId = occurrenceId;
                    });
                }
                _ctx.RecordAction(tx, GameActionType.EventVendorPurchase, a => a.RefId = occurrenceId);
                tx.Commit();
                return OpResult.Success();
            }
        }

        // ================================================================ challenges

        private void OnAction(GameAction action)
        {
            switch (action.Type)
            {
                case GameActionType.CardShowVisited:
                case GameActionType.CardShowChallengeCompleted:
                case GameActionType.CardShowCompleted:
                case GameActionType.AchievementUnlocked:
                    return;
            }
            foreach (var p in State.Occurrences.Values.ToList())
            {
                if (!p.Visited || p.Completed || action.Time < p.StartsAt || action.Time >= p.EndsAt) continue;
                var occ = Occurrence(p.OccurrenceId);
                if (occ != null) Evaluate(occ);
            }
        }

        public IReadOnlyList<ChallengeView> Challenges(string occurrenceId)
        {
            var occ = Occurrence(occurrenceId);
            if (occ == null) return Array.Empty<ChallengeView>();
            var p = Participation(occurrenceId);
            return occ.Definition.Challenges.Select(c => new ChallengeView
            {
                Definition = c,
                Target = c.Count,
                Progress = Math.Min(c.Count, CountProgress(occ, c)),
                Completed = p != null && p.CompletedChallenges.ContainsKey(c.Id),
            }).ToList();
        }

        /// <summary>Counts committed actions inside the show's window that satisfy the challenge.</summary>
        public int CountProgress(EventOccurrence occ, ChallengeDef challenge)
        {
            var matching = ActionsBetween(occ.StartsAt, occ.EndsAt).Where(a =>
                a.Type == challenge.Action && Matches(a, challenge.Filter, occ.Definition));
            if (challenge.Filter.DistinctTemplates) return matching.Where(a => a.TemplateId != null).Select(a => a.TemplateId).Distinct().Count();
            if (challenge.Filter.DistinctBrands) return matching.Where(a => a.BrandId != null).Select(a => a.BrandId).Distinct().Count();
            return matching.Count();
        }

        /// <summary>The action log is append-only in game-time order, so the window is found by binary search.</summary>
        private IEnumerable<GameAction> ActionsBetween(long from, long to)
        {
            var actions = _ctx.State.Actions;
            int lo = 0, hi = actions.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (actions[mid].Time < from) lo = mid + 1;
                else hi = mid;
            }
            for (int i = lo; i < actions.Count && actions[i].Time < to; i++) yield return actions[i];
        }

        private static bool Matches(GameAction a, ChallengeFilter f, CardShowEventDef def)
        {
            if (f == null) return true;
            if (f.SetIds.Count > 0 && !f.SetIds.Contains(a.SetId)) return false;
            if (f.BrandIds.Count > 0 && !f.BrandIds.Contains(a.BrandId)) return false;
            if (f.FeaturedOnly && !def.FeaturedSetIds.Contains(a.SetId) && !def.FeaturedBrandIds.Contains(a.BrandId)) return false;
            if (f.Rookie.HasValue && a.IsRookie != f.Rookie.Value) return false;
            if (f.Vintage.HasValue && a.IsVintage != f.Vintage.Value) return false;
            if (f.SerialNumbered.HasValue && a.IsSerialNumbered != f.SerialNumbered.Value) return false;
            if (f.Category.HasValue && a.Category != f.Category.Value) return false;
            return true;
        }

        /// <summary>Records newly completed challenges and the show completion. Idempotent.</summary>
        public void Evaluate(EventOccurrence occ)
        {
            var p = Participation(occ.OccurrenceId);
            if (p == null || !p.Visited || p.Completed) return;

            var newly = occ.Definition.Challenges
                .Where(c => !p.CompletedChallenges.ContainsKey(c.Id) && CountProgress(occ, c) >= c.Count)
                .ToList();
            bool allDone = occ.Definition.Challenges.Count > 0 &&
                           occ.Definition.Challenges.All(c => p.CompletedChallenges.ContainsKey(c.Id) || newly.Contains(c));
            if (newly.Count == 0 && !allDone) return;

            using (var tx = _ctx.Begin("show-progress:" + occ.OccurrenceId + ":" + _ctx.State.Ids.NextSequence))
            {
                foreach (var c in newly)
                {
                    p.CompletedChallenges[c.Id] = new ChallengeProgress { ChallengeId = c.Id, CompletedAt = _ctx.Now, PointsAwarded = c.Points };
                    p.PointsEarned += c.Points;
                    var challenge = c;
                    _ctx.RecordAction(tx, GameActionType.CardShowChallengeCompleted, a => a.RefId = occ.OccurrenceId + "/" + challenge.Id);
                    _ctx.Notify(NotificationKind.CardShow, occ.Definition.Name + ": challenge complete",
                        c.Description + " (+" + c.Points + " points)", occ.OccurrenceId, "challenge:" + occ.OccurrenceId + ":" + c.Id);
                }
                if (allDone)
                {
                    p.Completed = true;
                    p.CompletedAt = _ctx.Now;
                    _ctx.RecordAction(tx, GameActionType.CardShowCompleted, a => a.RefId = occ.OccurrenceId);
                }
                tx.Commit();
            }

            if (allDone && !occ.Definition.CompletionReward.IsEmpty)
                _rewards.Grant("show:" + occ.OccurrenceId + ":complete", occ.Definition.CompletionReward,
                    occ.Definition.Name + " completion", eventId: occ.Definition.Id);
        }

        // ================================================================ rewards

        public OpResult<RewardGrantRecord> Redeem(string occurrenceId, string rewardId)
        {
            var occ = Occurrence(occurrenceId);
            if (occ == null) return OpResult<RewardGrantRecord>.Fail(OpError.NotFound, "Card show not found.");
            var def = occ.Definition.Rewards.FirstOrDefault(r => r.Id == rewardId);
            if (def == null) return OpResult<RewardGrantRecord>.Fail(OpError.NotFound, "Reward not found.");
            var p = Participation(occurrenceId);
            if (p == null || !p.Visited) return OpResult<RewardGrantRecord>.Fail(OpError.NotEligible, "Visit the show to earn points first.");
            if (_ctx.Now >= occ.EndsAt + RedemptionGraceSeconds) return OpResult<RewardGrantRecord>.Fail(OpError.NotAvailable, "The reward counter has closed.");
            if (p.RedeemedRewards.Contains(rewardId)) return OpResult<RewardGrantRecord>.Fail(OpError.AlreadyClaimed, "Already redeemed.");
            if (p.PointsAvailable < def.CostPoints) return OpResult<RewardGrantRecord>.Fail(OpError.InsufficientPoints, "Not enough event points.");

            var grant = _rewards.Grant("show:" + occurrenceId + ":" + rewardId, def.Reward, occ.Definition.Name + " - " + def.Name, eventId: occ.Definition.Id);
            if (!grant.Ok) return grant;
            p.RedeemedRewards.Add(rewardId);
            p.PointsSpent += def.CostPoints;
            var record = State.History.FirstOrDefault(h => h.OccurrenceId == occurrenceId);
            if (record != null) record.RewardsRedeemed = p.RedeemedRewards.OrderBy(r => r, StringComparer.Ordinal).ToList();
            return grant;
        }

        // ================================================================ lifecycle

        /// <summary>Writes the event history record for shows that have ended. Idempotent.</summary>
        public void Tick()
        {
            foreach (var p in State.Occurrences.Values.Where(x => !x.Finalized && x.EndsAt <= _ctx.Now).OrderBy(x => x.EndsAt).ToList())
            {
                var occ = Occurrence(p.OccurrenceId);
                if (occ == null) continue;
                p.Finalized = true;
                State.History.Add(new CardShowRecord
                {
                    OccurrenceId = p.OccurrenceId,
                    EventId = p.EventId,
                    Name = occ.Definition.Name,
                    StartsAt = p.StartsAt,
                    EndsAt = p.EndsAt,
                    ChallengesCompleted = p.CompletedChallenges.Count,
                    ChallengesTotal = occ.Definition.Challenges.Count,
                    PointsEarned = p.PointsEarned,
                    RewardsRedeemed = p.RedeemedRewards.OrderBy(r => r, StringComparer.Ordinal).ToList(),
                    Completed = p.Completed,
                    PurchasesMade = _ctx.State.Actions.Count(a => a.Type == GameActionType.EventVendorPurchase && a.RefId == p.OccurrenceId),
                });
            }
        }
    }
}
