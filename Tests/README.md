# Storage regression checks

## Valheim 1.0.30 follow-up (2026-11-26)

The reported startup exception comes from CaptainValheim 1.1.1's transpiler of
`KeyHints.UpdateHints`, which requires nine direct combat-hint visibility writes.
Original 1.0.30 delegates visibility to helper methods and has zero such writes.
Adding TrollingFishing's ordinary postfix causes Harmony to rerun that external
transpiler. The native TF target, UI fields and lifecycle contracts remain valid;
its patch is retained, and no other mod's patches are removed or suppressed.

Two focused production changes were made:

- Bag catches now preserve the new vanilla quality-5 catch correction: increment
  the Tier4 statistic only when its `DifficultyRequirement.Any` value is zero.
  The existing local-owner and successful-storage boundary remains in place.
- Selected-bait highlighting uses public `InventoryElement.Position` and
  `m_equiped`, removing the missing `m_pos` lookup and per-element reflection.
  This was already incorrect in 1.0.21; it is not a newly introduced 1.0.30 change.

The final Debug build (`DeployToGame=true`) completed with zero warnings/errors;
the deployed Steam plugins DLL matches SHA-256
`3ED8FF0527BAEF398A38144C32964D00C6C47B7A0B389D64FBD62FB441567DCD`.
All 114 resolved game-DLL references match preserved original client 1.0.30 hashes.
That Debug validation added no publicization, version bump, Release package, new save format or migration.

Validation: storage/CraftFrom 36/36; casting flow 19/19; extracted catch-statistics
behavior 16/16 and selected-bait visuals 11/11; original-Mono UI/Harmony checks 16/16; supplied PoV 6.0.16 contract
checks 35/35. The storage/casting harnesses emit ten existing nullable-analysis
warnings; the production build has none. [GameContracts](GameContracts/README.md)
passed against both client 26870846 and dedicated server 26870897 originals:
17 final dependencies, 7,111 instruction references, 58 static Harmony targets,
139 injections, 35 cached reflection contracts, 21 KeyHints contracts, the selected
bait access regression, and the private inventory notification/8 item-reader fixtures.
The reader fixtures execute in isolated .NET 10, not a Unity scene.

Comparison inputs, detailed scope and reproducible probes are recorded in
`C:/Users/blizz/.codex/references/valheim/comparisons/1.0.20--1.0.30-trollingfishing-20261126/README.md`.
An experimental native KeyHints wrapper test could register Awake, but UpdateHints
JIT requires Unity's Animator native calls; that experiment was not counted as a
passing test and no native-call stubs were introduced.

Still required: use a CaptainValheim build compatible with 1.0.30 (or disable that
plugin), then verify actual startup, KB/controller hints and selection marks, bag
catch statistics, save/reconnect, free/paid bait accounting and host/client/server
behavior. The supplied live Gale log was rewritten during review and its TF/Captain
files were disabled as `.dll.old`; no Gale profile files were changed by this task.

Release 1.1.7 follow-up: `dotnet build TrollingFishing.sln -c Release -p:CreatePackages=true`
completed with zero warnings/errors. The final merged Release DLL passed the
client/server GameContracts checks above, 16 UI/Harmony checks and 35 PoV 6.0.16
contract checks. Both ZIPs passed CRC and exact-entry/source-byte checks. Assembly
version is 1.1.7.0; BepInPlugin, file and manifest versions are 1.1.7. The manifest
retains BepInExPack 5.4.2360. DLL SHA-256 is
`BF913E264EB666B637C6DD11D4C84D4FFE9E3EBBD1923124BD9E806369AF432E`.
This release also includes the previously committed CraftFrom integration. It
does not modify CaptainValheim or add actual game/multiplayer verification.

## Current EpicLoot input integration (2026-11-05)

The adapter now uses EpicLoot's GUI-aware shard-input decision and registers enabled
fishing bags through its item-container API, reviewed against EpicLoot 0.15.6.
It also yields the focused shard panel's gamepad close gesture before fishing bag
full-close handling. The independent shard panel no longer triggers a chest close.
The API is bound once during initialization; no version/hash gate, old socket-UI
fallback, new save format, or migration was added. Reset unregisters our callback,
and plugin destruction starts with that cleanup before config saving.

See [EpicLoot checks](EpicLoot/README.md) for adapter failure/cleanup tests and the
original-DLL Unity Mono contract probe: 34/34 source-linked adapter checks and
20/20 original-DLL registry/access checks passed. The existing storage harness passed 32/32
checks, and the final-DLL UI/Harmony probe passed 16/16. The Debug build completed
with zero warnings/errors and its merged DLL matched the deployed Steam plugins DLL.
The manifest dependency is now BepInExPack 5.4.2360; that Debug validation generated no Release package.

