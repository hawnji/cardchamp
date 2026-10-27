using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    internal static class Fmt
    {
        public static string Credits(long credits) => credits.ToString("N0") + " cr";
        public static string Date(CardVaultGame game, long gameSeconds) => game.Clock.ToCalendarDate(gameSeconds).ToString("MMM d, yyyy");
        public static string Percent(double pct) => Math.Floor(pct).ToString("0") + "%";
    }

    public enum CardFilter { All, Sports, Tcg, Graded, Serial, Vintage, Rookie }

    internal static class CardFilters
    {
        public static readonly CardFilter[] All = (CardFilter[])Enum.GetValues(typeof(CardFilter));

        public static string Label(CardFilter f) => f == CardFilter.Tcg ? "TCG" : f.ToString();

        public static bool Matches(CardVaultGame game, CardInstance c, CardFilter f)
        {
            var set = game.Content.Sets[c.OriginSetId ?? game.Content.Cards[c.TemplateId].SetId];
            switch (f)
            {
                case CardFilter.Sports: return set.Category == CardCategory.Sports;
                case CardFilter.Tcg: return set.Category == CardCategory.Tcg;
                case CardFilter.Graded: return c.IsGraded;
                case CardFilter.Serial: return c.IsSerialNumbered;
                case CardFilter.Vintage: return set.IsVintage;
                case CardFilter.Rookie: return game.Content.Cards[c.TemplateId].IsRookie;
                default: return true;
            }
        }

        /// <summary>Most valuable first, so the cards players want to show off are on top.</summary>
        public static IEnumerable<CardInstance> Sorted(CardVaultGame game, IEnumerable<CardInstance> cards)
            => cards.OrderByDescending(c => game.Prices.EstimateValue(c)).ThenBy(c => c.InstanceId, StringComparer.Ordinal);
    }

    /// <summary>Renders a page of cards using pooled views. Pagination keeps the visible set small.</summary>
    internal sealed class CardGrid
    {
        public const int PageSize = 24;

        private readonly CardViewPool _pool = new CardViewPool();

        public void Build(AppShell shell, VisualElement root, IReadOnlyList<CardInstance> cards, int page, Action<int> onPage,
            Action<CardInstance> onTap, float cardWidth = 300)
        {
            _pool.ReleaseAll();
            if (cards.Count == 0)
            {
                root.Add(Ui.Muted("No cards here yet."));
                return;
            }
            int pages = (cards.Count + PageSize - 1) / PageSize;
            page = Math.Max(0, Math.Min(page, pages - 1));
            var grid = Ui.Grid(33.3f);
            foreach (var card in cards.Skip(page * PageSize).Take(PageSize))
            {
                var view = _pool.Get();
                view.Bind(shell.Game, card, cardWidth, shell.ReducedEffects);
                var c = card;
                view.OnTap(() => onTap(c));
                Ui.AddToGrid(grid, view);
            }
            root.Add(grid);
            if (pages > 1)
            {
                root.Add(Ui.Row(
                    Ui.Button("Previous", () => onPage(page - 1), Ui.ButtonKind.Secondary, page > 0),
                    Ui.Spacer(),
                    Ui.Muted("Page " + (page + 1) + " of " + pages),
                    Ui.Spacer(),
                    Ui.Button("Next", () => onPage(page + 1), Ui.ButtonKind.Secondary, page < pages - 1)));
            }
        }

        public void Release() => _pool.ReleaseAll();
    }

    /// <summary>Bottom sheet for choosing one of the player's cards, with filters.</summary>
    internal static class CardPicker
    {
        public static void Show(AppShell shell, string title, IEnumerable<CardInstance> source, Action<CardInstance> onPick)
        {
            var all = CardFilters.Sorted(shell.Game, source).ToList();
            var body = Ui.Column();
            var grid = new CardGrid();
            var filter = CardFilter.All;
            int page = 0;
            void Render()
            {
                body.Clear();
                body.Add(Ui.Tabs(CardFilters.All.Select(CardFilters.Label).ToList(), (int)filter, i =>
                {
                    filter = (CardFilter)i;
                    page = 0;
                    Render();
                }));
                var cards = all.Where(c => CardFilters.Matches(shell.Game, c, filter)).ToList();
                grid.Build(shell, body, cards, page, p => { page = p; Render(); }, c =>
                {
                    grid.Release();
                    shell.CloseModal();
                    onPick(c);
                }, 280);
            }
            Render();
            shell.ShowModal(title, body);
        }
    }

    /// <summary>The first-time full set completion moment.</summary>
    internal static class SetCompletionCelebration
    {
        public static void Show(AppShell shell, string setId)
        {
            var game = shell.Game;
            if (!game.Content.Sets.TryGetValue(setId, out var set)) return;
            game.SetRewards.MarkCelebrated(setId);

            var body = Ui.Column();
            body.style.alignItems = Align.Center;
            var badge = Ui.Text("SET COMPLETE", Theme.FontHero, Theme.Background, bold: true, wrap: false);
            badge.style.backgroundColor = Theme.Accent;
            Theme.Round(badge, 40);
            Theme.Padding(badge, 24, 48);
            Theme.BorderColor(badge, Color.white, 6);
            body.Add(badge);
            body.Add(Ui.Spacer(24));
            body.Add(Ui.Title(set.Name));
            var record = game.Sets.ExistingRecord(setId);
            if (record?.FirstCompletedAt != null) body.Add(Ui.Muted("Completed " + Fmt.Date(game, record.FirstCompletedAt.Value)));
            body.Add(Ui.Spacer(24));
            body.Add(Ui.Button("View set and claim rewards", () =>
            {
                shell.CloseModal();
                shell.Push(new SetDetailScreen(setId));
            }));

            if (!shell.ReducedEffects)
            {
                float t = 0;
                badge.schedule.Execute(() =>
                {
                    t += 0.08f;
                    float s = 1f + 0.06f * Mathf.Sin(t * 3f);
                    badge.style.scale = new Scale(new Vector3(s, s, 1));
                    badge.style.backgroundColor = Color.Lerp(Theme.Accent, Color.white, 0.25f * (0.5f + 0.5f * Mathf.Sin(t * 5f)));
                }).Every(33).Until(() => badge.panel == null);
            }
            shell.ShowModal("Congratulations", body);
        }
    }
}
