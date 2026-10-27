using System.Linq;
using CardVault.Core.Calendar;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.Save;
using CardVault.Core.State;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase H: the full collector loop across every feature, ending in a save/reload.</summary>
    public class IntegrationTests
    {
        private const long Day = GameClock.SecondsPerDay;

        [Test]
        public void FullCollectorLoopStaysConsistentThroughSaveAndReload()
        {
            var storage = new InMemorySaveStorage();
            var saves = new SaveService(storage);
            var game = TestGame.Create(2026);
            TestGame.GiveCredits(game, 200000);

            // 1. Visit a card show and work its challenges with real actions.
            const string meetup = "cs-daily-meetup@45";
            Assert.That(game.CardShows.Visit(meetup).Ok);
            Assert.That(game.CardShows.BuyFromVendor(meetup, "meetup-milo", "meetup-fh").Ok);
            foreach (var pack in game.Sealed.PlayerUnopened.Where(p => p.ProductId == "fh26-pack").ToList())
                Assert.That(game.Packs.Open(pack.InstanceId).Ok);
            Assert.That(game.CardShows.Participation(meetup).Completed, "Opening 3 packs on show day meets both goals");

            // 2. Build the Rising Rookies set from the shop's finite release stock.
            game.AdvanceGameTime(Day / 2); // Rising Rookies releases on day 46
            Assert.That(game.Shop.Buy("rel-fhr26", "fhr26-pack").Error, Is.EqualTo(OpError.NotAvailable));
            game.AdvanceGameTime(Day);
            for (int i = 0; i < 30 && !game.SetRewards.IsCompleted("fhr26"); i++)
            {
                var bought = game.Shop.Buy("rel-fhr26", "fhr26-pack");
                Assert.That(bought.Ok, bought.ToString());
                Assert.That(game.Packs.Open(bought.Value.InstanceId).Ok);
            }
            foreach (var missing in game.Sets.Progress("fhr26").MissingBaseTemplateIds.ToList()) TestGame.GiveCard(game, missing);
            Assert.That(game.SetRewards.IsCompleted("fhr26"));
            var claim = game.SetRewards.Claim("fhr26", "complete");
            Assert.That(claim.Ok, claim.ToString());

            // 3. Feature a serial-numbered card in the showcase.
            var rare = game.Inventory.PlayerCards.FirstOrDefault(c => c.IsSerialNumbered && c.Tradable)
                       ?? TestGame.GiveCard(game, "fhr26-010", "gold50");
            Assert.That(game.Showcase.SetFeatured(rare.InstanceId).Ok);
            Assert.That(game.Showcase.Place("completed-sets", 0, "fhr26").Ok);

            // 4. Sell the featured card: the showcase clears it; history keeps the whole story.
            var sale = game.Market.SellToNpc(rare.InstanceId);
            Assert.That(sale.Ok);
            Assert.That(game.Showcase.FeaturedCardId, Is.Null);
            var story = game.History.For(rare.InstanceId);
            Assert.That(story.First().Type, Is.EqualTo(HistoryEventType.Created));
            Assert.That(story.Any(e => e.Type == HistoryEventType.PackOpened || e.Type == HistoryEventType.Acquired));
            Assert.That(story.Last().Type, Is.EqualTo(HistoryEventType.NpcSale));
            Assert.That(story.Last().PriceCredits, Is.EqualTo(sale.Value));
            Assert.That(game.SetRewards.IsCompleted("fhr26"), "Selling does not undo historical completion");

            // 5. Meetup challenges were met by real actions in the window; redeem once.
            var meetupParticipation = game.CardShows.Participation(meetup);
            Assert.That(meetupParticipation.CompletedChallenges.Keys, Is.EquivalentTo(new[] { "open2", "acquire8" }));
            Assert.That(game.CardShows.Redeem(meetup, "meetup-credits").Ok);
            Assert.That(game.CardShows.Redeem(meetup, "meetup-credits").Error, Is.EqualTo(OpError.AlreadyClaimed));

            // 6. The calendar moves Rising Rookies to retired and its leftovers to the vintage market.
            game.Calendar.Watch("rel-fhr26");
            game.AdvanceGameTime(15 * Day); // day ~61
            var release = game.Calendar.Release("rel-fhr26");
            Assert.That(game.Calendar.StatusOf(release), Is.EqualTo(ReleaseStatus.Retired));
            Assert.That(game.Shop.Buy("rel-fhr26", "fhr26-pack").Ok, Is.False);
            int vintageListings = game.Market.VintageListings.Count(l => l.SealedInstanceId != null && game.Sealed.Get(l.SealedInstanceId).ProductId == "fhr26-pack");
            Assert.That(vintageListings, Is.GreaterThan(0));

            // 7. Save, "close" and reload.
            game.Save(saves);
            string snapshot = Json.Serialize(game.State);
            var result = saves.Load();
            Assert.That(result.Source, Is.EqualTo(LoadSource.Primary));
            var reloaded = CardVaultGame.FromState(TestContent.Database, result.State);
            reloaded.Tick(TestGame.StartTime); // device clock unchanged

            // 8. Everything is consistent and nothing was duplicated.
            Assert.That(Json.Serialize(reloaded.State), Is.EqualTo(snapshot), "Loading changed nothing");
            Assert.That(reloaded.History.VerifyIntegrity(), Is.Empty);
            Assert.That(reloaded.Inventory.Get(rare.InstanceId).OwnerId, Is.Not.EqualTo(Owners.Player));
            Assert.That(reloaded.Showcase.DisplayedCardIds, Does.Not.Contain(rare.InstanceId));
            Assert.That(reloaded.SetRewards.Claim("fhr26", "complete").Error, Is.EqualTo(OpError.AlreadyClaimed));
            Assert.That(reloaded.CardShows.Redeem(meetup, "meetup-credits").Error, Is.Not.EqualTo(OpError.None));
            Assert.That(reloaded.CardShows.History.Any(h => h.OccurrenceId == meetup && h.Completed));
            Assert.That(reloaded.Calendar.IsWatched("rel-fhr26"));
            Assert.That(reloaded.Market.VintageListings.Count(l => l.SealedInstanceId != null && reloaded.Sealed.Get(l.SealedInstanceId).ProductId == "fhr26-pack"),
                Is.EqualTo(vintageListings));
            Assert.That(reloaded.State.Rewards.Grants.Keys.Distinct().Count(), Is.EqualTo(reloaded.State.Rewards.Grants.Count));
            Assert.That(reloaded.State.SerialAllocations.Values.Distinct().Count(), Is.EqualTo(reloaded.State.SerialAllocations.Count));

            // Wallet equals the ledger: no currency appeared outside a recorded transaction.
            Assert.That(reloaded.Economy.Credits, Is.EqualTo(reloaded.State.Ledger.Sum(l => l.CreditsDelta)));
            Assert.That(reloaded.Economy.EventTickets, Is.EqualTo(reloaded.State.Ledger.Sum(l => l.TicketsDelta)));
        }

        [Test]
        public void LongOfflineSimulationKeepsInvariants()
        {
            var game = TestGame.Create(77);
            TestGame.GiveCredits(game, 500000);
            for (int day = 0; day < 40; day++)
            {
                foreach (var occ in game.CardShows.Active()) game.CardShows.Visit(occ.OccurrenceId);
                var offer = game.Shop.Offers(includeSoldOut: false).FirstOrDefault();
                if (offer != null)
                {
                    var bought = game.Shop.Buy(offer.Release.Id, offer.Product.Id);
                    if (bought.Ok) game.Packs.Open(bought.Value.InstanceId);
                }
                foreach (var pack in game.Sealed.PlayerUnopened.Take(3).ToList()) game.Packs.Open(pack.InstanceId);
                var listing = game.Market.ActiveCardListings.FirstOrDefault();
                if (listing != null) game.Market.Buy(listing.ListingId);
                var spare = game.Inventory.PlayerCards.Where(c => c.Status == CardStatus.Available && c.Tradable).Skip(20).FirstOrDefault();
                if (spare != null) game.Market.SellToNpc(spare.InstanceId);
                var raw = game.Inventory.PlayerCards.FirstOrDefault(c => !c.IsGraded && c.Status == CardStatus.Available);
                if (raw != null && day % 5 == 0) game.Grading.Submit(raw.InstanceId);
                foreach (var set in game.Content.Pack.Sets)
                foreach (var m in game.SetRewards.Milestones(set.Id).Where(m => m.Claimable)) game.SetRewards.Claim(set.Id, m.Definition.Id);
                game.AdvanceGameTime(Day);
            }

            Assert.That(game.History.VerifyIntegrity(), Is.Empty);
            Assert.That(game.Economy.Credits, Is.EqualTo(game.State.Ledger.Sum(l => l.CreditsDelta)));
            Assert.That(game.State.Progression.ProcessedActionCount, Is.EqualTo(game.State.Actions.Count));
            foreach (var release in game.Calendar.All)
            foreach (var p in release.Products)
                Assert.That(game.Calendar.Remaining(release, p.ProductId), Is.InRange(0, p.InitialStock));

            var reloaded = TestGame.SaveAndReload(game);
            Assert.That(Json.Serialize(reloaded.State), Is.EqualTo(Json.Serialize(game.State)));
        }
    }
}
