using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using HarmonyLib.Public.Patching;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Calls the final mod's actual verifier. Mutations only touch temporary copies in
// this probe's directory under %TEMP%; neither plugins nor source DLLs are written.
// Patch completeness checks populate Harmony metadata in an isolated process only:
// they do not install detours or start Unity/gameplay.
internal static class PathOfValheimanContractProbe
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string PovGuid = "treextr.pathofvalheiman";
    private static string[] directories;
    private static int checks;
    private static Type compatibility;
    private static MethodInfo verify;
    private static MethodInfo fingerprint;
    private static MethodInfo reset;
    private static int mutationNumber;

    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            System.Console.Error.WriteLine("Usage: probe <final TrollingFishing.dll> <PathOfValheiman.dll> [Valheim game directory]");
            return 2;
        }

        string modPath = Path.GetFullPath(args[0]);
        string povPath = Path.GetFullPath(args[1]);
        string game = args.Length > 2 ? args[2] : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim";
        directories = new[] { Path.Combine(game, "BepInEx", "core"), Path.Combine(game, "valheim_Data", "Managed"), Path.GetDirectoryName(modPath), Path.GetDirectoryName(povPath) };
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        string scratch = Path.Combine(Path.GetTempPath(), "TrollingFishing-PoV-contract-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            Assembly.LoadFrom(Path.Combine(directories[0], "Mono.Cecil.dll"));
            Assembly.LoadFrom(Path.Combine(directories[0], "0Harmony.dll"));
            Run(modPath, povPath, scratch);
            System.Console.WriteLine("PASS " + checks + " checks against actual final mod verifier.");
            System.Console.WriteLine("Scope: original DLL metadata, normalized IL mutations, and synthetic registration of real Harmony patch methods. No detours, Unity scene, gameplay, inventory, or network execution.");
            System.Console.WriteLine("Temporary mutated assemblies: " + scratch);
            return 0;
        }
        catch (Exception error)
        {
            System.Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs request)
    {
        string name = new AssemblyName(request.Name).Name + ".dll";
        foreach (string directory in directories)
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string modPath, string povPath, string scratch)
    {
        Assembly mod = Assembly.LoadFrom(modPath);
        Assembly pov = Assembly.LoadFrom(povPath);
        compatibility = mod.GetType("TrollingFishing.PathOfValheimanCompatibility", true);
        verify = compatibility.GetMethod("VerifyNativeContract", All);
        fingerprint = compatibility.GetMethod("Fingerprint", All);
        reset = compatibility.GetMethod("Reset", All);
        Check(verify != null && fingerprint != null && reset != null, "actual production verifier methods exist");
        Verify(pov);
        Check(true, "supplied unmodified PoV passes the production contract");

        Mutated(povPath, scratch, "unrelated-metadata", definition =>
        {
            definition.MainModule.Mvid = Guid.NewGuid();
            definition.Name.Version = new Version(91, 2, 3, 4);
            TypeDefinition fisher = definition.MainModule.GetType("PassiveTree.CraftFisher");
            var method = new MethodDefinition("UnrelatedProbeOnly", Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static,
                definition.MainModule.TypeSystem.Void);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            fisher.Methods.Add(method);
            MethodDefinition nativeFor = Method(definition, "PassiveTree.CraftFisher", "NativeFor");
            string before = Fingerprint(nativeFor);
            Instruction branch = nativeFor.Body.Instructions.First(i => i.OpCode == OpCodes.Brfalse_S || i.OpCode == OpCodes.Brtrue_S || i.OpCode == OpCodes.Br_S);
            branch.OpCode = branch.OpCode == OpCodes.Brfalse_S ? OpCodes.Brfalse : branch.OpCode == OpCodes.Brtrue_S ? OpCodes.Brtrue : OpCodes.Br;
            Check(Fingerprint(nativeFor) == before, "branch byte-offset/short encoding changes preserve production fingerprint");
        }, true);

        Mutated(povPath, scratch, "native-prefab-policy", definition =>
        {
            MethodDefinition method = Method(definition, "PassiveTree.CraftFisher", "NativeFor");
            string before = Fingerprint(method);
            method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr).Operand = "ChangedNativeBaitPrefab";
            Check(Fingerprint(method) != before, "NativeFor semantic change changes production fingerprint");
        }, false);

        Mutated(povPath, scratch, "free-flag", definition =>
        {
            MethodDefinition method = Method(definition, "PassiveTree.CraftFisher", "Free");
            string before = Fingerprint(method);
            method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldstr).Operand = "pt.changed.freebait";
            Check(Fingerprint(method) != before, "free-bait flag change changes production fingerprint");
        }, false);

        Mutated(povPath, scratch, "refund-policy", definition =>
        {
            MethodDefinition method = Method(definition, "PassiveTree.Fishing/FishingFloat_SetCatch", "Postfix");
            string before = Fingerprint(method);
            method.Body.Instructions.First(i => i.OpCode == OpCodes.Ldc_R4).Operand = 0.5f;
            Check(Fingerprint(method) != before, "SetCatch refund policy change changes production fingerprint");
        }, false);

        Mutated(povPath, scratch, "native-stat-identifier", definition =>
        {
            definition.MainModule.GetType("PassiveTree.Stat").Fields.Single(f => f.Name == "NativeBait").Constant = 657;
        }, false);

        PatchCompleteness(pov);
    }

    private static void Mutated(string source, string scratch, string label, Action<AssemblyDefinition> mutate, bool allowed)
    {
        string path = Path.Combine(scratch, label + ".dll");
        using (AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { InMemory = true }))
        {
            // A fresh assembly identity lets one isolated Mono process load every
            // candidate independently; identity itself is deliberately not a gate.
            definition.Name.Name += ".ContractProbe" + (++mutationNumber);
            definition.MainModule.Mvid = Guid.NewGuid();
            definition.MainModule.Resources.Clear(); // The large asset bundle is unrelated to this managed contract.
            mutate(definition);
            definition.Write(path);
        }

        Assembly loaded = Assembly.LoadFile(path);
        bool accepted;
        try
        {
            Verify(loaded);
            accepted = true;
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
        {
            accepted = false;
        }
        Check(accepted == allowed, label + (allowed ? " is allowed" : " is rejected") + " by the production verifier");
    }

    private static MethodDefinition Method(AssemblyDefinition definition, string type, string name) =>
        definition.MainModule.GetType(type).Methods.Single(m => m.Name == name);

    private static string Fingerprint(MethodDefinition method) => (string)fingerprint.Invoke(null, new object[] { method });

    private static void Verify(Assembly assembly)
    {
        reset.Invoke(null, null);
        verify.Invoke(null, new object[] { assembly });
    }

    private static void PatchCompleteness(Assembly pov)
    {
        Verify(pov);
        Set("_initialized", true);
        Set("_present", true);
        Set("_nativeReady", true);
        Set("_nativeWarning", true); // A failed metadata case needs no plugin logger/Unity setup.
        MethodInfo add = compatibility.GetMethod("AddRequiredPatch", All);
        MethodInfo validate = compatibility.GetMethod("ValidateNativeBaitPatches", All);
        Assembly game = Assembly.Load("assembly_valheim");
        Type attack = game.GetType("Attack", true);
        Type humanoid = game.GetType("Humanoid", true);
        Type item = game.GetType("ItemDrop+ItemData", true);
        Type fish = game.GetType("Fish", true);
        Type fishingFloat = game.GetType("FishingFloat", true);
        Type character = game.GetType("Character", true);
        Type hit = game.GetType("HitData", true);
        Type vector = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Vector3", true);
        var specs = new[]
        {
            new Spec(attack, "HaveAmmo", new[] { humanoid, item }, "PassiveTree.CraftFisher+Attack_HaveAmmo", false),
            new Spec(attack, "EquipAmmoItem", new[] { humanoid, item }, "PassiveTree.CraftFisher+Attack_EquipAmmo", false),
            new Spec(attack, "UseAmmo", new[] { item.MakeByRefType() }, "PassiveTree.CraftFisher+Attack_UseAmmo", false),
            new Spec(fishingFloat, "Setup", new[] { character, vector, typeof(float), hit, item, item }, "PassiveTree.CraftFisher+Float_Setup", true),
            new Spec(fish, "TestBate", new[] { fishingFloat }, "PassiveTree.CraftFisher+Fish_TestBate", false),
            new Spec(fishingFloat, "ReturnBait", Type.EmptyTypes, "PassiveTree.CraftFisher+Float_ReturnBait", false),
            new Spec(fishingFloat, "SetCatch", new[] { fish }, "PassiveTree.Fishing+FishingFloat_SetCatch", false),
            new Spec(fishingFloat, "SetCatch", new[] { fish }, "PassiveTree.Fishing+FishingFloat_SetCatch", true)
        };
        Harmony harmony = new Harmony(PovGuid);
        foreach (Spec spec in specs)
        {
            add.Invoke(null, new object[] { pov, spec.TargetType, spec.TargetName, spec.Parameters, spec.PatchType, spec.Postfix });
            Type patchType = pov.GetType(spec.PatchType, true);
            HarmonyMethod metadata = EffectiveMethod(harmony, patchType, spec);
            Check(metadata.method.DeclaringType == patchType && (metadata.priority == -1 || metadata.priority == Priority.Normal),
                spec.PatchType + ": effective method and normal priority verified");
            spec.Target = AccessTools.DeclaredMethod(spec.TargetType, spec.TargetName, spec.Parameters);
            spec.Metadata = metadata;
            Register(spec, PovGuid, spec.Postfix);
        }

        Check((bool)validate.Invoke(null, null), "complete real-method registration metadata is accepted");
        Spec useAmmo = specs.Single(s => s.TargetName == "UseAmmo");
        PatchInfo useInfo = PatchManager.ToPatchInfo(useAmmo.Target);
        useInfo.RemovePrefix(PovGuid);
        Check(!(bool)validate.Invoke(null, null), "missing UseAmmo patch is rejected");
        Register(useAmmo, "wrong.owner", false);
        Check(!(bool)validate.Invoke(null, null), "wrong patch owner is rejected");
        useInfo.RemovePrefix("wrong.owner");
        Register(useAmmo, PovGuid, true);
        Check(!(bool)validate.Invoke(null, null), "Prefix registered as Postfix is rejected");
        useInfo.RemovePostfix(PovGuid);
        Register(useAmmo, PovGuid, false);
        Check((bool)validate.Invoke(null, null), "restored complete registration is accepted");
        Register(useAmmo, "duplicate.owner", false);
        Check(!(bool)validate.Invoke(null, null), "duplicate real patch under a second owner is rejected");
        useInfo.RemovePrefix("duplicate.owner");

        MethodInfo helper = pov.GetType("PassiveTree.CraftFisher", true).GetMethod("CastsFree", All);
        AddRegistration(PatchManager.ToPatchInfo(helper), "test.helper.change", false,
            new HarmonyMethod(typeof(PathOfValheimanContractProbe).GetMethod("Noop", All)));
        Check(!(bool)validate.Invoke(null, null), "runtime patch on a verified helper is rejected");
        PatchManager.ToPatchInfo(helper).RemovePrefix("test.helper.change");
        Check((bool)validate.Invoke(null, null), "helper patch removal restores the contract");
        foreach (Spec spec in specs)
        {
            PatchInfo info = PatchManager.ToPatchInfo(spec.Target);
            if (spec.Postfix) info.RemovePostfix(PovGuid); else info.RemovePrefix(PovGuid);
        }
    }

    private static void Register(Spec spec, string owner, bool postfix)
    {
        PatchInfo info = PatchManager.ToPatchInfo(spec.Target);
        AddRegistration(info, owner, postfix, spec.Metadata);
    }

    private static void AddRegistration(PatchInfo info, string owner, bool postfix, HarmonyMethod method) =>
        typeof(PatchInfo).GetMethod(postfix ? "AddPostfixes" : "AddPrefixes", All)
            .Invoke(info, new object[] { owner, new[] { method } });

    private static HarmonyMethod EffectiveMethod(Harmony harmony, Type type, Spec spec)
    {
        string kind = spec.Postfix ? "Postfix" : "Prefix";
        object processor = harmony.CreateClassProcessor(type);
        HarmonyMethod container = (HarmonyMethod)processor.GetType().GetField("containerAttributes", All).GetValue(processor);
        Check(container.declaringType == spec.TargetType && container.methodName == spec.TargetName,
            type.FullName + ": actual Harmony target metadata matches " + spec.TargetType.Name + "." + spec.TargetName);
        FieldInfo field = processor.GetType().GetField("patchMethods", All);
        foreach (object patch in (IEnumerable)field.GetValue(processor))
        {
            HarmonyMethod info = (HarmonyMethod)patch.GetType().GetField("info", All).GetValue(patch);
            if (info.method != null && info.method.Name == kind) return info;
        }
        throw new MissingMethodException(type.FullName, kind);
    }

    private static void Set(string name, object value) => compatibility.GetField(name, All).SetValue(null, value);
    private static void Noop() { }
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + description);
        checks++;
        System.Console.WriteLine("PASS " + description);
    }

    private sealed class Spec
    {
        internal readonly Type TargetType;
        internal readonly string TargetName;
        internal readonly Type[] Parameters;
        internal readonly string PatchType;
        internal readonly bool Postfix;
        internal MethodInfo Target;
        internal HarmonyMethod Metadata;
        internal Spec(Type targetType, string targetName, Type[] parameters, string patchType, bool postfix)
        {
            TargetType = targetType; TargetName = targetName; Parameters = parameters; PatchType = patchType; Postfix = postfix;
        }
    }
}
