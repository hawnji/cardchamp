using System;
using System.IO;
using System.Linq;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Game;
using CardVault.Core.History;
using CardVault.Core.Save;
using CardVault.Core.State;

namespace CardVault.Tests
{
    /// <summary>Shared fixtures. Every test runs against the real shipped content file.</summary>
    public static class TestContent
    {
        private static ContentDatabase _cached;

        public static string ContentPath
        {
            get
            {
#if CARDVAULT_DOTNET_TESTS
                return Path.Combine(AppContext.BaseDirectory, "Content", "cardvault_content.json");
#else
                return Path.Combine("Assets", "CardVault", "Resources", "Content", "cardvault_content.json");
#endif
            }
        }

        public static string Json => File.ReadAllText(ContentPath);

        public static ContentDatabase Database => _cached ?? (_cached = ContentDatabase.FromJson(Json));
    }

    public static class TestGame
    {
        public static readonly DateTime StartTime = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        public static CardVaultGame Create(ulong seed = 42) => CardVaultGame.NewGame(TestContent.Database, seed, StartTime, "test");

        /// <summary>Gives the player a specific card through a real, history-recording transaction.</summary>
        public static CardInstance GiveCard(CardVaultGame game, string templateId, string parallelId = ContentDatabase.BaseParallelId,
            double condition = 9.0, double? grade = null)
        {
            using (var tx = game.Context.Begin())
            {
                var card = game.Inventory.Create(tx, new CardCreationRequest
                {
                    TemplateId = templateId,
                    ParallelId = parallelId,
                    OwnerId = Owners.Player,
                    Condition = condition,
                    Grade = grade,
                    OriginRef = "test",
                });
                if (card == null) return null;
                game.History.Append(tx, card.InstanceId, HistoryEventType.Acquired, e =>
                {
                    e.PreviousOwnerId = Owners.Unallocated;
                    e.NewOwnerId = Owners.Player;
                    e.SetId = game.Content.Cards[templateId].SetId;
                });
                game.Context.RecordCardAction(tx, GameActionType.CardAcquired, card, refId: "test");
                tx.Commit();
                return card;
            }
        }

        public static void GiveCredits(CardVaultGame game, long credits)
        {
            using (var tx = game.Context.Begin())
            {
                game.Economy.Apply(tx, LedgerKind.Reward, credits, 0, "test", "test credits");
                tx.Commit();
            }
        }

        public static void DrainCredits(CardVaultGame game)
        {
            using (var tx = game.Context.Begin())
            {
                game.Economy.Apply(tx, LedgerKind.CardPurchase, -game.Economy.Credits, 0, "test", "drain");
                tx.Commit();
            }
        }

        /// <summary>Saves, discards the in-memory game, and loads a fresh instance from storage.</summary>
        public static CardVaultGame SaveAndReload(CardVaultGame game, InMemorySaveStorage storage = null)
        {
            storage = storage ?? new InMemorySaveStorage();
            var saves = new SaveService(storage);
            game.Save(saves);
            var result = saves.Load();
            if (result.State == null) throw new InvalidOperationException("Reload failed: " + string.Join("; ", result.Warnings));
            return CardVaultGame.FromState(TestContent.Database, result.State);
        }

        public static CardInstance FirstPlayerCard(CardVaultGame game) => game.Inventory.PlayerCards.OrderBy(c => c.InstanceId).First();

        public static string FirstPlayerPack(CardVaultGame game, string productId = null)
            => game.Sealed.PlayerUnopened.Where(s => productId == null || s.ProductId == productId)
                .OrderBy(s => s.InstanceId).Select(s => s.InstanceId).FirstOrDefault();
    }
}
