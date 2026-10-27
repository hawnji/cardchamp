using System.Collections.Generic;
using CardVault.Core.Common;

namespace CardVault.Core.State
{
    /// <summary>
    /// The complete persistent game state. There is exactly one copy of each fact
    /// (ownership lives only on CardInstance.OwnerId, currency only in Wallet), and every
    /// service reads and writes this object. Saving serializes it as a whole.
    /// </summary>
    public sealed class GameState
    {
        public const int CurrentSchemaVersion = 2;

        public int SchemaVersion = CurrentSchemaVersion;
        public int ContentVersion;
        public long CreatedAtGameSeconds;

        public IdState Ids = new IdState();
        public RngState Rng = new RngState();
        public ClockState Clock = new ClockState();
        public WalletState Wallet = new WalletState();

        /// <summary>Every card instance in the world (player-, NPC- and vendor-owned).</summary>
        public Dictionary<string, CardInstance> Cards = new Dictionary<string, CardInstance>();
        /// <summary>Global serial registry: "template|parallel|serial" -> instance id.</summary>
        public Dictionary<string, string> SerialAllocations = new Dictionary<string, string>();
        public Dictionary<string, SealedProductInstance> SealedProducts = new Dictionary<string, SealedProductInstance>();

        public List<LedgerEntry> Ledger = new List<LedgerEntry>();
        /// <summary>Idempotency keys of committed transactions.</summary>
        public HashSet<string> ProcessedTransactions = new HashSet<string>();
        public List<GameAction> Actions = new List<GameAction>();
        public List<GameNotification> Notifications = new List<GameNotification>();

        public MarketState Market = new MarketState();
        public GradingState Grading = new GradingState();
        public HistoryState History = new HistoryState();
        public SetRewardState SetRewards = new SetRewardState();
        public RewardState Rewards = new RewardState();
        public ProfileState Profile = new ProfileState();
        public ShowcaseState Showcase = new ShowcaseState();
        public CalendarState Calendar = new CalendarState();
        public CardShowState CardShows = new CardShowState();
        public ProgressionState Progression = new ProgressionState();
    }
}
