using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;

namespace CardVault.Core.History
{
    /// <summary>
    /// Append-only provenance log for card instances.
    ///
    /// Entries are only written inside a transaction by the service that performs the
    /// gameplay operation, after validation has passed, so a failed purchase/sale/trade never
    /// leaves a completed entry. Entry IDs derive from (transaction, card, type), so a replayed
    /// transaction cannot append duplicates. Mistakes are fixed by Correction entries, never by
    /// editing existing ones.
    /// </summary>
    public sealed class CardHistoryService
    {
        private readonly GameContext _ctx;
        private readonly HashSet<string> _entryIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CardHistoryEntry>> _byCard = new Dictionary<string, List<CardHistoryEntry>>(StringComparer.Ordinal);

        public CardHistoryService(GameContext ctx)
        {
            _ctx = ctx;
            RebuildIndex();
        }

        private HistoryState State => _ctx.State.History;

        public void RebuildIndex()
        {
            _entryIds.Clear();
            _byCard.Clear();
            foreach (var e in State.Entries) Index(e);
        }

        private void Index(CardHistoryEntry e)
        {
            _entryIds.Add(e.EntryId);
            if (!_byCard.TryGetValue(e.CardInstanceId, out var list))
            {
                list = new List<CardHistoryEntry>();
                _byCard[e.CardInstanceId] = list;
            }
            list.Add(e);
        }

        public static string EntryIdFor(string transactionId, string cardInstanceId, HistoryEventType type)
            => IdPrefixes.History + ":" + transactionId + ":" + cardInstanceId + ":" + type;

        /// <summary>
        /// Appends an entry as part of <paramref name="tx"/>. Returns the existing entry when the
        /// same (transaction, card, type) was already recorded.
        /// </summary>
        public CardHistoryEntry Append(Transaction tx, string cardInstanceId, HistoryEventType type, Action<CardHistoryEntry> fill = null)
        {
            if (tx == null) throw new ArgumentNullException(nameof(tx), "History entries must be written inside a transaction.");
            if (string.IsNullOrEmpty(cardInstanceId)) throw new ArgumentException("Card instance id is required.", nameof(cardInstanceId));

            string entryId = EntryIdFor(tx.Id, cardInstanceId, type);
            if (_entryIds.Contains(entryId)) return _byCard[cardInstanceId].First(e => e.EntryId == entryId);

            var entry = new CardHistoryEntry
            {
                EntryId = entryId,
                CardInstanceId = cardInstanceId,
                Type = type,
                Time = _ctx.Now,
                Sequence = ++State.NextSequence,
                TransactionId = tx.Id,
            };
            fill?.Invoke(entry);
            if (string.IsNullOrEmpty(entry.Description)) entry.Description = DefaultDescription(entry);

            State.Entries.Add(entry);
            Index(entry);
            tx.EnqueueCardChanged(cardInstanceId);
            return entry;
        }

        /// <summary>Appends an explicit correction that references (but never edits) an earlier entry.</summary>
        public OpResult<CardHistoryEntry> AppendCorrection(string correctsEntryId, string description, string transactionId = null)
        {
            var original = State.Entries.FirstOrDefault(e => e.EntryId == correctsEntryId);
            if (original == null) return OpResult<CardHistoryEntry>.Fail(OpError.NotFound, "History entry not found.");
            if (string.IsNullOrWhiteSpace(description)) return OpResult<CardHistoryEntry>.Fail(OpError.InvalidArgument, "A correction needs a description.");
            if (_ctx.IsProcessed(transactionId)) return OpResult<CardHistoryEntry>.Fail(OpError.AlreadyProcessed, "Correction already recorded.");

            using (var tx = _ctx.Begin(transactionId))
            {
                var entry = Append(tx, original.CardInstanceId, HistoryEventType.Correction, e =>
                {
                    e.CorrectsEntryId = original.EntryId;
                    e.Description = "Record correction: " + description.Trim();
                });
                tx.Commit();
                return OpResult<CardHistoryEntry>.Success(entry);
            }
        }

        /// <summary>Chronological timeline for one card.</summary>
        public IReadOnlyList<CardHistoryEntry> For(string cardInstanceId)
        {
            if (!_byCard.TryGetValue(cardInstanceId, out var list)) return Array.Empty<CardHistoryEntry>();
            return list.OrderBy(e => e.Time).ThenBy(e => e.Sequence).ToList();
        }

        public bool HasEntries(string cardInstanceId) => _byCard.ContainsKey(cardInstanceId);

        public IEnumerable<CardHistoryEntry> All => State.Entries;

        /// <summary>Past owners in order, starting with the first owner after creation.</summary>
        public IReadOnlyList<string> OwnershipChain(string cardInstanceId)
        {
            var chain = new List<string>();
            foreach (var e in For(cardInstanceId))
            {
                if (string.IsNullOrEmpty(e.NewOwnerId)) continue;
                if (chain.Count == 0 || chain[chain.Count - 1] != e.NewOwnerId) chain.Add(e.NewOwnerId);
            }
            return chain;
        }

