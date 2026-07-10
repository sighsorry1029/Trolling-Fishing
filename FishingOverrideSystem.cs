using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace TrollingFishing;

internal static partial class FishingOverrideSystem
{
    private static readonly FieldInfo? FishingFloatBaitConsumedField = AccessTools.Field(typeof(FishingFloat), "m_baitConsumed");
    private static readonly FieldInfo? InventoryGridElementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
    private static readonly MethodInfo? InventoryGuiCloseContainerMethod = AccessTools.Method(typeof(InventoryGui), "CloseContainer");

    internal enum FishingRodAmmoSource
    {
        Inventory,
        FishingRodBag
    }

    internal readonly struct FishingRodAmmoSelection
    {
        internal readonly ItemDrop.ItemData AmmoItem;
        internal readonly FishingRodAmmoSource Source;

        internal FishingRodAmmoSelection(ItemDrop.ItemData ammoItem, FishingRodAmmoSource source)
        {
            AmmoItem = ammoItem;
            Source = source;
        }

        internal bool IsValid => AmmoItem != null;
    }

    internal static bool IsFishingRod(ItemDrop.ItemData? item)
    {
        return item?.m_dropPrefab != null &&
               string.Equals(item.m_dropPrefab.name, FishingRodBagStoreState.FishingRodPrefabName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCreateItemFromPrefabName(string prefabName, out ItemDrop.ItemData item)
    {
        item = null!;
        if (string.IsNullOrWhiteSpace(prefabName) || ZNetScene.instance == null)
        {
            return false;
        }

        GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
        ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null!;
        if (itemDrop == null)
        {
            return false;
        }

        item = itemDrop.m_itemData.Clone();
        item.m_stack = 1;
        item.m_dropPrefab = prefab;
        return true;
    }

    private static string StripCloneSuffix(string name)
    {
        const string cloneSuffix = "(Clone)";
        return name.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - cloneSuffix.Length)
            : name;
    }

    private static bool TryResolveMissingDropPrefab(ItemDrop.ItemData item)
    {
        if (item == null || item.m_dropPrefab != null || ObjectDB.instance == null)
        {
            return item?.m_dropPrefab != null;
        }

        if (!ObjectDB.instance.TryGetItemPrefab(item.m_shared, out GameObject itemPrefab))
        {
            return false;
        }

        item.m_dropPrefab = itemPrefab;
        return true;
    }
}
