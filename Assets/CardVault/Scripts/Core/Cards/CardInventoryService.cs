using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.State;

namespace CardVault.Core.Cards
{
    /// <summary>
    /// Card instance inventory: the only code that creates card instances or changes
    /// ownership. Serial numbers are allocated through a global registry, so every numbered
    /// card (including one-of-ones) exists at most once across player, NPCs and vendors.
    /// </summary>
    public sealed class CardInventoryService
    {
        private readonly GameContext _ctx;
        private readonly CardHistoryService _history;

        public CardInventoryService(GameContext ctx, CardHistoryService history)
        {
            _ctx = ctx;
            _history = history;
        }

        private GameState State => _ctx.State;

        public CardInstance Get(string instanceId)
            => instanceId != null && State.Cards.TryGetValue(instanceId, out var c) ? c : null;

        public bool IsOwnedBy(string instanceId, string ownerId)
        {
            var c = Get(instanceId);
            return c != null && c.OwnerId == ownerId;
        }

        public bool IsPlayerOwned(string instanceId) => IsOwnedBy(instanceId, Owners.Player);

        public IEnumerable<CardInstance> OwnedBy(string ownerId) => State.Cards.Values.Where(c => c.OwnerId == ownerId);

        public IEnumerable<CardInstance> PlayerCards => OwnedBy(Owners.Player);

        public int PlayerCardCount => State.Cards.Values.Count(c => c.OwnerId == Owners.Player);

        // ------------------------------------------------------------- serials

        public static string SerialKey(string templateId, string parallelId, int serial)
            => templateId + "|" + parallelId + "|" + serial;

        public int SerialsAllocated(string templateId, string parallelId)
        {
            var parallel = _ctx.Content.Parallel(parallelId);
            if (!parallel.IsSerialNumbered) return 0;
            int count = 0;
            for (int s = 1; s <= parallel.PrintRun; s++)
                if (State.SerialAllocations.ContainsKey(SerialKey(templateId, parallelId, s))) count++;
            return count;
        }

        public bool HasFreeSerial(string templateId, string parallelId)
        {
            var parallel = _ctx.Content.Parallel(parallelId);
            return !parallel.IsSerialNumbered || SerialsAllocated(templateId, parallelId) < parallel.PrintRun;
        }

        private int PickFreeSerial(string templateId, ParallelDef parallel)
        {
            var free = new List<int>();
            for (int s = 1; s <= parallel.PrintRun; s++)
                if (!State.SerialAllocations.ContainsKey(SerialKey(templateId, parallel.Id, s))) free.Add(s);
            return free.Count == 0 ? 0 : free[_ctx.Rng.Range(0, free.Count)];
        }

        // ------------------------------------------------------------- creation

        /// <summary>
        /// Creates a card inside <paramref name="tx"/> and records its Created history entry.
        /// The caller must record the first acquisition entry in the same transaction.
        /// Returns null if the parallel is serial-numbered and every serial is already allocated.
        /// </summary>
        public CardInstance Create(Transaction tx, CardCreationRequest request)
        {
            var template = _ctx.Content.Cards[request.TemplateId];
            var parallel = _ctx.Content.Parallel(request.ParallelId);

            int serial = 0;
            if (parallel.IsSerialNumbered)
            {
                serial = PickFreeSerial(template.Id, parallel);
                if (serial == 0) return null;
            }

            var card = new CardInstance
            {
                InstanceId = _ctx.Ids.Next(IdPrefixes.Card),
                TemplateId = template.Id,
                ParallelId = parallel.Id,
                SerialNumber = serial,
                PrintRun = parallel.PrintRun,
                OwnerId = request.OwnerId,
                Status = CardStatus.Available,
                Condition = Math.Round(Math.Max(1.0, Math.Min(10.0, request.Condition)), 1),
                Grade = request.Grade,
                Tradable = request.Tradable,
                CreatedAt = _ctx.Now,
                OriginRef = request.OriginRef,
                OriginSetId = template.SetId,
            };
            if (card.Grade.HasValue) card.GradingCertId = "CERT-" + card.InstanceId.ToUpperInvariant();

            State.Cards[card.InstanceId] = card;
            if (serial > 0) State.SerialAllocations[SerialKey(template.Id, parallel.Id, serial)] = card.InstanceId;

            // Created carries no owner: the caller records how the first owner obtained it
            // (pack pull, vendor stock, reward...) as the next entry in the same transaction.
            _history.Append(tx, card.InstanceId, HistoryEventType.Created, e =>
            {
                e.NewCondition = card.Condition;
                e.NewGrade = card.Grade;
                e.SetId = template.SetId;
                e.ProductId = request.ProductId;
                e.EventId = request.EventId;
                e.Description = CreationDescription(card, template, request);
            });
            return card;
        }

        private string CreationDescription(CardInstance card, CardTemplateDef template, CardCreationRequest request)
        {
            string setName = _ctx.Content.Sets[template.SetId].Name;
            string serial = CardCatalog.SerialLabel(card);
            string text = "Printed for " + setName;
            if (serial != null) text += ", serial " + serial;
            return text + ".";
        }

        // ------------------------------------------------------------- ownership

        /// <summary>
        /// Changes ownership inside <paramref name="tx"/>. The caller records the matching
        /// history entry (purchase, sale, trade...) in the same transaction.
        /// </summary>
        public void Transfer(Transaction tx, CardInstance card, string newOwnerId)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            string previous = card.OwnerId;
            if (previous == newOwnerId) return;
            card.OwnerId = newOwnerId;
            card.Status = CardStatus.Available;
            tx.Enqueue(new OwnershipChange(card.InstanceId, previous, newOwnerId, tx.Id));
        }

        /// <summary>Common validation for cards the player wants to give away.</summary>
        public OpResult CanPlayerRelease(CardInstance card)
        {
            if (card == null) return OpResult.Fail(OpError.NotFound, "Card not found.");
            if (card.OwnerId != Owners.Player) return OpResult.Fail(OpError.NotOwned, "You do not own this card.");
            if (!card.Tradable) return OpResult.Fail(OpError.NotTradable, "Commemorative cards cannot be sold or traded.");
            if (card.Status == CardStatus.InGrading) return OpResult.Fail(OpError.CardBusy, "This card is at the grading service.");
            return OpResult.Success();
        }
    }

    public sealed class CardCreationRequest
    {
        public string TemplateId;
        public string ParallelId = ContentDatabase.BaseParallelId;
        public string OwnerId;
        public double Condition = 9.0;
        public double? Grade;
        public bool Tradable = true;
        public string OriginRef;
        public string ProductId;
        public string EventId;
    }
}