        /// <summary>
        /// Checks the invariants the rest of the game relies on. Used by tests and by the save
        /// loader to surface (not silently fix) inconsistencies.
        /// </summary>
        public IReadOnlyList<string> VerifyIntegrity()
        {
            var issues = new List<string>();
            var seen = new HashSet<string>();
            var completedTransfers = new HashSet<string>();
            foreach (var e in State.Entries)
            {
                if (!seen.Add(e.EntryId)) issues.Add("Duplicate history entry " + e.EntryId);
                if (!_ctx.State.Cards.ContainsKey(e.CardInstanceId)) issues.Add("History references unknown card " + e.CardInstanceId);
                if (IsTransfer(e.Type) && !string.IsNullOrEmpty(e.TransactionId))
                {
                    if (!completedTransfers.Add(e.TransactionId + "|" + e.CardInstanceId))
                        issues.Add("Duplicate completed transfer for " + e.CardInstanceId + " in " + e.TransactionId);
                    if (!_ctx.State.ProcessedTransactions.Contains(e.TransactionId))
                        issues.Add("Transfer entry " + e.EntryId + " has no committed transaction");
                }
            }

            foreach (var card in _ctx.State.Cards.Values)
            {
                if (card.OwnerId != Owners.Player) continue;
                var timeline = For(card.InstanceId);
                if (timeline.Count == 0)
                {
                    issues.Add("Player card " + card.InstanceId + " has no history");
                    continue;
                }
                var lastOwner = timeline.LastOrDefault(x => !string.IsNullOrEmpty(x.NewOwnerId))?.NewOwnerId;
                if (lastOwner != card.OwnerId) issues.Add("History owner mismatch for " + card.InstanceId);
            }
            return issues;
        }

        public static bool IsTransfer(HistoryEventType type)
            => type == HistoryEventType.NpcPurchase || type == HistoryEventType.NpcSale ||
               type == HistoryEventType.NpcTrade || type == HistoryEventType.ListingSold ||
               type == HistoryEventType.OwnershipTransfer;

        // ------------------------------------------------------------- presentation helpers

        public string OwnerName(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return null;
            if (ownerId == Owners.Player) return "You";
            if (ownerId == Owners.Unallocated) return "Unreleased stock";
            var npcId = Owners.NpcIdOf(ownerId);
            if (npcId != null && _ctx.Content.Npcs.TryGetValue(npcId, out var npc)) return npc.Name;
            return "Another collector";
        }

        private string ProductName(string productId)
            => productId != null && _ctx.Content.Products.TryGetValue(productId, out var p) ? p.Name : "a sealed product";

        private string SetName(string setId)
            => setId != null && _ctx.Content.Sets.TryGetValue(setId, out var s) ? s.Name : "a set";

        private string DefaultDescription(CardHistoryEntry e)
        {
            string price = e.PriceCredits.HasValue ? e.PriceCredits.Value.ToString("N0") + " credits" : null;
            switch (e.Type)
            {
                case HistoryEventType.Created: return "Printed for " + SetName(e.SetId) + ".";
                case HistoryEventType.PackOpened: return "Pulled from " + ProductName(e.ProductId) + ".";
                case HistoryEventType.Acquired: return e.NewOwnerId == Owners.Player ? "Added to your collection." : "Added to " + OwnerName(e.NewOwnerId) + "'s inventory.";
                case HistoryEventType.NpcPurchase: return "Bought from " + OwnerName(e.PreviousOwnerId) + (price != null ? " for " + price : "") + ".";
                case HistoryEventType.NpcSale: return "Sold to " + OwnerName(e.NewOwnerId) + (price != null ? " for " + price : "") + ".";
                case HistoryEventType.NpcTrade: return "Traded from " + OwnerName(e.PreviousOwnerId) + " to " + OwnerName(e.NewOwnerId) + ".";
                case HistoryEventType.GradingSubmitted: return "Submitted for grading.";
                case HistoryEventType.GradeAwarded: return "Graded " + (e.NewGrade.HasValue ? e.NewGrade.Value.ToString("0.#") : "?") + ".";
                case HistoryEventType.ConditionUpdated: return "Condition updated.";
                case HistoryEventType.ListingCreated: return "Listed for sale" + (price != null ? " at " + price : "") + ".";
                case HistoryEventType.ListingCanceled: return "Listing withdrawn.";
                case HistoryEventType.ListingSold: return "Listing sold to " + OwnerName(e.NewOwnerId) + (price != null ? " for " + price : "") + ".";
                case HistoryEventType.OwnershipTransfer: return "Ownership transferred to " + OwnerName(e.NewOwnerId) + ".";
                case HistoryEventType.EventAcquisition: return "Acquired at a card show.";
                case HistoryEventType.RewardGrant: return "Awarded as a collector reward.";
                case HistoryEventType.SetMilestone: return "Completed a collection milestone in " + SetName(e.SetId) + ".";
                case HistoryEventType.CollectionAchievement: return "Part of a collection achievement.";
                case HistoryEventType.Correction: return "Record correction.";
                case HistoryEventType.MigrationBackfill: return "Record created when your collection was upgraded.";
                default: return e.Type.ToString();
            }
        }
    }
}
