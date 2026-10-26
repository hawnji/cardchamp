using System.Collections.Generic;

namespace CardVault.Core.State
{
    // ---------------------------------------------------------------- Card history

    public enum HistoryEventType
    {
        Created,
        PackOpened,
        Acquired,
        NpcPurchase,
        NpcSale,
        NpcTrade,
        GradingSubmitted,
        GradeAwarded,
        ConditionUpdated,
        ListingCreated,
        ListingCanceled,
        ListingSold,
        OwnershipTransfer,
        EventAcquisition,
        RewardGrant,
        SetMilestone,
        CollectionAchievement,
        Correction,
        MigrationBackfill,
    }

    /// <summary>
    /// One immutable provenance entry. EntryId is derived from the transaction that produced
    /// it, so replaying a transaction can never append a second copy.
    /// </summary>
    public sealed class CardHistoryEntry
    {
        public string EntryId;
        public string CardInstanceId;
        public HistoryEventType Type;
        public long Time;
        /// <summary>Monotonic append order; breaks ties between entries at the same game time.</summary>
        public long Sequence;
        public string TransactionId;
        public string PreviousOwnerId;
        public string NewOwnerId;
        public double? PreviousCondition;
        public double? NewCondition;
        public double? PreviousGrade;
        public double? NewGrade;
        public string ProductId;
        public string SetId;
        public string EventId;
        public long? PriceCredits;
        public string Description;
        /// <summary>For Correction entries: the entry being corrected.</summary>
        public string CorrectsEntryId;
    }

    public sealed class HistoryState
    {
        public List<CardHistoryEntry> Entries = new List<CardHistoryEntry>();
        public long NextSequence;
    }

    // ---------------------------------------------------------------- Set rewards

    public sealed class MilestoneRecord
    {
        public string MilestoneId;
        public long AchievedAt;
        public string TriggerCardInstanceId;
        public bool Claimed;
        public long ClaimedAt;
        public string ClaimTransactionId;
    }

    /// <summary>
    /// Historical, permanent record for a set. Unlike current ownership it never shrinks:
    /// selling a card does not undo a completion.
    /// </summary>
    public sealed class SetRecord
    {
        public string SetId;
        public Dictionary<string, MilestoneRecord> Milestones = new Dictionary<string, MilestoneRecord>();
        /// <summary>Base checklist template IDs the player has ever owned (historical, never removed).</summary>
        public HashSet<string> EverCollectedBase = new HashSet<string>();
        public long? FirstCompletedAt;
        public bool CompletionCelebrated;
    }

    public sealed class SetRewardState
    {
        public Dictionary<string, SetRecord> Sets = new Dictionary<string, SetRecord>();
    }

    // ---------------------------------------------------------------- Rewards and cosmetics

    public sealed class RewardGrantRecord
    {
        public string GrantKey;
        public string TransactionId;
        public long Time;
        public string Source;
        public long Credits;
        public int Tickets;
        public List<string> CosmeticIds = new List<string>();
        public List<string> CardInstanceIds = new List<string>();
    }

    public sealed class RewardState
    {
        public Dictionary<string, RewardGrantRecord> Grants = new Dictionary<string, RewardGrantRecord>();
    }

    public sealed class ProfileState
    {
        public string CollectorName = "Collector";
        public HashSet<string> UnlockedCosmetics = new HashSet<string>();
        public string EquippedTitleId;
        public int BonusShowcaseSlots;
    }

    // ---------------------------------------------------------------- Showcase

    public sealed class ShowcaseAreaState
    {
        public string AreaId;
        /// <summary>Card instance IDs (or set IDs for the completed-set area); null = empty slot.</summary>
        public List<string> Slots = new List<string>();
    }

    public enum ShowcaseLayout { Gallery, Grid, Spotlight }

    public sealed class ShowcaseState
    {
        public Dictionary<string, ShowcaseAreaState> Areas = new Dictionary<string, ShowcaseAreaState>();
        public string BackgroundId;
        public string FrameId;
        public string PedestalId;
        public string ShelfId;
        public string WallId;
        public string EffectId;
        public ShowcaseLayout Layout;
        public bool DecorativeEffectsEnabled = true;
        public bool ReducedEffects;
        public HashSet<string> CountedUpgrades = new HashSet<string>();
    }

    // ---------------------------------------------------------------- Release calendar

    public sealed class ReleaseRuntimeState
    {
        public string ReleaseId;
        public bool StockAllocated;
        /// <summary>Remaining units per product, allocated exactly once.</summary>
        public Dictionary<string, int> RemainingStock = new Dictionary<string, int>();
        public bool RetirementProcessed;
        public long RetiredAt;
    }

    public sealed class CalendarState
    {
        public Dictionary<string, ReleaseRuntimeState> Releases = new Dictionary<string, ReleaseRuntimeState>();
        public HashSet<string> Watchlist = new HashSet<string>();
        public HashSet<string> RemindersIssued = new HashSet<string>();
        public HashSet<string> MilestonesRecorded = new HashSet<string>();
    }

    // ---------------------------------------------------------------- Card shows

    public sealed class ChallengeProgress
    {
        public string ChallengeId;
        public long CompletedAt;
        public int PointsAwarded;
    }

    public sealed class EventParticipation
    {
        public string OccurrenceId;
        public string EventId;
        public int OccurrenceIndex;
        public long StartsAt;
        public long EndsAt;
        public bool Visited;
        public long FirstVisitedAt;
        public Dictionary<string, ChallengeProgress> CompletedChallenges = new Dictionary<string, ChallengeProgress>();
        public int PointsEarned;
        public int PointsSpent;
        public HashSet<string> RedeemedRewards = new HashSet<string>();
        public bool InventoryInitialized;
        /// <summary>Remaining quantity per vendor stock id.</summary>
        public Dictionary<string, int> StockRemaining = new Dictionary<string, int>();
        /// <summary>Concrete card instances held by vendors for card stock entries.</summary>
        public Dictionary<string, List<string>> VendorCardInstances = new Dictionary<string, List<string>>();
        public bool Completed;
        public long CompletedAt;
        public bool Finalized;

        public int PointsAvailable => PointsEarned - PointsSpent;
    }

    public sealed class CardShowRecord
    {
        public string OccurrenceId;
        public string EventId;
        public string Name;
        public long StartsAt;
        public long EndsAt;
        public int ChallengesCompleted;
        public int ChallengesTotal;
        public int PointsEarned;
        public List<string> RewardsRedeemed = new List<string>();
        public bool Completed;
        public int PurchasesMade;
    }

    public sealed class CardShowState
    {
        public Dictionary<string, EventParticipation> Occurrences = new Dictionary<string, EventParticipation>();
        public List<CardShowRecord> History = new List<CardShowRecord>();
    }

    // ---------------------------------------------------------------- Progression

    public sealed class ProgressionState
    {
        public Dictionary<string, long> Stats = new Dictionary<string, long>();
        public Dictionary<string, long> Achievements = new Dictionary<string, long>();
        public long CollectorXp;
        public int CollectorRank = 1;
        public HashSet<string> EverOwnedVariants = new HashSet<string>();
        public HashSet<string> ProcessedActionIds = new HashSet<string>();
    }
}
