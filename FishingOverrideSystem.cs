using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

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
}
