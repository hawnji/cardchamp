using System.Linq;
using CardVault.Core.Content;
using CardVault.Core.Showcase;
using CardVault.Core.State;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI.Screens
{
    /// <summary>
    /// The collector's display room: a layered 2D presentation (wall, shelves, pedestal, light)
    /// built from pooled card views. Only displayed cards are rendered and only their art loads.
    /// </summary>
    public sealed class ShowcaseScreen : GameScreen
    {
        private readonly CardViewPool _pool = new CardViewPool();

        public override string Title => "Collector's Showcase";

        public override void Teardown() => _pool.ReleaseAll();

        private CosmeticDef Cosmetic(string id) => id != null && Game.Content.Cosmetics.TryGetValue(id, out var c) ? c : null;

        public override void Build(VisualElement root)
        {
            var settings = Game.Showcase.Settings;
            var background = Cosmetic(settings.BackgroundId);
            var wall = Cosmetic(settings.WallId);
            var shelf = Cosmetic(settings.ShelfId);
            var pedestal = Cosmetic(settings.PedestalId);
            var frame = Cosmetic(settings.FrameId);
            bool effects = settings.DecorativeEffectsEnabled && !settings.ReducedEffects;

            root.Add(Ui.Row(Ui.Button("Customize", () => Shell.Push(new ShowcaseCustomizeScreen())), Ui.Spacer(16),
                Ui.Muted(background?.Name + " · " + settings.Layout)));

            var room = Ui.Column();
            room.style.backgroundColor = Theme.Hex(background?.ColorHex, Theme.Surface);
            Theme.Round(room, 28);
            Theme.Padding(room, 28, 20);
            Theme.BorderColor(room, Theme.Hex(background?.AccentHex, Theme.Border), 4);
            root.Add(room);

            foreach (var area in Game.Showcase.Areas())
            {
                if (settings.Layout == ShowcaseLayout.Spotlight &&
                    area.Definition.Kind != ShowcaseAreaKind.FeaturedPedestal && area.Definition.Kind != ShowcaseAreaKind.MainWall) continue;

                var section = Ui.Column();
                section.style.marginBottom = 28;
                section.style.backgroundColor = Theme.Hex(wall?.ColorHex, Theme.SurfaceRaised);
                Theme.Round(section, 20);
                Theme.Padding(section, 20, 16);
                section.Add(Ui.Row(Ui.Text(area.Definition.Name, Theme.FontBody, Theme.Hex(background?.AccentHex, Theme.Accent), bold: true),
                    Ui.Spacer(), Ui.Muted(area.Unlocked ? area.Filled + "/" + area.SlotCount : "Locked")));

                if (!area.Unlocked)
                {
                    section.Add(Ui.Muted(area.UnlockHint));
                    room.Add(section);
                    continue;
                }

                bool featured = area.Definition.Kind == ShowcaseAreaKind.FeaturedPedestal;
                float width = featured ? 520 : settings.Layout == ShowcaseLayout.Grid ? 220 : 290;
                var slots = Ui.Row();
                slots.style.flexWrap = Wrap.Wrap;
                slots.style.justifyContent = featured ? Justify.Center : Justify.FlexStart;
                for (int i = 0; i < area.SlotCount; i++)
                {
                    int slot = i;
                    string item = area.Slots[i];
                    var holder = Ui.Column();
                    holder.style.alignItems = Align.Center;
                    Theme.Margin(holder, 8, 8);

                    if (effects && featured)
                    {
                        var light = new VisualElement();
                        light.style.width = width + 80;
                        light.style.height = 30;
                        light.style.backgroundColor = new Color(1f, 0.96f, 0.8f, 0.18f);
                        Theme.Round(light, 15);
                        holder.Add(light);
                    }

                    if (item == null) holder.Add(EmptySlot(area.Definition, slot, width));
                    else if (area.Definition.Kind == ShowcaseAreaKind.CompletedSets) holder.Add(SetPlaque(area.Definition, slot, item, width));
                    else
                    {
                        var card = Game.Inventory.Get(item);
                        var view = _pool.Get();
                        view.Bind(Game, card, width, Shell.ReducedEffects);
                        if (frame != null && !card.IsGraded) Theme.BorderColor(view, Theme.Hex(frame.ColorHex), 6);
                        if (card.IsSerialNumbered && effects) view.style.borderBottomColor = Theme.Accent;
                        view.OnTap(() => SlotOptions(area.Definition, slot, card));
                        holder.Add(view);
                    }

                    var ledge = new VisualElement();
                    ledge.style.height = featured ? 46 : 22;
                    ledge.style.width = width + 24;
                    var ledgeCosmetic = featured ? pedestal : shelf;
                    ledge.style.backgroundColor = Theme.Hex(ledgeCosmetic?.ColorHex, Theme.Border);
                    ledge.style.borderBottomColor = Theme.Hex(ledgeCosmetic?.AccentHex, Theme.Border);
                    ledge.style.borderBottomWidth = 8;
                    Theme.Round(ledge, 8);
                    holder.Add(ledge);
                    slots.Add(holder);
                }
                section.Add(slots);
                room.Add(section);
            }
        }

        private VisualElement EmptySlot(ShowcaseAreaDef area, int slot, float width)
        {
            var empty = Ui.Column(Ui.Text("+", Theme.FontHero, Theme.TextMuted, bold: true), Ui.Muted("Add"));
            empty.style.width = width;
            empty.style.height = width * 1.4f;
            empty.style.justifyContent = Justify.Center;
            empty.style.alignItems = Align.Center;
            Theme.Round(empty, 18);
            Theme.BorderColor(empty, Theme.Border, 3);
            empty.RegisterCallback<ClickEvent>(_ => Pick(area, slot));
            return empty;
        }

        private VisualElement SetPlaque(ShowcaseAreaDef area, int slot, string setId, float width)
        {
            var set = Game.Content.Sets[setId];
            var record = Game.Sets.ExistingRecord(setId);
            var plaque = Ui.Column(Ui.Badge("COMPLETE", Theme.Accent), Ui.Text(set.Name, Theme.FontSmall, bold: true),
                record?.FirstCompletedAt != null ? Ui.Muted(Fmt.Date(Game, record.FirstCompletedAt.Value)) : null);
            plaque.style.width = width;
            plaque.style.height = width * 0.8f;
            plaque.style.justifyContent = Justify.Center;
            plaque.style.alignItems = Align.Center;
            plaque.style.backgroundColor = Theme.Hex("#2A2416");
            Theme.Round(plaque, 18);
            Theme.BorderColor(plaque, Theme.Accent, 4);
            plaque.RegisterCallback<ClickEvent>(_ =>
            {
                var body = Ui.Column(
                    Ui.Button("View set", () => { Shell.CloseModal(); Shell.Push(new SetDetailScreen(setId)); }),
                    Ui.Button("Remove plaque", () => { Shell.CloseModal(); Shell.Run(() => Game.Showcase.Remove(area.Id, slot)); }, Ui.ButtonKind.Secondary));
                Shell.ShowModal(set.Name, body);
            });
            return plaque;
        }

        private void Pick(ShowcaseAreaDef area, int slot)
        {
            if (area.Kind == ShowcaseAreaKind.CompletedSets)
            {
                var body = Ui.Column();
                var sets = Game.Showcase.EligibleSets().ToList();
                if (sets.Count == 0) body.Add(Ui.Muted("Complete a set to display it here."));
                foreach (var id in sets)
                {
                    var setId = id;
                    body.Add(Ui.ListRow(Game.Content.Sets[id].Name, null, "Display", () =>
                    {
                        Shell.CloseModal();
                        Shell.Run(() => Game.Showcase.Place(area.Id, slot, setId), "Plaque placed");
                    }));
                }
                Shell.ShowModal("Choose a completed set", body);
                return;
            }
            var eligible = Game.Showcase.EligibleCards(area.Id).ToList();
            if (eligible.Count == 0)
            {
                Shell.Toast(ShowcaseService.EligibilityHint(area));
                return;
            }
            CardPicker.Show(Shell, "Display in " + area.Name, eligible,
                c => Shell.Run(() => Game.Showcase.Place(area.Id, slot, c.InstanceId), "On display"));
        }

        private void SlotOptions(ShowcaseAreaDef area, int slot, CardInstance card)
        {
            var body = Ui.Column(
                Ui.Text(Game.Catalog.DisplayName(card), Theme.FontBody, bold: true),
                Ui.Button("View card details", () => { Shell.CloseModal(); Shell.Push(new CardDetailScreen(card.InstanceId)); }),
                Ui.Button("Make featured card", () => { Shell.CloseModal(); Shell.Run(() => Game.Showcase.SetFeatured(card.InstanceId), "Featured"); },
                    Ui.ButtonKind.Secondary, area.Kind != ShowcaseAreaKind.FeaturedPedestal),
                Ui.Button("Move left", () => { Shell.CloseModal(); Shell.Run(() => Game.Showcase.Swap(area.Id, slot, slot - 1)); }, Ui.ButtonKind.Secondary, slot > 0),
                Ui.Button("Move right", () => { Shell.CloseModal(); Shell.Run(() => Game.Showcase.Swap(area.Id, slot, slot + 1)); },
                    Ui.ButtonKind.Secondary, slot + 1 < Game.Showcase.SlotCount(area)),
                Ui.Button("Replace", () => { Shell.CloseModal(); Pick(area, slot); }, Ui.ButtonKind.Secondary),
                Ui.Button("Remove from display", () => { Shell.CloseModal(); Shell.Run(() => Game.Showcase.Remove(area.Id, slot), "Removed. The card is still in your collection."); },
                    Ui.ButtonKind.Danger));
            Shell.ShowModal(area.Name, body);
        }
    }

    public sealed class ShowcaseCustomizeScreen : GameScreen
    {
        private static readonly (CosmeticKind kind, string label)[] Kinds =
        {
            (CosmeticKind.ShowcaseBackground, "Background theme"),
            (CosmeticKind.WallStyle, "Wall"),
            (CosmeticKind.ShelfStyle, "Shelves"),
            (CosmeticKind.DisplayPedestal, "Pedestal"),
            (CosmeticKind.CardFrame, "Card frame"),
            (CosmeticKind.DecorativeEffect, "Decorative effect"),
        };

        public override string Title => "Customize Showcase";

        public override void Build(VisualElement root)
        {
            var s = Game.Showcase.Settings;
            root.Add(Ui.SectionHeader("Layout"));
            root.Add(Ui.Tabs(new[] { "Gallery", "Grid", "Spotlight" }, (int)s.Layout, i =>
            {
                Game.Showcase.SetLayout((ShowcaseLayout)i);
                Shell.AfterChange();
            }));
            root.Add(Ui.Button(s.DecorativeEffectsEnabled ? "Decorative effects: on" : "Decorative effects: off", () =>
            {
                Game.Showcase.SetDecorativeEffects(!s.DecorativeEffectsEnabled);
                Shell.AfterChange();
            }, Ui.ButtonKind.Secondary));
            root.Add(Ui.Button(s.ReducedEffects ? "Reduced motion: on" : "Reduced motion: off", () =>
            {
                Game.Showcase.SetReducedEffects(!s.ReducedEffects);
                Shell.AfterChange();
            }, Ui.ButtonKind.Secondary));

            foreach (var (kind, label) in Kinds)
            {
                root.Add(Ui.SectionHeader(label));
                string equipped = Equipped(kind);
                var all = Game.Content.Pack.Cosmetics.Where(c => c.Kind == kind).ToList();
                foreach (var c in all)
                {
                    var cosmetic = c;
                    bool unlocked = Game.Rewards.IsCosmeticUnlocked(c.Id);
                    var row = Ui.ListRow(c.Name, unlocked ? (c.Id == equipped ? "Equipped" : "Unlocked") : "Earn it through sets, shows or achievements",
                        unlocked && c.Id != equipped ? "Equip" : null,
                        unlocked && c.Id != equipped ? () => Shell.Run(() => Game.Showcase.SetCosmetic(kind, cosmetic.Id), cosmetic.Name + " equipped") : (System.Action)null,
                        Theme.Hex(c.ColorHex));
                    if (!unlocked) row.style.opacity = 0.5f;
                    root.Add(row);
                }
            }
        }

        private string Equipped(CosmeticKind kind)
        {
            var s = Game.Showcase.Settings;
            switch (kind)
            {
                case CosmeticKind.ShowcaseBackground: return s.BackgroundId;
                case CosmeticKind.WallStyle: return s.WallId;
                case CosmeticKind.ShelfStyle: return s.ShelfId;
                case CosmeticKind.DisplayPedestal: return s.PedestalId;
                case CosmeticKind.CardFrame: return s.FrameId;
                case CosmeticKind.DecorativeEffect: return s.EffectId;
                default: return null;
            }
        }
    }
}
