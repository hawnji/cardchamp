using System.Collections.Generic;

namespace CardVault.Core.Content
{
    // ---------------------------------------------------------------- Release calendar

    public enum ReleaseCategory
    {
        SportsFlagship,
        SportsRookie,
        SportsPremium,
        TcgExpansion,
        AlternateArtCollection,
        Anniversary,
        VintageRestock,
        CardShowEvent,
        LimitedSeasonal,
    }

    public sealed class ReleaseProductStock
    {
        public string ProductId;
        /// <summary>Total units ever allocated to this release. Never replenished.</summary>
        public int InitialStock;
        /// <summary>Units moved into the vintage market when the release retires (taken from unsold stock).</summary>
        public int VintageAllocationOnRetire;
    }

    public sealed class ReleaseDef
    {
        public string Id;
        public string Name;
        public string BrandId;
        public ReleaseCategory Category;
        public string SetId;
        public string Description;
        public string ArtKey;
        /// <summary>Game-time seconds (from the content epoch).</summary>
        public long AnnounceAt;
        public long ReleaseAt;
        /// <summary>0 = never retires.</summary>
        public long RetireAt;
        /// <summary>0 = never archived.</summary>
        public long ArchiveAt;
        public List<ReleaseProductStock> Products = new List<ReleaseProductStock>();
        public List<string> Themes = new List<string>();
        public List<string> ChaseTemplateIds = new List<string>();
        public List<string> EventIds = new List<string>();
        /// <summary>Remaining stock at or below this fraction of initial shows Limited Stock.</summary>
        public double LimitedStockFraction = 0.2;
        public double VintagePriceMultiplier = 1.6;
        public long UpcomingWindowSeconds = 7 * 86400;
    }

    // ---------------------------------------------------------------- Card show events

    public enum CardShowCategory
    {
        SportsCardExpo,
        TcgCollectorConvention,
        VintageCardFair,
        PremiumCollectorShowcase,
        DailyMiniEvent,
    }

    public enum ScheduleKind { Once, Daily, Weekly, Monthly }

    /// <summary>
    /// Deterministic schedule. Occurrence n starts at FirstStartAt + n * period and lasts
    /// DurationSeconds. Monthly uses a 28-day game month so the schedule is pure arithmetic.
    /// </summary>
    public sealed class ScheduleDef
    {
        public ScheduleKind Kind;
        public long FirstStartAt;
        public long DurationSeconds = 86400;
        /// <summary>0 = repeats forever.</summary>
        public int MaxOccurrences;
    }

    public sealed class VendorStockDef
    {
        public string Id;
        public string ProductId;
        public string TemplateId;
        public string ParallelId;
        public double? Grade;
        public double Condition = 9.0;
        public int Quantity = 1;
        public long PriceCredits;
        public int PriceTickets;
        /// <summary>Requires an access unlock from a reward (UnlockListingIds).</summary>
        public string RequiresUnlockId;
    }

    public sealed class VendorDef
    {
        public string Id;
        public string NpcId;
        public string BoothName;
        public string Description;
        public List<VendorStockDef> Stock = new List<VendorStockDef>();
    }

    public enum GameActionType
    {
        PackOpened,
        CardAcquired,
        ProductPurchased,
        CardPurchased,
        CardSold,
        TradeCompleted,
        CardGraded,
        SetMilestoneReached,
        CardShowVisited,
        CardShowChallengeCompleted,
        CardShowCompleted,
        ReleaseWatched,
        ShowcaseUpdated,
        AchievementUnlocked,
    }

    public sealed class ChallengeFilter
    {
        public List<string> SetIds = new List<string>();
        public List<string> BrandIds = new List<string>();
        public bool? Rookie;
        public bool? Vintage;
        public bool? SerialNumbered;
        public CardCategory? Category;
        /// <summary>Count distinct card templates instead of actions.</summary>
        public bool DistinctTemplates;
        /// <summary>Count distinct brands instead of actions.</summary>
        public bool DistinctBrands;
        /// <summary>Restrict to the event's featured sets/brands.</summary>
        public bool FeaturedOnly;
    }

