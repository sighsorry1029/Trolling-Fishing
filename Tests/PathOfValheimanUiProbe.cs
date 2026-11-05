using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

// Run in the game's isolated Mono host via Tests/run-mono-probe.py.
// Arguments: [final TrollingFishing.dll] [Valheim game directory].
// Reads real Harmony class-processor output; detours affect only this probe's stub target.
internal static class PathOfValheimanUiProbe
{
    private const BindingFlags AllFields = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance;
    private const string PovGuid = "treextr.pathofvalheiman";
    private const string EpicLootGuid = "randyknapp.mods.epicloot";
    private static string[] referenceDirectories;
    private static int checks;
    private static readonly List<string> orderEvents = new List<string>();

    private static int Main(string[] args)
    {
        string modPath = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "bin", "Debug", "TrollingFishing.dll"));
        string gamePath = args.Length > 1 ? args[1] : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim";
        referenceDirectories = new[]
        {
            Path.Combine(gamePath, "BepInEx", "core"),
            Path.Combine(gamePath, "valheim_Data", "Managed"),
            Path.GetDirectoryName(modPath)
        };
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        try
        {
            if (!File.Exists(modPath)) throw new FileNotFoundException("Build the final Debug mod before running this probe.", modPath);
            Assembly.LoadFrom(Path.Combine(referenceDirectories[0], "0Harmony.dll"));
            Run(Assembly.LoadFrom(modPath));
            System.Console.WriteLine("PASS " + checks + " isolated checks against " + modPath);
            System.Console.WriteLine("Scope: effective patch metadata, stub-prefix dispatch order, and canceled-close early returns. Ordering tests do not execute production prefix bodies. No Unity scene, normal close/save/events/Destroy, input UI, or multiplayer execution.");
            return 0;
        }
        catch (Exception error)
        {
            System.Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
        }
    }

    private static Assembly ResolveAssembly(object sender, ResolveEventArgs request)
    {
        string name = new AssemblyName(request.Name).Name + ".dll";
        foreach (string directory in referenceDirectories)
        {
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
        }
        return null;
    }

    // Keep Harmony type resolution after Main has installed the assembly resolver.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(Assembly mod)
    {
        var harmony = new Harmony("TrollingFishing.Tests.PathOfValheimanUiMetadataOnly");
        HarmonyMethod input = PrefixInfo(harmony, mod, "InventoryGuiUpdateFishingRodBagInputPatch");
        int priority = input.priority == -1 ? Priority.Normal : input.priority;
        Check(priority == 801, "Inventory input effective priority is 801 (actual " + priority + ")");
        Check(Contains(input.before, EpicLootGuid), "Inventory input retains effective HarmonyBefore EpicLoot");

        foreach (string patch in new[]
        {
            "AttackUseAmmoFishingRodBagPatch",
            "AttackFireProjectileBurstMultiLineFishingPatch",
            "FishFindFloatFishingSkillPatch",
            "FishingFloatCatchExtraDropSkillPatch"
        })
        {
            HarmonyMethod prefix = PrefixInfo(harmony, mod, patch);
            Check(Contains(prefix.after, PovGuid), patch + " has effective HarmonyAfter PathOfValheiman");
            RegisteredOrder(prefix, patch);
        }

        CanceledClose(mod, "InventoryGuiCloseFishingRodBagPatch");
        CanceledClose(mod, "InventoryGuiHideFishingRodBagPatch");
    }

    private static void RegisteredOrder(HarmonyMethod effective, string patchName)
    {
        MethodInfo target = AccessTools.Method(typeof(PathOfValheimanUiProbe), nameof(OrderTarget));
        for (int order = 0; order < 2; order++)
        {
            var pov = new Harmony(PovGuid);
            var fishing = new Harmony("TrollingFishing.Tests.PrefixOrder");
            var povPrefix = new HarmonyMethod(AccessTools.Method(typeof(PathOfValheimanUiProbe), nameof(PovPrefix)));
            var fishingPrefix = new HarmonyMethod(AccessTools.Method(typeof(PathOfValheimanUiProbe), nameof(FishingPrefix)))
            {
                priority = effective.priority,
                before = effective.before == null ? null : (string[])effective.before.Clone(),
                after = effective.after == null ? null : (string[])effective.after.Clone()
            };
            try
            {
                if (order == 0)
                {
                    pov.Patch(target, prefix: povPrefix);
                    fishing.Patch(target, prefix: fishingPrefix);
                }
                else
                {
                    fishing.Patch(target, prefix: fishingPrefix);
                    pov.Patch(target, prefix: povPrefix);
                }
                orderEvents.Clear();
                OrderTarget();
                string actual = string.Join(",", orderEvents);
                Check(actual == "PoV,TF,original", patchName + " registration " +
                    (order == 0 ? "PoV then TF" : "TF then PoV") + " dispatches " + actual);
            }
            finally
            {
                // Unpatch only the probe-owned target, even when an assertion fails.
                fishing.Unpatch(target, HarmonyPatchType.All, fishing.Id);
                pov.Unpatch(target, HarmonyPatchType.All, pov.Id);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OrderTarget() { orderEvents.Add("original"); }
    private static void PovPrefix() { orderEvents.Add("PoV"); }
    private static void FishingPrefix() { orderEvents.Add("TF"); }

    private static HarmonyMethod PrefixInfo(Harmony harmony, Assembly mod, string patchName)
    {
        Type patchType = mod.GetType("TrollingFishing." + patchName, throwOnError: true);
        object processor = harmony.CreateClassProcessor(patchType);
        FieldInfo field = processor.GetType().GetField("patchMethods", AllFields)
            ?? throw new MissingFieldException(processor.GetType().FullName, "patchMethods");
        foreach (object patch in (IEnumerable)field.GetValue(processor))
        {
            FieldInfo infoField = patch.GetType().GetField("info", AllFields)
                ?? throw new MissingFieldException(patch.GetType().FullName, "info");
            var info = (HarmonyMethod)infoField.GetValue(patch);
            if (info.method != null && info.method.Name == "Prefix") return info;
        }
        throw new MissingMethodException(patchType.FullName, "effective Prefix");
    }

    private static void CanceledClose(Assembly mod, string patchName)
    {
        Type state = mod.GetType("TrollingFishing.FishingRodBagProxyState", throwOnError: true);
        FieldInfo keepAlive = state.GetField("KeepAliveUntil", AllFields)
            ?? throw new MissingFieldException(state.FullName, "KeepAliveUntil");
        object previous = keepAlive.GetValue(null);
        const float sentinel = 12345.25f;
        try
        {
            keepAlive.SetValue(null, sentinel);
            Type patch = mod.GetType("TrollingFishing." + patchName, throwOnError: true);
            MethodInfo postfix = patch.GetMethod("Postfix", AllFields)
                ?? throw new MissingMethodException(patch.FullName, "Postfix");
            ParameterInfo[] parameters = postfix.GetParameters();
            object[] arguments = new object[parameters.Length];
            bool hasRunOriginal = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].Name == "__runOriginal" && parameters[i].ParameterType == typeof(bool))
                {
                    hasRunOriginal = true;
                    arguments[i] = false;
                }
                else if (parameters[i].ParameterType.IsValueType)
                {
                    throw new InvalidOperationException("Unexpected postfix parameter: " + parameters[i]);
                }
                // Deliberately leave Unity instance, animator and captured bag null.
                // A canceled close must return before dereferencing any of them.
            }
            if (!hasRunOriginal) throw new InvalidOperationException(patchName + " does not receive __runOriginal");
            postfix.Invoke(null, arguments);
            Check((float)keepAlive.GetValue(null) == sentinel,
                patchName + " canceled close does not access Unity state or clear proxy lifetime");
        }
        finally
        {
            keepAlive.SetValue(null, previous);
        }
    }

    private static bool Contains(string[] values, string expected)
    {
        return values != null && Array.IndexOf(values, expected) >= 0;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + description);
        checks++;
        System.Console.WriteLine("PASS " + description);
    }
}
