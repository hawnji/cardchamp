using System.Linq;
using CardVault.Core.CardShows;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase F: card show events.</summary>
    public class CardShowTests
    {
        private const long Day = GameClock.SecondsPerDay;
        private const string Expo = "cs-sports-expo@0"; // days 45-48

        private static void GivePacks(CardVaultGame game, string productId, int count)
        {
            using (var tx = game.Context.Begin())
            {
                for (int i = 0; i < count; i++) game.Sealed.Create(tx, productId, Owners.Player, "test");
                tx.Commit();
            }
        }

        private static void OpenAll(CardVaultGame game, string productId)
        {
            foreach (var s in game.Sealed.PlayerUnopened.Where(x => x.ProductId == productId).ToList()) game.Packs.Open(s.InstanceId);
        }

        /// <summary>Completes every Sports Card Expo challenge with real actions.</summary>
        private static void CompleteExpo(CardVaultGame game)
        {
            game.CardShows.Visit(Expo);
            GivePacks(game, "fh26-pack", 5);
            OpenAll(game, "fh26-pack");
            foreach (var t in game.Content.CardsInSet("fh26").Where(t => t.IsRookie).Take(3)) TestGame.GiveCard(game, t.Id);
            TestGame.GiveCard(game, "apex26-001");
            var raw = TestGame.GiveCard(game, "fh26-015");
            game.Grading.Submit(raw.InstanceId);
            game.AdvanceGameTime(2 * Day); // day 47: graded, trade offers posted
            var trade = game.Market.OpenTrades.First(t => t.RequestedCardIds.All(id => game.Inventory.Get(id).Status == CardStatus.Available));
            Assert.That(game.Market.AcceptTrade(trade.TradeId).Ok);
        }

        [Test]
        public void EventAvailabilityFollowsTheSchedule()
        {
            var game = TestGame.Create();
            Assert.That(game.CardShows.Active().Select(o => o.OccurrenceId), Does.Contain(Expo));
            Assert.That(game.CardShows.Active().Select(o => o.Definition.Id), Does.Contain("cs-daily-meetup"));
            Assert.That(game.CardShows.Upcoming(30).Select(o => o.OccurrenceId), Does.Contain("cs-tcg-convention@0"));

            game.AdvanceGameTime(3 * Day);
            Assert.That(game.CardShows.StatusOf(game.CardShows.Occurrence(Expo)), Is.EqualTo(OccurrenceStatus.Ended));
            Assert.That(game.CardShows.Upcoming().Select(o => o.OccurrenceId), Does.Contain("cs-sports-expo@1"));
            Assert.That(game.CardShows.Visit(Expo).Error, Is.EqualTo(OpError.NotAvailable));
        }

        [Test]
        public void VisitingShowsVendorsButGrantsNoProgress()
        {
            var game = TestGame.Create();
            var visit = game.CardShows.Visit(Expo);
            Assert.That(visit.Ok);
            Assert.That(visit.Value.PointsEarned, Is.EqualTo(0));
            Assert.That(game.CardShows.Challenges(Expo).All(c => !c.Completed && c.Progress == 0));

            var stock = game.CardShows.VendorStock(Expo, "expo-rosa");
            Assert.That(stock.Single(s => s.Definition.Id == "expo-fh-pack").Remaining, Is.EqualTo(10));
            var vendorCard = game.Inventory.Get(stock.Single(s => s.Definition.Id == "expo-rookie-single").NextCardInstanceId);
            Assert.That(vendorCard.OwnerId, Is.EqualTo(Owners.Npc("rosa-vale")), "Vendor cards are real NPC-owned instances");
        }

        [Test]
        public void ChallengesCountOnlyMatchingCommittedActions()
        {
            var game = TestGame.Create();
            game.CardShows.Visit(Expo);
            GivePacks(game, "aeth-booster", 5);
            OpenAll(game, "aeth-booster");
            Assert.That(game.CardShows.Challenges(Expo).Single(c => c.Definition.Id == "open5").Progress, Is.EqualTo(0), "Wrong set");

            Assert.That(game.Sealed.PlayerUnopened.Count(x => x.ProductId == "fh26-pack"), Is.EqualTo(2), "Starter kit packs");
            GivePacks(game, "fh26-pack", 2);
            OpenAll(game, "fh26-pack");
            var open5 = game.CardShows.Challenges(Expo).Single(c => c.Definition.Id == "open5");
            Assert.That(open5.Progress, Is.EqualTo(4));
            Assert.That(open5.Completed, Is.False);

            GivePacks(game, "fh26-pack", 1);
            OpenAll(game, "fh26-pack");
            Assert.That(game.CardShows.Challenges(Expo).Single(c => c.Definition.Id == "open5").Completed);
            var p = game.CardShows.Participation(Expo);
            Assert.That(p.CompletedChallenges["open5"].PointsAwarded, Is.EqualTo(20));
            Assert.That(p.PointsEarned, Is.EqualTo(p.CompletedChallenges.Values.Sum(c => c.PointsAwarded)));
        }

        [Test]
        public void DistinctRookieChallengeIgnoresDuplicates()
        {
            var game = TestGame.Create();
            game.CardShows.Visit(Expo);
            for (int i = 0; i < 3; i++) TestGame.GiveCard(game, "fh26-003");
            Assert.That(game.CardShows.Challenges(Expo).Single(c => c.Definition.Id == "rookies3").Progress, Is.EqualTo(1));
        }

        [Test]
        public void ActionsAfterTheShowClosesDoNotCount()
        {
            var game = TestGame.Create();
            game.CardShows.Visit(Expo);
            game.AdvanceGameTime(3 * Day);
            GivePacks(game, "fh26-pack", 5);
            OpenAll(game, "fh26-pack");
            Assert.That(game.CardShows.Challenges(Expo).Single(c => c.Definition.Id == "open5").Completed, Is.False);
        }

        [Test]
        public void RewardsAreRedeemedOnceAndCostPoints()
        {
            var game = TestGame.Create();
            CompleteExpo(game);
            var p = game.CardShows.Participation(Expo);
            Assert.That(p.Completed);
            int points = p.PointsAvailable;

            var first = game.CardShows.Redeem(Expo, "expo-frame");
            var second = game.CardShows.Redeem(Expo, "expo-frame");

            Assert.That(first.Ok, first.ToString());
            Assert.That(second.Error, Is.EqualTo(OpError.AlreadyClaimed));
            Assert.That(p.PointsAvailable, Is.EqualTo(points - 30));
            Assert.That(game.Rewards.IsCosmeticUnlocked("frame-expo-2026"));
        }

        [Test]
        public void CompletionRewardIsGrantedExactlyOnce()
        {
            var game = TestGame.Create();
            long creditsBefore = game.Economy.Credits;
            CompleteExpo(game);
            Assert.That(game.Rewards.IsGranted("show:" + Expo + ":complete"));
            Assert.That(game.State.Ledger.Count(l => l.RefId == "show:" + Expo + ":complete"), Is.EqualTo(1));

            game.CardShows.Evaluate(game.CardShows.Occurrence(Expo));
            var reloaded = TestGame.SaveAndReload(game);
            reloaded.CardShows.Evaluate(reloaded.CardShows.Occurrence(Expo));
            Assert.That(reloaded.State.Ledger.Count(l => l.RefId == "show:" + Expo + ":complete"), Is.EqualTo(1));
        }

        [Test]
        public void NotEnoughPointsCannotRedeem()
        {
            var game = TestGame.Create();
            game.CardShows.Visit(Expo);
            Assert.That(game.CardShows.Redeem(Expo, "expo-frame").Error, Is.EqualTo(OpError.InsufficientPoints));
            Assert.That(game.Rewards.IsCosmeticUnlocked("frame-expo-2026"), Is.False);
        }

        [Test]
        public void VendorInventoryIsFiniteAndConsistent()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            game.CardShows.Visit(Expo);
            var before = game.CardShows.VendorStock(Expo, "expo-rosa").Single(s => s.Definition.Id == "expo-rookie-single");
            string cardId = before.NextCardInstanceId;

            Assert.That(game.CardShows.BuyFromVendor(Expo, "expo-rosa", "expo-rookie-single").Ok);
            Assert.That(game.Inventory.Get(cardId).OwnerId, Is.EqualTo(Owners.Player), "The same instance changes hands");
            Assert.That(game.History.For(cardId).Last().Type, Is.EqualTo(HistoryEventType.NpcPurchase));
            Assert.That(game.History.For(cardId).Last().EventId, Is.EqualTo("cs-sports-expo"));

            Assert.That(game.CardShows.BuyFromVendor(Expo, "expo-rosa", "expo-rookie-single").Ok);
            Assert.That(game.CardShows.BuyFromVendor(Expo, "expo-rosa", "expo-rookie-single").Error, Is.EqualTo(OpError.OutOfStock));
            Assert.That(game.CardShows.VendorStock(Expo, "expo-rosa").Single(s => s.Definition.Id == "expo-rookie-single").Remaining, Is.EqualTo(0));

            for (int i = 0; i < 2; i++) Assert.That(game.CardShows.BuyFromVendor(Expo, "expo-rosa", "expo-fh-box").Ok);
            Assert.That(game.CardShows.BuyFromVendor(Expo, "expo-rosa", "expo-fh-box").Error, Is.EqualTo(OpError.OutOfStock));
            Assert.That(game.Sealed.PlayerUnopened.Count(s => s.ProductId == "fh26-box"), Is.EqualTo(2));
        }

        [Test]
        public void VendorPricesApplyFeaturedDiscount()
        {
            var game = TestGame.Create();
            var def = game.Content.CardShows["cs-sports-expo"];
            var fhPack = def.Vendors[0].Stock.Single(s => s.Id == "expo-fh-pack");
            var hlWax = def.Vendors[1].Stock.Single(s => s.Id == "expo-hl-wax");
            Assert.That(game.CardShows.PriceFor(def, fhPack), Is.EqualTo((long)System.Math.Round(fhPack.PriceCredits * 0.9)));
            Assert.That(game.CardShows.PriceFor(def, hlWax), Is.EqualTo(hlWax.PriceCredits));
        }

        [Test]
        public void FailedVendorPurchaseLeavesNoTrace()
        {
            var game = TestGame.Create();
            TestGame.DrainCredits(game);
            game.CardShows.Visit(Expo);
            int history = game.History.All.Count();
            int ledger = game.State.Ledger.Count;
            Assert.That(game.CardShows.BuyFromVendor(Expo, "expo-gus", "expo-hl-graded").Error, Is.EqualTo(OpError.InsufficientFunds));
            Assert.That(game.History.All.Count(), Is.EqualTo(history));
            Assert.That(game.State.Ledger.Count, Is.EqualTo(ledger));
        }

        [Test]
        public void EventExclusiveSerialsUseGlobalUniqueness()
        {
            var game = TestGame.Create();
            for (int i = 0; i < 25; i++) TestGame.GiveCard(game, "fh26-001", "expo25");
            CompleteExpo(game);
            game.CardShows.Redeem(Expo, "expo-tickets"); // spend so points are exactly what we expect
            var p = game.CardShows.Participation(Expo);
            p.PointsEarned += 100;
            int available = p.PointsAvailable;

            var result = game.CardShows.Redeem(Expo, "expo-exclusive");

            Assert.That(result.Error, Is.EqualTo(OpError.OutOfStock));
            Assert.That(p.PointsAvailable, Is.EqualTo(available), "No points spent on a failed redemption");
            Assert.That(game.Inventory.SerialsAllocated("fh26-001", "expo25"), Is.EqualTo(25));
        }

        [Test]
        public void ProgressAndInventorySurviveRestart()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            game.CardShows.Visit(Expo);
            game.CardShows.BuyFromVendor(Expo, "expo-rosa", "expo-fh-pack");
            GivePacks(game, "fh26-pack", 5);
            OpenAll(game, "fh26-pack");
            var vendorCard = game.CardShows.VendorStock(Expo, "expo-rosa").Single(s => s.Definition.Id == "expo-rookie-single").NextCardInstanceId;

            var reloaded = TestGame.SaveAndReload(game);
            reloaded.CardShows.Visit(Expo);

            Assert.That(reloaded.CardShows.Challenges(Expo).Single(c => c.Definition.Id == "open5").Completed);
            Assert.That(reloaded.CardShows.Participation(Expo).PointsEarned, Is.EqualTo(game.CardShows.Participation(Expo).PointsEarned));
            Assert.That(reloaded.CardShows.VendorStock(Expo, "expo-rosa").Single(s => s.Definition.Id == "expo-fh-pack").Remaining, Is.EqualTo(9));
            Assert.That(reloaded.CardShows.VendorStock(Expo, "expo-rosa").Single(s => s.Definition.Id == "expo-rookie-single").NextCardInstanceId,
                Is.EqualTo(vendorCard), "Inventory is not re-created on revisit");
        }

        [Test]
        public void ClockChangesDoNotGrantProgressOrReopenShows()
        {
            var game = TestGame.Create();
            game.CardShows.Visit(Expo);
            game.Tick(TestGame.StartTime.AddDays(30));
            Assert.That(game.CardShows.Participation(Expo).PointsEarned, Is.EqualTo(0), "Time passing alone earns nothing");
            Assert.That(game.CardShows.StatusOf(game.CardShows.Occurrence(Expo)), Is.EqualTo(OccurrenceStatus.Ended));

            game.Tick(TestGame.StartTime.AddDays(-30));
            Assert.That(game.CardShows.StatusOf(game.CardShows.Occurrence(Expo)), Is.EqualTo(OccurrenceStatus.Ended), "Rolling the clock back does not reopen it");
        }

        [Test]
        public void EndedShowsAreRecordedInEventHistory()
        {
            var game = TestGame.Create();
            CompleteExpo(game);
            game.CardShows.Redeem(Expo, "expo-frame");
            game.AdvanceGameTime(2 * Day);

            var record = game.CardShows.History.Single(h => h.OccurrenceId == Expo);
            Assert.That(record.Completed);
            Assert.That(record.ChallengesCompleted, Is.EqualTo(record.ChallengesTotal));
            Assert.That(record.RewardsRedeemed, Does.Contain("expo-frame"));

            game.AdvanceGameTime(Day);
            Assert.That(game.CardShows.History.Count(h => h.OccurrenceId == Expo), Is.EqualTo(1));
            Assert.That(TestGame.SaveAndReload(game).CardShows.History.Count(h => h.OccurrenceId == Expo), Is.EqualTo(1));
        }

        [Test]
        public void SchedulesAreDeterministicArithmetic()
        {
            var game = TestGame.Create();
            var occ = game.CardShows.Occurrence("cs-sports-expo@3");
            Assert.That(occ.StartsAt, Is.EqualTo(45 * Day + 3 * 7 * Day));
            Assert.That(occ.EndsAt - occ.StartsAt, Is.EqualTo(3 * Day));
            Assert.That(game.CardShows.Occurrence("cs-sports-expo@-1"), Is.Null);
            Assert.That(game.CardShows.Occurrence("unknown@0"), Is.Null);
            Assert.That(game.Calendar.UpcomingCardShows(30).Select(i => i.Id), Does.Contain("cs-tcg-convention@0"));
        }
    }
}
