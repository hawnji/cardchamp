using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Sets;
using CardVault.Core.State;
using NUnit.Framework;

namespace CardVault.Tests
{
    /// <summary>Phase C: set completion tracking and milestone rewards.</summary>
    public class SetRewardTests
    {
        private const string Set = "fh26";

        private static void GiveBase(Core.Game.CardVaultGame game, int count, string setId = Set)
        {
            foreach (var t in game.Content.CardsInSet(setId).Take(count)) TestGame.GiveCard(game, t.Id);
        }

        [Test]
        public void DuplicateCardsDoNotIncreaseUniqueCompletion()
        {
            var game = TestGame.Create();
            for (int i = 0; i < 5; i++) TestGame.GiveCard(game, "fh26-001");

            var progress = game.Sets.Progress(Set);
            Assert.That(progress.BaseOwned, Is.EqualTo(1));
            Assert.That(progress.DuplicateCopies, Is.EqualTo(4));
            Assert.That(progress.MissingBaseTemplateIds.Count, Is.EqualTo(progress.BaseTotal - 1));
        }

        [Test]
        public void ParallelsAndOneOfOnesAreTrackedSeparatelyFromBase()
        {
            var game = TestGame.Create();
            TestGame.GiveCard(game, "fh26-001", "black1");
            TestGame.GiveCard(game, "fh26-002", "gold50");

            var progress = game.Sets.Progress(Set);
            Assert.That(progress.BaseOwned, Is.EqualTo(0), "Parallels do not fill base slots");
            Assert.That(progress.Parallels.Single(p => p.ParallelId == "black1").Owned, Is.EqualTo(1));
            Assert.That(progress.Parallels.Single(p => p.ParallelId == "gold50").Owned, Is.EqualTo(1));

            GiveBase(game, progress.BaseTotal);
            Assert.That(game.SetRewards.IsCompleted(Set), "Base set completes without owning any 1/1");
        }

        [Test]
        public void EachMilestoneTriggersOnceAndAutoGrantPaysOnce()
        {
            var game = TestGame.Create();
            long before = game.Economy.Credits;
            GiveBase(game, 5); // 25% of 20

            var pct25 = game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "pct25");
            Assert.That(pct25.Claimed, "25% is auto-granted");
            long reward = pct25.Definition.Reward.Credits;
            Assert.That(game.Economy.Credits, Is.EqualTo(before + reward));

            // Sell everything and re-acquire: the milestone must not fire or pay again.
            foreach (var c in game.Inventory.PlayerCards.ToList()) game.Market.SellToNpc(c.InstanceId);
            long afterSales = game.Economy.Credits;
            GiveBase(game, 5);
            Assert.That(game.Economy.Credits, Is.EqualTo(afterSales));
            Assert.That(game.State.Actions.Count(a => a.Type == GameActionType.SetMilestoneReached && a.RefId == "pct25"), Is.EqualTo(1));
        }

        [Test]
        public void ManualMilestoneRequiresClaimAndCannotBeClaimedTwice()
        {
            var game = TestGame.Create();
            GiveBase(game, 10);
            var pct50 = game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "pct50");
            Assert.That(pct50.Claimable);
            long before = game.Economy.Credits;
            int ticketsBefore = game.Economy.EventTickets;

            var first = game.SetRewards.Claim(Set, "pct50");
            var second = game.SetRewards.Claim(Set, "pct50");

