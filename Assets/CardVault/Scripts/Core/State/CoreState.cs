using System.Collections.Generic;
using CardVault.Core.Content;

namespace CardVault.Core.State
{
    public enum CardStatus
    {
        /// <summary>Held by its owner and free to use.</summary>
        Available,
        /// <summary>Submitted for grading; cannot be sold, listed or traded.</summary>
        InGrading,
        /// <summary>Listed on the market by its owner.</summary>
        Listed,
    }

    /// <summary>
    /// One persistent, individually identified card. The InstanceId is the only identity
    /// the rest of the game uses; showcase slots, history and listings reference it.
    /// </summary>
    public sealed class CardInstance
    {
        public string InstanceId;
        public string TemplateId;
        public string ParallelId = ContentDatabase.BaseParallelId;
        /// <summary>Serial number when the parallel is numbered (1..PrintRun), otherwise 0.</summary>
        public int SerialNumber;
        public int PrintRun;
        public string OwnerId;
        public CardStatus Status;
        /// <summary>Raw condition 1.0-10.0 (persisted, affects grading).</summary>
        public double Condition;
        /// <summary>Awarded grade, null while raw.</summary>
        public double? Grade;
        public string GradingCertId;
        public bool Tradable = true;
        public long CreatedAt;
        /// <summary>Where this card entered the world (product, event, reward, market seed).</summary>
        public string OriginRef;
        public string OriginSetId;

        public bool IsSerialNumbered => PrintRun > 0;
        public bool IsOneOfOne => PrintRun == 1;
        public bool IsGraded => Grade.HasValue;
    }

    public sealed class SealedProductInstance
    {
        public string InstanceId;
        public string ProductId;
        public string OwnerId;
        public bool Opened;
        public long AcquiredAt;
        public long OpenedAt;
        public string SourceRef;
    }

    public sealed class WalletState
    {
        public long Credits;
        public int EventTickets;
    }

    public enum LedgerKind
    {
        StartingGrant,
        ProductPurchase,
        CardPurchase,
        CardSale,
        TradeCash,
        GradingFee,
        Reward,
        EventPurchase,
        ListingSale,
    }

    /// <summary>Append-only record of every currency movement.</summary>
    public sealed class LedgerEntry
    {
        public string TransactionId;
        public LedgerKind Kind;
        public long CreditsDelta;
        public int TicketsDelta;
        public long CreditsAfter;
        public int TicketsAfter;
        public long Time;
        public string RefId;
        public string Description;
    }

    public enum ListingStatus { Active, Sold, Cancelled, Expired }

    public enum ListingSource { NpcDaily, Vintage, PlayerListing, EventVendor, RewardAccess }

    public sealed class MarketListing
    {
        public string ListingId;
        public string SellerId;
        public string CardInstanceId;
        public string SealedInstanceId;
        public long PriceCredits;
        public long CreatedAt;
        public long ExpiresAt;
        public ListingStatus Status;
        public ListingSource Source;
        public string BuyerId;
        public string ClosedTransactionId;
        public string RequiresUnlockId;
    }

    public enum TradeStatus { Open, Accepted, Declined, Expired }

    /// <summary>An NPC proposal: NPC gives OfferedCardIds (+credits) for the player's RequestedCardIds.</summary>
    public sealed class TradeOffer
    {
        public string TradeId;
        public string NpcId;
        public List<string> OfferedCardIds = new List<string>();
        public List<string> RequestedCardIds = new List<string>();
        /// <summary>Positive: NPC pays the player. Negative: player pays the NPC.</summary>
        public long CreditsToPlayer;
        public long CreatedAt;
        public long ExpiresAt;
        public TradeStatus Status;
        public string ClosedTransactionId;
    }

    public sealed class MarketState
    {
        public List<MarketListing> Listings = new List<MarketListing>();
        public List<TradeOffer> TradeOffers = new List<TradeOffer>();
        public long LastRefreshDay = -1;
        public HashSet<string> UnlockedListingIds = new HashSet<string>();
    }

    public enum GradingStatus { Submitted, Completed }

    public sealed class GradingSubmission
    {
        public string SubmissionId;
        public string CardInstanceId;
        public long SubmittedAt;
        public long ReadyAt;
        public long FeeCredits;
        public GradingStatus Status;
        public double? AwardedGrade;
        public string TransactionId;
    }

    public sealed class GradingState
    {
        public List<GradingSubmission> Submissions = new List<GradingSubmission>();
    }

    /// <summary>
    /// Persistent record of a gameplay action. Challenges and progression evaluate this log,
    /// never UI state, so progress reflects only completed, committed actions.
    /// </summary>
    public sealed class GameAction
    {
        public string ActionId;
        public GameActionType Type;
        public long Time;
        public string TransactionId;
        public string CardInstanceId;
        public string TemplateId;
        public string ParallelId;
        public string SetId;
        public string BrandId;
        public string ProductId;
        public string RefId;
        public bool IsRookie;
        public bool IsVintage;
        public bool IsSerialNumbered;
        public bool IsOneOfOne;
        public CardCategory? Category;
    }

    public enum NotificationKind { SetMilestone, ReleaseReminder, CardShow, Achievement, Grading, Market, RankUp, System }

    public sealed class GameNotification
    {
        public string NotificationId;
        public NotificationKind Kind;
        public string Title;
        public string Body;
        public long CreatedAt;
        public bool Read;
        /// <summary>Target to open (set id, release id, event occurrence id, card id).</summary>
        public string RefId;
        /// <summary>Dedup key: a notification with the same key is never created twice.</summary>
        public string DedupKey;
    }
}
