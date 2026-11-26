using Mono.Cecil;

// Metadata only, except the explicit managed reader check below. Never publicize inputs.
if (args.Length != 3) throw new ArgumentException("GameContracts <final mod.dll> <original Managed directory> <BepInEx core directory>");
string gameDir = Path.GetFullPath(args[1]);
string coreDir = Path.GetFullPath(args[2]);
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(gameDir);
resolver.AddSearchDirectory(coreDir);
using var mod = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, "assembly_valheim.dll"));
using var utils = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, "assembly_utils.dll"));
using var guiUtils = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, "assembly_guiutils.dll"));
using var tmp = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, "Unity.TextMeshPro.dll"));
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
var types = new[] { game, utils, guiUtils, tmp }.SelectMany(a => Types(a.MainModule.Types))
    .Where(t => t.Name != "<Module>").GroupBy(t => t.FullName).ToDictionary(g => g.Key, g => g.First());
FieldDefinition FindField(TypeDefinition type, string name)
{
    var field = type.Fields.FirstOrDefault(f => f.Name == name);
    if (field != null) return field;
    if (type.BaseType != null && types.TryGetValue(type.BaseType.FullName, out var parent)) return FindField(parent, name);
    throw new Exception($"Missing injected field {type.FullName}.{name}");
}

foreach (var reference in mod.MainModule.AssemblyReferences)
{
    if (!File.Exists(Path.Combine(gameDir, reference.Name + ".dll")) && !File.Exists(Path.Combine(coreDir, reference.Name + ".dll")))
        throw new Exception($"Unresolved final assembly dependency: {reference.FullName}");
    if (reference.Name is "ServerSync" or "YamlDotNet" or "CraftFrom" or "EpicLoot" or "PathOfValheiman")
        throw new Exception($"Unexpected unmerged/optional assembly dependency: {reference.Name}");
}
Console.WriteLine($"PASS {mod.MainModule.AssemblyReferences.Count} final assembly dependencies available in supplied originals/BepInEx core");

int references = 0, patches = 0, injections = 0, dynamicPatches = 0;
foreach (var type in Types(mod.MainModule.Types))
{
    foreach (var method in type.Methods.Where(m => m.HasBody))
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.Operand is not MemberReference member || member.DeclaringType == null) continue;
        string scope = member.DeclaringType.Scope.Name;
        if (!File.Exists(Path.Combine(gameDir, scope + ".dll"))) continue;
        bool originalGame = scope.StartsWith("assembly_", StringComparison.Ordinal);
        if (member is MethodReference mr)
        {
            var definition = mr.Resolve() ?? throw new Exception($"Missing method: {mr} from {method}");
            if (originalGame && !definition.IsPublic) throw new Exception($"Nonpublic direct call: {mr} from {method}");
        }
        else if (member is FieldReference fr)
        {
            var definition = fr.Resolve() ?? throw new Exception($"Missing field: {fr} from {method}");
            if (definition.IsLiteral) throw new Exception($"Field instruction against constant: {fr}");
            if (originalGame && !definition.IsPublic) throw new Exception($"Nonpublic direct field: {fr} from {method}");
        }
        else continue;
        references++;
    }
    if (type.Namespace != "TrollingFishing") continue;
    foreach (var patch in type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
    {
        if (patch.ConstructorArguments.Count == 0)
        {
            if (!type.Methods.Any(m => m.Name == "TargetMethod")) throw new Exception($"Unresolved Harmony target: {type}");
            dynamicPatches++;
            continue; // Optional external-mod discovery; tested by its own integration probe.
        }
        var target = types[((TypeReference)patch.ConstructorArguments[0].Value).FullName];
        string name = (string)patch.ConstructorArguments[1].Value;
        var candidates = target.Methods.Where(m => m.Name == name);
        if (patch.ConstructorArguments.Count == 3)
        {
            var signature = ((CustomAttributeArgument[])patch.ConstructorArguments[2].Value)
                .Select(a => ((TypeReference)a.Value).FullName);
            candidates = candidates.Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(signature));
        }
        var original = candidates.Single();
        foreach (var handler in type.Methods.Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer"))
        foreach (var parameter in handler.Parameters)
        {
            string supplied = parameter.ParameterType.GetElementType().FullName;
            string? expected = null;
            if (parameter.Name.StartsWith("___", StringComparison.Ordinal))
                expected = FindField(target, parameter.Name[3..]).FieldType.FullName;
            else if (parameter.Name == "__result") expected = original.ReturnType.FullName;
            else if (parameter.Name == "__instance")
            {
                if (original.IsStatic) throw new Exception($"Static target received __instance: {handler}");
                expected = target.FullName;
            }
            else if (parameter.Name == "__runOriginal") expected = "System.Boolean";
            else if (parameter.Name == "__state")
            {
                var state = type.Methods.Single(m => m.Name == "Prefix").Parameters.Single(p => p.Name == "__state");
                expected = state.ParameterType.GetElementType().FullName;
            }
            else if (!parameter.Name.StartsWith("__", StringComparison.Ordinal))
                expected = original.Parameters.Single(p => p.Name == parameter.Name).ParameterType.GetElementType().FullName;
            if (expected != null && expected != supplied) throw new Exception($"Harmony injection mismatch {handler}: {parameter.Name}: {supplied} != {expected}");
            injections++;
        }
        patches++;
    }
}
Console.WriteLine($"PASS {references} original game/Unity/framework instruction references, {patches} static Harmony targets, {injections} injections; {dynamicPatches} optional dynamic targets excluded");

