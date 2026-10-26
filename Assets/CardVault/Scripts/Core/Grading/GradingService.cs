using System;
using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Economy;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.State;

namespace CardVault.Core.Grading
{
    /// <summary>
    /// Grading: the player submits a raw card for a fee; after a game-time turnaround the
    /// card receives a persistent grade derived from its condition.
    /// </summary>
    public sealed class GradingService
    {
        private readonly GameContext _ctx;
        private readonly CardInventoryService _inventory;
        private readonly CardHistoryService _history;
        private readonly EconomyService _economy;
        private readonly CardCatalog _catalog;

        public GradingService(GameContext ctx, CardInventoryService inventory, CardHistoryService history,
            EconomyService economy, CardCatalog catalog)
        {
            _ctx = ctx;
            _inventory = inventory;
            _history = history;
            _economy = economy;
            _catalog = catalog;
        }

        private GradingConfig Config => _ctx.Content.Pack.Grading;

        public IEnumerable<GradingSubmission> Pending => _ctx.State.Grading.Submissions.Where(s => s.Status == GradingStatus.Submitted);
        public IEnumerable<GradingSubmission> All => _ctx.State.Grading.Submissions;

        public OpResult CanSubmit(CardInstance card)
        {
            if (card == null) return OpResult.Fail(OpError.NotFound, "Card not found.");
            if (card.OwnerId != Owners.Player) return OpResult.Fail(OpError.NotOwned, "You do not own this card.");
            if (card.IsGraded) return OpResult.Fail(OpError.NotEligible, "This card is already graded.");
            if (card.Status != CardStatus.Available) return OpResult.Fail(OpError.CardBusy, "This card is listed or already at the grader.");
            return _economy.CheckAfford(Config.FeeCredits);
        }

        public OpResult<GradingSubmission> Submit(string cardInstanceId, string transactionId = null)
        {
            if (_ctx.IsProcessed(transactionId)) return OpResult<GradingSubmission>.Fail(OpError.AlreadyProcessed, "Already submitted.");
            var card = _inventory.Get(cardInstanceId);
            var check = CanSubmit(card);
            if (!check.Ok) return OpResult<GradingSubmission>.Fail(check.Error, check.Message);

            using (var tx = _ctx.Begin(transactionId))
            {
                var submission = new GradingSubmission
                {
                    SubmissionId = _ctx.Ids.Next(IdPrefixes.Grading),
                    CardInstanceId = card.InstanceId,
                    SubmittedAt = _ctx.Now,
                    ReadyAt = _ctx.Now + Config.TurnaroundSeconds,
                    FeeCredits = Config.FeeCredits,
                    Status = GradingStatus.Submitted,
                    TransactionId = tx.Id,
                };
                _economy.Apply(tx, LedgerKind.GradingFee, -Config.FeeCredits, 0, submission.SubmissionId,
                    "Grading fee for " + _catalog.DisplayName(card));
                card.Status = CardStatus.InGrading;
                _ctx.State.Grading.Submissions.Add(submission);
                _history.Append(tx, card.InstanceId, HistoryEventType.GradingSubmitted, e =>
                {
                    e.PreviousCondition = card.Condition;
                    e.PriceCredits = Config.FeeCredits;
                });
                tx.Commit();
                return OpResult<GradingSubmission>.Success(submission);
            }
        }

        /// <summary>Completes every submission whose turnaround has elapsed. Safe to call repeatedly.</summary>
        public IReadOnlyList<GradingSubmission> ProcessReady()
        {
            var completed = new List<GradingSubmission>();
            foreach (var sub in Pending.Where(s => s.ReadyAt <= _ctx.Now).ToList())
            {
                var card = _inventory.Get(sub.CardInstanceId);
                if (card == null) continue;

                // The grade roll is seeded from the submission, so it is fixed regardless of when
                // the player next opens the game.
                var rng = new DeterministicRng(new RngState { Value = DeterministicRng.Hash(_ctx.State.Ids.Namespace + "/" + sub.SubmissionId) });
                double grade = RollGrade(rng, card.Condition, Config);

                using (var tx = _ctx.Begin("grade:" + sub.SubmissionId))
                {
                    double? previousGrade = card.Grade;
                    card.Grade = grade;
                    card.GradingCertId = "CERT-" + sub.SubmissionId.ToUpperInvariant();
                    if (card.Status == CardStatus.InGrading) card.Status = CardStatus.Available;
                    sub.Status = GradingStatus.Completed;
                    sub.AwardedGrade = grade;
                    _history.Append(tx, card.InstanceId, HistoryEventType.GradeAwarded, e =>
                    {
                        e.PreviousCondition = card.Condition;
                        e.PreviousGrade = previousGrade;
                        e.NewGrade = grade;
                    });
                    _ctx.RecordCardAction(tx, GameActionType.CardGraded, card, refId: sub.SubmissionId);
                    _ctx.Notify(NotificationKind.Grading, "Grading complete",
                        _catalog.DisplayName(card) + " came back graded " + grade.ToString("0.#") + ".",
                        card.InstanceId, "graded:" + sub.SubmissionId);
                    tx.Commit();
                }
                completed.Add(sub);
            }
            return completed;
        }

        /// <summary>Grade distribution centred on condition, in half-point steps.</summary>
        public static double RollGrade(DeterministicRng rng, double condition, GradingConfig config)
        {
            double centre = condition - 0.3;
            double roll = centre + (rng.NextDouble() + rng.NextDouble() - 1.0) * 1.5;
            double grade = Math.Round(roll * 2, MidpointRounding.AwayFromZero) / 2.0;
            return Math.Max(config.MinimumGrade, Math.Min(config.MaximumGrade, grade));
        }
    }
}
