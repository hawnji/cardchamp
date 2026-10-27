using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Economy;
using CardVault.Core.Game;
using CardVault.Core.Packs;
using CardVault.Core.State;

namespace CardVault.Core.Calendar
{
    public sealed class ShopOffer
    {
        public ReleaseDef Release;
        public ProductDef Product;
        public ReleaseStatus Status;
        public int Remaining;
        public long Price;
        public long RetireAt;
    }

    /// <summary>
    /// The in-game shop sells new sealed product only from releases that are currently on
    /// sale, drawing down each release's finite allocation.
    /// </summary>
    public sealed class ShopService
    {
        private readonly GameContext _ctx;
        private readonly ReleaseCalendarService _calendar;
        private readonly SealedInventoryService _sealed;
        private readonly EconomyService _economy;

        public ShopService(GameContext ctx, ReleaseCalendarService calendar, SealedInventoryService sealedInventory, EconomyService economy)
        {
            _ctx = ctx;
            _calendar = calendar;
            _sealed = sealedInventory;
            _economy = economy;
        }

        public IReadOnlyList<ShopOffer> Offers(bool includeSoldOut = true)
        {
            var offers = new List<ShopOffer>();
            foreach (var release in _calendar.All)
            {
                var status = _calendar.StatusOf(release);
                if (!ReleaseCalendarService.IsPurchasable(status) && !(includeSoldOut && status == ReleaseStatus.SoldOut)) continue;
                foreach (var p in _calendar.Products(release))
                {
                    if (!includeSoldOut && p.Remaining <= 0) continue;
                    offers.Add(new ShopOffer
                    {
                        Release = release,
                        Product = p.Product,
                        Status = p.Remaining <= 0 ? ReleaseStatus.SoldOut : status,
                        Remaining = p.Remaining,
                        Price = p.Product.PriceCredits,
                        RetireAt = release.RetireAt,
                    });
                }
            }
            return offers.OrderByDescending(o => o.Release.ReleaseAt).ThenBy(o => o.Product.PriceCredits).ToList();
        }

        public IReadOnlyList<ShopOffer> OffersFor(string releaseId) => Offers().Where(o => o.Release.Id == releaseId).ToList();

        public OpResult<SealedProductInstance> Buy(string releaseId, string productId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult<SealedProductInstance>.Fail(OpError.AlreadyProcessed, "Purchase already completed.");
            var release = _calendar.Release(releaseId);
            if (release == null) return OpResult<SealedProductInstance>.Fail(OpError.NotFound, "Release not found.");
            var cfg = release.Products.FirstOrDefault(p => p.ProductId == productId);
            if (cfg == null) return OpResult<SealedProductInstance>.Fail(OpError.NotFound, "This product is not part of the release.");

            var status = _calendar.StatusOf(release);
            if (status == ReleaseStatus.Retired || status == ReleaseStatus.Archived)
                return OpResult<SealedProductInstance>.Fail(OpError.NotAvailable, "This product has retired. Check the vintage market.");
            if (!ReleaseCalendarService.IsPurchasable(status))
                return OpResult<SealedProductInstance>.Fail(OpError.NotAvailable, status == ReleaseStatus.SoldOut ? "Sold out." : "Not on sale yet.");
            if (_calendar.Remaining(release, productId) <= 0) return OpResult<SealedProductInstance>.Fail(OpError.OutOfStock, "Sold out.");

            var product = _ctx.Content.Products[productId];
            var afford = _economy.CheckAfford(product.PriceCredits);
            if (!afford.Ok) return OpResult<SealedProductInstance>.Fail(afford.Error, afford.Message);

            using (var tx = _ctx.Begin(transactionId))
            {
                if (!_calendar.TryConsume(release, productId, 1))
                    return OpResult<SealedProductInstance>.Fail(OpError.OutOfStock, "Sold out.");
                _economy.Apply(tx, LedgerKind.ProductPurchase, -product.PriceCredits, 0, release.Id, "Bought " + product.Name);
                var item = _sealed.Create(tx, productId, Owners.Player, "shop:" + release.Id);
                var set = _ctx.Content.Sets[product.SetId];
                _ctx.RecordAction(tx, GameActionType.ProductPurchased, a =>
                {
                    a.ProductId = productId;
                    a.SetId = set.Id;
                    a.BrandId = set.BrandId;
                    a.IsVintage = product.IsVintage || set.IsVintage;
                    a.Category = set.Category;
                    a.RefId = release.Id;
                });
                _calendar.RecordPurchase(tx, release);
                tx.Commit();
                return OpResult<SealedProductInstance>.Success(item);
            }
        }
    }
}
