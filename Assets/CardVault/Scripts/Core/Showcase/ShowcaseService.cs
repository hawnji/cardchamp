using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.Progression;
using CardVault.Core.State;

namespace CardVault.Core.Showcase
{
    public sealed class ShowcaseAreaView
    {
        public ShowcaseAreaDef Definition;
        public bool Unlocked;
        public string UnlockHint;
        public int SlotCount;
        /// <summary>Card instance IDs (set IDs for the completed-sets area); null for empty slots.</summary>
        public IReadOnlyList<string> Slots;
        public int Filled => Slots.Count(s => s != null);
    }

    /// <summary>
    /// Collector's showcase. Slots hold references (card instance IDs) to cards the player
    /// owns; nothing is copied, so displaying a card never duplicates it or removes it from the
    /// inventory. When a card leaves the player's ownership it is cleared from every slot.
    /// </summary>
    public sealed class ShowcaseService
    {
        public const string FeaturedAreaId = "featured";

        private readonly GameContext _ctx;
        private readonly UnlockEvaluator _unlocks;

        public event Action Changed;

        public ShowcaseService(GameContext ctx, UnlockEvaluator unlocks)
        {
            _ctx = ctx;
            _unlocks = unlocks;
            _ctx.Events.OwnershipChanged += OnOwnershipChanged;
            EnsureState();
        }

        private ShowcaseState State => _ctx.State.Showcase;
        private ShowcaseConfig Config => _ctx.Content.Pack.Showcase;

        public ShowcaseState Settings => State;

        // ------------------------------------------------------------- structure

        public void EnsureState()
        {
            State.BackgroundId = State.BackgroundId ?? Config.DefaultBackgroundId;
            State.FrameId = State.FrameId ?? Config.DefaultFrameId;
            State.PedestalId = State.PedestalId ?? Config.DefaultPedestalId;
            State.ShelfId = State.ShelfId ?? Config.DefaultShelfId;
            State.WallId = State.WallId ?? Config.DefaultWallId;
            foreach (var area in Config.Areas)
            {
                if (!State.Areas.TryGetValue(area.Id, out var s))
                {
                    s = new ShowcaseAreaState { AreaId = area.Id };
                    State.Areas[area.Id] = s;
                }
                int count = SlotCount(area);
                while (s.Slots.Count < count) s.Slots.Add(null);
            }
        }

        public ShowcaseAreaDef Area(string areaId) => Config.Areas.FirstOrDefault(a => a.Id == areaId);

        public bool IsUnlocked(ShowcaseAreaDef area) => _unlocks.IsMet(area.AreaUnlock);

        public int SlotCount(ShowcaseAreaDef area)
        {
            int count = area.BaseSlots;
            foreach (var extra in area.ExtraSlots)
                if (_unlocks.IsMet(extra.Condition)) count += extra.Slots;
            if (area.Kind == ShowcaseAreaKind.MainWall) count += _ctx.State.Profile.BonusShowcaseSlots;
            return count;
        }

        public IReadOnlyList<ShowcaseAreaView> Areas()
        {
            EnsureState();
            return Config.Areas.Select(a =>
            {
                int count = SlotCount(a);
                return new ShowcaseAreaView
                {
                    Definition = a,
                    Unlocked = IsUnlocked(a),
                    UnlockHint = _unlocks.Describe(a.AreaUnlock),
                    SlotCount = count,
                    Slots = State.Areas[a.Id].Slots.Take(count).ToList(),
                };
            }).ToList();
        }

        /// <summary>Every card currently on display (for loading only the artwork that is needed).</summary>
        public IReadOnlyCollection<string> DisplayedCardIds
            => State.Areas.Where(kv => Area(kv.Key)?.Kind != ShowcaseAreaKind.CompletedSets)
                .SelectMany(kv => kv.Value.Slots).Where(id => id != null).Distinct().ToList();

        public string FeaturedCardId => State.Areas.TryGetValue(FeaturedAreaId, out var a) ? a.Slots.FirstOrDefault() : null;

        public (string areaId, int slot)? LocationOf(string itemId)
        {
            foreach (var kv in State.Areas)
            {
                int i = kv.Value.Slots.IndexOf(itemId);
                if (i >= 0) return (kv.Key, i);
            }
            return null;
        }

        // ------------------------------------------------------------- placement

