# Card Vault architecture

## Layers

```
CardVault.Unity  (UnityEngine, UI Toolkit)        presentation only
   │  calls services, renders state, never mutates GameState directly
   ▼
CardVault.Core   (netstandard2.1, no UnityEngine)  all rules and persistence
   ├─ GameContext: content, GameState, GameClock, IdGenerator, DeterministicRng, GameEvents
   ├─ Services: inventory, economy, packs, market, grading, history, sets, rewards,
   │            showcase, calendar/shop, card shows, progression, home summary, saves
   └─ CardVaultGame: composition root, load/tick/save lifecycle
```

The core has no engine references, so the same code runs in Unity, in Unity's EditMode test runner and in the .NET test harness.

## State

`GameState` is the single persistent object. Each fact has one home:

- Card ownership lives only in `CardInstance.OwnerId`.
- Currency lives only in `Wallet`.
- Every currency movement is appended to the `Ledger`.
- Every meaningful player action is appended to the `Actions` log, which challenges, achievements and progression read.
- Card provenance is appended to `CardHistory`.

Derived views (set progress, release status, the home summary) are computed from state on demand and never stored, so they cannot drift.

## Transactions

Every operation that changes state follows the same pattern:

1. Check the transaction id against `ProcessedTransactions`. A repeated id returns `AlreadyProcessed` and changes nothing.
2. Validate everything (ownership, funds, stock, availability, locks) before mutating.
3. `ctx.Begin(txId)` opens a `Transaction`; mutate; `Commit()`.
4. Domain events (ownership changed, action recorded, notification, card changed, XP) are queued during the transaction and dispatched only after commit.

Nested `Begin` calls join the outer transaction, so a pack purchase that triggers a set milestone that grants a reward is one atomic unit. Results are `OpResult` / `OpResult<T>` with an `OpError` code; the UI shows the message and never has to guess what happened.

## Identity and scarcity

- Instance ids come from a persisted `IdGenerator`, so they are unique across sessions.
- Serial-numbered parallels are allocated through a global serial registry keyed `template|parallel|serial`. A 1/1 can exist exactly once in the whole game, whoever owns it (player, NPC, show vendor).
- Release stock is allocated once. Retired releases move a fixed allocation to the vintage market; vintage sealed product is never created from nothing.

## Features

| Feature | Service | Notes |
| --- | --- | --- |
| Card history and provenance | `CardHistoryService` | Append-only. Entry id `hist:{tx}:{card}:{type}` makes appends idempotent. Corrections are new entries that reference the original. `VerifyIntegrity` checks that the ownership chain matches the current owner. |
| Set completion rewards | `SetRegistryService`, `SetRewardService` | Milestones (percent, all base, rookies, subject, parallel, graded) are evaluated from the cards the player owns. Rewards go through `RewardService` with key `set:{set}:{milestone}`, granted once even if the card is sold and re-acquired. |
| Collector's showcase | `ShowcaseService` | Areas with eligibility rules and unlock conditions, layouts, cosmetics. References to cards the player no longer owns are purged on load and on ownership change. |
| Release calendar | `ReleaseCalendarService`, `ShopService` | Status (Hidden → Announced → Upcoming → Available → LimitedStock → SoldOut → Retired → Archived) is a pure function of time, config and remaining stock. |
| Card show events | `CardShowService` | Occurrences `eventId@index` from a deterministic schedule (daily 1 day, weekly 7, monthly 28). Vendor cards are real NPC-owned instances created at first visit. Challenges count actions in the occurrence window. Points redeem until the show ends plus one day. |
| Collector progression | `ProgressionService`, `UnlockEvaluator` | Processes the action log exactly once (tracked by `ProcessedActionCount`) into stats, XP, ranks, achievements and showcase upgrades. |

All rewards from all features go through `RewardService.Grant(key, …)`. Key formats: `set:{set}:{milestone}`, `show:{occurrence}:{reward}`, `show:{occurrence}:complete`, `ach:{id}`, `rank:{n}`.

## Offline clock and scheduling

`GameClock` keeps game time in seconds since the game epoch (2026-01-01). New games start on day 45.

- On each sync the clock advances by the real time elapsed since the highest device time it has seen, capped at 72 hours per sync.
- If the device clock moves backward, game time does not. The clock waits until the device passes the highest time it observed. Setting the device clock forward gains at most 72 hours per sync.
- `AdvanceBy` is the developer accelerated calendar (Profile > Settings > Advance 1 day / 7 days).

`CardVaultGame.Tick` and `AdvanceGameTime` run, in order: grading completions, release calendar (reminders, stock, retirement), card shows (finalizing closed shows into history) and the daily market refresh. Each step is idempotent, so a long absence catches up correctly in one tick.

## Saves

`SaveService` writes an envelope `{schemaVersion, checksum, payload}` where the checksum is SHA-256 of the exact payload text.

Write order: write to a temp file, read it back and verify the checksum, copy the current primary to `.bak`, then move the temp file over the primary. A crash at any step leaves either the old or the new save intact.

Load order: the primary; if it is missing, corrupt or fails its checksum, the backup; otherwise a new game. Load warnings are kept in `CardVaultGame.LoadWarnings`.

`SaveMigrator` upgrades older schemas step by step. v1 → v2 backfills `MigrationBackfill` history entries for cards that predate card history. After load, `OnLoaded` rebuilds the history index, re-evaluates set rewards, catches up progression and purges invalid showcase references.

The Unity layer saves on a 1.5 s debounce after each change, and immediately on pause, focus loss and quit.

## UI

The UI is UI Toolkit built in code (`AppShell`, `GameScreen`, `Ui` helpers), with no UXML or scene setup.

- Five tabs: Home, Shop, Collection, Market, Profile.
- A top bar with credits, tickets and the inbox.
- A screen stack with Android back support, toasts and modal sheets.

Card visuals come from a pooled procedural `CardView`. Art is loaded through an LRU `ArtCache`. Card grids paginate at 24, so large collections stay cheap on mid-range devices. A reduced effects setting turns off shine and animation.
