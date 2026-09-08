# Storage regression checks

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project Tests/TrollingFishing.RegressionTests.csproj
```

This console harness uses no test packages, game installation, or BepInEx installation.
It compiles the actual `FishingRodBagStorage.cs` and `FishingRodBagRules.cs` through linked
source files; it does not copy their implementation. It exits with code 1 on failure.
The production project explicitly lists its source files and does not compile `Tests/`.

Checks cover inventory package versions 100 through 106, valid empty bags, malformed and
partial loads, missing optional-mod prefabs, failed add/save preservation, catch fallback,
overflow after shrinking and expanding capacity, one storage clone per item, live item
immutability, overcapacity rejection, exceptions before the metadata commit, and weight
migration without `Inventory.Load` or item cloning.

`GameStubs.cs` contains small managed doubles for Unity/game APIs. Its package primitives
use `BinaryReader`/`BinaryWriter`; its inventory fixture codec models the 100–106 field
layout inspected in the installed game's `Inventory.Load`. The double deliberately skips
missing prefabs, matching the failure mode that production must detect. Unrelated game
behavior is omitted; world spawning throws instead of pretending to work.

These are storage regression checks, **not a game execution test**. They do not establish
the correctness of the real game's serializer, Unity initialization/destruction, Harmony
patch targets/order, UI or proxy event cleanup, external mod callbacks, or networking.
Real Valheim checks remain necessary for legacy saved rods, optional prefab removal and
restoration, Azu/QuickStack combinations, and client/host/dedicated-server item accounting.
The item-count guard also does not detect a game's stack clamp caused by an external mod
changing an item's maximum stack size.
