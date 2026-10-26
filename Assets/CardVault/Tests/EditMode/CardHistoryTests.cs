using System.Linq;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Game;
using CardVault.Core.Save;
using CardVault.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase B: card history and provenance.</summary>
    public class CardHistoryTests
    {
        [Test]
        public void PackOpeningRecordsCreationAndPullWithProductAndSet()
        {
            var game = TestGame.Create();
            var packId = TestGame.FirstPlayerPack(game, "fh26-pack");
            var result = game.Packs.Open(packId).Value;

            foreach (var card in result.Cards)
            {
                var timeline = game.History.For(card.InstanceId);
                Assert.That(timeline.Select(e => e.Type), Is.EqualTo(new[] { HistoryEventType.Created, HistoryEventType.PackOpened }));
                var pull = timeline[1];
                Assert.That(pull.ProductId, Is.EqualTo("fh26-pack"));
                Assert.That(pull.SetId, Is.EqualTo("fh26"));
                Assert.That(pull.NewOwnerId, Is.EqualTo(Owners.Player));
                Assert.That(pull.TransactionId, Is.EqualTo(result.TransactionId));
                Assert.That(pull.Description, Does.Contain("Fieldhouse 2026 Hobby Pack"));
            }
        }

        [Test]
        public void NpcPurchaseRecordsSellerAndPrice()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            var listing = game.Market.ActiveCardListings.First();
            game.Market.Buy(listing.ListingId);

            var timeline = game.History.For(listing.CardInstanceId);
            var purchase = timeline.Last();
            Assert.That(purchase.Type, Is.EqualTo(HistoryEventType.NpcPurchase));
            Assert.That(purchase.PreviousOwnerId, Is.EqualTo(listing.SellerId));
            Assert.That(purchase.NewOwnerId, Is.EqualTo(Owners.Player));
            Assert.That(purchase.PriceCredits, Is.EqualTo(listing.PriceCredits));
            Assert.That(purchase.TransactionId, Is.EqualTo(listing.ClosedTransactionId));
            Assert.That(timeline.Any(e => e.Type == HistoryEventType.ListingCreated), "The NPC listing is part of the story");

            var ledger = game.State.Ledger.Single(l => l.TransactionId == purchase.TransactionId);
            Assert.That(ledger.CreditsDelta, Is.EqualTo(-listing.PriceCredits), "History and ledger come from the same transaction");
        }

        [Test]
        public void SaleRecordsBuyerPriceAndKeepsEarlierEntries()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "aeth-020");
            var sale = game.Market.SellToNpc(card.InstanceId);

            var timeline = game.History.For(card.InstanceId);
            Assert.That(timeline.Select(e => e.Type), Is.EqualTo(new[] { HistoryEventType.Created, HistoryEventType.Acquired, HistoryEventType.NpcSale }));
            Assert.That(timeline[2].PriceCredits, Is.EqualTo(sale.Value));
            Assert.That(timeline[2].NewOwnerId, Is.EqualTo(card.OwnerId));
        }

        [Test]
        public void FailedPurchaseCreatesNoHistory()
        {
            var game = TestGame.Create();
            TestGame.DrainCredits(game);
            var listing = game.Market.ActiveCardListings.First();
            int before = game.History.For(listing.CardInstanceId).Count;

            Assert.That(game.Market.Buy(listing.ListingId).Ok, Is.False);

            Assert.That(game.History.For(listing.CardInstanceId).Count, Is.EqualTo(before));
            Assert.That(game.History.All.Any(e => e.Type == HistoryEventType.NpcPurchase), Is.False);
        }

        [Test]
        public void FailedSaleAndTradeCreateNoHistory()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-005");
            game.Grading.Submit(card.InstanceId);
            int before = game.History.For(card.InstanceId).Count;

            Assert.That(game.Market.SellToNpc(card.InstanceId).Error, Is.EqualTo(OpError.CardBusy));
            Assert.That(game.Market.CreatePlayerListing(card.InstanceId, 50).Ok, Is.False);

            Assert.That(game.History.For(card.InstanceId).Count, Is.EqualTo(before));
            Assert.That(game.History.All.Any(e => e.Type == HistoryEventType.NpcSale), Is.False);
        }

        [Test]
        public void ReplayedTransactionDoesNotDuplicateHistory()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            var listing = game.Market.ActiveCardListings.First();
            game.Market.Buy(listing.ListingId, "purchase-abc");
            int count = game.History.All.Count();

            var replay = game.Market.Buy(listing.ListingId, "purchase-abc");

            Assert.That(replay.WasDuplicate);
            Assert.That(game.History.All.Count(), Is.EqualTo(count));
            Assert.That(game.History.VerifyIntegrity(), Is.Empty);
        }

        [Test]
        public void AppendingSameTransactionEntryTwiceIsIdempotent()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-006");
            using (var tx = game.Context.Begin("manual"))
            {
                var a = game.History.Append(tx, card.InstanceId, HistoryEventType.OwnershipTransfer, e => e.NewOwnerId = Owners.Player);
                var b = game.History.Append(tx, card.InstanceId, HistoryEventType.OwnershipTransfer, e => e.NewOwnerId = Owners.Player);
                Assert.That(b.EntryId, Is.EqualTo(a.EntryId));
                tx.Commit();
            }
            Assert.That(game.History.For(card.InstanceId).Count(e => e.Type == HistoryEventType.OwnershipTransfer), Is.EqualTo(1));
        }

        [Test]
        public void TradeRecordsBothSides()
        {
            var game = TestGame.Create();
            TestGame.GiveCard(game, "fh26-007");
            game.AdvanceGameTime(GameClock.SecondsPerDay);
            var offer = game.Market.OpenTrades.First();
            Assert.That(game.Market.AcceptTrade(offer.TradeId).Ok);

            var given = game.History.For(offer.RequestedCardIds[0]).Last();
            var received = game.History.For(offer.OfferedCardIds[0]).Last();
            Assert.That(given.Type, Is.EqualTo(HistoryEventType.NpcTrade));
            Assert.That(given.NewOwnerId, Is.EqualTo(Owners.Npc(offer.NpcId)));
            Assert.That(received.Type, Is.EqualTo(HistoryEventType.NpcTrade));
            Assert.That(received.NewOwnerId, Is.EqualTo(Owners.Player));
            Assert.That(given.TransactionId, Is.EqualTo(received.TransactionId));
        }

        [Test]
        public void GradingRecordsSubmissionAndAwardWithPreviousCondition()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-008", condition: 8.7);
            game.Grading.Submit(card.InstanceId);
            game.AdvanceGameTime(game.Content.Pack.Grading.TurnaroundSeconds);

            var timeline = game.History.For(card.InstanceId);
            var submitted = timeline.Single(e => e.Type == HistoryEventType.GradingSubmitted);
            var awarded = timeline.Single(e => e.Type == HistoryEventType.GradeAwarded);
            Assert.That(submitted.PreviousCondition, Is.EqualTo(8.7));
            Assert.That(awarded.PreviousGrade, Is.Null);
            Assert.That(awarded.NewGrade, Is.EqualTo(card.Grade));
            Assert.That(awarded.Time, Is.GreaterThan(submitted.Time));
        }

        [Test]
        public void ListingCreateAndCancelAreRecorded()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-009");
            var listing = game.Market.CreatePlayerListing(card.InstanceId, 75).Value;
            game.Market.CancelPlayerListing(listing.ListingId);

            var types = game.History.For(card.InstanceId).Select(e => e.Type).ToList();
            Assert.That(types, Does.Contain(HistoryEventType.ListingCreated));
            Assert.That(types.Last(), Is.EqualTo(HistoryEventType.ListingCanceled));
            Assert.That(card.OwnerId, Is.EqualTo(Owners.Player));
        }

        [Test]
        public void SerialNumberAndIdentityStayConsistentAcrossOwners()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-001", "gold50");
            int serial = card.SerialNumber;
            string id = card.InstanceId;
            game.Market.SellToNpc(id);

            var reloaded = TestGame.SaveAndReload(game);
            var after = reloaded.Inventory.Get(id);
            Assert.That(after.SerialNumber, Is.EqualTo(serial));
            Assert.That(after.TemplateId, Is.EqualTo("fh26-001"));
            Assert.That(reloaded.State.SerialAllocations[CardInventoryService.SerialKey("fh26-001", "gold50", serial)], Is.EqualTo(id));
            Assert.That(reloaded.History.For(id).First().Description, Does.Contain(CardCatalog.SerialLabel(after)));
        }

        [Test]
        public void HistorySurvivesSaveAndLoad()
        {
            var game = TestGame.Create();
            game.Packs.Open(TestGame.FirstPlayerPack(game));
            var card = TestGame.FirstPlayerCard(game);
            game.Market.SellToNpc(card.InstanceId);
            var expected = game.History.For(card.InstanceId).Select(e => e.EntryId + "|" + e.Type + "|" + e.Description).ToList();

            var reloaded = TestGame.SaveAndReload(game);

            Assert.That(reloaded.History.For(card.InstanceId).Select(e => e.EntryId + "|" + e.Type + "|" + e.Description), Is.EqualTo(expected));
            Assert.That(reloaded.History.VerifyIntegrity(), Is.Empty);
        }

        [Test]
        public void SchemaOneSaveIsMigratedWithBackfilledHistory()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-011");

            // Build a schema-1 save: the core collection without any feature data.
            var root = JObject.Parse(Json.Serialize(game.State));
            root["schemaVersion"] = 1;
            foreach (var key in new[] { "history", "setRewards", "rewards", "showcase", "calendar", "cardShows", "progression" })
                root.Remove(key);
            var storage = new InMemorySaveStorage();
            WriteRawSave(storage, root);

            var result = new SaveService(storage).Load();
            Assert.That(result.State, Is.Not.Null, string.Join(";", result.Warnings));
            Assert.That(result.MigratedFromVersion, Is.EqualTo(1));
            var migrated = CardVaultGame.FromState(TestContent.Database, result.State);

            var timeline = migrated.History.For(card.InstanceId);
            Assert.That(timeline.Count, Is.EqualTo(1));
            Assert.That(timeline[0].Type, Is.EqualTo(HistoryEventType.MigrationBackfill));
            Assert.That(timeline[0].NewOwnerId, Is.EqualTo(Owners.Player));
            Assert.That(migrated.History.VerifyIntegrity(), Is.Empty);

            // Migrated saves round-trip at the current schema.
            var again = TestGame.SaveAndReload(migrated);
            Assert.That(again.State.SchemaVersion, Is.EqualTo(GameState.CurrentSchemaVersion));
            Assert.That(again.History.For(card.InstanceId).Count, Is.EqualTo(1));
        }

        [Test]
        public void CorrectionsAppendWithoutEditingOriginal()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-012");
            var original = game.History.For(card.InstanceId).Last();
            string originalText = original.Description;

            var correction = game.History.AppendCorrection(original.EntryId, "Acquisition source was a gift, not a purchase.");

            Assert.That(correction.Ok);
            Assert.That(original.Description, Is.EqualTo(originalText));
            Assert.That(correction.Value.CorrectsEntryId, Is.EqualTo(original.EntryId));
            Assert.That(game.History.For(card.InstanceId).Last().Type, Is.EqualTo(HistoryEventType.Correction));
        }

        [Test]
        public void OwnershipChainFollowsTransfers()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            var card = TestGame.GiveCard(game, "fh26-013");
            game.Market.SellToNpc(card.InstanceId);
            string npc = card.OwnerId;

            Assert.That(game.History.OwnershipChain(card.InstanceId), Is.EqualTo(new[] { Owners.Player, npc }));
        }

        [Test]
        public void DescriptionsNeverExposeInternalIdentifiers()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            game.Packs.Open(TestGame.FirstPlayerPack(game));
            game.Market.Buy(game.Market.ActiveCardListings.First().ListingId);
            game.Market.SellToNpc(TestGame.FirstPlayerCard(game).InstanceId);

            foreach (var e in game.History.All)
            {
                Assert.That(e.Description, Does.Not.Contain("npc:"));
                Assert.That(e.Description, Does.Not.Contain("card-"));
                Assert.That(e.Description, Does.Not.Match(@"[A-Za-z_]/|\\|\.json"), "No file paths (serial labels like 07/25 are fine)");
            }
        }

        [Test]
        public void IntegrityHoldsAfterManyOperations()
        {
            var game = TestGame.Create(99);
            TestGame.GiveCredits(game, 200000);
            foreach (var pack in game.Sealed.PlayerUnopened.ToList()) game.Packs.Open(pack.InstanceId);
            for (int day = 0; day < 5; day++)
            {
                var listing = game.Market.ActiveCardListings.FirstOrDefault();
                if (listing != null) game.Market.Buy(listing.ListingId);
                var mine = game.Inventory.PlayerCards.FirstOrDefault(c => c.Status == CardStatus.Available && c.Tradable);
                if (mine != null) game.Market.SellToNpc(mine.InstanceId);
                var trade = game.Market.OpenTrades.FirstOrDefault();
                if (trade != null) game.Market.AcceptTrade(trade.TradeId);
                game.AdvanceGameTime(GameClock.SecondsPerDay);
            }
            Assert.That(game.History.VerifyIntegrity(), Is.Empty);
        }

        private static void WriteRawSave(InMemorySaveStorage storage, JObject payload)
        {
            string text = payload.ToString(Newtonsoft.Json.Formatting.None);
            string checksum;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                checksum = string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text)).Select(b => b.ToString("x2")));
            var envelope = new JObject { ["schemaVersion"] = 1, ["checksum"] = checksum, ["payload"] = text };
            storage.Files[SaveService.PrimaryKey] = envelope.ToString();
        }
    }
}
