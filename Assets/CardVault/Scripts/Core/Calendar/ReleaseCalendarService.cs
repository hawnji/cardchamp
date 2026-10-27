using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.Market;
using CardVault.Core.Packs;
using CardVault.Core.State;

namespace CardVault.Core.Calendar
{
    public enum ReleaseStatus
    {
        /// <summary>Not announced yet; not shown to players.</summary>
        Hidden,
        Announced,
        Upcoming,
        Available,
        LimitedStock,
        SoldOut,
        Retired,
        Archived,
    }

    public enum CalendarItemKind { Release, Retirement, CardShow }

    /// <summary>One dated row for the month/list views (releases, retirements, card shows).</summary>
    public sealed class CalendarItem
    {
        public CalendarItemKind Kind;
        public string Id;
        public string Name;
        public long Time;
        public long EndTime;
        public string StatusLabel;
        public string CategoryLabel;
        public CardCategory? Category;
        public string BrandId;
        public string UniverseId;
    }

    public sealed class CalendarFilter
    {
        public CardCategory? Category;
        public string BrandId;
        public string UniverseId;
        public ReleaseCategory? ReleaseCategory;
        public bool WatchlistOnly;

        public static readonly CalendarFilter None = new CalendarFilter();
    }

    public sealed class ReleaseProductView
    {
        public string ProductId;
        public ProductDef Product;
        public int InitialStock;
        public int Remaining;
    }

    /// <summary>
    /// Release calendar. A release's lifecycle state is a pure function of game time,
    /// configuration and remaining allocated stock, so it is deterministic and survives
    /// restarts. Stock is allocated exactly once per release and retirement is processed
    /// exactly once, so clock changes cannot mint product.
    /// </summary>
    public sealed class ReleaseCalendarService
    {
        public const long RecentWindowSeconds = 14 * GameClock.SecondsPerDay;
        public const long RetiringSoonWindowSeconds = 14 * GameClock.SecondsPerDay;
        public const long ReleaseWeekSeconds = 7 * GameClock.SecondsPerDay;

        private readonly GameContext _ctx;
        private readonly SealedInventoryService _sealed;
        private readonly MarketService _market;

        /// <summary>Supplies card show occurrences for the calendar (set by the composition root).</summary>
        public Func<long, long, IEnumerable<CalendarItem>> CardShowItems;

        public ReleaseCalendarService(GameContext ctx, SealedInventoryService sealedInventory, MarketService market)
        {
            _ctx = ctx;
            _sealed = sealedInventory;
            _market = market;
        }

        private CalendarState State => _ctx.State.Calendar;

        public IEnumerable<ReleaseDef> All => _ctx.Content.Pack.Releases;

        public ReleaseDef Release(string id) => _ctx.Content.Releases.TryGetValue(id ?? "", out var r) ? r : null;

        private ReleaseRuntimeState Runtime(string releaseId)
        {
            if (!State.Releases.TryGetValue(releaseId, out var rt))
            {
                rt = new ReleaseRuntimeState { ReleaseId = releaseId };
                State.Releases[releaseId] = rt;
            }
            return rt;
        }

        // ================================================================ lifecycle

        /// <summary>A release can only go on sale if every product has a real card pool.</summary>
        public bool HasValidCardPool(ReleaseDef release)
        {
            if (_ctx.Content.CardsInSet(release.SetId).Count == 0) return false;
            foreach (var p in release.Products)
            {
                var product = _ctx.Content.Products[p.ProductId];
                var leaf = product;
                for (int depth = 0; leaf.IsContainer && depth < 4; depth++) leaf = _ctx.Content.Products[leaf.ContainsProductId];
                if (leaf.IsContainer || leaf.Slots.Count == 0) return false;
                if (_ctx.Content.CardsInSet(leaf.SetId).Count == 0) return false;
            }
            return true;
        }

        private bool HasConfiguredStock(ReleaseDef release) => release.Products.Any(p => p.InitialStock > 0);

        public ReleaseStatus StatusOf(ReleaseDef release) => StatusAt(release, _ctx.Now);

