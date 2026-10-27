# Card Vault

Premium, Android-first, offline single-player sports-card and TCG collecting simulator built in Unity.

Collect, open, grade, trade and display cards across fictional sports and TCG sets. The game includes card show events, set completion rewards, a collector's showcase, card history and provenance, a release calendar and integrated collector progression. Everything runs offline on an internal game clock.

## Project layout

| Path | What it is |
| --- | --- |
| `Assets/CardVault/Scripts/Core` | Engine-free game logic (`CardVault.Core`, no UnityEngine references). All rules, economy, saves and the six collector features live here. |
| `Assets/CardVault/Scripts/Unity` | Unity presentation layer (`CardVault.Unity`): bootstrap, UI Toolkit app shell and screens, procedural card renderer. |
| `Assets/CardVault/Tests/EditMode` | NUnit tests. Unity's Test Runner runs them as EditMode tests; the .NET harness runs the same files. |
| `Assets/CardVault/Resources/Content/cardvault_content.json` | Generated game content (cards, sets, products, releases, card shows, cosmetics, achievements, ranks). |
| `tools/content/generate_content.py` | Content source. Edit this, then regenerate the JSON. |
| `tools/dotnet` | .NET 8 harness: builds the core, runs the tests and compile-checks the Unity layer. |
| `docs/` | Architecture, offline behaviour and implementation status. |

## Running in Unity

1. Open the folder with **Unity 6 (6000.6.62f1)**, which `ProjectSettings/ProjectVersion.txt` pins. Unity resolves Newtonsoft JSON, the Input System and the test framework from `Packages/manifest.json`. If Unity asks to enable the new input backend, either answer works: the Android back button is handled for both.
2. Create an empty scene, save it (for example `Assets/CardVault/Scenes/Main.unity`) and add it to **File > Build Settings**. No GameObjects are needed: `CardVaultApp` bootstraps itself after the scene loads and builds the whole UI in code.
3. Press Play. The Game view should use a portrait resolution such as 1080x1920.
4. Run the tests with **Window > General > Test Runner > EditMode > Run All**.

Android build: switch the platform to Android, set Orientation to Portrait, Scripting Backend IL2CPP and Target Architectures ARM64, then Build.

## Running the tests without Unity

Requires the .NET 8 SDK.

```sh
cd tools/dotnet
dotnet test Tests/CardVault.Tests.csproj
dotnet build UnityCompileCheck/CardVault.UnityCompileCheck.csproj   # compile-checks the Unity layer
```

The compile check builds `Scripts/Unity` against the `UnityEngine.Modules` reference assemblies from NuGet. It catches type and API errors but does not run anything.

## Regenerating content

```sh
python3 tools/content/generate_content.py
```

The game validates the content at load (missing references, duplicate ids, bad pack odds) and fails fast with a list of problems.

## Artwork

Card, set and show art is procedural by default. Drop images into `Assets/CardVault/Resources/Art/` named after the content's `artKey` (sets use `sets/{artKey}`) and the card renderer picks them up automatically.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/IMPLEMENTATION_STATUS.md](docs/IMPLEMENTATION_STATUS.md).
