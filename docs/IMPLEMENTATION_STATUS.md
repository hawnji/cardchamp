# Implementation status

The repository was empty, so the whole game was built from scratch: an engine-free core, a Unity UI Toolkit front end, generated content and an automated test suite.

## Verification summary

| Check | How | Result |
| --- | --- | --- |
| Core compiles | `dotnet build tools/dotnet/Core` (netstandard2.1, C# 9) | Pass |
| Automated tests | `dotnet test tools/dotnet/Tests/CardVault.Tests.csproj` | **101 passed, 0 failed** |
| Unity layer compiles | `dotnet build tools/dotnet/UnityCompileCheck` against UnityEngine.Modules 2021.3.33 reference assemblies (the newest published; see known issues) | Pass, 0 warnings |
| Unity Editor import, Play Mode, EditMode runner | Not available in this environment | **Not verified** |
| Android build and on-device performance | Not available in this environment | **Not verified** |

The tests are the same source files Unity's EditMode runner uses. The only difference is the path they load content from, which the `CARDVAULT_DOTNET_TESTS` define selects.

## Phases

### A + B: core systems and card history

Implemented:

- Content database with validation. Generated content covers 102 cards, 7 sets (2 vintage), 14 parallels, 12 products, 11 releases, 5 card shows, cosmetics, achievements and 10 ranks.
- Card instances with condition and grade, plus a global serial registry for numbered and 1/1 cards.
- Wallet and append-only ledger.
- Pack opening with deterministic RNG.
- Market with NPC listings, NPC buying, player listings and trade offers.
- Grading.
- Transactions with idempotency and post-commit events.
- Saves with checksum, backup and migration.
- Card history: append-only, idempotent, corrections, ownership chain, integrity check, v1 → v2 migration backfill.

Tests: `CoreSystemsTests` (18) and `CardHistoryTests` (18).

### C: set completion rewards

Implemented:

- Set registry with progress per set.
- Six milestone kinds, with auto-granted or claimable rewards.
- Once-only grant keys.
- Celebration on first completion.
- Records that survive selling the cards.

Tests: `SetRewardTests` (11).

### D: collector's showcase

Implemented:

- Nine display areas with eligibility rules and unlock conditions.
- Place, feature, swap and remove.
- Layouts, cosmetics and reduced effects.
- Purge of cards no longer owned.

Tests: `ShowcaseTests` (12).

### E: release calendar

Implemented:

- Eight-state release lifecycle computed from time and stock.
- Finite stock and purchase limits.
- Watchlist and reminders.
- Retirement that moves a fixed allocation into the vintage market.
- Calendar views: upcoming, current, recent, retiring, seasonal, vintage, archive, card shows and month.
- Shop.

Tests: `ReleaseCalendarTests` (11).

### F: card show events

Implemented:

- Deterministic schedules and themes.
- Vendors holding real NPC-owned card instances with finite stock, featured-set discounts and ticket prices.
- Challenges verified against the action log.
- Event points with redemption until one day after close.
- Completion bonus and event history.

Tests: `CardShowTests` (16).

### G: integrated collector progression

Implemented:

- Stats computed once per action.
- XP and ranks, achievements (including hidden ones) and showcase upgrades.
- Rank and achievement rewards granted once.
- Unlock evaluator shared by all features.

Tests: `ProgressionTests` (13).

### H: integration and UI

Implemented:

- End-to-end flows covering a pack, set milestones, a show, progression, and save and reload with no duplicate rewards.
- Unity bootstrap: loads or creates the save, ticks the clock every 15 s, debounces autosave, saves on pause, focus loss and quit, and handles the Android back button.
- Screens built on UI Toolkit:
  - Home: profile, claimable rewards, featured release, active shows, upcoming releases, collection milestones, recent cards.
  - Shop: new, vintage, my sealed and retiring products, plus pack reveal with numbered-card callouts.
  - Release calendar and release detail.
  - Collection: binder with filters and pagination, set registry, set detail with milestone claims, card detail with Details and History tabs.
  - Showcase room and customization.
  - Market: listings, sell, trades, vintage, my listings and history.
  - Profile: overview and stats, achievements, card show records, and settings for titles, reduced effects and the accelerated calendar.
  - Inbox, which opens the item a notification refers to.
  - Card show list and show detail with vendors, challenges and rewards.

Tests: `IntegrationTests` (2).

## Transaction and save integrity rules

| Rule | Where enforced | Test coverage |
| --- | --- | --- |
| Validate before mutating | Every service operation | Insufficient funds, out of stock, not owned, locked |
| Atomic multi-step changes | `Transaction`, nested `Begin` joins | Failed purchase, sale, trade and vendor purchase leave no trace |
| Idempotent transaction ids | `ProcessedTransactions` | Replayed transaction id rejected, no duplicate history |
| Single source of ownership | `CardInstance.OwnerId` | Ownership chain matches owner |
| Once-only rewards | `RewardService` grant keys | Re-acquire, reload, re-evaluate |
| Unique serials | Serial registry | 1/1 exists once across player, NPCs and shows |
| Append-only history and ledger | `CardHistoryService`, `EconomyService` | Corrections, integrity check |
| Safe save writes | Temp, verify, backup, move | Corrupt primary falls back to backup; interrupted write keeps previous save |
| Versioned migration | `SaveMigrator` | v1 save loads with backfilled history |
| Monotonic offline clock | `GameClock` | Backward device time, 72 h cap |

## Known issues and limits

- **Unity not run.** The UI compiles against Unity reference assemblies but has not been opened in the Editor. Layout, styling and touch feel need a pass on a device. The project targets Unity 6 (6000.6.6f1), but the only reference assemblies available outside the Editor are 2021.3. The UI code avoids APIs that Unity 6 removed or deprecated (for example it uses `FindFirstObjectByType`), but Unity 6-specific compile errors or warnings can only be confirmed in the Editor.
- **Back button.** The project has no Input System package dependency (version 1.14.0 failed to compile on 6000.6). With the legacy Input Manager the back key is polled; otherwise UI Toolkit's `NavigationCancelEvent` handles it. Both branches compile in the check.
- **Package versions.** `com.unity.test-framework` 1.5.1 is set as a minimum. If 6000.6 bundles a newer locked version, the Package Manager upgrades it on first open.
- **Theme stylesheet.** `Resources/UI/CardVaultTheme.tss` imports `unity-theme://default`. If Unity reports a missing theme on first import, create one with **Create > UI Toolkit > Default Runtime Theme File** at the same path.
- **Art is procedural.** No image assets are included. The art pipeline is ready for drop-in images (see README).
- **No audio, no haptics.**
- **Collector name cannot be changed** in the UI yet; it defaults to "Collector".
- **Tuning.** Prices, odds and XP curves are first-pass values in `generate_content.py`.
- **`.meta` files** are not committed; Unity generates them on first import.

## Manual verification steps (Unity and Android)

1. Open the project in Unity 6000.6.6f1 and let packages resolve. Confirm the console shows no compile errors or obsolete-API warnings.
2. Run **Test Runner > EditMode > Run All**. Expect 101 passing tests.
3. Create and save an empty scene, add it to Build Settings and press Play at 1080x1920.
4. Smoke-test the main flows:
   1. Open a starter pack from **Shop > My Sealed**. The pulled cards should appear in Collection.
   2. Buy a product from **Shop > New**.
   3. Claim a set reward from the set registry.
   4. Place a card in the Showcase.
   5. Sell a card in **Market > Sell**.
   6. Use **Profile > Settings > Advance 1 day** until a card show opens, then enter it, buy from a vendor, complete a challenge and redeem a reward.
   7. Open a card's **History** tab and confirm every step above appears.
5. Quit and relaunch. Confirm credits, cards, show points and rewards are unchanged and that no reward is granted twice.
6. Android: build an ARM64 IL2CPP APK, install it on a mid-range device and repeat step 4. Check that scrolling a 500+ card collection stays smooth, that the back button pops screens, and that backgrounding the app and killing it from recents keeps progress.

## Next step

Open the project in Unity, run the EditMode tests, then do the device smoke test above and fix whatever layout issues it shows.