        public ReleaseStatus StatusAt(ReleaseDef release, long time)
        {
            if (time < release.AnnounceAt) return ReleaseStatus.Hidden;
            if (time < release.ReleaseAt)
                return release.ReleaseAt - time <= release.UpcomingWindowSeconds ? ReleaseStatus.Upcoming : ReleaseStatus.Announced;
            if (release.RetireAt > 0 && time >= release.RetireAt)
                return release.ArchiveAt > 0 && time >= release.ArchiveAt ? ReleaseStatus.Archived : ReleaseStatus.Retired;

            // Informational entries (e.g. a card show weekend) have no products to sell.
            if (release.Products.Count == 0) return release.EventIds.Count > 0 ? ReleaseStatus.Available : ReleaseStatus.Upcoming;

            // A date alone never makes a product available: it needs stock and a valid card pool.
            if (!HasConfiguredStock(release) || !HasValidCardPool(release)) return ReleaseStatus.Upcoming;

            int initial = release.Products.Sum(p => p.InitialStock);
            int remaining = RemainingTotal(release, time);
            if (remaining <= 0) return ReleaseStatus.SoldOut;
            if (remaining <= Math.Ceiling(initial * release.LimitedStockFraction)) return ReleaseStatus.LimitedStock;
            return ReleaseStatus.Available;
        }

        public static bool IsPurchasable(ReleaseStatus s) => s == ReleaseStatus.Available || s == ReleaseStatus.LimitedStock;

        public int Remaining(ReleaseDef release, string productId) => RemainingAt(release, productId, _ctx.Now);

        /// <summary>Remaining units; before allocation this is the configured stock while on sale.</summary>
        private int RemainingAt(ReleaseDef release, string productId, long time)
        {
            var cfg = release.Products.FirstOrDefault(p => p.ProductId == productId);
            if (cfg == null) return 0;
            var rt = State.Releases.TryGetValue(release.Id, out var r) ? r : null;
            if (rt == null || !rt.StockAllocated) return time >= release.ReleaseAt && (release.RetireAt == 0 || time < release.RetireAt) ? cfg.InitialStock : 0;
            return rt.RemainingStock.TryGetValue(productId, out var n) ? n : 0;
        }

        private int RemainingTotal(ReleaseDef release, long time) => release.Products.Sum(p => RemainingAt(release, p.ProductId, time));

        public IReadOnlyList<ReleaseProductView> Products(ReleaseDef release)
            => release.Products.Select(p => new ReleaseProductView
            {
                ProductId = p.ProductId,
                Product = _ctx.Content.Products[p.ProductId],
                InitialStock = p.InitialStock,
                Remaining = Remaining(release, p.ProductId),
            }).ToList();

        /// <summary>Allocates a release's stock once. Never re-runs, so stock cannot be duplicated.</summary>
        internal void EnsureAllocated(ReleaseDef release)
        {
            var rt = Runtime(release.Id);
            if (rt.StockAllocated) return;
            rt.StockAllocated = true;
            foreach (var p in release.Products) rt.RemainingStock[p.ProductId] = p.InitialStock;
        }

        internal bool TryConsume(ReleaseDef release, string productId, int quantity)
        {
            EnsureAllocated(release);
            var rt = Runtime(release.Id);
            if (!rt.RemainingStock.TryGetValue(productId, out var n) || n < quantity) return false;
            rt.RemainingStock[productId] = n - quantity;
            return true;
        }

        /// <summary>Runs lifecycle transitions that have side effects. Idempotent.</summary>
        public void Tick()
        {
            foreach (var release in All)
            {
                var status = StatusOf(release);
                if (_ctx.Now >= release.ReleaseAt && release.Products.Count > 0) EnsureAllocated(release);
                if ((status == ReleaseStatus.Retired || status == ReleaseStatus.Archived) && !Runtime(release.Id).RetirementProcessed)
                    ProcessRetirement(release);
                IssueReminders(release, status);
            }
        }

        /// <summary>
        /// Moves the configured share of unsold stock to the vintage market and zeroes the
        /// rest. Runs once per release.
        /// </summary>
        private void ProcessRetirement(ReleaseDef release)
        {
            EnsureAllocated(release);
            var rt = Runtime(release.Id);
            using (var tx = _ctx.Begin("retire:" + release.Id))
            {
                var dealer = _ctx.Content.Pack.Npcs.FirstOrDefault(n => n.DealsVintage) ?? _ctx.Content.Pack.Npcs.First();
                foreach (var p in release.Products)
                {
                    int left = rt.RemainingStock.TryGetValue(p.ProductId, out var n) ? n : 0;
                    int toVintage = Math.Min(left, p.VintageAllocationOnRetire);
                    for (int i = 0; i < toVintage; i++)
                    {
                        var item = _sealed.Create(tx, p.ProductId, Owners.Npc(dealer.Id), "retired:" + release.Id);
                        _market.PostRetiredStock(tx, dealer, item, release.VintagePriceMultiplier);
                    }
                    rt.RemainingStock[p.ProductId] = 0;
                }
                rt.RetirementProcessed = true;
                rt.RetiredAt = _ctx.Now;
                tx.Commit();
            }
        }

        // ================================================================ watchlist & reminders

        public bool IsWatched(string releaseId) => State.Watchlist.Contains(releaseId);