int reflection = 0;
foreach (var (type, name, value, isStatic) in new[] {
    ("Attack", "m_character", "Humanoid", false), ("Attack", "m_ammoItem", "ItemDrop/ItemData", false),
    ("Attack", "m_lastUsedAmmo", "ItemDrop/ItemData", false), ("Attack", "m_attackDrawPercentage", "System.Single", false),
    ("Humanoid", "m_currentAttack", "Attack", false), ("Humanoid", "m_currentAttackIsSecondary", "System.Boolean", false),
    ("Container", "m_inventory", "Inventory", false), ("Container", "m_inUse", "System.Boolean", false),
    ("InventoryGui", "m_currentContainer", "Container", false), ("KeyHints", "m_keyHintsEnabled", "System.Boolean", false),
    ("SkillsDialog", "m_elements", "System.Collections.Generic.List`1<UnityEngine.GameObject>", false),
    ("UITooltip", "m_anchor", "UnityEngine.RectTransform", false), ("UITooltip", "m_fixedPosition", "UnityEngine.Vector2", false),
    ("UITooltip", "m_current", "UITooltip", true), ("UITooltip", "m_tooltip", "UnityEngine.GameObject", true),
    ("FishingFloat", "m_baitConsumed", "System.Boolean", false),
    ("InventoryGrid", "m_elements", "System.Collections.Generic.List`1<InventoryElement>", false),
    ("InventoryElement", "m_equiped", "UnityEngine.UI.Image", false),
    ("Projectile", "m_vel", "UnityEngine.Vector3", false), ("Projectile", "m_didHit", "System.Boolean", false),
    ("Projectile", "m_stayAfterHitStatic", "System.Boolean", false), ("Projectile", "m_stayAfterHitDynamic", "System.Boolean", false),
    ("Projectile", "m_attachToClosestBone", "System.Boolean", false), ("Projectile", "m_attachToRigidBody", "System.Boolean", false),
    ("TMPro.TMP_Asset", "m_Version", "System.String", false),
    ("TMPro.MaterialReferenceManager", "m_SpriteAssetReferenceLookup", "System.Collections.Generic.Dictionary`2<System.Int32,TMPro.TMP_SpriteAsset>", false),
    ("TMPro.MaterialReferenceManager", "m_FontMaterialReferenceLookup", "System.Collections.Generic.Dictionary`2<System.Int32,UnityEngine.Material>", false) })
{
    var field = types[type].Fields.Single(f => f.Name == name);
    if (field.FieldType.FullName != value || field.IsStatic != isStatic) throw new Exception($"Reflection field mismatch {type}.{name}");
    reflection++;
}
foreach (var (type, name, parameters, result) in new[] {
    ("Attack", "GetProjectileSpawnPoint", "UnityEngine.Vector3&,UnityEngine.Vector3&", "System.Void"),
    ("Attack", "ModifyDamage", "HitData,System.Single", "System.Void"),
    ("FishingFloat", "GetOwner", "", "Character"), ("FishingFloat", "ReturnBait", "", "System.Void"),
    ("Localization", "AddWord", "System.String,System.String", "System.Void"),
    ("Localization", "FindNextWord", "System.String,System.Int32,System.String&,System.Int32&,System.Int32&", "System.Boolean"),
    ("Inventory", "Changed", "System.Boolean,System.Boolean", "System.Void"), ("InventoryGui", "CloseContainer", "", "System.Void") })
{
    var method = types[type].Methods.Single(m => m.Name == name && string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) == parameters);
    if (method.IsStatic || method.ReturnType.FullName != result) throw new Exception($"Reflection method mismatch {type}.{name}");
    reflection++;
}
Console.WriteLine($"PASS {reflection} cached game/TMP reflection contracts (including original nonpublic members)");
var positionGetter = types["InventoryElement"].Methods.Single(m => m.Name == "get_Position" && m.Parameters.Count == 0);
if (!positionGetter.IsPublic || positionGetter.IsStatic || positionGetter.ReturnType.FullName != "Vector2i")
    throw new Exception("InventoryElement.Position must be a public instance Vector2i getter");
var selectedBait = mod.MainModule.GetType("TrollingFishing.FishingOverrideSystem").Methods.Single(m => m.Name == "UpdateFishingRodBagSelectedBaitVisual");
if (!selectedBait.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "InventoryElement" && m.Name == "get_Position") ||
    selectedBait.Body.Instructions.Any(i => Equals(i.Operand, "m_pos")))
    throw new Exception("Selected bait visuals must use the current public position contract");
Console.WriteLine("PASS selected bait visual uses InventoryElement.Position rather than the absent m_pos field");
KeyHintsContracts.Run(mod, game);
ManagedStorageContracts.Run(gameDir);
