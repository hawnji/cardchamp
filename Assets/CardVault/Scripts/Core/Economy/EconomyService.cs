using System;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.State;

namespace CardVault.Core.Economy
{
    /// <summary>
    /// Closed virtual economy. Credits and event tickets only change through this service,
    /// inside a transaction, and every change is written to the ledger.
    /// </summary>
    public sealed class EconomyService
    {
        private readonly GameContext _ctx;

        public EconomyService(GameContext ctx)
        {
            _ctx = ctx;
        }

        private WalletState Wallet => _ctx.State.Wallet;

        public long Credits => Wallet.Credits;
        public int EventTickets => Wallet.EventTickets;

        public bool CanAfford(long credits, int tickets = 0) => Wallet.Credits >= credits && Wallet.EventTickets >= tickets;

        public OpResult CheckAfford(long credits, int tickets = 0)
        {
            if (Wallet.Credits < credits) return OpResult.Fail(OpError.InsufficientFunds, "Not enough credits.");
            if (Wallet.EventTickets < tickets) return OpResult.Fail(OpError.InsufficientTickets, "Not enough event tickets.");
            return OpResult.Success();
        }

        /// <summary>Applies a validated currency change. Throws if it would go negative (callers must check first).</summary>
        public LedgerEntry Apply(Transaction tx, LedgerKind kind, long creditsDelta, int ticketsDelta, string refId, string description)
        {
            if (tx == null) throw new ArgumentNullException(nameof(tx));
            if (Wallet.Credits + creditsDelta < 0 || Wallet.EventTickets + ticketsDelta < 0)
                throw new InvalidOperationException("Currency change would make the wallet negative; validate before applying.");

            Wallet.Credits += creditsDelta;
            Wallet.EventTickets += ticketsDelta;
            var entry = new LedgerEntry
            {
                TransactionId = tx.Id,
                Kind = kind,
                CreditsDelta = creditsDelta,
                TicketsDelta = ticketsDelta,
                CreditsAfter = Wallet.Credits,
                TicketsAfter = Wallet.EventTickets,
                Time = _ctx.Now,
                RefId = refId,
                Description = description,
            };
            _ctx.State.Ledger.Add(entry);
            return entry;
        }
    }

    /// <summary>Estimates card values for NPC pricing. Deterministic for a given card state.</summary>
    public sealed class PriceService
    {
        private readonly ContentDatabase _content;

        public PriceService(ContentDatabase content)
        {
            _content = content;
        }

        public long EstimateValue(CardInstance card)
        {
            var template = _content.Cards[card.TemplateId];
            var set = _content.Sets[template.SetId];
            var parallel = _content.Parallel(card.ParallelId);

            double value = Math.Max(1, template.BaseValue) * parallel.ValueMultiplier;
            if (template.IsRookie) value *= 1.25;
            if (template.IsAutograph) value *= 2.0;
            if (card.IsOneOfOne) value *= 3.0;
            if (set.IsVintage) value *= 1.4;

            if (card.Grade.HasValue) value *= GradeMultiplier(card.Grade.Value);
            else value *= ConditionMultiplier(card.Condition);

            return Math.Max(1, (long)Math.Round(value));
        }

        public long EstimateProductValue(ProductDef product, double vintageMultiplier = 1.0)
            => Math.Max(1, (long)Math.Round(product.PriceCredits * (product.IsVintage ? Math.Max(1.0, vintageMultiplier) : 1.0)));

        public static double ConditionMultiplier(double condition)
            => 0.35 + 0.65 * Math.Pow(Math.Max(1, Math.Min(10, condition)) / 10.0, 2);

        public static double GradeMultiplier(double grade)
        {
            if (grade >= 10) return 4.0;
            if (grade >= 9.5) return 2.6;
            if (grade >= 9) return 1.8;
            if (grade >= 8) return 1.2;
            if (grade >= 7) return 0.9;
            return 0.6;
        }
    }
}
