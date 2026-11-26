# Original game contract checks

After building the final merged mod DLL, run from the repository root:

```powershell
dotnet run --project Tests/GameContracts/GameContracts.csproj -c Debug -- bin/Debug/TrollingFishing.dll '<original client Managed directory>' '<BepInEx core directory>'
dotnet run --project Tests/GameContracts/GameContracts.csproj -c Debug --no-build -- bin/Debug/TrollingFishing.dll '<original dedicated-server Managed directory>' '<BepInEx core directory>'
```

Use original, unmodified game DLLs. The .NET 10 runner uses Mono.Cecil 0.11.6 and
does not build or deploy the mod, publicize assemblies, or apply Harmony patches.
Each role runs in its own process. It returns a nonzero exit code on failure.

The metadata checks resolve final assembly dependencies from the supplied game
and BepInEx directories; resolve instruction references into the supplied game,
Unity and framework assemblies; reject direct nonpublic game calls/fields and
field instructions against constants; and validate static game Harmony targets,
argument/result/field/state injections and cached game/TMP reflection contracts.
They also check the original KeyHints lifecycle and group-selection IL against
the mod's hook metadata, plus the public inventory-element position getter used
by selected-bait visuals. The old selected-bait implementation fails that check.

The runner additionally invokes the original `Inventory.Changed(false, false)`
on an empty managed inventory and decodes eight independent binary fixtures with
the original `ItemData.Load`. These cover formats 109/110, compact/default and
optional fields, metadata, and the version-specific cheated flag. This is **.NET
10 managed execution**, not a Unity Mono or game session. No prefab is instantiated.
It does not execute original `Inventory.Load`/`Save` or a complete bag transaction;
the separate linked-source storage harness covers the mod's response to prescribed
game outcomes.

Excluded: the two dynamic Harmony targets discovered from optional external mods,
third-party API behavior, actual Harmony installation/order, Unity UI rendering
and input, network ownership/RPC behavior, and host/client/server gameplay. A
matching method signature alone does not prove unchanged behavior. The explicit
KeyHints IL assertions describe the inspected 1.0.30 implementation and should be
reviewed when a later game's behavior changes.

On 2026-11-26 the final Debug DLL passed against original Valheim 1.0.30 client
26870846 and dedicated-server 26870897: 17 final dependencies, 7,111 instruction
references, 58 static Harmony targets, 139 injections, 35 cached reflection
contracts, selected-bait position access, 21 KeyHints checks, one Changed callback
and all eight item-reader fixtures. This result does not establish actual game
compatibility with other installed mods.
