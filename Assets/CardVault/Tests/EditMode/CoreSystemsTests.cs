using System;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.Save;
using CardVault.Core.State;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase A: the core collection systems every feature builds on.</summary>
    public class CoreSystemsTests
    {
        [Test]
        public void ShippedContentLoadsAndValidates()
        {
            var db = TestContent.Database;
            Assert.That(db.Sets.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(db.Sets.Values.Any(s => s.Category == CardCategory.Sports));
            Assert.That(db.Sets.Values.Any(s => s.Category == CardCategory.Tcg));
            Assert.That(db.Sets.Values.Any(s => s.IsVintage));
            Assert.That(db.Parallels.Values.Any(p => p.IsOneOfOne));
        }

        [Test]
        public void ContentWithDuplicateIdsIsRejected()
        {
            var pack = Json.Deserialize<ContentPack>(TestContent.Json);
            pack.Cards.Add(pack.Cards[0]);
            Assert.Throws<ContentException>(() => new ContentDatabase(pack));
        }

        [Test]
        public void NewGameGrantsStartingCreditsAndProducts()
        {
            var game = TestGame.Create();
            var economy = TestContent.Database.Pack.Economy;
            Assert.That(game.Economy.Credits, Is.EqualTo(economy.StartingCredits));
            Assert.That(game.Sealed.PlayerUnopened.Count(), Is.EqualTo(economy.StartingProductIds.Count));
            Assert.That(game.Clock.Day, Is.EqualTo(economy.StartGameDay));
        }

        [Test]
        public void OpeningAPackCreatesUniquePlayerOwnedInstances()
        {
            var game = TestGame.Create();
            var packId = TestGame.FirstPlayerPack(game, "fh26-pack");
            var result = game.Packs.Open(packId);

            Assert.That(result.Ok, result.ToString());
            Assert.That(result.Value.Cards.Count, Is.EqualTo(6));
            Assert.That(result.Value.Cards.Select(c => c.InstanceId).Distinct().Count(), Is.EqualTo(6));
            Assert.That(result.Value.Cards.All(c => c.OwnerId == Owners.Player));
            Assert.That(game.Sealed.Get(packId).Opened);
            Assert.That(game.Packs.Open(packId).Ok, Is.False, "A pack cannot be opened twice");
        }

        [Test]
        public void OpeningABoxProducesItsPacks()
        {
            var game = TestGame.Create();
            using (var tx = game.Context.Begin())
            {
                game.Sealed.Create(tx, "fh26-box", Owners.Player, "test");
                tx.Commit();
            }
            var box = TestGame.FirstPlayerPack(game, "fh26-box");
            var result = game.Packs.Open(box);
            Assert.That(result.Ok);
            Assert.That(result.Value.ContainedProducts.Count, Is.EqualTo(12));
            Assert.That(result.Value.Cards, Is.Empty);
        }

        [Test]
        public void OneOfOneSerialCanOnlyExistOnce()
        {
            var game = TestGame.Create();
            var first = TestGame.GiveCard(game, "fh26-001", "black1");
            var second = TestGame.GiveCard(game, "fh26-001", "black1");

            Assert.That(first, Is.Not.Null);
            Assert.That(first.SerialNumber, Is.EqualTo(1));
            Assert.That(first.IsOneOfOne);
            Assert.That(second, Is.Null, "The 1/1 is already allocated");
            Assert.That(game.Inventory.HasFreeSerial("fh26-001", "black1"), Is.False);
            Assert.That(game.Inventory.HasFreeSerial("fh26-002", "black1"), Is.True, "Each card has its own 1/1");
        }

        [Test]
        public void NumberedParallelNeverExceedsPrintRun()
        {
            var game = TestGame.Create();
            var serials = Enumerable.Range(0, 12).Select(_ => TestGame.GiveCard(game, "apex26-001", "red10")).ToList();
            var created = serials.Where(c => c != null).ToList();
            Assert.That(created.Count, Is.EqualTo(10));
            Assert.That(created.Select(c => c.SerialNumber).Distinct().Count(), Is.EqualTo(10));
            Assert.That(created.All(c => c.SerialNumber >= 1 && c.SerialNumber <= 10));
        }

        [Test]
        public void PackPullsAreDeterministicForASeed()
        {
            string Pull(ulong seed)
            {
                var g = TestGame.Create(seed);
                var r = g.Packs.Open(TestGame.FirstPlayerPack(g, "aeth-booster"));
                return string.Join(",", r.Value.Cards.Select(c => c.TemplateId + "/" + c.ParallelId));
            }
            Assert.That(Pull(7), Is.EqualTo(Pull(7)));
            Assert.That(Pull(7), Is.Not.EqualTo(Pull(8)));
        }

        [Test]
        public void MarketPostsDailyListingsOwnedByNpcs()
        {
            var game = TestGame.Create();
            var listings = game.Market.ActiveCardListings.ToList();
            Assert.That(listings, Is.Not.Empty);
            foreach (var l in listings)
            {
                var card = game.Inventory.Get(l.CardInstanceId);
                Assert.That(Owners.IsNpc(card.OwnerId));
                Assert.That(card.OwnerId, Is.EqualTo(l.SellerId));
            }
        }

        [Test]
        public void BuyingAListingMovesCardAndCredits()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            var listing = game.Market.ActiveCardListings.First();
            long before = game.Economy.Credits;

            var result = game.Market.Buy(listing.ListingId);

            Assert.That(result.Ok, result.ToString());
            Assert.That(game.Inventory.Get(listing.CardInstanceId).OwnerId, Is.EqualTo(Owners.Player));
            Assert.That(game.Economy.Credits, Is.EqualTo(before - listing.PriceCredits));
            Assert.That(listing.Status, Is.EqualTo(ListingStatus.Sold));
            Assert.That(game.Market.Buy(listing.ListingId).Ok, Is.False, "Cannot buy a sold listing");
        }

        [Test]
        public void PurchaseWithoutFundsChangesNothing()
        {
            var game = TestGame.Create();
            TestGame.DrainCredits(game);
            var listing = game.Market.ActiveCardListings.First();
            var card = game.Inventory.Get(listing.CardInstanceId);
            string owner = card.OwnerId;
            int ledger = game.State.Ledger.Count;

            var result = game.Market.Buy(listing.ListingId);

            Assert.That(result.Error, Is.EqualTo(OpError.InsufficientFunds));
            Assert.That(card.OwnerId, Is.EqualTo(owner));
            Assert.That(listing.Status, Is.EqualTo(ListingStatus.Active));
            Assert.That(game.State.Ledger.Count, Is.EqualTo(ledger));
        }

        [Test]
        public void SellingToNpcCreditsPlayerAndTransfersCard()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-020");
            long before = game.Economy.Credits;
            var result = game.Market.SellToNpc(card.InstanceId);

            Assert.That(result.Ok, result.ToString());
            Assert.That(Owners.IsNpc(card.OwnerId));
            Assert.That(game.Economy.Credits, Is.EqualTo(before + result.Value));
            Assert.That(game.Market.SellToNpc(card.InstanceId).Error, Is.EqualTo(OpError.NotOwned));
        }

        [Test]
        public void GradingCompletesAfterTurnaroundAndPersistsGrade()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-003", condition: 9.4);
            var sub = game.Grading.Submit(card.InstanceId);
            Assert.That(sub.Ok, sub.ToString());
            Assert.That(card.Status, Is.EqualTo(CardStatus.InGrading));
            Assert.That(game.Market.SellToNpc(card.InstanceId).Error, Is.EqualTo(OpError.CardBusy));

            game.AdvanceGameTime(game.Content.Pack.Grading.TurnaroundSeconds);

            Assert.That(card.Grade.HasValue);
            Assert.That(card.Status, Is.EqualTo(CardStatus.Available));
            var reloaded = TestGame.SaveAndReload(game);
            Assert.That(reloaded.Inventory.Get(card.InstanceId).Grade, Is.EqualTo(card.Grade));
        }

        [Test]
        public void TradeSwapsCardsBetweenPlayerAndNpc()
        {
            var game = TestGame.Create();
            var mine = TestGame.GiveCard(game, "fh26-010");
            game.AdvanceGameTime(GameClock.SecondsPerDay);
            var offer = game.Market.OpenTrades.First();
            var requested = game.Inventory.Get(offer.RequestedCardIds[0]);
            var offered = game.Inventory.Get(offer.OfferedCardIds[0]);

            var result = game.Market.AcceptTrade(offer.TradeId);

            Assert.That(result.Ok, result.ToString());
            Assert.That(requested.OwnerId, Is.EqualTo(Owners.Npc(offer.NpcId)));
            Assert.That(offered.OwnerId, Is.EqualTo(Owners.Player));
            Assert.That(game.Market.AcceptTrade(offer.TradeId).Ok, Is.False);
        }

        [Test]
        public void ReplayedTransactionIdIsRejected()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-004");
            var first = game.Market.SellToNpc(card.InstanceId, "client-sale-1");
            long credits = game.Economy.Credits;
            var replay = game.Market.SellToNpc(card.InstanceId, "client-sale-1");

            Assert.That(first.Ok);
            Assert.That(replay.WasDuplicate);
            Assert.That(game.Economy.Credits, Is.EqualTo(credits));
        }

        [Test]
        public void SaveRoundTripPreservesEntireState()
        {
            var game = TestGame.Create();
            game.Packs.Open(TestGame.FirstPlayerPack(game));
            TestGame.GiveCard(game, "apex26-002", "gold25");
            string before = Json.Serialize(game.State);

            var reloaded = TestGame.SaveAndReload(game);

            Assert.That(Json.Serialize(reloaded.State), Is.EqualTo(before));
        }

        [Test]
        public void CorruptPrimarySaveFallsBackToBackup()
        {
            var storage = new InMemorySaveStorage();
            var saves = new SaveService(storage);
            var game = TestGame.Create();
            game.Save(saves);
            game.Packs.Open(TestGame.FirstPlayerPack(game));
            game.Save(saves);

            storage.Files[SaveService.PrimaryKey] = storage.Files[SaveService.PrimaryKey].Substring(0, 200);
            var result = saves.Load();

            Assert.That(result.Source, Is.EqualTo(LoadSource.Backup));
            Assert.That(result.State, Is.Not.Null);
            Assert.That(result.Warnings, Is.Not.Empty);
        }

        [Test]
        public void InterruptedWriteKeepsPreviousSaveLoadable()
        {
            var storage = new InMemorySaveStorage();
            var saves = new SaveService(storage);
            var game = TestGame.Create();
            game.Save(saves);
            long creditsAtSave = game.Economy.Credits;

            TestGame.GiveCredits(game, 999);
            storage.FailNextWrite = true;
            Assert.Throws<System.IO.IOException>(() => game.Save(saves));

            var result = saves.Load();
            Assert.That(result.Source, Is.EqualTo(LoadSource.Primary));
            Assert.That(result.State.Wallet.Credits, Is.EqualTo(creditsAtSave));
        }

        [Test]
        public void ClockIgnoresRollbackAndClampsForwardJumps()
        {
            var game = TestGame.Create();
            long start = game.Clock.Now;

            game.Tick(TestGame.StartTime.AddHours(-48));
            Assert.That(game.Clock.Now, Is.EqualTo(start), "Rolling the device clock back never moves game time");

            game.Tick(TestGame.StartTime.AddHours(1));
            Assert.That(game.Clock.Now, Is.EqualTo(start + 3600));

            game.Tick(TestGame.StartTime.AddDays(365));
            Assert.That(game.Clock.Now - start - 3600, Is.EqualTo(GameClock.DefaultMaxAdvancePerSyncSeconds));

            game.Tick(TestGame.StartTime);
            game.Tick(TestGame.StartTime.AddDays(365));
            Assert.That(game.Clock.Now - start - 3600, Is.EqualTo(GameClock.DefaultMaxAdvancePerSyncSeconds),
                "Winding back and forward again does not replay the interval");
        }
    }
}