        public OpResult CanPlace(string areaId, int slotIndex, string itemId)
        {
            var area = Area(areaId);
            if (area == null) return OpResult.Fail(OpError.NotFound, "Display area not found.");
            if (!IsUnlocked(area)) return OpResult.Fail(OpError.Locked, "Locked: " + _unlocks.Describe(area.AreaUnlock) + ".");
            if (slotIndex < 0 || slotIndex >= SlotCount(area)) return OpResult.Fail(OpError.Locked, "This slot is not unlocked yet.");

            if (area.Kind == ShowcaseAreaKind.CompletedSets)
            {
                if (!_ctx.Content.Sets.ContainsKey(itemId ?? "")) return OpResult.Fail(OpError.NotFound, "Set not found.");
                var record = _ctx.State.SetRewards.Sets.TryGetValue(itemId, out var r) ? r : null;
                if (record?.FirstCompletedAt == null) return OpResult.Fail(OpError.NotEligible, "Only completed sets can be displayed here.");
                return OpResult.Success();
            }

            if (!_ctx.State.Cards.TryGetValue(itemId ?? "", out var card)) return OpResult.Fail(OpError.NotFound, "Card not found.");
            if (card.OwnerId != Owners.Player) return OpResult.Fail(OpError.NotOwned, "You can only display cards you own.");
            if (card.Status == CardStatus.InGrading) return OpResult.Fail(OpError.CardBusy, "This card is at the grading service.");
            return IsEligible(area, card)
                ? OpResult.Success()
                : OpResult.Fail(OpError.NotEligible, EligibilityHint(area));
        }

        public bool IsEligible(ShowcaseAreaDef area, CardInstance card)
        {
            var set = _ctx.Content.Sets[card.OriginSetId ?? _ctx.Content.Cards[card.TemplateId].SetId];
            switch (area.Kind)
            {
                case ShowcaseAreaKind.GradedShelf: return card.IsGraded;
                case ShowcaseAreaKind.VintageCabinet: return set.IsVintage;
                case ShowcaseAreaKind.SportsSection: return set.Category == CardCategory.Sports;
                case ShowcaseAreaKind.TcgSection: return set.Category == CardCategory.Tcg;
                case ShowcaseAreaKind.OneOfOne: return card.IsOneOfOne;
                case ShowcaseAreaKind.CompletedSets: return false;
                default: return true;
            }
        }

        public static string EligibilityHint(ShowcaseAreaDef area)
        {
            switch (area.Kind)
            {
                case ShowcaseAreaKind.GradedShelf: return "Only graded cards fit on the slab shelf.";
                case ShowcaseAreaKind.VintageCabinet: return "Only vintage cards go in the cabinet.";
                case ShowcaseAreaKind.SportsSection: return "Only sports cards belong in this section.";
                case ShowcaseAreaKind.TcgSection: return "Only TCG cards belong in this section.";
                case ShowcaseAreaKind.OneOfOne: return "Only genuine one-of-one cards qualify.";
                default: return "This card cannot be displayed here.";
            }
        }

        /// <summary>Owned cards eligible for an area, for the selection menu (filtering is the caller's).</summary>
        public IEnumerable<CardInstance> EligibleCards(string areaId)
        {
            var area = Area(areaId);
            if (area == null || area.Kind == ShowcaseAreaKind.CompletedSets) return Enumerable.Empty<CardInstance>();
            return _ctx.State.Cards.Values.Where(c => c.OwnerId == Owners.Player && c.Status != CardStatus.InGrading && IsEligible(area, c));
        }

        public IEnumerable<string> EligibleSets()
            => _ctx.State.SetRewards.Sets.Values.Where(r => r.FirstCompletedAt.HasValue).Select(r => r.SetId);

        /// <summary>Places an item. If it is displayed elsewhere it moves (one location per card).</summary>
        public OpResult Place(string areaId, int slotIndex, string itemId)
        {
            var check = CanPlace(areaId, slotIndex, itemId);
            if (!check.Ok) return check;
            EnsureState();
            var existing = LocationOf(itemId);
            if (existing.HasValue) State.Areas[existing.Value.areaId].Slots[existing.Value.slot] = null;
            State.Areas[areaId].Slots[slotIndex] = itemId;
            RaiseChanged();
            return OpResult.Success();
        }

        public OpResult PlaceInFirstFreeSlot(string areaId, string itemId)
        {
            var area = Area(areaId);
            if (area == null) return OpResult.Fail(OpError.NotFound, "Display area not found.");
            EnsureState();
            var slots = State.Areas[areaId].Slots;
            int count = SlotCount(area);
            for (int i = 0; i < count; i++)
                if (slots[i] == null) return Place(areaId, i, itemId);
            return OpResult.Fail(OpError.OutOfStock, "This display area is full.");
        }

        public OpResult SetFeatured(string cardId) => Place(FeaturedAreaId, 0, cardId);

        public OpResult Remove(string areaId, int slotIndex)
        {
            if (!State.Areas.TryGetValue(areaId, out var area) || slotIndex < 0 || slotIndex >= area.Slots.Count)
                return OpResult.Fail(OpError.NotFound, "Slot not found.");
            if (area.Slots[slotIndex] == null) return OpResult.Fail(OpError.InvalidState, "This slot is already empty.");
            area.Slots[slotIndex] = null;
            RaiseChanged();
            return OpResult.Success();
        }

        public OpResult RemoveItem(string itemId)
        {
            var loc = LocationOf(itemId);
            return loc.HasValue ? Remove(loc.Value.areaId, loc.Value.slot) : OpResult.Fail(OpError.NotFound, "Not on display.");
        }

