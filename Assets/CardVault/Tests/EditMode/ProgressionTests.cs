using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase G: integrated collector progression.</summary>
    public class ProgressionTests
    {
        [Test]
        public void StatsCountEachCommittedActionOnceAcrossReloads()
        {
            var game = TestGame.Create();
            game.Packs.Open(TestGame.FirstPlayerPack(game));
            Assert.That(game.Progression.Stat(StatKey.PacksOpened), Is.EqualTo(1));

            var reloaded = TestGame.SaveAndReload(game);
            reloaded.Progression.CatchUp();
            Assert.That(reloaded.Progression.Stat(StatKey.PacksOpened), Is.EqualTo(1));

            reloaded.Packs.Open(TestGame.FirstPlayerPack(reloaded));
            Assert.That(reloaded.Progression.Stat(StatKey.PacksOpened), Is.EqualTo(2));
        }

        [Test]
        public void UniqueCardsCountVariantsNotCopies()
        {
            var game = TestGame.Create();
            TestGame.GiveCard(game, "fh26-001");
            TestGame.GiveCard(game, "fh26-001");
            TestGame.GiveCard(game, "fh26-001", "silver");
            Assert.That(game.Progression.Stat(StatKey.UniqueCardsAcquired), Is.EqualTo(2));
        }

        [Test]
        public void SellingAndRebuyingDoesNotFarmUniqueProgress()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-001");
            long xp = game.State.Progression.CollectorXp;
            game.Market.SellToNpc(card.InstanceId);
            TestGame.GiveCard(game, "fh26-001");
            Assert.That(game.Progression.Stat(StatKey.UniqueCardsAcquired), Is.EqualTo(1));
            Assert.That(game.State.Progression.CollectorXp, Is.EqualTo(xp));
        }

        [Test]
        public void AchievementUnlocksOnceAndPaysOnce()
        {
            var game = TestGame.Create();
            game.Packs.Open(TestGame.FirstPlayerPack(game));
            game.Packs.Open(TestGame.FirstPlayerPack(game));

            Assert.That(game.State.Progression.Achievements.ContainsKey("ach-first-pack"));
            Assert.That(game.State.Ledger.Count(l => l.RefId == "ach:ach-first-pack"), Is.EqualTo(1));
            Assert.That(game.State.Ledger.Single(l => l.RefId == "ach:ach-first-pack").CreditsDelta, Is.EqualTo(100));

            var reloaded = TestGame.SaveAndReload(game);
            reloaded.Progression.CatchUp();
            Assert.That(reloaded.State.Ledger.Count(l => l.RefId == "ach:ach-first-pack"), Is.EqualTo(1));
        }

        [Test]
        public void NotEveryAchievementPaysCurrency()
        {
            var achievements = TestContent.Database.Pack.Achievements;
            Assert.That(achievements.Count(a => a.Reward.Credits == 0 && a.Reward.EventTickets == 0), Is.GreaterThan(achievements.Count / 3));
            Assert.That(achievements.Any(a => a.Reward.CosmeticIds.Count > 0));
        }

        [Test]
        public void GradingUnlocksTheSlabShelfAsAShowcaseUpgrade()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "fh26-004", condition: 9.5);
            game.Grading.Submit(card.InstanceId);
            game.AdvanceGameTime(game.Content.Pack.Grading.TurnaroundSeconds);

            Assert.That(game.Progression.Stat(StatKey.CardsGraded), Is.EqualTo(1));
            Assert.That(game.Showcase.Areas().Single(a => a.Definition.Id == "graded-shelf").Unlocked);
            Assert.That(game.Progression.Stat(StatKey.ShowcaseUpgradesUnlocked), Is.GreaterThanOrEqualTo(1));
            Assert.That(game.Showcase.Place("graded-shelf", 0, card.InstanceId).Ok);
        }

        [Test]
        public void CompletingASetFeedsProgressionAndUnlocksDisplay()
        {
            var game = TestGame.Create();
            foreach (var t in game.Content.CardsInSet("fhr26")) TestGame.GiveCard(game, t.Id);

            Assert.That(game.Progression.Stat(StatKey.SetsCompleted), Is.EqualTo(1));
            Assert.That(game.State.Progression.Achievements.ContainsKey("ach-first-set"));
            Assert.That(game.Showcase.Place("completed-sets", 0, "fhr26").Ok);
        }

        [Test]
        public void XpRaisesRankAndRankRewardIsGrantedOnce()
        {
            var game = TestGame.Create();
            for (int i = 0; i < 20; i++) TestGame.GiveCard(game, game.Content.Pack.Cards[i].Id);
            Assert.That(game.State.Progression.CollectorXp, Is.GreaterThanOrEqualTo(150));
            Assert.That(game.State.Progression.CollectorRank, Is.GreaterThanOrEqualTo(2));
            Assert.That(game.State.Ledger.Count(l => l.RefId == "rank:2"), Is.EqualTo(1));

            var reloaded = TestGame.SaveAndReload(game);
            reloaded.Progression.CatchUp();
            Assert.That(reloaded.State.Ledger.Count(l => l.RefId == "rank:2"), Is.EqualTo(1));
            Assert.That(reloaded.Progression.Profile().RankName, Is.EqualTo(game.Content.Ranks.Single(r => r.Level == reloaded.State.Progression.CollectorRank).Name));
        }

        [Test]
        public void OneOfOneDiscoveryUnlocksTheVault()
        {
            var game = TestGame.Create();
            var one = TestGame.GiveCard(game, "aeth-020", "mythic1");
            Assert.That(game.Progression.Stat(StatKey.OneOfOnesDiscovered), Is.EqualTo(1));
            Assert.That(game.State.Progression.Achievements.ContainsKey("ach-first-one-of-one"));
            Assert.That(game.Showcase.Place("one-of-one", 0, one.InstanceId).Ok);
        }

        [Test]
        public void StoriedCardBecomesAHistoricalMilestone()
        {
            var game = TestGame.Create();
            var card = TestGame.GiveCard(game, "hl94-001");
            foreach (var owner in new[] { Owners.Npc("gus-tan"), Owners.Npc("rosa-vale"), Owners.Player })
            {
                using (var tx = game.Context.Begin())
                {
                    string previous = card.OwnerId;
                    game.Inventory.Transfer(tx, card, owner);
                    game.History.Append(tx, card.InstanceId, HistoryEventType.OwnershipTransfer, e =>
                    {
                        e.PreviousOwnerId = previous;
                        e.NewOwnerId = owner;
                    });
                    if (owner == Owners.Player) game.Context.RecordCardAction(tx, GameActionType.CardAcquired, card);
                    tx.Commit();
                }
            }
            Assert.That(game.Progression.Stat(StatKey.HistoricalMilestones), Is.EqualTo(1));
            Assert.That(game.History.For(card.InstanceId).Last().Type, Is.EqualTo(HistoryEventType.CollectionAchievement));
        }

        [Test]
        public void CardShowAndReleaseMilestonesFeedStats()
        {
            var game = TestGame.Create();
            TestGame.GiveCredits(game, 100000);
            game.CardShows.Visit("cs-sports-expo@0");
            game.AdvanceGameTime(GameClock.SecondsPerDay);
            game.Shop.Buy("rel-fhr26", "fhr26-pack");

            Assert.That(game.Progression.Stat(StatKey.CardShowsVisited), Is.EqualTo(1));
            Assert.That(game.Progression.Stat(StatKey.ReleaseMilestones), Is.EqualTo(1));
        }

        [Test]
        public void HomeSummaryReflectsLiveState()
        {
            var game = TestGame.Create();
            game.Packs.Open(TestGame.FirstPlayerPack(game, "fh26-pack"));
            var home = game.Home.Build();

            Assert.That(home.FeaturedRelease, Is.Not.Null);
            Assert.That(home.ActiveCardShows, Is.Not.Empty);
            Assert.That(home.UpcomingReleases, Is.Not.Empty);
            Assert.That(home.RecentlyAcquired, Is.Not.Empty);
            Assert.That(home.RecentlyAcquired.All(c => c.OwnerId == Owners.Player));
            Assert.That(home.CollectionProgress.Any(p => p.Set.Id == "fh26"));
        }

        [Test]
        public void MigratedSaveRebuildsStatsFromTheActionLog()
        {
            var game = TestGame.Create();
            game.Packs.Open(TestGame.FirstPlayerPack(game));
            game.State.Progression = new ProgressionState();

            var rebuilt = CardVaultGame.FromState(TestContent.Database, game.State);

            Assert.That(rebuilt.Progression.Stat(StatKey.PacksOpened), Is.EqualTo(1));
            Assert.That(rebuilt.State.Ledger.Count(l => l.RefId == "ach:ach-first-pack"), Is.EqualTo(1), "The reward key was already granted");
        }
    }
}
