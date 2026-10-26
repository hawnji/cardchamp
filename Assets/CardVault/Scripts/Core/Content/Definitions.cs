using System.Collections.Generic;

namespace CardVault.Core.Content
{
    // Data-driven content model. Everything here is loaded from JSON (see ContentDatabase)
    // and is immutable at runtime. Player-specific data lives in CardVault.Core.State.

    public enum CardCategory { Sports, Tcg }

    public enum Rarity { Common, Uncommon, Rare, SuperRare, Legendary }

    public enum SubjectKind { Athlete, Character }

    public enum ProductType { Pack, Box, Blaster, HobbyBox, Tin, Bundle }

    public enum CosmeticKind
    {
        CardFrame,
        ShowcaseBackground,
        BinderTheme,
        DisplayPedestal,
        ShelfStyle,
        WallStyle,
        DecorativeEffect,
        ProfileTitle,
    }

    public sealed class BrandDef
    {
        public string Id;
        public string Name;
        public CardCategory Category;
        public string Description;
    }

    public sealed class UniverseDef
    {
        public string Id;
        public string Name;
        public string Description;
    }

    public sealed class LeagueDef
    {
        public string Id;
        public string Name;
        public string Sport;
    }

    public sealed class TeamDef
    {
        public string Id;
        public string Name;
        public string City;
        public string LeagueId;
        public string PrimaryColor;
        public string SecondaryColor;
    }

    /// <summary>A fictional athlete or TCG character featured on cards.</summary>
    public sealed class SubjectDef
    {
        public string Id;
        public string Name;
        public SubjectKind Kind;
        public string TeamId;
        public string UniverseId;
        public string Position;
        public string Element;
        public Dictionary<string, string> Stats = new Dictionary<string, string>();
    }

    public sealed class CardTemplateDef
    {
        public string Id;
        public string SetId;
        public int Number;
        public string Name;
        public string SubjectId;
        public Rarity Rarity;
        public bool IsRookie;
        public bool IsAutograph;
        public bool IsAlternateArt;
        public long BaseValue;
        public string ArtKey;
        public string FlavorText;
        public List<string> Tags = new List<string>();
    }

    /// <summary>
    /// A parallel/variant treatment. PrintRun 0 means unnumbered; PrintRun 1 means a genuine
    /// one-of-one; any other value means serial-numbered /PrintRun.
    /// </summary>
    public sealed class ParallelDef
    {
        public string Id;
        public string Name;
        public int PrintRun;
        public double ValueMultiplier = 1.0;
        public string ColorHex;
        public bool IsBase;

        public bool IsSerialNumbered => PrintRun > 0;
        public bool IsOneOfOne => PrintRun == 1;
    }

    public enum SetMilestoneKind
    {
        /// <summary>Base checklist percentage reached (Threshold = percent).</summary>
        BasePercent,
        AllBase,
        AllRookies,
        /// <summary>All base cards featuring SubjectId.</summary>
        AllOfSubject,
        /// <summary>Every checklist card in ParallelId (a chase collection, may include serials/1-of-1s).</summary>
        ParallelComplete,
        /// <summary>Every checklist card owned graded at or above MinGrade.</summary>
        GradedComplete,
    }

    public sealed class SetMilestoneDef
    {
        public string Id;
        public string Name;
        public SetMilestoneKind Kind;
        public double Threshold;
        public string SubjectId;
        public string ParallelId;
        public double MinGrade;
        public bool AutoGrant;
        public RewardDef Reward = new RewardDef();
    }

    public sealed class SetDef
    {
        public string Id;
        public string Name;
        public string BrandId;
        public CardCategory Category;
        public string UniverseId;
        public string LeagueId;
        public int Year;
        public bool IsVintage;
        public string ArtKey;
        public string Description;
        /// <summary>Base checklist in display order.</summary>
        public List<string> CardTemplateIds = new List<string>();
        /// <summary>Parallels this set can be printed in (excluding base).</summary>
        public List<string> ParallelIds = new List<string>();
        public List<SetMilestoneDef> Milestones = new List<SetMilestoneDef>();
    }