Release 1.1.6 follow-up: `dotnet build TrollingFishing.sln -c Release -p:CreatePackages=true`
completed with zero warnings/errors. The final merged Release DLL passed 16 UI/Harmony,
20 original EpicLoot API/Mono-access, and 35 PoV contract checks. The PoV DLL was
located in the current `PathOfValheim` Gale profile; the earlier `asdfasdf` path no
longer exists. Both ZIPs passed CRC checks and contain exactly their expected source
files, including the final DLL. Assembly version is 1.1.6.0; BepInPlugin, file and
manifest versions are 1.1.6. This adds no actual game or server execution validation.

Still required in game: EpicLoot absent/present, ordinary/socketed rods, bag on/off,
bare Use, modified/separate/rebound socket keys, prompts, gamepad player-grid focus
and shard-panel focus (including ButtonY), and both bag/shard opening orders.
Check drag/quickmove, saved bait and socket contents after close/reconnect, and
client/host/dedicated-server behavior. The isolated checks do not execute these UI,
save/event, or multiplayer flows. The older EpicLoot section below is historical.

## Path of Valheiman follow-up (2026-10-25)

The optional integration targets the reviewed Native Bait contract from PoV 5.3.2,
with Valheim 1.0.20 original references. No game DLL publicization, new saved bag
format, migration, legacy support, or hard PoV dependency was added.

Validation performed:

- Existing storage checks: 32/32 passed.
- [Linked casting-flow checks](NativeBait/README.md): 19/19 passed. Free and paid
  2/3/10-line casts, output preservation, talent changes, failed generated bait,
  contract failures at three transaction boundaries, and per-Attack isolation.
  Restoring the old output-clobbering bug in a temporary source copy failed the
  corresponding test, as expected.
- Final-DLL UI/Harmony probe: 16/16 passed in installed Unity Mono/HarmonyX.
  Effective priority/order metadata, both registration orders for four calculation
  boundaries, and canceled Hide/CloseContainer preserving proxy lifetime.
  Ordering detours execute probe-only role methods with production metadata,
  not the actual gameplay Prefix bodies.
- Final-DLL PoV contract probe: 35/35 passed in installed Unity Mono. The supplied
  original PoV DLL is accepted; unrelated MVID/version/method/resource changes and
  branch offset/encoding changes are accepted. Relevant NativeFor/Free/SetCatch IL
  and NativeBait enum changes are rejected. Actual PoV patch declarations match
  the expected targets; synthetic Harmony registration metadata exercises missing,
  wrong-owner, wrong-kind, duplicate, and modified-helper rejection and recovery.
  This registration test does not install PoV gameplay detours.
- `dotnet build TrollingFishing.sln -c Debug -p:DeployToGame=true`: succeeded with
  zero warnings/errors. Final merged DLL and the default Steam plugins copy have
  matching SHA-256. No Release package was generated during that Debug validation.

Release 1.1.5 follow-up: `dotnet build TrollingFishing.sln -c Release -p:CreatePackages=true`
completed with zero warnings/errors. The final Release DLL passed the 16 UI/Harmony
and 35 PoV contract checks above. Both ZIPs contain that exact DLL; the Thunderstore
manifest, changelog, README, icon, and English template match their sources. Assembly
version is 1.1.5.0, file/manifest version is 1.1.5, and BepInExPack remains 5.4.2351.
This does not add in-game or multiplayer validation.

Run the casting harness as documented in its directory. For the original-runtime
probes, build the mod first, then run these commands sequentially (replace Python
and PoV paths with the local installations):

```powershell
dotnet build Tests/PathOfValheimanUiProbe.csproj -c Debug
dotnet build Tests/PoVContract/PathOfValheimanContractProbe.csproj -c Debug
$probePython = 'C:\Users\blizz\.codex\references\valheim\tools\unitypy-resource-review-r1\Scripts\python.exe'
$povDll = 'C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\PathOfValheim\BepInEx\plugins\treextr-PathOfValheiman\PathOfValheiman\PathOfValheiman.dll'
& $probePython Tests/run-mono-probe.py Tests/bin/Debug/net48/PathOfValheimanUiProbe.exe
& $probePython Tests/run-mono-probe.py Tests/PoVContract/bin/Debug/net48/PathOfValheimanContractProbe.exe bin/Debug/TrollingFishing.dll $povDll
```

The contract verifier reads only relevant methods once at initialization, caches
delegates/accessors, and checks active patch registrations at cast boundaries.
It does not check an entire DLL hash/version or scan IL each frame. This narrow
structural check is conservative; it does not prove arbitrary future code equivalent
or cover every other mod's patches. Replacing a loaded DLL is unsupported.

