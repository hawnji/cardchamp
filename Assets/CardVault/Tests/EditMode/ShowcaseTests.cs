using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Showcase;
using CardVault.Core.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase D: collector's showcase.</summary>
    public class ShowcaseTests
    {
        [Test]
        public void DisplayingACardDoesNotDuplicateOrMoveIt()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-020");
            int cards = game.State.Cards.Count;
            int owned = game.Inventory.PlayerCardCount;

            Assert.That(game.Showcase.SetFeatured(card.InstanceId).Ok);

            Assert.That(game.State.Cards.Count, Is.EqualTo(cards));
            Assert.That(game.Inventory.PlayerCardCount, Is.EqualTo(owned));
            Assert.That(card.OwnerId, Is.EqualTo(Owners.Player));
            Assert.That(game.Showcase.FeaturedCardId, Is.SameAs(card.InstanceId), "The slot holds the instance id, not a copy");
        }

        [Test]
        public void ACardOccupiesOnlyOneSlot()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-020");
            game.Showcase.Place("main-wall", 0, card.InstanceId);
            game.Showcase.Place("sports", 1, card.InstanceId);

            Assert.That(game.Showcase.Areas().SelectMany(a => a.Slots).Count(s => s == card.InstanceId), Is.EqualTo(1));
            Assert.That(game.Showcase.LocationOf(card.InstanceId).Value.areaId, Is.EqualTo("sports"));
        }

        [Test]
        public void RemovingACardDoesNotAffectOwnership()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "aeth-005");
            game.Showcase.Place("tcg", 0, card.InstanceId);
            Assert.That(game.Showcase.Remove("tcg", 0).Ok);

            Assert.That(card.OwnerId, Is.EqualTo(Owners.Player));
            Assert.That(game.Inventory.Get(card.InstanceId), Is.Not.Null);
            Assert.That(game.Showcase.DisplayedCardIds, Is.Empty);
        }

        [Test]
        public void SoldCardIsRemovedFromShowcaseAutomatically()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-019");
            game.Showcase.SetFeatured(card.InstanceId);
            game.Showcase.Place("favorites", 0, TestGame.GiveCard(game, "fh26-003").InstanceId);

            game.Market.SellToNpc(card.InstanceId);

            Assert.That(game.Showcase.FeaturedCardId, Is.Null);
            Assert.That(game.Showcase.DisplayedCardIds, Does.Not.Contain(card.InstanceId));
            Assert.That(game.Showcase.DisplayedCardIds.Count, Is.EqualTo(1), "Other displays are untouched");
        }

        [Test]
        public void TradedAwayCardIsRemovedFromShowcase()
        {
            var game = TestGame.Create();
            TestGame.GiveCard(game, "fh26-007");
            game.AdvanceGameTime(GameClock.SecondsPerDay);
            var offer = game.Market.OpenTrades.First();
            game.Showcase.Place("main-wall", 0, offer.RequestedCardIds[0]);

            game.Market.AcceptTrade(offer.TradeId);

            Assert.That(game.Showcase.DisplayedCardIds, Does.Not.Contain(offer.RequestedCardIds[0]));
        }

        [Test]
        public void CannotDisplayCardsYouDoNotOwn()
        {
            var game = TestGame.Create();
            var npcCard = game.Market.ActiveCardListings.First().CardInstanceId;
            Assert.That(game.Showcase.SetFeatured(npcCard).Error, Is.EqualTo(OpError.NotOwned));
            Assert.That(game.Showcase.SetFeatured("card-does-not-exist").Error, Is.EqualTo(OpError.NotFound));
        }

        [Test]
        public void AreaRulesAndLocksAreEnforced()
        {
            var game = TestGame.Create();
            var tcg = TestGame.GiveCard(game, "aeth-001");
            var raw = TestGame.GiveCard(game, "fh26-001");

            Assert.That(game.Showcase.Place("sports", 0, tcg.InstanceId).Error, Is.EqualTo(OpError.NotEligible));
            Assert.That(game.Showcase.Place("graded-shelf", 0, raw.InstanceId).Error, Is.EqualTo(OpError.Locked), "Shelf unlocks after grading a card");
            Assert.That(game.Showcase.Place("main-wall", 4, raw.InstanceId).Error, Is.EqualTo(OpError.Locked), "Extra wall slots need rank 3");
        }

        [Test]
        public void ReorderSwapsSlots()
        {
            var game = TestGame.Create();
            var a = TestGame.GiveCard(game, "fh26-001");
            var b = TestGame.GiveCard(game, "fh26-002");
            game.Showcase.Place("main-wall", 0, a.InstanceId);
            game.Showcase.Place("main-wall", 1, b.InstanceId);

            game.Showcase.Swap("main-wall", 0, 1);

            var wall = game.Showcase.Areas().Single(x => x.Definition.Id == "main-wall");
            Assert.That(wall.Slots[0], Is.EqualTo(b.InstanceId));
            Assert.That(wall.Slots[1], Is.EqualTo(a.InstanceId));
        }

        [Test]
        public void CustomizationRequiresUnlockedCosmetics()
        {
            var game = TestGame.Create();
            Assert.That(game.Showcase.SetCosmetic(CosmeticKind.ShowcaseBackground, "bg-fantasy-gallery").Error, Is.EqualTo(OpError.Locked));
            Assert.That(game.Showcase.SetCosmetic(CosmeticKind.CardFrame, "bg-modern-luxury").Error, Is.EqualTo(OpError.InvalidArgument));

            game.State.Profile.UnlockedCosmetics.Add("bg-fantasy-gallery");
            Assert.That(game.Showcase.SetCosmetic(CosmeticKind.ShowcaseBackground, "bg-fantasy-gallery").Ok);
            Assert.That(game.Showcase.Settings.BackgroundId, Is.EqualTo("bg-fantasy-gallery"));
        }

        [Test]
        public void ShowcaseLayoutPersistsAfterRestart()
        {
            var game = TestGame.Create();
            var a = TestGame.GiveCard(game, "fh26-001");
            var b = TestGame.GiveCard(game, "aeth-001");
            game.Showcase.SetFeatured(a.InstanceId);
            game.Showcase.Place("tcg", 2, b.InstanceId);
            game.Showcase.SetLayout(ShowcaseLayout.Spotlight);
            game.Showcase.SetReducedEffects(true);

            var reloaded = TestGame.SaveAndReload(game);

            Assert.That(reloaded.Showcase.FeaturedCardId, Is.EqualTo(a.InstanceId));
            Assert.That(reloaded.Showcase.Areas().Single(x => x.Definition.Id == "tcg").Slots[2], Is.EqualTo(b.InstanceId));
            Assert.That(reloaded.Showcase.Settings.Layout, Is.EqualTo(ShowcaseLayout.Spotlight));
            Assert.That(reloaded.Showcase.Settings.ReducedEffects);
        }

        [Test]
        public void InvalidReferencesInASaveAreClearedOnLoad()
        {
            var game = TestGame.Create();
            var a = TestGame.GiveCard(game, "fh26-001");
            game.Showcase.Place("main-wall", 0, a.InstanceId);
            var root = JObject.Parse(Json.Serialize(game.State));
            root["showcase"]["areas"]["main-wall"]["slots"][1] = "card-forged-0000001";
            root["showcase"]["areas"]["main-wall"]["slots"][2] = a.InstanceId;

            var state = root.ToObject<GameState>(Json.CreateSerializer());
            var loaded = Core.Game.CardVaultGame.FromState(TestContent.Database, state);

            var wall = loaded.Showcase.Areas().Single(x => x.Definition.Id == "main-wall");
            Assert.That(wall.Slots[0], Is.EqualTo(a.InstanceId));
            Assert.That(wall.Slots[1], Is.Null, "Unknown card cleared");
            Assert.That(wall.Slots[2], Is.Null, "Duplicate reference cleared");
        }

        [Test]
        public void CompletedSetDisplayRequiresHistoricalCompletion()
        {
            var game = TestGame.Create();
            Assert.That(game.Showcase.Place("completed-sets", 0, "fh26").Ok, Is.False);
            foreach (var t in game.Content.CardsInSet("fhr26")) TestGame.GiveCard(game, t.Id);
            game.State.Progression.Stats[StatKey.SetsCompleted.ToString()] = 1;
            Assert.That(game.Showcase.Place("completed-sets", 0, "fhr26").Ok);
        }
    }
}
