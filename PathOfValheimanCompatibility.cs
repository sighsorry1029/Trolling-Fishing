using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace TrollingFishing;

internal static class PathOfValheimanCompatibility
{
    internal const string PluginGuid = "treextr.pathofvalheiman";
    internal const string FreeBaitKey = "pt.freebait";
    private const string FisherType = "PassiveTree.CraftFisher";
    private static bool _initialized;
    private static bool _present;
    private static bool _nativeReady;
    private static bool _uiReady;
    private static bool _nativeWarning;
    private static bool _uiWarning;
    private static Func<ItemDrop.ItemData, bool>? _castsFree;
    private static AccessTools.FieldRef<ItemDrop.ItemData>? _nativeAmmo;
    private static Func<bool>? _trashActive;
    private static Func<bool>? _trashPopup;
    private static Func<bool>? _lootFilterOpen;
    private static readonly List<RequiredPatch> RequiredPatches = new();
    private static readonly List<MethodInfo> ContractMethods = new();

    // Reviewed Native Bait implementation, not an assembly/version allowlist. Unrelated
    // methods, metadata tokens, assembly versions and MVIDs are not fingerprint inputs.
    private static readonly Dictionary<string, string> NativeContract = new()
    {
        [FisherType + "::get_S"] = "0EF80F29F6654F5D744DE08EAF427C2C065DA450A2192E70380C2C2E2FA43C96",
        [FisherType + "::Has"] = "920B7205BC460BA6D3C3633C27D29C0A7B917B9B2C47DF952AFDECAAC2CC0A06",
        [FisherType + "::BaitOf"] = "1F25C392D5683F000658083281A7106A49AAB0451FED12B30FCA4C0C02CE1012",
        [FisherType + "::NoBait"] = "A492571ACCC6DB9791B7943DBFD673C2D36FEDA199FB99ACAD3460E4084EF5BE",
        [FisherType + "::CastsFree"] = "2D977822E76C19F910D960EA998110F0FAD8D372B9D238CBF2EB1078BC7B8560",
        [FisherType + "::NativeFor"] = "8DE345CC13875707C6E3D6D2430D99B836203B509155F779A0249A678F4E808C",
        [FisherType + "::CastBait"] = "7F0E5D0FF66D2B4912A6CA0EAC087BBA024CBFE1F8135C8730AA05B446EADBCF",
        [FisherType + "::OwnBaitChance"] = "D11FEA882BE5E40C1B78A1A076F0303BE14BDCF41E16F0837EA5001CE91E55A2",
        [FisherType + "::Free"] = "6EE3AD64D74C41E59DDFBD5FF92A2D04613B0B85F274260E6C05CDC2C0CB68D2",
        [FisherType + "/Attack_HaveAmmo::Prefix"] = "63E789937DBA6998AE0371BF37419F32532B9C83DBF44AF4ADBB439C964F3711",
        [FisherType + "/Attack_EquipAmmo::Prefix"] = "821259E34E5FD705835DDFFF3114E6E1D2B0B9528C1EAE951568A41F2812E93F",
        [FisherType + "/Attack_UseAmmo::Prefix"] = "66BD4E6BFDD5CE29A6CFC3B43353A457CFD3A74037469A798420DA06956A2E75",
        [FisherType + "/Float_Setup::Postfix"] = "13FFD795B8BDBD05A919078F3D044A7BCB339B745201BCD68D575849DBB44FD7",
        [FisherType + "/Fish_TestBate::Prefix"] = "35B0D6B48414812F610E1F1C85629C33D883CCB9073AED69D5530489B6A517BA",
        [FisherType + "/Float_ReturnBait::Prefix"] = "2C020FDA0AF20DFC81BA00FD0E0EBFEDED58DDADE22971CDEE3A221455DC53D5",
        ["PassiveTree.Fishing/FishingFloat_SetCatch::Prefix"] = "5C5858E57F9CDF05D9442F6A3C71EFF12C079D3E9C1424F74E6C930EE172E0F2",
        ["PassiveTree.Fishing/FishingFloat_SetCatch::Postfix"] = "A785E3050A364EA8F94860D6A7B37EF65D7340C0D900864B4C9DA08CC1413FC3"
    };

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin))
        {
            return;
        }

        _present = true;
        Assembly? assembly = plugin.Instance?.GetType().Assembly;
        if (assembly == null)
        {
            WarnNative("the plugin instance is unavailable");
            WarnUi("the plugin instance is unavailable");
            return;
        }

        // Input cooperation has its own failure boundary; it does not require the
        // fishing contract or fingerprint unrelated UI implementation details.
        try
        {
            _trashActive = BindBoolGetter(assembly, "PassiveTree.UI.TrashMode", "Active");
            _trashPopup = BindBoolGetter(assembly, "PassiveTree.UI.TrashMode", "PopupOpen");
            _lootFilterOpen = BindBoolGetter(assembly, "PassiveTree.UI.LootFilterWindow", "IsOpen");
            _uiReady = true;
        }
        catch (Exception error)
        {
            WarnUi(error.GetBaseException().Message);
        }

        try
        {
            VerifyNativeContract(assembly);
            Type fisher = assembly.GetType(FisherType, throwOnError: true)!;
            MethodInfo castsFree = RequireMethod(fisher, "CastsFree", typeof(bool), typeof(ItemDrop.ItemData));
            _castsFree = (Func<ItemDrop.ItemData, bool>)Delegate.CreateDelegate(typeof(Func<ItemDrop.ItemData, bool>), castsFree);
            FieldInfo native = fisher.GetField("native", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                               ?? throw new MissingFieldException(FisherType, "native");
            if (native.FieldType != typeof(ItemDrop.ItemData) || !native.IsStatic)
            {
                throw new InvalidOperationException("the Native Bait item field has changed");
            }

            _nativeAmmo = AccessTools.StaticFieldRefAccess<ItemDrop.ItemData>(native);
            AddRequiredPatch(assembly, typeof(Attack), "HaveAmmo", new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }, FisherType + "+Attack_HaveAmmo", false);
            AddRequiredPatch(assembly, typeof(Attack), "EquipAmmoItem", new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) }, FisherType + "+Attack_EquipAmmo", false);
            AddRequiredPatch(assembly, typeof(Attack), "UseAmmo", new[] { typeof(ItemDrop.ItemData).MakeByRefType() }, FisherType + "+Attack_UseAmmo", false);
            AddRequiredPatch(assembly, typeof(FishingFloat), "Setup", new[] { typeof(Character), typeof(UnityEngine.Vector3), typeof(float), typeof(HitData), typeof(ItemDrop.ItemData), typeof(ItemDrop.ItemData) }, FisherType + "+Float_Setup", true);
            AddRequiredPatch(assembly, typeof(Fish), "TestBate", new[] { typeof(FishingFloat) }, FisherType + "+Fish_TestBate", false);
            AddRequiredPatch(assembly, typeof(FishingFloat), "ReturnBait", Type.EmptyTypes, FisherType + "+Float_ReturnBait", false);
            // The refund exclusion is part of free-bait conservation, too.
            AddRequiredPatch(assembly, typeof(FishingFloat), "SetCatch", new[] { typeof(Fish) }, "PassiveTree.Fishing+FishingFloat_SetCatch", false);
            AddRequiredPatch(assembly, typeof(FishingFloat), "SetCatch", new[] { typeof(Fish) }, "PassiveTree.Fishing+FishingFloat_SetCatch", true);
            _nativeReady = true;
        }
        catch (Exception error)
        {
            _nativeReady = false;
            WarnNative(error.GetBaseException().Message);
        }
    }

    internal static void Reset()
    {
        _initialized = _present = _nativeReady = _uiReady = _nativeWarning = _uiWarning = false;
        _castsFree = null;
        _nativeAmmo = null;
        _trashActive = _trashPopup = _lootFilterOpen = null;
        RequiredPatches.Clear();
        ContractMethods.Clear();
    }

    internal static bool ShouldYieldInventoryInput
    {
        get
        {
            Initialize();
            if (!_present)
            {
                return false;
            }

            if (!_uiReady)
            {
                return true;
            }

            try
            {
                return _trashActive!() || _trashPopup!() || _lootFilterOpen!();
            }
            catch (Exception error)
            {
                _uiReady = false;
                WarnUi(error.GetBaseException().Message);
                return true;
            }
        }
    }

    internal static bool TryGetFreeCast(Humanoid who, ItemDrop.ItemData rod, out bool free)
    {
        free = false;
        Initialize();
        if (!_present || who == null || Player.m_localPlayer == null || who != Player.m_localPlayer)
        {
            return true;
        }

        if (!_nativeReady)
        {
            return false;
        }

        try
        {
            free = rod != null && _castsFree!(rod);
            return true;
        }
        catch (Exception error)
        {
            _nativeReady = false;
            WarnNative(error.GetBaseException().Message);
            return false;
        }
    }

    internal static bool IsNativeAmmo(Attack attack, ItemDrop.ItemData ammo)
    {
        if (!_nativeReady || attack == null || ammo?.m_shared == null || ammo.m_dropPrefab == null || ammo.m_stack != 1)
        {
            return false;
        }

        try
        {
            return Player.m_localPlayer != null && FishingGameAccess.AttackCharacter(attack) == Player.m_localPlayer &&
                   ReferenceEquals(ammo, _nativeAmmo!()) && ReferenceEquals(ammo, FishingGameAccess.AttackAmmo(attack)) &&
                   ammo.m_shared.m_ammoType == attack.GetWeapon()?.m_shared?.m_ammoType;
        }
        catch (Exception error)
        {
            _nativeReady = false;
            WarnNative(error.GetBaseException().Message);
            return false;
        }
    }

    internal static bool ValidateNativeBaitPatches()
    {
        Initialize();
        if (!_present)
        {
            return true;
        }

        if (!_nativeReady)
        {
            return false;
        }

        try
        {
            foreach (RequiredPatch required in RequiredPatches)
            {
                Patches? info = Harmony.GetPatchInfo(required.Target);
                IEnumerable<Patch> patches = info == null ? Enumerable.Empty<Patch>() : required.Postfix ? info.Postfixes : info.Prefixes;
                int registrations = info == null ? 0 : info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Concat(info.ILManipulators)
                    .Count(patch => patch.PatchMethod == required.Method);
                int matches = 0;
                foreach (Patch patch in patches)
                {
                    if (patch.PatchMethod == required.Method && patch.owner == PluginGuid && patch.priority == Priority.Normal &&
                        patch.before.Length == 0 && patch.after.Length == 0)
                    {
                        matches++;
                    }
                }

                if (matches != 1 || registrations != 1)
                {
                    WarnNative("the required patch is missing or changed: " + required.Method.DeclaringType?.FullName + "." + required.Method.Name);
                    return false;
                }
            }

            // A patch on one of these helpers changes the executing contract even
            // though the original method body on disk still has its reviewed IL.
            foreach (MethodInfo method in ContractMethods)
            {
                Patches? info = Harmony.GetPatchInfo(method);
                if (info != null && info.Owners.Count > 0)
                {
                    WarnNative("a Native Bait helper is patched: " + method.DeclaringType?.FullName + "." + method.Name);
                    return false;
                }
            }

            return true;
        }
        catch (Exception error)
        {
            WarnNative(error.GetBaseException().Message);
            return false;
        }
    }

    private static Func<bool> BindBoolGetter(Assembly assembly, string typeName, string property)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        MethodInfo getter = RequireMethod(type, "get_" + property, typeof(bool));
        return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
    }

    private static MethodInfo RequireMethod(Type type, string name, Type result, params Type[] args)
    {
        MethodInfo? method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters)
        {
            throw new MissingMethodException(type.FullName, name);
        }

        return method;
    }

    private static void AddRequiredPatch(Assembly assembly, Type targetType, string targetName, Type[] parameters, string patchType, bool postfix)
    {
        MethodInfo target = AccessTools.DeclaredMethod(targetType, targetName, parameters) ?? throw new MissingMethodException(targetType.FullName, targetName);
        Type type = assembly.GetType(patchType, throwOnError: true)!;
        MethodInfo patch = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Single(m => m.Name == (postfix ? "Postfix" : "Prefix"));
        RequiredPatches.Add(new RequiredPatch(target, patch, postfix));
    }

    private static void VerifyNativeContract(Assembly assembly)
    {
        using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(assembly.Location,
            new ReaderParameters { ReadingMode = ReadingMode.Deferred, ReadSymbols = false });
        // This only ties the inspected file to the assembly already loaded in this
        // process; there is no stored MVID allowlist. Live DLL replacement is unsupported.
        if (definition.MainModule.Mvid != assembly.ManifestModule.ModuleVersionId)
        {
            throw new InvalidOperationException("the loaded plugin no longer matches its file");
        }

        RequireConstant(assembly, "PassiveTree.Stat", "NativeBait", 658);
        RequireConstant(assembly, "PassiveTree.Stat", "RefundChance", 561);
        RequireConstant(assembly, "PassiveTree.Stat", "BaitSaveChance", 595);
        RequireConstant(assembly, "PassiveTree.Subject", "Bait", 127);
        foreach (KeyValuePair<string, string> entry in NativeContract)
        {
            int separator = entry.Key.IndexOf("::", StringComparison.Ordinal);
            string typeName = entry.Key.Substring(0, separator);
            string methodName = entry.Key.Substring(separator + 2);
            TypeDefinition type = definition.MainModule.GetType(typeName) ?? throw new TypeLoadException(typeName);
            MethodDefinition method = type.Methods.Single(m => m.Name == methodName);
            if (!method.HasBody || !string.Equals(Fingerprint(method), entry.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("the Native Bait behavior contract changed: " + entry.Key);
            }

            Type runtimeType = assembly.GetType(typeName.Replace('/', '+'), throwOnError: true)!;
            MethodInfo runtimeMethod = runtimeType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly).Single(m => m.Name == methodName);
            ContractMethods.Add(runtimeMethod);
        }
    }

    private static void RequireConstant(Assembly assembly, string typeName, string name, int expected)
    {
        FieldInfo? field = assembly.GetType(typeName, throwOnError: true)!.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || !field.IsLiteral || Convert.ToInt32(field.GetRawConstantValue(), CultureInfo.InvariantCulture) != expected)
        {
            throw new InvalidOperationException("the Native Bait stat identifier changed: " + typeName + "." + name);
        }
    }

    private static string Fingerprint(MethodDefinition method)
    {
        Mono.Cecil.Cil.MethodBody body = method.Body;
        StringBuilder text = new();
        text.Append(method.FullName).Append('|').Append(method.IsStatic).Append('|').Append(body.InitLocals).Append('\n');
        foreach (VariableDefinition variable in body.Variables)
        {
            text.Append("L:").Append(variable.VariableType.FullName).Append('\n');
        }

        foreach (Instruction instruction in body.Instructions)
        {
            string operand = instruction.Operand switch
            {
                null => string.Empty,
                Instruction target => "I:" + InstructionIndex(body, target),
                Instruction[] targets => "B:" + string.Join(",", targets.Select(target => InstructionIndex(body, target))),
                MethodReference methodReference => "M:" + methodReference.FullName,
                FieldReference field => "F:" + field.FullName,
                TypeReference type => "T:" + type.FullName,
                VariableDefinition variable => "V:" + variable.Index + ":" + variable.VariableType.FullName,
                ParameterDefinition parameter => "P:" + parameter.Index + ":" + parameter.ParameterType.FullName,
                Mono.Cecil.CallSite site => "C:" + site.FullName,
                string value => "S:" + value.Length + ":" + value,
                float value => value.ToString("R", CultureInfo.InvariantCulture),
                double value => value.ToString("R", CultureInfo.InvariantCulture),
                _ => Convert.ToString(instruction.Operand, CultureInfo.InvariantCulture) ?? string.Empty
            };
            text.Append(instruction.OpCode.Code.ToString().Replace("_S", string.Empty)).Append(' ').Append(operand).Append('\n');
        }

        foreach (ExceptionHandler handler in body.ExceptionHandlers)
        {
            text.Append("E:").Append(handler.HandlerType).Append(':').Append(InstructionIndex(body, handler.TryStart)).Append(':')
                .Append(InstructionIndex(body, handler.TryEnd)).Append(':').Append(InstructionIndex(body, handler.HandlerStart)).Append(':')
                .Append(InstructionIndex(body, handler.HandlerEnd)).Append(':').Append(InstructionIndex(body, handler.FilterStart)).Append(':')
                .Append(handler.CatchType?.FullName).Append('\n');
        }

        using SHA256 sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", string.Empty);
    }

    private static int InstructionIndex(Mono.Cecil.Cil.MethodBody body, Instruction? instruction) => instruction == null ? body.Instructions.Count : body.Instructions.IndexOf(instruction);

    private static void WarnNative(string reason)
    {
        if (!_nativeWarning)
        {
            _nativeWarning = true;
            WriteWarning("Path of Valheiman Native Bait integration is unavailable; free multi-line casts must be stopped before bait consumption. " + reason);
        }
    }

    private static void WarnUi(string reason)
    {
        if (!_uiWarning)
        {
            _uiWarning = true;
            WriteWarning("Path of Valheiman inventory input state is unavailable; fishing bag clicks are left to that mod. " + reason);
        }
    }

    // Keep logger initialization behind the once-only warning decision.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteWarning(string message) => TrollingFishingPlugin.ModLogger.LogWarning(message);

    private sealed class RequiredPatch
    {
        internal readonly MethodInfo Target;
        internal readonly MethodInfo Method;
        internal readonly bool Postfix;

        internal RequiredPatch(MethodInfo target, MethodInfo method, bool postfix)
        {
            Target = target;
            Method = method;
            Postfix = postfix;
        }
    }
}
