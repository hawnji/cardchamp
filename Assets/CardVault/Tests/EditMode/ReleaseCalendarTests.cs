using System.Linq;
using CardVault.Core.Calendar;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase E: release calendar, lifecycle states and the shop.</summary>
    public class ReleaseCalendarTests
    {
        private const long Day = GameClock.SecondsPerDay;

        [Test]
        public void ReleaseStatesFollowConfiguredDates()
        {
            var game = TestGame.Create();
            var r = game.Calendar.Release("rel-nova"); // announce 46, release 62, retire 200, archive 260
            Assert.That(game.Calendar.StatusAt(r, 40 * Day), Is.EqualTo(ReleaseStatus.Hidden));
            Assert.That(game.Calendar.StatusAt(r, 47 * Day), Is.EqualTo(ReleaseStatus.Announced));
            Assert.That(game.Calendar.StatusAt(r, 58 * Day), Is.EqualTo(ReleaseStatus.Upcoming));
            Assert.That(game.Calendar.StatusAt(r, 62 * Day), Is.EqualTo(ReleaseStatus.Available));
            Assert.That(game.Calendar.StatusAt(r, 200 * Day), Is.EqualTo(ReleaseStatus.Retired));
            Assert.That(game.Calendar.StatusAt(r, 260 * Day), Is.EqualTo(ReleaseStatus.Archived));
        }

        [Test]
        public void ReleaseWithoutStockOrCardPoolNeverBecomesAvailable()
        {
            var pack = Json.Deserialize<ContentPack>(TestContent.Json);
            pack.Releases.Single(r => r.Id == "rel-nova").Products.ForEach(p => p.InitialStock = 0);
            pack.Products.Single(p => p.Id == "aeth-booster").Slots.Clear();
            var db = new ContentDatabase(pack);
            var game = CardVaultGame.NewGame(db, 1, TestGame.StartTime, "t");

            Assert.That(game.Calendar.StatusAt(db.Releases["rel-nova"], 70 * Day), Is.EqualTo(ReleaseStatus.Upcoming), "No stock");
            Assert.That(game.Calendar.HasValidCardPool(db.Releases["rel-aeth"]), Is.False);
            Assert.That(game.Calendar.StatusOf(db.Releases["rel-aeth"]), Is.EqualTo(ReleaseStatus.Upcoming), "No card pool");
            Assert.That(game.Shop.Buy("rel-aeth", "aeth-booster").Ok, Is.False);
        }

        [Test]
        public void BuyingDrawsDownFiniteStockUntilSoldOut()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 1000000);
            game.AdvanceGameTime(2 * Day); // day 47: Rising Rookies is on sale (60 packs)
            var release = game.Calendar.Release("rel-fhr26");
            Assert.That(game.Calendar.StatusOf(release), Is.EqualTo(ReleaseStatus.Available));

            for (int i = 0; i < 48; i++) Assert.That(game.Shop.Buy(release.Id, "fhr26-pack").Ok);
            Assert.That(game.Calendar.StatusOf(release), Is.EqualTo(ReleaseStatus.LimitedStock));
            for (int i = 0; i < 12; i++) Assert.That(game.Shop.Buy(release.Id, "fhr26-pack").Ok);

            Assert.That(game.Calendar.StatusOf(release), Is.EqualTo(ReleaseStatus.SoldOut));
            Assert.That(game.Shop.Buy(release.Id, "fhr26-pack").Ok, Is.False);
            Assert.That(game.Sealed.PlayerUnopened.Count(s => s.ProductId == "fhr26-pack"), Is.EqualTo(60));
        }

        [Test]
        public void RetiredProductsAreNotSoldAsNewAndMoveToVintageMarket()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 1000000);
            game.AdvanceGameTime(2 * Day);
            for (int i = 0; i < 5; i++) game.Shop.Buy("rel-fhr26", "fhr26-pack");

            game.AdvanceGameTime(14 * Day); // day 61: rel-fhr26 retired on day 60
            var release = game.Calendar.Release("rel-fhr26");
            Assert.That(game.Calendar.StatusOf(release), Is.EqualTo(ReleaseStatus.Retired));
            Assert.That(game.Shop.Buy(release.Id, "fhr26-pack").Error, Is.EqualTo(OpError.NotAvailable));
            Assert.That(game.Shop.Offers().Any(o => o.Release.Id == release.Id), Is.False);
            Assert.That(game.Calendar.Remaining(release, "fhr26-pack"), Is.EqualTo(0));

            var vintage = VintageListingsFor(game, "fhr26-pack");
            Assert.That(vintage, Is.EqualTo(10), "Configured vintage allocation moves to the vintage market");
            Assert.That(game.Calendar.Archive().Select(r => r.Id), Does.Contain(release.Id));

            game.AdvanceGameTime(Day);
            game.Tick(TestGame.StartTime.AddDays(-30));
            game.Tick(TestGame.StartTime.AddDays(30));
            Assert.That(VintageListingsFor(game, "fhr26-pack"), Is.EqualTo(10), "Retirement is processed once");
        }

        [Test]
        public void ReleaseRetiredBeforeTheSaveStartedIsOnTheVintageMarket()
        {
            var game = TestGame.Create();
            Assert.That(game.Calendar.StatusOf(game.Calendar.Release("rel-hl94")), Is.EqualTo(ReleaseStatus.Archived));
            Assert.That(VintageListingsFor(game, "hl94-wax"), Is.EqualTo(6));
            Assert.That(VintageListingsFor(game, "hl94-box"), Is.EqualTo(1));

            TestGame.GiveCredits(game, 100000);
            var listing = game.Market.VintageListings.First(l => l.SealedInstanceId != null);
            Assert.That(game.Market.Buy(listing.ListingId).Ok);
            Assert.That(game.Sealed.Get(listing.SealedInstanceId).OwnerId, Is.EqualTo(Owners.Player));
        }

        [Test]
        public void WatchlistPersistsAndRemindersAreIssuedOnce()
        {
            var game = TestGame.Create();
            Assert.That(game.Calendar.Watch("rel-nova").Ok, Is.False, "Not announced until day 46");
            game.AdvanceGameTime(Day);
            Assert.That(game.Calendar.Watch("rel-nova").Ok);
            Assert.That(game.Calendar.Watch("rel-nova").Ok, Is.False);
            Assert.That(game.Calendar.Watch("rel-does-not-exist").Ok, Is.False);
            Assert.That(game.State.Notifications.Count(n => n.RefId == "rel-nova"), Is.EqualTo(0), "Announced is not a reminder");

            game.AdvanceGameTime(11 * Day); // day 57: upcoming
            game.AdvanceGameTime(Day);
            Assert.That(game.State.Notifications.Count(n => n.RefId == "rel-nova"), Is.EqualTo(1));

            var reloaded = TestGame.SaveAndReload(game);
            Assert.That(reloaded.Calendar.IsWatched("rel-nova"));
            reloaded.AdvanceGameTime(6 * Day); // day 64: available
            reloaded.AdvanceGameTime(Day);
            Assert.That(reloaded.State.Notifications.Count(n => n.RefId == "rel-nova"), Is.EqualTo(2));
            Assert.That(reloaded.State.Notifications.Last(n => n.RefId == "rel-nova").Title, Does.Contain("out now"));
        }

        [Test]
        public void ClockChangesDoNotDuplicateStockOrRewards()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            game.AdvanceGameTime(2 * Day);
            game.Shop.Buy("rel-fhr26", "fhr26-pack");
            int remaining = game.Calendar.Remaining(game.Calendar.Release("rel-fhr26"), "fhr26-pack");
            int sealedCount = game.State.SealedProducts.Count;
            long credits = game.Economy.Credits;

            game.Tick(TestGame.StartTime.AddYears(-1));
            game.Tick(TestGame.StartTime);
            game.Tick(TestGame.StartTime.AddMinutes(1));

            Assert.That(game.Calendar.Remaining(game.Calendar.Release("rel-fhr26"), "fhr26-pack"), Is.EqualTo(remaining));
            Assert.That(game.State.SealedProducts.Count, Is.EqualTo(sealedCount));
            Assert.That(game.Economy.Credits, Is.EqualTo(credits));
        }

        [Test]
        public void SchedulingIsDeterministic()
        {
            var a = TestGame.Create(5);
            var b = TestGame.Create(6);
            a.AdvanceGameTime(30 * Day);
            b.AdvanceGameTime(30 * Day);
            Assert.That(a.Calendar.All.Select(r => a.Calendar.StatusOf(r)), Is.EqualTo(b.Calendar.All.Select(r => b.Calendar.StatusOf(r))));
        }

        [Test]
        public void ReleaseWeekPurchaseRecordsOneMilestone()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            game.AdvanceGameTime(2 * Day); // rel-fhr26 released day 46
            game.Shop.Buy("rel-fhr26", "fhr26-pack");
            game.Shop.Buy("rel-fhr26", "fhr26-pack");
            Assert.That(game.State.Actions.Count(a => a.Type == GameActionType.ReleaseMilestone && a.RefId == "rel-fhr26"), Is.EqualTo(1));
        }

        [Test]
        public void ListViewsAndFiltersWork()
        {
            var game = TestGame.Create();
            var sportsOnly = new CalendarFilter { Category = CardCategory.Sports };
            var tcgOnly = new CalendarFilter { Category = CardCategory.Tcg };

            Assert.That(game.Calendar.Upcoming(sportsOnly).Select(r => r.Id), Does.Contain("rel-fhr26"));
            Assert.That(game.Calendar.Upcoming(tcgOnly).All(r => game.Content.Sets[r.SetId].Category == CardCategory.Tcg));
            Assert.That(game.Calendar.Current().Select(r => r.Id), Does.Contain("rel-fh26"));
            Assert.That(game.Calendar.Current(new CalendarFilter { BrandId = "mythforge" }).All(r => r.BrandId == "mythforge"));
            Assert.That(game.Calendar.Current(new CalendarFilter { UniverseId = "aetherfall" }).Select(r => r.Id), Does.Contain("rel-aeth"));
            Assert.That(game.Calendar.Upcoming().Any(r => r.Id == "rel-summer"), Is.False, "Not announced yet on day 45");

            var feb = game.Calendar.Month(2026, 2);
            Assert.That(feb.Any(i => i.Id == "rel-fhr26" && i.Kind == CalendarItemKind.Release)); // day 46 = Feb 16
        }

        [Test]
        public void ShopOnlyOffersPurchasableReleases()
        {
            var game = TestGame.Create();
            foreach (var offer in game.Shop.Offers(includeSoldOut: false))
                Assert.That(ReleaseCalendarService.IsPurchasable(game.Calendar.StatusOf(offer.Release)));
            Assert.That(game.Shop.Buy("rel-nova", "nova-booster").Error, Is.EqualTo(OpError.NotAvailable));
        }

        private static int VintageListingsFor(CardVaultGame game, string productId)
            => game.Market.VintageListings.Count(l => l.SealedInstanceId != null && game.Sealed.Get(l.SealedInstanceId).ProductId == productId);
    }
}