    /// <summary>
    /// One slot in a pack. Each roll picks an option by weight, then a card matching the
    /// option's filters from the product's set.
    /// </summary>
    public sealed class PackSlotDef
    {
        public int Count = 1;
        public List<PackSlotOption> Options = new List<PackSlotOption>();
    }

    public sealed class PackSlotOption
    {
        public double Weight = 1;
        public List<Rarity> Rarities = new List<Rarity>();
        public string ParallelId;
        public bool RookieOnly;
        public bool AutographOnly;
        public bool AlternateArtOnly;
    }

    public sealed class ProductDef
    {
        public string Id;
        public string Name;
        public string BrandId;
        public string SetId;
        public ProductType Type;
        public long PriceCredits;
        public bool IsVintage;
        public string ArtKey;
        public string Description;
        /// <summary>For boxes: product contained and how many.</summary>
        public string ContainsProductId;
        public int ContainsCount;
        /// <summary>For packs: card slots.</summary>
        public List<PackSlotDef> Slots = new List<PackSlotDef>();
        /// <summary>Condition range for pulled cards (vintage packs pull lower conditions).</summary>
        public double MinCondition = 7.0;
        public double MaxCondition = 10.0;

        public bool IsContainer => !string.IsNullOrEmpty(ContainsProductId) && ContainsCount > 0;
    }

    public sealed class NpcDef
    {
        public string Id;
        public string Name;
        public string Persona;
        public string Specialty;
        /// <summary>Price an NPC asks relative to estimated value.</summary>
        public double SellMarkup = 1.15;
        /// <summary>Price an NPC pays relative to estimated value.</summary>
        public double BuyFactor = 0.7;
        public CardCategory? FocusCategory;
        public bool DealsVintage;
    }

    public sealed class CosmeticDef
    {
        public string Id;
        public string Name;
        public CosmeticKind Kind;
        public string Description;
        public string ColorHex;
        public string AccentHex;
        public bool UnlockedByDefault;
    }

    /// <summary>
    /// A grant of player benefits. Every grant goes through RewardService with a stable
    /// grant key so it can only ever be applied once.
    /// </summary>
    public sealed class RewardDef
    {
        public long Credits;
        public int EventTickets;
        public List<string> CosmeticIds = new List<string>();
        public List<RewardCardGrant> Cards = new List<RewardCardGrant>();
        public List<string> UnlockListingIds = new List<string>();
        public int ShowcaseSlots;
        public long CollectorXp;

        public bool IsEmpty =>
            Credits == 0 && EventTickets == 0 && CosmeticIds.Count == 0 && Cards.Count == 0 &&
            UnlockListingIds.Count == 0 && ShowcaseSlots == 0 && CollectorXp == 0;
    }

    public sealed class RewardCardGrant
    {
        public string TemplateId;
        public string ParallelId;
        public bool Tradable;
        public double Condition = 10.0;
        public string Note;
    }

    public sealed class GradingConfig
    {
        public long FeeCredits = 150;
        public long TurnaroundSeconds = 2 * 86400;
        public double MinimumGrade = 1;
        public double MaximumGrade = 10;
    }

    public sealed class MarketConfig
    {
        public int DailyListingCount = 10;
        public int VintageListingCount = 4;
        public int ListingLifetimeDays = 3;
        public int DailyTradeOffers = 2;
        public double PlayerListingSaleChancePerDay = 0.5;
        public double MaxSellPriceOverValue = 1.5;
    }

    public sealed class EconomyConfig
    {
        public long StartingCredits = 2500;
        public int StartingEventTickets = 0;
        public List<string> StartingProductIds = new List<string>();
        /// <summary>Game day a new save starts on, so the calendar already has history.</summary>
        public int StartGameDay = 0;
    }
}
