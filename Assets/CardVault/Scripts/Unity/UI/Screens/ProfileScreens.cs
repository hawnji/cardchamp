using System.Linq;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.Progression;
using CardVault.Core.State;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    /// <summary>Collector profile: rank, stats, achievements, show records, titles and settings.</summary>
    public sealed class ProfileScreen : GameScreen
    {
        private static readonly string[] Tabs = { "Overview", "Achievements", "Card Shows", "Settings" };
        private int _tab;

        public override string Title => "Profile";

        public override void Build(VisualElement root)
        {
            var profile = Game.Progression.Profile();
            root.Add(Ui.Panel(
                Ui.Text(profile.Name, Theme.FontTitle, bold: true),
                Ui.Muted("Rank " + profile.Rank + " · " + profile.RankName + (profile.TitleName != null ? " · " + profile.TitleName : "")),
                Ui.Spacer(12),
                Ui.ProgressBar(profile.XpForNextRank.HasValue
                    ? (profile.Xp - profile.XpForCurrentRank) / (double)(profile.XpForNextRank.Value - profile.XpForCurrentRank)
                    : 1.0),
                Ui.Muted(profile.XpForNextRank.HasValue
                    ? profile.Xp.ToString("N0") + " / " + profile.XpForNextRank.Value.ToString("N0") + " XP to the next rank"
                    : profile.Xp.ToString("N0") + " XP · highest rank reached")));
            root.Add(Ui.Row(
                Ui.Button("Showcase", () => Shell.Push(new ShowcaseScreen())),
                Ui.Button("Set registry", () => Shell.Push(new SetListScreen()), Ui.ButtonKind.Secondary)));
            root.Add(Ui.Tabs(Tabs, _tab, i => { _tab = i; Shell.Refresh(); }));
            switch (_tab)
            {
                case 0: BuildOverview(root, profile); break;
                case 1: BuildAchievements(root); break;
                case 2: BuildShows(root); break;
                default: BuildSettings(root); break;
            }
        }

        private void BuildOverview(VisualElement root, CollectorProfile profile)
        {
            root.Add(Ui.SectionHeader("Collection milestones"));
            int completed = Game.SetRewards.SetsCompleted;
            int claimable = Game.SetRewards.ClaimableCount;
            root.Add(Ui.ListRow(completed + " of " + Game.Content.Sets.Count + " sets complete",
                claimable > 0 ? claimable + " reward(s) ready to claim" : "Track progress in the set registry",
                "Open", () => Shell.Push(new SetListScreen()), claimable > 0 ? Theme.Positive : (UnityEngine.Color?)null));

            root.Add(Ui.SectionHeader("Statistics"));
            foreach (var kv in profile.Stats)
                root.Add(Ui.Row(Ui.Text(StatLabel(kv.Key), Theme.FontSmall), Ui.Text(kv.Value.ToString("N0"), Theme.FontSmall, Theme.Accent, bold: true)));

            var pending = Game.Grading.Pending.ToList();
            root.Add(Ui.SectionHeader("Grading queue"));
            if (pending.Count == 0) root.Add(Ui.Muted("No cards at the grader."));
            foreach (var s in pending)
            {
                var card = Game.Inventory.Get(s.CardInstanceId);
                var id = s.CardInstanceId;
                root.Add(Ui.ListRow(card != null ? Game.Catalog.DisplayName(card) : "Card", "Returns " + Fmt.Date(Game, s.ReadyAt), "View",
                    () => Shell.Push(new CardDetailScreen(id))));
            }
        }

        private static string StatLabel(StatKey key)
        {
            var s = key.ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
                sb.Append(i == 0 ? s[i] : char.ToLowerInvariant(s[i]));
            }
            return sb.ToString().Replace("npc", "NPC");
        }

        private void BuildAchievements(VisualElement root)
        {
            var all = Game.Progression.Achievements();
            root.Add(Ui.Muted(all.Count(a => a.Unlocked) + " of " + all.Count + " unlocked"));
            foreach (var a in all.OrderByDescending(a => a.Unlocked).ThenBy(a => a.Definition.Name))
            {
                bool secret = a.Definition.Hidden && !a.Unlocked;
                var panel = Ui.Panel(
                    Ui.Text(secret ? "Hidden achievement" : a.Definition.Name, Theme.FontBody, a.Unlocked ? Theme.Accent : (UnityEngine.Color?)null, bold: true),
                    Ui.Muted(secret ? "Keep collecting to reveal this one." : a.Definition.Description));
                if (!secret)
                {
                    panel.Add(Ui.ProgressBar(a.Definition.Threshold > 0 ? a.Progress / (double)a.Definition.Threshold : 1.0, height: 12));
                    panel.Add(Ui.Muted(a.Unlocked
                        ? "Unlocked " + Fmt.Date(Game, a.UnlockedAt ?? 0)
                        : a.Progress + " / " + a.Definition.Threshold + " · reward: " + Core.Rewards.RewardService.Describe(a.Definition.Reward, Game.Content)));
                }
                root.Add(panel);
            }
        }

        private void BuildShows(VisualElement root)
        {
            root.Add(Ui.Button("Card show schedule", () => Shell.Push(new CardShowsScreen()), Ui.ButtonKind.Secondary));
            var history = Game.CardShows.History;
            root.Add(Ui.SectionHeader("Past shows"));
            if (history.Count == 0) root.Add(Ui.Muted("Shows you visit are recorded here after they close."));
            foreach (var h in history)
            {
                var occId = h.OccurrenceId;
                root.Add(Ui.ListRow(h.Name, Fmt.Date(Game, h.StartsAt) + " · " + h.ChallengesCompleted + "/" + h.ChallengesTotal +
                                            " challenges · " + h.PointsEarned + " points · " + h.PurchasesMade + " purchases",
                    h.Completed ? "Completed" : null, () => Shell.Push(new CardShowDetailScreen(occId)),
                    h.Completed ? Theme.Positive : (UnityEngine.Color?)null));
            }
        }

        private void BuildSettings(VisualElement root)
        {
            root.Add(Ui.SectionHeader("Title"));
            var titles = Game.Content.Cosmetics.Values.Where(c => c.Kind == CosmeticKind.ProfileTitle).ToList();
            var equipped = Game.State.Profile.EquippedTitleId;
            if (titles.Count == 0) root.Add(Ui.Muted("No titles available."));
            foreach (var t in titles)
            {
                var title = t;
                bool unlocked = Game.State.Profile.UnlockedCosmetics.Contains(t.Id);
                root.Add(Ui.ListRow(t.Name, unlocked ? t.Description : "Locked · " + t.Description,
                    t.Id == equipped ? "Equipped" : unlocked ? "Equip" : null,
                    unlocked && t.Id != equipped ? () => Shell.Run(() => Game.Showcase.SetCosmetic(CosmeticKind.ProfileTitle, title.Id), title.Name + " equipped") : (System.Action)null));
            }

            root.Add(Ui.SectionHeader("Display"));
            bool reduced = Game.Showcase.Settings.ReducedEffects;
            root.Add(Ui.ListRow("Reduced effects", "Turns off shine and animation on cards and in the showcase", reduced ? "On" : "Off",
                () => { Game.Showcase.SetReducedEffects(!reduced); Shell.AfterChange(); }));
            root.Add(Ui.Button("Customize showcase", () => Shell.Push(new ShowcaseCustomizeScreen()), Ui.ButtonKind.Secondary));

            root.Add(Ui.SectionHeader("Game time"));
            root.Add(Ui.Muted("Today is " + Fmt.Date(Game, Game.Clock.Now) + " (day " + Game.Clock.Now / Core.Common.GameClock.SecondsPerDay + ")."));
            root.Add(Ui.Muted("Developer: the accelerated calendar advances the offline clock to test releases and shows."));
            root.Add(Ui.Row(
                Ui.Button("Advance 1 day", () => { Game.AdvanceGameTime(Core.Common.GameClock.SecondsPerDay); Shell.AfterChange(); }, Ui.ButtonKind.Secondary),
                Ui.Button("Advance 7 days", () => { Game.AdvanceGameTime(7 * Core.Common.GameClock.SecondsPerDay); Shell.AfterChange(); }, Ui.ButtonKind.Secondary)));
        }
    }

    /// <summary>Inbox. Tapping a notification marks it read and opens what it refers to.</summary>
    public sealed class NotificationsScreen : GameScreen
    {
        public override string Title => "Inbox";

        public override void Build(VisualElement root)
        {
            var items = Game.State.Notifications.OrderByDescending(n => n.CreatedAt).Take(100).ToList();
            root.Add(Ui.Button("Mark all read", () => { Game.MarkAllNotificationsRead(); Shell.AfterChange(); }, Ui.ButtonKind.Secondary,
                items.Any(n => !n.Read)));
            if (items.Count == 0) root.Add(Ui.Muted("No notifications yet."));
            foreach (var n in items)
            {
                var note = n;
                root.Add(Ui.ListRow(n.Title, n.Body + " · " + Fmt.Date(Game, n.CreatedAt), n.Read ? null : "New",
                    () => Open(note), n.Read ? (UnityEngine.Color?)null : Theme.Info));
            }
        }

        private void Open(GameNotification n)
        {
            Game.MarkNotificationRead(n.NotificationId);
            Shell.AfterChange();
            var target = Target(n);
            if (target != null) Shell.Push(target);
        }

        private GameScreen Target(GameNotification n)
        {
            var id = n.RefId;
            switch (n.Kind)
            {
                case NotificationKind.SetMilestone when id != null && Game.Content.Sets.ContainsKey(id):
                    return new SetDetailScreen(id);
                case NotificationKind.ReleaseReminder when id != null && Game.Content.Releases.ContainsKey(id):
                    return new ReleaseDetailScreen(id);
                case NotificationKind.CardShow when id != null && Game.CardShows.Occurrence(id) != null:
                    return new CardShowDetailScreen(id);
                case NotificationKind.Achievement:
                case NotificationKind.RankUp:
                    Shell.SwitchTab(4);
                    return null;
            }
            if (id != null && Game.Inventory.Get(id) != null) return new CardDetailScreen(id);
            return null;
        }
    }
}