        public OpResult Watch(string releaseId)
        {
            var release = Release(releaseId);
            if (release == null || StatusOf(release) == ReleaseStatus.Hidden) return OpResult.Fail(OpError.NotFound, "Release not found.");
            if (!State.Watchlist.Add(releaseId)) return OpResult.Fail(OpError.InvalidState, "Already on your watchlist.");
            return OpResult.Success();
        }

        public OpResult Unwatch(string releaseId)
            => State.Watchlist.Remove(releaseId) ? OpResult.Success() : OpResult.Fail(OpError.NotFound, "Not on your watchlist.");

        public IEnumerable<ReleaseDef> Watchlist => All.Where(r => State.Watchlist.Contains(r.Id));

        private void IssueReminders(ReleaseDef release, ReleaseStatus status)
        {
            if (!State.Watchlist.Contains(release.Id)) return;
            string key = null, title = null, body = null;
            if (status == ReleaseStatus.Upcoming)
            {
                key = "upcoming";
                title = release.Name + " is coming soon";
                body = "Releases on " + FormatDate(release.ReleaseAt) + ".";
            }
            else if (IsPurchasable(status))
            {
                bool retiringSoon = release.RetireAt > 0 && release.RetireAt - _ctx.Now <= RetiringSoonWindowSeconds;
                key = retiringSoon ? "retiring" : "available";
                title = retiringSoon ? release.Name + " retires soon" : release.Name + " is out now";
                body = retiringSoon ? "Last day on shelves: " + FormatDate(release.RetireAt) + "." : "Find it in the Shop while stock lasts.";
            }
            else if (status == ReleaseStatus.Retired)
            {
                key = "retired";
                title = release.Name + " has retired";
                body = "Leftover sealed stock has moved to the vintage market.";
            }
            if (key == null) return;
            string dedup = "release:" + release.Id + ":" + key;
            if (!State.RemindersIssued.Add(dedup)) return;
            _ctx.Notify(NotificationKind.ReleaseReminder, title, body, release.Id, dedup);
        }

        // ================================================================ release milestones

        /// <summary>Records a purchase made during release week. Counted once per release.</summary>
        internal void RecordPurchase(Transaction tx, ReleaseDef release)
        {
            if (_ctx.Now < release.ReleaseAt || _ctx.Now >= release.ReleaseAt + ReleaseWeekSeconds) return;
            if (!State.MilestonesRecorded.Add("release-week:" + release.Id)) return;
            var set = _ctx.Content.Sets[release.SetId];
            _ctx.RecordAction(tx, GameActionType.ReleaseMilestone, a =>
            {
                a.RefId = release.Id;
                a.SetId = release.SetId;
                a.BrandId = release.BrandId;
                a.Category = set.Category;
            });
        }

        // ================================================================ views

        public bool Matches(ReleaseDef release, CalendarFilter filter)
        {
            if (filter == null) return true;
            var set = _ctx.Content.Sets[release.SetId];
            if (filter.Category.HasValue && set.Category != filter.Category.Value) return false;
            if (filter.BrandId != null && release.BrandId != filter.BrandId) return false;
            if (filter.UniverseId != null && set.UniverseId != filter.UniverseId) return false;
            if (filter.ReleaseCategory.HasValue && release.Category != filter.ReleaseCategory.Value) return false;
            if (filter.WatchlistOnly && !IsWatched(release.Id)) return false;
            return true;
        }

        private IEnumerable<ReleaseDef> Visible(CalendarFilter filter)
            => All.Where(r => StatusOf(r) != ReleaseStatus.Hidden && Matches(r, filter));

        public IReadOnlyList<ReleaseDef> Upcoming(CalendarFilter filter = null)
            => Visible(filter).Where(r => StatusOf(r) == ReleaseStatus.Announced || StatusOf(r) == ReleaseStatus.Upcoming)
                .OrderBy(r => r.ReleaseAt).ToList();

        public IReadOnlyList<ReleaseDef> Current(CalendarFilter filter = null)
            => Visible(filter).Where(r => r.Products.Count > 0 && (IsPurchasable(StatusOf(r)) || StatusOf(r) == ReleaseStatus.SoldOut))
                .OrderByDescending(r => r.ReleaseAt).ToList();

        public IReadOnlyList<ReleaseDef> RecentlyReleased(CalendarFilter filter = null)
            => Visible(filter).Where(r => r.ReleaseAt <= _ctx.Now && _ctx.Now - r.ReleaseAt <= RecentWindowSeconds)
                .OrderByDescending(r => r.ReleaseAt).ToList();