            Assert.That(first.Ok, first.ToString());
            Assert.That(second.Error, Is.EqualTo(OpError.AlreadyClaimed));
            Assert.That(game.Economy.Credits, Is.EqualTo(before + pct50.Definition.Reward.Credits));
            Assert.That(game.Economy.EventTickets, Is.EqualTo(ticketsBefore + pct50.Definition.Reward.EventTickets));
            Assert.That(game.State.Ledger.Count(l => l.RefId == SetRewardService.GrantKey(Set, "pct50")), Is.EqualTo(1));
        }

        [Test]
        public void UnreachedMilestoneCannotBeClaimed()
        {
            var game = TestGame.Create();
            Assert.That(game.SetRewards.Claim(Set, "complete").Error, Is.EqualTo(OpError.NotEligible));
        }

        [Test]
        public void ClaimsPersistAfterSaveAndReload()
        {
            var game = TestGame.Create();
            GiveBase(game, 20);
            game.SetRewards.Claim(Set, "complete");
            var reloaded = TestGame.SaveAndReload(game);

            Assert.That(reloaded.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "complete").Claimed);
            Assert.That(reloaded.SetRewards.Claim(Set, "complete").Error, Is.EqualTo(OpError.AlreadyClaimed));
            Assert.That(reloaded.Rewards.IsCosmeticUnlocked("frame-set-master"));
        }

        [Test]
        public void HistoricalCompletionRemainsAfterSellingCards()
        {
            var game = TestGame.Create();
            GiveBase(game, 20);
            long completedAt = game.Sets.ExistingRecord(Set).FirstCompletedAt.Value;
            game.Market.SellToNpc(game.Inventory.PlayerCards.First(c => c.OriginSetId == Set).InstanceId);

            var progress = game.Sets.Progress(Set);
            Assert.That(progress.IsCompleteNow, Is.False, "Current ownership dropped");
            Assert.That(progress.EverCompleted, "History is kept");
            Assert.That(progress.FirstCompletedAt, Is.EqualTo(completedAt));
            Assert.That(progress.EverCollected, Is.EqualTo(20));
            Assert.That(game.SetRewards.SetsCompleted, Is.EqualTo(1));

            var reloaded = TestGame.SaveAndReload(game);
            Assert.That(reloaded.SetRewards.IsCompleted(Set));
        }

        [Test]
        public void CompletingCardGetsMilestoneHistoryEntry()
        {
            var game = TestGame.Create();
            GiveBase(game, 19);
            var last = TestGame.GiveCard(game, game.Content.CardsInSet(Set)[19].Id);

            var entry = game.History.For(last.InstanceId).Single(e => e.Type == HistoryEventType.SetMilestone);
            Assert.That(entry.Description, Does.Contain("Base Set Complete"));
            Assert.That(entry.SetId, Is.EqualTo(Set));
        }

        [Test]
        public void FirstCompletionIsCelebratedOnce()
        {
            var game = TestGame.Create();
            GiveBase(game, 20);
            Assert.That(game.SetRewards.PendingCelebrations, Does.Contain(Set));
            game.SetRewards.MarkCelebrated(Set);
            Assert.That(game.SetRewards.PendingCelebrations, Is.Empty);
        }

        [Test]
        public void RookieSubjectGradedAndParallelChasesAreSeparateMilestones()
        {
            var game = TestGame.Create();
            var rookies = game.Content.CardsInSet(Set).Where(t => t.IsRookie).ToList();
            foreach (var r in rookies) TestGame.GiveCard(game, r.Id);
            Assert.That(game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "rookies").Achieved);

            foreach (var t in game.Content.CardsInSet(Set).Where(t => t.SubjectId == "mara-voss")) TestGame.GiveCard(game, t.Id);
            Assert.That(game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "star-mara-voss").Achieved);

            Assert.That(game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "chase-gold50").Achieved, Is.False);
            foreach (var t in game.Content.CardsInSet(Set)) TestGame.GiveCard(game, t.Id, "gold50");
            Assert.That(game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "chase-gold50").Achieved);

            Assert.That(game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "graded9").Achieved, Is.False);
            foreach (var t in game.Content.CardsInSet(Set)) TestGame.GiveCard(game, t.Id, condition: 10, grade: 9.5);
            Assert.That(game.SetRewards.Milestones(Set).Single(m => m.Definition.Id == "graded9").Achieved);
        }

        [Test]
        public void ReloadDoesNotRetriggerMilestones()
        {
            var game = TestGame.Create();
            GiveBase(game, 10);
            int actions = game.State.Actions.Count(a => a.Type == GameActionType.SetMilestoneReached);
            int notifications = game.State.Notifications.Count;
            var reloaded = TestGame.SaveAndReload(game);
            Assert.That(reloaded.State.Actions.Count(a => a.Type == GameActionType.SetMilestoneReached), Is.EqualTo(actions));
            Assert.That(reloaded.State.Notifications.Count, Is.EqualTo(notifications));
        }
    }
}
