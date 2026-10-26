using System;
using System.Collections.Generic;
using CardVault.Core.Cards;
using CardVault.Core.Common;
using CardVault.Core.Content;
using CardVault.Core.Economy;
using CardVault.Core.Grading;
using CardVault.Core.History;
using CardVault.Core.Market;
using CardVault.Core.Packs;
using CardVault.Core.Save;
using CardVault.Core.State;

namespace CardVault.Core.Game
{
    /// <summary>
    /// Composition root. Builds every domain service over one GameContext and exposes the
    /// game loop entry points (Tick, Save, Load). UI and tests use this as their only facade.
    /// </summary>
    public sealed class CardVaultGame
    {
        public GameContext Context { get; }
        public ContentDatabase Content => Context.Content;
        public GameState State => Context.State;
        public GameClock Clock => Context.Clock;
        public GameEvents Events => Context.Events;

        public CardCatalog Catalog { get; }
        public CardHistoryService History { get; }
        public CardInventoryService Inventory { get; }
        public SealedInventoryService Sealed { get; }
        public EconomyService Economy { get; }
        public PriceService Prices { get; }
        public PackOpeningService Packs { get; }
        public MarketService Market { get; }
        public GradingService Grading { get; }

        /// <summary>Warnings from loading/migrating the save (shown in a debug panel, never to players as paths).</summary>
        public List<string> LoadWarnings { get; } = new List<string>();

        private CardVaultGame(ContentDatabase content, GameState state)
        {
            Context = new GameContext(content, state);
            Catalog = new CardCatalog(content);
            History = new CardHistoryService(Context);
            Inventory = new CardInventoryService(Context, History);
            Sealed = new SealedInventoryService(Context);
            Economy = new EconomyService(Context);
            Prices = new PriceService(content);
            Packs = new PackOpeningService(Context, Inventory, Sealed, History, Catalog);
            Grading = new GradingService(Context, Inventory, History, Economy, Catalog);
            Market = new MarketService(Context, Inventory, Sealed, History, Economy, Prices, Catalog);
        }

        /// <summary>Starts a fresh game with starting credits and products from content.</summary>
        public static CardVaultGame NewGame(ContentDatabase content, ulong seed, DateTime deviceUtcNow, string saveNamespace = null)
        {
            var state = new GameState
            {
                ContentVersion = content.Pack.ContentVersion,
                Rng = { Value = seed },
                Ids = { Namespace = saveNamespace ?? seed.ToString("x").PadLeft(8, '0').Substring(0, 8) },
            };
            state.Clock.GameSeconds = content.Pack.Economy.StartGameDay * GameClock.SecondsPerDay;
            var game = new CardVaultGame(content, state);
            game.Clock.SyncWithDevice(deviceUtcNow);
            state.CreatedAtGameSeconds = game.Clock.Now;

            using (var tx = game.Context.Begin("new-game"))
            {
                var economy = content.Pack.Economy;
                game.Economy.Apply(tx, LedgerKind.StartingGrant, economy.StartingCredits, economy.StartingEventTickets, null, "Welcome to Card Vault");
                foreach (var productId in economy.StartingProductIds)
                    game.Sealed.Create(tx, productId, Owners.Player, "starter-kit");
                foreach (var cosmetic in content.Pack.Cosmetics)
                    if (cosmetic.UnlockedByDefault) state.Profile.UnlockedCosmetics.Add(cosmetic.Id);
                tx.Commit();
            }
            game.OnLoaded();
            game.Tick(deviceUtcNow);
            return game;
        }

        public static CardVaultGame FromState(ContentDatabase content, GameState state)
        {
            var game = new CardVaultGame(content, state);
            game.OnLoaded();
            return game;
        }

        /// <summary>Loads the save (or starts a new game when none is usable) and catches up the clock.</summary>
        public static CardVaultGame LoadOrCreate(ContentDatabase content, SaveService saves, ulong newGameSeed, DateTime deviceUtcNow)
        {
            var result = saves.Load();
            CardVaultGame game;
            if (result.State == null)
            {
                game = NewGame(content, newGameSeed, deviceUtcNow);
            }
            else
            {
                game = FromState(content, result.State);
                game.Tick(deviceUtcNow);
            }
            game.LoadWarnings.AddRange(result.Warnings);
            return game;
        }

        /// <summary>Re-establishes derived indexes and reconciles references after load.</summary>
        private void OnLoaded()
        {
            History.RebuildIndex();
        }

        /// <summary>
        /// Advances the internal calendar from the device clock and runs time-based systems.
        /// Called on launch, on resume and periodically while playing.
        /// </summary>
        public void Tick(DateTime deviceUtcNow)
        {
            Clock.SyncWithDevice(deviceUtcNow);
            RunScheduledSystems();
        }

        /// <summary>Accelerated calendar for developers and tests.</summary>
        public void AdvanceGameTime(long seconds)
        {
            Clock.AdvanceBy(seconds);
            RunScheduledSystems();
        }

        private void RunScheduledSystems()
        {
            Grading.ProcessReady();
            Market.Tick();
        }

        public void Save(SaveService saves) => saves.Save(State);
    }
}