    public sealed class ChallengeDef
    {
        public string Id;
        public string Description;
        public GameActionType Action;
        public int Count = 1;
        public ChallengeFilter Filter = new ChallengeFilter();
        public int Points = 10;
    }

    public sealed class EventRewardDef
    {
        public string Id;
        public string Name;
        public int CostPoints;
        public RewardDef Reward = new RewardDef();
    }

    public sealed class EventPricingRule
    {
        public double VendorMarkup = 1.0;
        public double FeaturedSetDiscount = 0.9;
    }

    public sealed class CardShowEventDef
    {
        public string Id;
        public string Name;
        public string Description;
        public CardShowCategory Category;
        public string ArtKey;
        public ScheduleDef Schedule = new ScheduleDef();
        public List<string> FeaturedBrandIds = new List<string>();
        public List<string> FeaturedSetIds = new List<string>();
        public List<VendorDef> Vendors = new List<VendorDef>();
        public List<ChallengeDef> Challenges = new List<ChallengeDef>();
        public List<EventRewardDef> Rewards = new List<EventRewardDef>();
        /// <summary>Granted once when every challenge in an occurrence is complete.</summary>
        public RewardDef CompletionReward = new RewardDef();
        public EventPricingRule Pricing = new EventPricingRule();
    }

    // ---------------------------------------------------------------- Showcase

    public enum ShowcaseAreaKind
    {
        FeaturedPedestal,
        MainWall,
        GradedShelf,
        VintageCabinet,
        SportsSection,
        TcgSection,
        CompletedSets,
        OneOfOne,
        Favorites,
    }

    public enum UnlockConditionKind { None, CollectorRank, SetsCompleted, Achievement, Cosmetic, CardShowsCompleted, CardsGraded, VintageProductsOpened }

    public sealed class UnlockCondition
    {
        public UnlockConditionKind Kind;
        public long Value;
        public string RefId;
    }

    public sealed class ShowcaseSlotUnlock
    {
        public int Slots;
        public UnlockCondition Condition = new UnlockCondition();
    }

    public sealed class ShowcaseAreaDef
    {
        public string Id;
        public string Name;
        public ShowcaseAreaKind Kind;
        public int BaseSlots;
        public UnlockCondition AreaUnlock = new UnlockCondition();
        public List<ShowcaseSlotUnlock> ExtraSlots = new List<ShowcaseSlotUnlock>();
    }

    public sealed class ShowcaseConfig
    {
        public List<ShowcaseAreaDef> Areas = new List<ShowcaseAreaDef>();
        public string DefaultBackgroundId;
        public string DefaultFrameId;
        public string DefaultPedestalId;
        public string DefaultShelfId;
        public string DefaultWallId;
    }

    // ---------------------------------------------------------------- Progression

    public enum StatKey
    {
        UniqueCardsAcquired,
        SetsCompleted,
        VintageProductsOpened,
        PacksOpened,
        CardsGraded,
        NpcTradesCompleted,
        NpcPurchases,
        NpcSales,
        CardShowChallengesCompleted,
        CardShowsCompleted,
        CardShowsVisited,
        ShowcaseUpgradesUnlocked,
        ReleaseMilestones,
        RareCardsDiscovered,
        OneOfOnesDiscovered,
        HistoricalMilestones,
        SetMilestonesReached,
    }

    public sealed class AchievementDef
    {
        public string Id;
        public string Name;
        public string Description;
        public StatKey Stat;
        public long Threshold;
        public RewardDef Reward = new RewardDef();
        /// <summary>Prestige-only achievements grant no currency by design.</summary>
        public bool Hidden;
    }

    public sealed class CollectorRankDef
    {
        public int Level;
        public string Name;
        public long XpRequired;
        public RewardDef Reward = new RewardDef();
    }

    public sealed class XpConfig
    {
        public long PerNewUniqueCard = 5;
        public long PerPackOpened = 2;
        public long PerCardGraded = 10;
        public long PerTrade = 15;
        public long PerSetMilestone = 40;
        public long PerChallenge = 20;
        public long PerRareDiscovery = 25;
    }
}
