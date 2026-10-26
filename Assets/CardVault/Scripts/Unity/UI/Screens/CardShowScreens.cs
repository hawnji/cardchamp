using System.Collections.Generic;
using System.Linq;
using CardVault.Core.CardShows;
using CardVault.Core.Rewards;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    /// <summary>Schedule of card shows: what is open now, what is coming and what has closed.</summary>
    public sealed class CardShowsScreen : GameScreen
    {
        private static readonly string[] Tabs = { "Active", "Upcoming", "Past" };
        private int _tab;

        public override string Title => "Card Shows";

        public override void Build(VisualElement root)
        {
            root.Add(Ui.Tabs(Tabs, _tab, i => { _tab = i; Shell.Refresh(); }));
            switch (_tab)
            {
                case 0: BuildOccurrences(root, Game.CardShows.Active(), "No shows are open today."); break;
                case 1: BuildOccurrences(root, Game.CardShows.Upcoming(28), "Nothing scheduled in the next four weeks."); break;
                default: BuildPast(root); break;
            }
        }

        private void BuildOccurrences(VisualElement root, IReadOnlyList<EventOccurrence> list, string empty)
        {
            if (list.Count == 0) root.Add(Ui.Muted(empty));
            foreach (var o in list)
            {
                var occ = o;
                var status = Game.CardShows.StatusOf(occ);
                var p = Game.CardShows.Participation(occ.OccurrenceId);
                string when = status == OccurrenceStatus.Active
                    ? "Open until " + Fmt.Date(Game, occ.EndsAt)
                    : "Opens " + Fmt.Date(Game, occ.StartsAt);
                string trailing = status == OccurrenceStatus.Active ? (p?.Visited == true ? p.PointsAvailable + " pts" : "Visit") : "Soon";
                root.Add(Ui.ListRow(occ.Definition.Name, when + " · " + occ.Definition.Description, trailing,
                    () => Shell.Push(new CardShowDetailScreen(occ.OccurrenceId)),
                    status == OccurrenceStatus.Active ? Theme.Info : (UnityEngine.Color?)null));
            }
        }

        private void BuildPast(VisualElement root)
        {
            var history = Game.CardShows.History;
            if (history.Count == 0) root.Add(Ui.Muted("Shows you visit are recorded here once they close."));
            foreach (var h in history)
            {
                var occId = h.OccurrenceId;
                root.Add(Ui.ListRow(h.Name, Fmt.Date(Game, h.StartsAt) + " · " + h.ChallengesCompleted + "/" + h.ChallengesTotal +
                                            " challenges · " + h.PointsEarned + " points",
                    h.Completed ? "Completed" : null, () => Shell.Push(new CardShowDetailScreen(occId)),
                    h.Completed ? Theme.Positive : (UnityEngine.Color?)null));
            }
        }
    }

    /// <summary>One show occurrence: entry, vendor booths, challenges and the reward counter.</summary>
    public sealed class CardShowDetailScreen : GameScreen
    {
        private static readonly string[] Tabs = { "Vendors", "Challenges", "Rewards" };
        private readonly string _occurrenceId;
        private int _tab;

        public CardShowDetailScreen(string occurrenceId)
        {
            _occurrenceId = occurrenceId;
        }

        public override string Title => "Card Show";

        public override void Build(VisualElement root)
        {
            var occ = Game.CardShows.Occurrence(_occurrenceId);
            if (occ == null)
            {
                root.Add(Ui.Muted("This show could not be found."));
                return;
            }
            var def = occ.Definition;
            var status = Game.CardShows.StatusOf(occ);
            var p = Game.CardShows.Participation(_occurrenceId);
            bool visited = p?.Visited == true;

            var header = Ui.Panel(
                Ui.Badge(status == OccurrenceStatus.Active ? "Open now" : status == OccurrenceStatus.Upcoming ? "Upcoming" : "Closed",
                    status == OccurrenceStatus.Active ? Theme.Positive : Theme.TextMuted),
                Ui.Spacer(8),
                Ui.Text(def.Name, Theme.FontTitle, bold: true),
                Ui.Muted(Fmt.Date(Game, occ.StartsAt) + " to " + Fmt.Date(Game, occ.EndsAt)),
                Ui.Text(def.Description, Theme.FontSmall));
            if (def.FeaturedSetIds.Count > 0)
                header.Add(Ui.Muted("Featured: " + string.Join(", ", def.FeaturedSetIds.Select(id => Game.Content.Sets[id].Name)) +
                                    " (discounted at every booth)"));
            if (visited)
                header.Add(Ui.Text(p.PointsAvailable + " event points · " + Game.State.Wallet.EventTickets + " tickets",
                    Theme.FontBody, Theme.Accent, bold: true));
            root.Add(header);

            if (status == OccurrenceStatus.Active && !visited)
            {
                root.Add(Ui.Button("Enter the show", () => Shell.Run(() => Game.CardShows.Visit(_occurrenceId).AsResult(),
                    "Welcome to " + def.Name)));
                root.Add(Ui.Muted("Entering sets up the vendor booths and starts tracking challenges."));
            }
            else if (status == OccurrenceStatus.Upcoming)
            {
                root.Add(Ui.Muted("Booths open on " + Fmt.Date(Game, occ.StartsAt) + ". Here is a preview of what to expect."));
            }

            root.Add(Ui.Tabs(Tabs, _tab, i => { _tab = i; Shell.Refresh(); }));
            switch (_tab)
            {
                case 0: BuildVendors(root, occ, status == OccurrenceStatus.Active && visited); break;
                case 1: BuildChallenges(root, occ, visited); break;
                default: BuildRewards(root, occ, p); break;
            }
        }

        private void BuildVendors(VisualElement root, EventOccurrence occ, bool canBuy)
        {
            foreach (var vendor in occ.Definition.Vendors)
            {
                var v = vendor;
                var npcName = Game.Content.Npcs.TryGetValue(v.NpcId, out var npc) ? npc.Name : v.NpcId;
                var panel = Ui.Panel(Ui.Text(v.BoothName, Theme.FontBody, bold: true), Ui.Muted(npcName + " · " + v.Description));
                foreach (var s in Game.CardShows.VendorStock(occ.OccurrenceId, v.Id))
                {
                    var stock = s;
                    string price = Fmt.Credits(s.PriceCredits) + (s.PriceTickets > 0 ? " + " + s.PriceTickets + " tickets" : "");
                    string sub = s.Locked ? "Requires collector access" : s.Remaining + " left";
                    var cardId = s.NextCardInstanceId;
                    var row = Ui.ListRow(s.DisplayName, sub, price,
                        cardId != null ? () => Shell.Push(new CardDetailScreen(cardId)) : (System.Action)null);
                    row.Add(Ui.Button(s.Remaining > 0 ? "Buy" : "Sold out",
                        () => Shell.Run(() => Game.CardShows.BuyFromVendor(occ.OccurrenceId, v.Id, stock.Definition.Id), stock.DisplayName + " purchased"),
                        Ui.ButtonKind.Secondary, canBuy && s.Remaining > 0 && !s.Locked));
                    panel.Add(row);
                }
                root.Add(panel);
            }
        }

        private void BuildChallenges(VisualElement root, EventOccurrence occ, bool visited)
        {
            if (!visited) root.Add(Ui.Muted("Challenges count actions you take while the show is open, after you enter."));
            foreach (var c in Game.CardShows.Challenges(occ.OccurrenceId))
            {
                var panel = Ui.Panel(
                    Ui.Text(c.Definition.Description, Theme.FontBody, c.Completed ? Theme.Positive : (UnityEngine.Color?)null, bold: true),
                    Ui.Muted(c.Completed ? "Complete · +" + c.Definition.Points + " points" : c.Progress + " / " + c.Target + " · " + c.Definition.Points + " points"),
                    Ui.ProgressBar(c.Target > 0 ? c.Progress / (double)c.Target : 1.0, height: 12));
                root.Add(panel);
            }
            var completion = occ.Definition.CompletionReward;
            root.Add(Ui.Muted("Finish every challenge for a bonus: " + RewardService.Describe(completion, Game.Content)));
        }

        private void BuildRewards(VisualElement root, EventOccurrence occ, Core.State.EventParticipation p)
        {
            bool open = Game.Clock.Now < occ.EndsAt + CardShowService.RedemptionGraceSeconds;
            root.Add(Ui.Muted(open
                ? "Redeem event points until " + Fmt.Date(Game, occ.EndsAt + CardShowService.RedemptionGraceSeconds) + "."
                : "The reward counter for this show has closed."));
            foreach (var r in occ.Definition.Rewards)
            {
                var reward = r;
                bool redeemed = p != null && p.RedeemedRewards.Contains(r.Id);
                bool affordable = p != null && p.Visited && p.PointsAvailable >= r.CostPoints;
                var panel = Ui.Panel(
                    Ui.Text(r.Name, Theme.FontBody, bold: true),
                    Ui.Muted(RewardService.Describe(r.Reward, Game.Content)),
                    Ui.Button(redeemed ? "Redeemed" : "Redeem for " + r.CostPoints + " points",
                        () => Shell.Run(() => Game.CardShows.Redeem(occ.OccurrenceId, reward.Id).AsResult(), reward.Name + " redeemed"),
                        enabled: open && !redeemed && affordable));
                root.Add(panel);
            }
        }
    }
}
