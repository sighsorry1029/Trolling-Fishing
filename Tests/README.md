# Storage regression checks

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project Tests/TrollingFishing.RegressionTests.csproj
```

This console harness uses no test packages, game installation, or BepInEx installation.
It compiles the actual `FishingRodBagStorage.cs` and `FishingRodBagRules.cs` through linked
source files; it does not copy their implementation. It exits with code 1 on failure.
The production project explicitly lists its source files and does not compile `Tests/`.

The 26 checks cover inventory package version 109, preservation and rejection of older
and future formats, valid empty bags, malformed and
partial loads, missing optional-mod prefabs, failed add/save preservation, catch fallback,
overflow after shrinking and expanding capacity, one storage clone per item, live item
immutability, overcapacity rejection, exceptions before the metadata commit, and weight
migration without `Inventory.Load` or item cloning.

`GameStubs.cs` contains small managed doubles for Unity/game APIs. Its package primitives
use `BinaryReader`/`BinaryWriter`; its inventory fixture codec models the format-109 header
and item flags inspected in original Valheim 1.0.7 DLLs. Custom-data fixtures use small
dictionaries; this is not a complete replacement for the game's serializer. The double deliberately skips
missing prefabs, matching the failure mode that production must detect. Unrelated game
behavior is omitted; world spawning throws instead of pretending to work.

These are storage regression checks, **not a game execution test**. They do not establish
the correctness of the real game's serializer, Unity initialization/destruction, Harmony
patch targets/order, UI or proxy event cleanup, external mod callbacks, or networking.
The compatibility review also ran an isolated probe using installed Unity Mono and original
game DLLs: final merged mod accessor initialization, `Inventory.Changed` invocation,
empty `Inventory.Save`, `ItemData.Load` with 260 custom-data entries, and a cached
`ReturnBait` delegate observing a subsequently installed Harmony prefix. These passed
against both client and dedicated-server DLLs using their installed Mono runtimes. This probe does
not initialize a Unity scene or run the full Harmony patch set.

Real Valheim checks remain necessary for preserved but blocked old-format rods, new format-109
rods, optional prefab removal and
restoration, Azu/QuickStack combinations, and client/host/dedicated-server item accounting.
The item-count guard also does not detect a game's stack clamp caused by an external mod
changing an item's maximum stack size.

## EpicLoot input follow-up (2026-09-14)

The bag Update Prefix now has method-level priority 801 and HarmonyBefore EpicLoot. The installed HarmonyX loses class-only priorities when merging a method without its own priority; simply checking attribute values is insufficient. UsefulTankards' `Tests/HarmonyInputOrder.ps1`, run under Windows PowerShell with this final DLL as `-FishingDll`, processes the actual patch types through the installed `Harmony.CreateClassProcessor` without applying detours. It confirms effective Prefix priorities of 801 for both item-storage mods and 400 for the supplied EpicLoot 0.14.5 DLL.

The optional adapter caches EpicLoot's prompt/socket getters and configured modifier at startup. Opening the bag requires a local owner and a rod still in inventory; transitioning from sockets calls the normal CloseContainer path, then rechecks ownership/membership. No EpicLoot binary, JSON dependency, saved bag format, or fishing policy was changed.

All 26 storage checks and the Debug build passed (zero warnings/errors). The final merged DLL was deployed to the default Steam plugins folder and the supplied Gale profile, with SHA-256 verification. Release 1.1.3 was packaged after the same storage and effective-order checks. The storage harness does not compile the UI source or execute EpicLoot; actual in-game modifier/prompt handling, socket save/event cleanup, controller interaction, Azu proxy transitions, and multiplayer item accounting still need gameplay validation.