Still required in game: Native Bait on/off with bag off/empty/selected bait and
inventory bait; empty recall, hooked fish/refund, ground hit, new-cast replacement,
full inventory, rod/talent changes; free flag replication and no item gains/losses
on client/host/dedicated server. Test PoV trash/filter/search windows with Use,
Escape/Inventory/B/Y, canceled closing followed by normal closing, and event cleanup.
Compare draw costs and Deep Cast/bite/catch effects, with PoV absent and installed.
The float lifecycle/ZDO and actual return/refund path were reviewed statically,
not executed in a Unity scene. Crossplay and other mods' interactions are unverified.

## Running the storage harness

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet run --project Tests/TrollingFishing.RegressionTests.csproj
```

This console harness uses no test packages, game installation, or BepInEx installation.
It compiles the actual `FishingRodBagStorage.cs` and `FishingRodBagRules.cs` through linked
source files; it does not copy their implementation. It exits with code 1 on failure.
The production project explicitly lists its source files and does not compile `Tests/`.

The 32 checks cover inventory package versions 109 and 110, preservation and rejection of older
and future formats, valid empty bags, malformed and
partial loads, missing optional-mod prefabs, failed add/save preservation, catch fallback,
overflow after shrinking and expanding capacity, one storage clone per item, live item
immutability, overcapacity rejection, exceptions before the metadata commit, and weight
migration without `Inventory.Load` or item cloning. Both supported versions save and reopen
as current format 110. The cheated flag is cleared when reading 109 and retained when
reading 110, matching the original 1.0.20 reader; unknown format 111 remains blocked.

`GameStubs.cs` contains small managed doubles for Unity/game APIs. Its package primitives
use `BinaryReader`/`BinaryWriter`; its inventory fixture codec models the format-109/110 header
and item flags inspected in original Valheim DLLs. Custom-data fixtures use small
dictionaries; this is not a complete replacement for the game's serializer. The double deliberately skips
missing prefabs, matching the failure mode that production must detect. Unrelated game
behavior is omitted; world spawning throws instead of pretending to work.

These are storage regression checks, **not a game execution test**. They do not establish
the correctness of the real game's serializer, Unity initialization/destruction, Harmony
patch targets/order, UI or proxy event cleanup, external mod callbacks, or networking.
The earlier compatibility review also ran an isolated probe using installed Unity Mono and original
game DLLs: final merged mod accessor initialization, `Inventory.Changed` invocation,
empty `Inventory.Save`, `ItemData.Load` with 260 custom-data entries, and a cached
`ReturnBait` delegate observing a subsequently installed Harmony prefix. These passed
against both client and dedicated-server DLLs using their installed Mono runtimes. This probe does
not initialize a Unity scene or run the full Harmony patch set.

Real Valheim checks remain necessary for preserved but blocked unsupported-format rods, new format-110
rods, optional prefab removal and
restoration, Azu/QuickStack combinations, and client/host/dedicated-server item accounting.
The item-count guard also does not detect a game's stack clamp caused by an external mod
changing an item's maximum stack size.

## Valheim 1.0.20 storage follow-up (2026-10-15)

All 32 linked-source checks and the Debug build passed. A separate final-DLL probe hosted
in original client/server Mono accepted empty 109/110 payloads, rejected 111 and malformed/trailing
data, and confirmed that original `Inventory.Save` writes 110. The same probe failed on
the previous release DLL for empty 110 data. The shared probe also checked UsefulTankards'
summary against original `ItemData.Load`; its 56 assertions passed for each role.
Evidence and the reproducible shared probe are in
`C:/Users/blizz/.codex/references/valheim/comparisons/1.0.20-four-mods-patch-20261015/`.
This is isolated managed execution, not a Unity bag/UI or multiplayer test.

## EpicLoot input follow-up (2026-09-14)

The bag Update Prefix now has method-level priority 801 and HarmonyBefore EpicLoot. The installed HarmonyX loses class-only priorities when merging a method without its own priority; simply checking attribute values is insufficient. UsefulTankards' `Tests/HarmonyInputOrder.ps1`, run under Windows PowerShell with this final DLL as `-FishingDll`, processes the actual patch types through the installed `Harmony.CreateClassProcessor` without applying detours. It confirms effective Prefix priorities of 801 for both item-storage mods and 400 for the supplied EpicLoot 0.14.5 DLL.

The optional adapter caches EpicLoot's prompt/socket getters and configured modifier at startup. Opening the bag requires a local owner and a rod still in inventory; transitioning from sockets calls the normal CloseContainer path, then rechecks ownership/membership. No EpicLoot binary, JSON dependency, saved bag format, or fishing policy was changed.

All 26 storage checks and the Debug build passed (zero warnings/errors). The final merged DLL was deployed to the default Steam plugins folder and the supplied Gale profile, with SHA-256 verification. Release 1.1.3 was packaged after the same storage and effective-order checks. The storage harness does not compile the UI source or execute EpicLoot; actual in-game modifier/prompt handling, socket save/event cleanup, controller interaction, Azu proxy transitions, and multiplayer item accounting still need gameplay validation.
