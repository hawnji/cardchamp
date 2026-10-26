using System;
using System.Collections.Generic;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.State;

namespace CardVault.Core.Game
{
    public readonly struct OwnershipChange
    {
        public readonly string CardInstanceId;
        public readonly string PreviousOwnerId;
        public readonly string NewOwnerId;
        public readonly string TransactionId;

        public OwnershipChange(string cardInstanceId, string previousOwnerId, string newOwnerId, string transactionId)
        {
            CardInstanceId = cardInstanceId;
            PreviousOwnerId = previousOwnerId;
            NewOwnerId = newOwnerId;
            TransactionId = transactionId;
        }
    }

    /// <summary>
    /// Domain events raised after a transaction commits. Feature services subscribe here
    /// instead of polling, so the showcase, set tracker, card shows and progression all
    /// react to the same committed facts.
    /// </summary>
    public sealed class GameEvents
    {
        public event Action<OwnershipChange> OwnershipChanged;
        public event Action<GameAction> ActionRecorded;
        public event Action<GameNotification> NotificationCreated;
        public event Action<string> CardChanged;

        internal void RaiseOwnershipChanged(OwnershipChange change) => OwnershipChanged?.Invoke(change);
        internal void RaiseAction(GameAction action) => ActionRecorded?.Invoke(action);
        internal void RaiseNotification(GameNotification n) => NotificationCreated?.Invoke(n);
        internal void RaiseCardChanged(string cardId) => CardChanged?.Invoke(cardId);
    }

    /// <summary>
    /// Shared runtime context: content, the single game state, clock, id/rng generators and
    /// the domain event bus. Every service is constructed from one context.
    /// </summary>
    public sealed class GameContext
    {
        public ContentDatabase Content { get; }
        public GameState State { get; }
        public GameClock Clock { get; }
        public IdGenerator Ids { get; }
        public DeterministicRng Rng { get; }
        public GameEvents Events { get; } = new GameEvents();

        private Transaction _current;

        public GameContext(ContentDatabase content, GameState state)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Clock = new GameClock(state.Clock, content.Pack.CalendarEpochUtc);
            Ids = new IdGenerator(state.Ids);
            Rng = new DeterministicRng(state.Rng);
        }

        public long Now => Clock.Now;

        public bool IsProcessed(string transactionId)
            => !string.IsNullOrEmpty(transactionId) && State.ProcessedTransactions.Contains(transactionId);

        /// <summary>
        /// Starts a logical transaction. Callers validate first, then mutate, then Commit.
        /// Domain events are queued and dispatched only on commit, so a failed operation never
        /// notifies listeners. Nested Begin calls join the outer transaction.
        /// </summary>
        public Transaction Begin(string transactionId = null)
        {
            if (_current != null) return _current.Nest();
            _current = new Transaction(this, string.IsNullOrEmpty(transactionId) ? Ids.Next(IdPrefixes.Transaction) : transactionId);
            return _current;
        }

        internal void EndTransaction(Transaction tx) => _current = null;

        public Transaction Current => _current;

        // ------------------------------------------------------------- helpers used inside transactions

        public GameAction RecordAction(Transaction tx, GameActionType type, Action<GameAction> fill = null)
        {
            var action = new GameAction
            {
                ActionId = tx.Id + ":" + type + ":" + tx.NextLocalIndex(),
                Type = type,
                Time = Now,
                TransactionId = tx.Id,
            };
            fill?.Invoke(action);
            State.Actions.Add(action);
            tx.Enqueue(() => Events.RaiseAction(action));
            return action;
        }

        public GameAction RecordCardAction(Transaction tx, GameActionType type, CardInstance card, string productId = null, string refId = null)
        {
            var template = Content.Cards[card.TemplateId];
            var set = Content.Sets[template.SetId];
            return RecordAction(tx, type, a =>
            {
                a.CardInstanceId = card.InstanceId;
                a.TemplateId = card.TemplateId;
                a.ParallelId = card.ParallelId;
                a.SetId = set.Id;
                a.BrandId = set.BrandId;
                a.ProductId = productId;
                a.RefId = refId;
                a.IsRookie = template.IsRookie;
                a.IsVintage = set.IsVintage;
                a.IsSerialNumbered = card.IsSerialNumbered;
                a.IsOneOfOne = card.IsOneOfOne;
                a.Category = set.Category;
            });
        }

        public GameNotification Notify(NotificationKind kind, string title, string body, string refId, string dedupKey)
        {
            if (!string.IsNullOrEmpty(dedupKey))
            {
                foreach (var existing in State.Notifications)
                    if (existing.DedupKey == dedupKey) return null;
            }
            var n = new GameNotification
            {
                NotificationId = Ids.Next(IdPrefixes.Notification),
                Kind = kind,
                Title = title,
                Body = body,
                CreatedAt = Now,
                RefId = refId,
                DedupKey = dedupKey,
            };
            State.Notifications.Add(n);
            if (_current != null) _current.Enqueue(() => Events.RaiseNotification(n));
            else Events.RaiseNotification(n);
            return n;
        }
    }

    public sealed class Transaction : IDisposable
    {
        private readonly GameContext _ctx;
        private readonly List<Action> _pending = new List<Action>();
        private int _depth = 1;
        private int _localIndex;
        private bool _committed;

        public string Id { get; }
        public bool Committed => _committed;

        internal Transaction(GameContext ctx, string id)
        {
            _ctx = ctx;
            Id = id;
        }

        internal Transaction Nest()
        {
            _depth++;
            return this;
        }

        internal int NextLocalIndex() => _localIndex++;

        internal void Enqueue(Action action) => _pending.Add(action);

        public void Enqueue(OwnershipChange change) => _pending.Add(() => _ctx.Events.RaiseOwnershipChanged(change));

        public void EnqueueCardChanged(string cardId) => _pending.Add(() => _ctx.Events.RaiseCardChanged(cardId));

        /// <summary>Marks the transaction as committed. The outermost commit dispatches events.</summary>
        public void Commit()
        {
            if (_depth > 1) return;
            if (_committed) return;
            _committed = true;
            _ctx.State.ProcessedTransactions.Add(Id);
        }

        public void Dispose()
        {
            _depth--;
            if (_depth > 0) return;
            _ctx.EndTransaction(this);
            if (!_committed) return;
            // Listeners may start their own transactions (e.g. auto-granted rewards).
            foreach (var action in _pending) action();
        }
    }
}
