using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TrollingFishing;

// Access to non-public members of the original Valheim 1.0.7 assemblies. Resolve once;
// FieldRefAccess/MethodDelegate validate the field types and method signatures at binding time.
internal static class FishingGameAccess
{
    internal static readonly AccessTools.FieldRef<Attack, Humanoid> AttackCharacter = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
    internal static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> AttackAmmo = AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_ammoItem");
    internal static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> LastAmmo = AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_lastUsedAmmo");
    internal static readonly AccessTools.FieldRef<Attack, float> DrawPercentage = AccessTools.FieldRefAccess<Attack, float>("m_attackDrawPercentage");
    internal static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
    internal static readonly AccessTools.FieldRef<Humanoid, bool> IsSecondaryAttack = AccessTools.FieldRefAccess<Humanoid, bool>("m_currentAttackIsSecondary");
    internal static readonly AccessTools.FieldRef<Container, Inventory> ContainerInventory = AccessTools.FieldRefAccess<Container, Inventory>("m_inventory");
    internal static readonly AccessTools.FieldRef<Container, bool> ContainerInUse = AccessTools.FieldRefAccess<Container, bool>("m_inUse");
    internal static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainer = AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");
    internal static readonly AccessTools.FieldRef<KeyHints, bool> KeyHintsEnabled = AccessTools.FieldRefAccess<KeyHints, bool>("m_keyHintsEnabled");
    internal static readonly AccessTools.FieldRef<SkillsDialog, List<GameObject>> SkillElements = AccessTools.FieldRefAccess<SkillsDialog, List<GameObject>>("m_elements");
    internal static readonly AccessTools.FieldRef<UITooltip, RectTransform?> TooltipAnchor = AccessTools.FieldRefAccess<UITooltip, RectTransform?>("m_anchor");
    internal static readonly AccessTools.FieldRef<UITooltip, Vector2> TooltipPosition = AccessTools.FieldRefAccess<UITooltip, Vector2>("m_fixedPosition");
    internal static readonly AccessTools.FieldRef<UITooltip> CurrentTooltip = AccessTools.StaticFieldRefAccess<UITooltip>(AccessTools.Field(typeof(UITooltip), "m_current"));
    internal static readonly AccessTools.FieldRef<GameObject> TooltipRoot = AccessTools.StaticFieldRefAccess<GameObject>(AccessTools.Field(typeof(UITooltip), "m_tooltip"));

    internal delegate void SpawnPoint(Attack attack, out Vector3 point, out Vector3 direction);
    internal delegate bool NextWord(Localization localization, string text, int position, out string word, out int start, out int end);
    internal static readonly SpawnPoint GetProjectileSpawnPoint = AccessTools.MethodDelegate<SpawnPoint>(AccessTools.DeclaredMethod(typeof(Attack), "GetProjectileSpawnPoint", new[] { typeof(Vector3).MakeByRefType(), typeof(Vector3).MakeByRefType() }));
    internal static readonly Action<Attack, HitData, float> ModifyDamage = AccessTools.MethodDelegate<Action<Attack, HitData, float>>(AccessTools.DeclaredMethod(typeof(Attack), "ModifyDamage", new[] { typeof(HitData), typeof(float) }));
    internal static readonly Func<FishingFloat, Character> FloatOwner = AccessTools.MethodDelegate<Func<FishingFloat, Character>>(AccessTools.DeclaredMethod(typeof(FishingFloat), "GetOwner", Type.EmptyTypes));
    internal static readonly Action<FishingFloat> ReturnBait = AccessTools.MethodDelegate<Action<FishingFloat>>(AccessTools.DeclaredMethod(typeof(FishingFloat), "ReturnBait", Type.EmptyTypes));
    internal static readonly Action<Localization, string, string> AddWord = AccessTools.MethodDelegate<Action<Localization, string, string>>(AccessTools.DeclaredMethod(typeof(Localization), "AddWord", new[] { typeof(string), typeof(string) }));
    internal static readonly NextWord FindNextWord = AccessTools.MethodDelegate<NextWord>(AccessTools.DeclaredMethod(typeof(Localization), "FindNextWord", new[] { typeof(string), typeof(int), typeof(string).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() }));
    private static readonly Action<Inventory, bool, bool> InventoryChanged = AccessTools.MethodDelegate<Action<Inventory, bool, bool>>(AccessTools.DeclaredMethod(typeof(Inventory), "Changed", new[] { typeof(bool), typeof(bool) }));

    internal static void NotifyChanged(this Inventory inventory) => InventoryChanged(inventory, false, false);
}