        public IReadOnlyList<ReleaseDef> Retiring(CalendarFilter filter = null)
            => Visible(filter).Where(r => r.RetireAt > 0 && r.RetireAt > _ctx.Now && r.RetireAt - _ctx.Now <= RetiringSoonWindowSeconds && r.ReleaseAt <= _ctx.Now)
                .OrderBy(r => r.RetireAt).ToList();

        public IReadOnlyList<ReleaseDef> Seasonal(CalendarFilter filter = null)
            => Visible(filter).Where(r => r.Category == ReleaseCategory.LimitedSeasonal || r.Category == ReleaseCategory.Anniversary)
                .OrderBy(r => r.ReleaseAt).ToList();

        public IReadOnlyList<ReleaseDef> VintageEvents(CalendarFilter filter = null)
            => Visible(filter).Where(r => r.Category == ReleaseCategory.VintageRestock || r.Category == ReleaseCategory.Anniversary ||
                                          _ctx.Content.Sets[r.SetId].IsVintage)
                .OrderBy(r => r.ReleaseAt).ToList();

        public IReadOnlyList<ReleaseDef> Archive(CalendarFilter filter = null)
            => Visible(filter).Where(r => StatusOf(r) == ReleaseStatus.Retired || StatusOf(r) == ReleaseStatus.Archived)
                .OrderByDescending(r => r.RetireAt).ToList();

        public IReadOnlyList<CalendarItem> UpcomingCardShows(int days = 30)
            => CardShowItems == null
                ? (IReadOnlyList<CalendarItem>)Array.Empty<CalendarItem>()
                : CardShowItems(_ctx.Now, _ctx.Now + days * GameClock.SecondsPerDay).Where(i => i.EndTime > _ctx.Now).OrderBy(i => i.Time).ToList();

        /// <summary>Dated items for one calendar month (game calendar), sorted by date.</summary>
        public IReadOnlyList<CalendarItem> Month(int year, int month, CalendarFilter filter = null)
        {
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            long from = _ctx.Clock.FromCalendarDate(start);
            long to = _ctx.Clock.FromCalendarDate(start.AddMonths(1));
            var items = new List<CalendarItem>();
            foreach (var r in Visible(filter))
            {
                var set = _ctx.Content.Sets[r.SetId];
                if (r.ReleaseAt >= from && r.ReleaseAt < to)
                    items.Add(Item(r, set, CalendarItemKind.Release, r.ReleaseAt));
                if (r.RetireAt > 0 && r.RetireAt >= from && r.RetireAt < to)
                    items.Add(Item(r, set, CalendarItemKind.Retirement, r.RetireAt));
            }
            if (CardShowItems != null && (filter == null || (filter.BrandId == null && filter.UniverseId == null && !filter.WatchlistOnly)))
                items.AddRange(CardShowItems(from, to).Where(i => filter?.Category == null || i.Category == null || i.Category == filter.Category));
            return items.OrderBy(i => i.Time).ThenBy(i => i.Name, StringComparer.Ordinal).ToList();
        }

        private CalendarItem Item(ReleaseDef r, SetDef set, CalendarItemKind kind, long time) => new CalendarItem
        {
            Kind = kind,
            Id = r.Id,
            Name = kind == CalendarItemKind.Retirement ? r.Name + " retires" : r.Name,
            Time = time,
            EndTime = r.RetireAt,
            StatusLabel = Label(StatusOf(r)),
            CategoryLabel = Label(r.Category),
            Category = set.Category,
            BrandId = r.BrandId,
            UniverseId = set.UniverseId,
        };

        public string FormatDate(long gameSeconds) => _ctx.Clock.ToCalendarDate(gameSeconds).ToString("MMM d, yyyy");

        public static string Label(ReleaseStatus s)
        {
            switch (s)
            {
                case ReleaseStatus.LimitedStock: return "Limited Stock";
                case ReleaseStatus.SoldOut: return "Sold Out";
                default: return s.ToString();
            }
        }

        public static string Label(ReleaseCategory c)
        {
            switch (c)
            {
                case ReleaseCategory.SportsFlagship: return "Sports Flagship";
                case ReleaseCategory.SportsRookie: return "Rookie Release";
                case ReleaseCategory.SportsPremium: return "Premium Sports";
                case ReleaseCategory.TcgExpansion: return "TCG Expansion";
                case ReleaseCategory.AlternateArtCollection: return "Alternate Art";
                case ReleaseCategory.Anniversary: return "Anniversary";
                case ReleaseCategory.VintageRestock: return "Vintage Restock";
                case ReleaseCategory.CardShowEvent: return "Card Show";
                case ReleaseCategory.LimitedSeasonal: return "Limited Seasonal";
                default: return c.ToString();
            }
        }
    }
}
