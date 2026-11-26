using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Original metadata/IL and final mod metadata only. No Unity scene or UI input is run.
internal static class KeyHintsContracts
{
    internal static int Checks { get; private set; }

    internal static void Run(AssemblyDefinition mod, AssemblyDefinition game)
    {
        Checks = 0;
        TypeDefinition hints = game.MainModule.GetType("KeyHints")
            ?? throw new InvalidOperationException("Original KeyHints type is missing.");
        foreach (string name in new[] { "Awake", "Update", "UpdateHints", "OnDestroy" })
        {
            MethodDefinition method = Method(hints, name);
            Check(method.IsPrivate && !method.IsStatic && method.ReturnType.FullName == "System.Void" &&
                  method.Parameters.Count == 0 && method.HasBody, "native KeyHints." + name + " lifecycle signature");
        }

        foreach (string name in new[] { "m_fishingHints", "m_secondaryAttackGP", "m_secondaryAttackKB" })
        {
            FieldDefinition? field = hints.Fields.SingleOrDefault(f => f.Name == name);
            Check(field != null && field.IsPublic && !field.IsStatic && field.FieldType.FullName == "UnityEngine.GameObject",
                "native hint object field " + name);
        }
        FieldDefinition? enabled = hints.Fields.SingleOrDefault(f => f.Name == "m_keyHintsEnabled");
        Check(enabled != null && enabled.IsPrivate && !enabled.IsStatic && enabled.FieldType.FullName == "System.Boolean",
            "cached private key-hint setting field");

        Check(Calls(Method(hints, "Update"), "KeyHints", "UpdateHints") == 1,
            "native frame update calls UpdateHints once");
        Check(Calls(Method(hints, "Awake"), "ZInput", "add_OnInputLayoutChanged") == 1 &&
              Calls(Method(hints, "OnDestroy"), "ZInput", "remove_OnInputLayoutChanged") == 1,
            "native input-layout subscription retains paired lifecycle cleanup");

        MethodDefinition update = Method(hints, "UpdateHints");
        Instruction[] body = update.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
        bool fishingSelection = body.Select((instruction, index) => (instruction, index)).Any(site =>
            site.index > 0 && IsCall(site.instruction, "KeyHints", "DisableAllExcept") &&
            body[site.index - 1].OpCode.Code == Code.Ldfld &&
            body[site.index - 1].Operand is FieldReference field && field.DeclaringType.FullName == "KeyHints" &&
            field.Name == "m_fishingHints");
        Check(fishingSelection, "1.0.30 native group selection still selects the fishing hierarchy");

        int directCombatWrites = body.Select((instruction, index) => (instruction, index)).Count(site =>
            site.index >= 2 && IsCall(site.instruction, "UnityEngine.GameObject", "SetActive") &&
            body[site.index - 2].Operand is FieldReference field && field.DeclaringType.FullName == "KeyHints" &&
            field.Name == "m_combatHints");
        Check(directCombatWrites == 0 && Calls(update, "KeyHints", "DisableAllExcept") > 0,
            "old CaptainValheim expectation of nine direct combat writes no longer matches original 1.0.30");

        foreach (var contract in new[]
        {
            (Patch: "KeyHintsAwakeFishingRodHintPatch", Target: "Awake", Hook: "Postfix", Handler: "Initialize"),
            (Patch: "KeyHintsUpdateFishingRodHintPatch", Target: "UpdateHints", Hook: "Postfix", Handler: "Update"),
            (Patch: "KeyHintsDestroyFishingRodHintPatch", Target: "OnDestroy", Hook: "Prefix", Handler: "OnKeyHintsDestroyed")
        })
        {
            TypeDefinition patch = mod.MainModule.GetType("TrollingFishing." + contract.Patch)
                ?? throw new InvalidOperationException("Final mod patch type is missing: " + contract.Patch);
            CustomAttribute? target = patch.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
            Check(target != null && target.ConstructorArguments.Any(a => a.Value is TypeReference type && type.FullName == "KeyHints") &&
                  target.ConstructorArguments.Any(a => a.Value is string name && name == contract.Target),
                "final mod patch target " + contract.Patch);
            MethodDefinition hook = Method(patch, contract.Hook);
            Check(hook.IsStatic && hook.ReturnType.FullName == "System.Void" && hook.Parameters.Count == 1 &&
                  hook.Parameters[0].Name == "__instance" && hook.Parameters[0].ParameterType.FullName == "KeyHints" &&
                  hook.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.Harmony" + contract.Hook) &&
                  Calls(hook, "TrollingFishing.FishingRodKeyHintSystem", contract.Handler) == 1 &&
                  !patch.Methods.Any(m => m.Name == "Transpiler"),
                "final mod delegates " + contract.Hook + " without rewriting native hints");
            if (contract.Hook == "Postfix")
            {
                CustomAttribute? priority = hook.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPriority");
                Check(priority?.ConstructorArguments.Count == 1 && Convert.ToInt32(priority.ConstructorArguments[0].Value) == 0,
                    "final mod hint postfix retains Priority.Last");
            }
        }

        TypeDefinition system = mod.MainModule.GetType("TrollingFishing.FishingRodKeyHintSystem")
            ?? throw new InvalidOperationException("Final mod hint system is missing.");
        Check(Calls(Method(system, "ShouldShow"), "UnityEngine.GameObject", "get_activeInHierarchy") == 1 &&
              Calls(Method(system, "ShouldShow"), "TrollingFishing.FishingOverrideSystem", "IsFishingRod") == 1,
            "custom hint retains native hierarchy visibility and fishing-rod gates");
        Console.WriteLine($"PASS {Checks} KeyHints original/final-mod contracts (no Unity UI execution).");
    }

    private static MethodDefinition Method(TypeDefinition type, string name) =>
        type.Methods.SingleOrDefault(m => m.Name == name)
        ?? throw new InvalidOperationException($"Method missing or ambiguous: {type.FullName}.{name}");

    private static int Calls(MethodDefinition method, string type, string name) =>
        method.Body.Instructions.Count(instruction => IsCall(instruction, type, name));

    private static bool IsCall(Instruction instruction, string type, string name) =>
        (instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt) &&
        instruction.Operand is MethodReference method && method.DeclaringType.FullName == type && method.Name == name;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("KeyHints contract failed: " + description);
        Checks++;
    }
}
