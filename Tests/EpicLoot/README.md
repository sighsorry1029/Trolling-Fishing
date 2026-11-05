# EpicLoot adapter checks

Run these two projects sequentially. They share this folder's NuGet restore output. Neither project builds, deploys or starts TrollingFishing or the game.

## Source-linked behavior checks

```powershell
dotnet run --project Tests/EpicLoot/EpicLootAdapter.Tests.csproj -c Debug -p:PythonExe='C:/Users/blizz/.codex/references/valheim/tools/unitypy-resource-review-r1/Scripts/python.exe'
```

`extract_adapter.py` extracts the current `EpicLootBagCompatibility` class from `FishingRodBagUi.Patches.cs` verbatim before compilation. This tests production control flow, with doubles for EpicLoot, Harmony reflection, game objects, configuration and logging. It does not duplicate the adapter implementation.

On 2026-11-05, **34/34 checks passed**: absent EpicLoot; stable provider ID; rod/configuration callback; prompt, shard-slot and focused gamepad decisions; forwarding the supplied GUI; missing contracts; existing provider preservation; rejected registration; registration notification failure rollback; runtime failure; inactive retained callbacks; cleanup retry; and repeated initialization/reset. These are isolated behavior checks, not evidence that the doubles implement every EpicLoot behavior.

## Original DLL and target Mono checks

Build the probe, then run it with the installed game's Mono runtime and the original, unmodified EpicLoot DLL:

```powershell
dotnet build Tests/EpicLoot/EpicLootContractProbe.csproj -c Debug
& 'C:/Users/blizz/.codex/references/valheim/tools/unitypy-resource-review-r1/Scripts/python.exe' Tests/run-mono-probe.py Tests/EpicLoot/bin/Debug/net48/EpicLootContractProbe.exe 'C:/Users/blizz/AppData/Roaming/com.kesomannen.gale/valheim/profiles/qwerqwer/BepInEx/plugins/RandyKnapp-EpicLoot/EpicLoot.dll' 'C:/Program Files (x86)/Steam/steamapps/common/Valheim' 'C:/Users/blizz/RiderProjects/TrollingFishing/bin/Debug/TrollingFishing.dll'
```

On 2026-11-05, **20/20 checks passed** against the supplied EpicLoot BepInPlugin version **0.15.6** and final Debug TrollingFishing DLL. These checks execute the original registry API, predicate dispatch and foreign-container recognition; verify signatures/accessibility and the provider snapshot; exercise duplicate registration and removal; and prove that an `OnReload` observer can throw after registration is committed, followed by explicit rollback.

The probe also invokes the final TrollingFishing DLL's private `BindFocusedPanelGamepadPress` method against the original EpicLoot types and evaluates the closed-panel result. A separate compiled accessor actually reads EpicLoot's private `_panel` field, so successful private access is checked independently of short-circuiting. No publicized assembly is used.

The probe restores its temporary event observer and unregisters its unique test ID. This occurs only inside the isolated process, not in a running game. `VALHEIM_GAME` can override the Mono host's installed-game path; if used, pass the same game directory to the probe.

## Remaining manual verification

Neither suite runs EpicLoot plugin `Awake`, Unity scenes, real keyboard/gamepad input, real inventory operations or multiplayer. The original GUI input delegate is bound but not invoked. In-game verification is still required for opening/closing the shard panel, focused gamepad close, normal fishing-bag input, prompt interaction, configuration toggles, startup/teardown and inventory preservation. The tests do not claim compatibility with arbitrary future EpicLoot implementations.