        /// <summary>Reorders within an area by swapping two slots.</summary>
        public OpResult Swap(string areaId, int from, int to)
        {
            var def = Area(areaId);
            if (def == null || !State.Areas.TryGetValue(areaId, out var area)) return OpResult.Fail(OpError.NotFound, "Display area not found.");
            int count = SlotCount(def);
            if (from < 0 || to < 0 || from >= count || to >= count) return OpResult.Fail(OpError.InvalidArgument, "Slot out of range.");
            var tmp = area.Slots[from];
            area.Slots[from] = area.Slots[to];
            area.Slots[to] = tmp;
            RaiseChanged();
            return OpResult.Success();
        }

        // ------------------------------------------------------------- customization

        public OpResult SetCosmetic(CosmeticKind kind, string cosmeticId)
        {
            if (!_ctx.Content.Cosmetics.TryGetValue(cosmeticId ?? "", out var def)) return OpResult.Fail(OpError.NotFound, "Cosmetic not found.");
            if (def.Kind != kind) return OpResult.Fail(OpError.InvalidArgument, def.Name + " is not a " + kind + ".");
            if (!_ctx.State.Profile.UnlockedCosmetics.Contains(cosmeticId)) return OpResult.Fail(OpError.Locked, def.Name + " is not unlocked yet.");
            switch (kind)
            {
                case CosmeticKind.ShowcaseBackground: State.BackgroundId = cosmeticId; break;
                case CosmeticKind.CardFrame: State.FrameId = cosmeticId; break;
                case CosmeticKind.DisplayPedestal: State.PedestalId = cosmeticId; break;
                case CosmeticKind.ShelfStyle: State.ShelfId = cosmeticId; break;
                case CosmeticKind.WallStyle: State.WallId = cosmeticId; break;
                case CosmeticKind.DecorativeEffect: State.EffectId = cosmeticId; break;
                case CosmeticKind.ProfileTitle: _ctx.State.Profile.EquippedTitleId = cosmeticId; break;
                default: return OpResult.Fail(OpError.InvalidArgument, "This cosmetic is not used in the showcase.");
            }
            RaiseChanged();
            return OpResult.Success();
        }

        public void SetLayout(ShowcaseLayout layout)
        {
            State.Layout = layout;
            RaiseChanged();
        }

        public void SetDecorativeEffects(bool enabled)
        {
            State.DecorativeEffectsEnabled = enabled;
            RaiseChanged();
        }

        public void SetReducedEffects(bool reduced)
        {
            State.ReducedEffects = reduced;
            RaiseChanged();
        }

        // ------------------------------------------------------------- integrity

        private void OnOwnershipChanged(OwnershipChange change)
        {
            if (change.PreviousOwnerId == Owners.Player && change.NewOwnerId != Owners.Player)
                if (ClearItem(change.CardInstanceId)) RaiseChanged();
        }

        private bool ClearItem(string itemId)
        {
            bool changed = false;
            foreach (var area in State.Areas.Values)
                for (int i = 0; i < area.Slots.Count; i++)
                    if (area.Slots[i] == itemId)
                    {
                        area.Slots[i] = null;
                        changed = true;
                    }
            return changed;
        }

        /// <summary>Clears any reference that no longer points at a valid item. Returns the number cleared.</summary>
        public int PurgeInvalidReferences()
        {
            int cleared = 0;
            var seen = new HashSet<string>();
            foreach (var kv in State.Areas)
            {
                var def = Area(kv.Key);
                var slots = kv.Value.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    string id = slots[i];
                    if (id == null) continue;
                    bool valid;
                    if (def == null) valid = false;
                    else if (def.Kind == ShowcaseAreaKind.CompletedSets)
                        valid = _ctx.State.SetRewards.Sets.TryGetValue(id, out var r) && r.FirstCompletedAt.HasValue;
                    else
                        valid = _ctx.State.Cards.TryGetValue(id, out var c) && c.OwnerId == Owners.Player && IsEligible(def, c);
                    if (!valid || !seen.Add(id))
                    {
                        slots[i] = null;
                        cleared++;
                    }
                }
            }
            if (cleared > 0) RaiseChanged();
            return cleared;
        }

        /// <summary>Area and slot unlocks reached so far; each counts once as a showcase upgrade.</summary>
        public IReadOnlyList<string> CollectNewUpgrades()
        {
            var fresh = new List<string>();
            foreach (var area in Config.Areas)
            {
                if (area.AreaUnlock.Kind != UnlockConditionKind.None && IsUnlocked(area) && State.CountedUpgrades.Add("area:" + area.Id))
                    fresh.Add("area:" + area.Id);
                for (int i = 0; i < area.ExtraSlots.Count; i++)
                {
                    string key = "slots:" + area.Id + ":" + i;
                    if (_unlocks.IsMet(area.ExtraSlots[i].Condition) && State.CountedUpgrades.Add(key)) fresh.Add(key);
                }
            }
            if (fresh.Count > 0) EnsureState();
            return fresh;
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}
