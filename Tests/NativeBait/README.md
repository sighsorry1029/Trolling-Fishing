# Native Bait casting flow checks

Run from the repository root:

```powershell
dotnet run --project Tests/NativeBait/NativeBait.Tests.csproj -c Debug
```

This isolated .NET 10 console project links the actual `MultiLineFishingSystem.cs`
and `FishingRodBagAmmo.Patches.cs`. It has no game installation, package, deployment,
or production-project reference. A failed assertion produces exit code 1.

The 19 checks cover two, three, and ten free or paid lines; preservation of incoming
ammo/result values; successful and rejected foreign-prefix transactions; a handled
bag transaction; free/paid transitions; missing generated bait; unknown/partial
contracts at preflight, ammo receipt, and projectile burst; and receipt isolation
between attacks. The burst checks execute the production branching, count, and loop
and observe its external reservation and marker calls.

`GameStubs.cs` supplies boundary doubles. It does not copy the casting implementation.
The PoV adapter returns controlled contract states. The physical reservation boundary
records requested amounts, and the test explicitly accounts for the first bait that
vanilla `UseAmmo` consumes before the burst. This does not test actual inventory
removal, bag serialization, or the PoV reflection/IL/active-patch validation.

`MultiLineFishingFloat.cs` is not linked. Its Unity marker lifecycle, projectile-to-float
handoff, ZDO free-bait replication, return/refund handling, ownership transfer, and
destruction require separate original-runtime and gameplay checks. Harmony attributes
are doubles here; the ammo Prefix is invoked through reflection after supplying the
incoming values. Actual HarmonyX ordering and `__runOriginal` behavior also require the
separate Mono probe. These tests do not establish multiplayer compatibility.
