using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrollingFishing;

[HarmonyPatch(typeof(Container), "Awake")]
internal static class ContainerAwakeFishingRodBagPatch
{
    private static bool Prefix(Container __instance)
    {
        return !FishingOverrideSystem.IsFishingRodBagContainer(__instance);
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.IsOwner))]
internal static class ContainerIsOwnerFishingRodBagPatch
{
    private static bool Prefix(Container __instance, ref bool __result)
    {
        if (!FishingOverrideSystem.IsFishingRodBagContainer(__instance))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.SetInUse))]
internal static class ContainerSetInUseFishingRodBagPatch
{
    private static bool Prefix(Container __instance, bool inUse)
    {
        if (!FishingOverrideSystem.IsFishingRodBagContainer(__instance))
        {
            return true;
        }

        __instance.m_inUse = inUse;
        return false;
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.IsInUse))]
internal static class ContainerIsInUseFishingRodBagPatch
{
    private static bool Prefix(Container __instance, ref bool __result)
    {
        if (!FishingOverrideSystem.IsFishingRodBagContainer(__instance))
        {
            return true;
        }

        __result = __instance.m_inUse;
        return false;
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.Save))]
internal static class ContainerSaveFishingRodBagPatch
{
    private static bool Prefix(Container __instance)
    {
        if (!FishingOverrideSystem.TrySaveFishingRodBagContainer(__instance))
        {
            return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(Container), nameof(Container.Load))]
internal static class ContainerLoadFishingRodBagPatch
{
    private static bool Prefix(Container __instance, ref bool __result)
    {
        if (!FishingOverrideSystem.IsFishingRodBagContainer(__instance))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new[] { typeof(ItemDrop.ItemData), typeof(int) })]
internal static class InventoryCanAddItemFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (FishingOverrideSystem.CanAddToFishingRodBag(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), new[] { typeof(GameObject), typeof(int) })]
internal static class InventoryCanAddPrefabFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, GameObject prefab, ref bool __result)
    {
        if (FishingOverrideSystem.CanAddToFishingRodBag(__instance, prefab))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData) })]
internal static class InventoryAddItemFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (FishingOverrideSystem.CanAddToFishingRodBag(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(GameObject), typeof(int) })]
internal static class InventoryAddPrefabFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, GameObject prefab, ref bool __result)
    {
        if (FishingOverrideSystem.CanAddToFishingRodBag(__instance, prefab))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
internal static class InventoryAddItemAtPositionFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (FishingOverrideSystem.CanAddToFishingRodBag(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
internal static class InventoryAddItemAmountFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (FishingOverrideSystem.CanAddToFishingRodBag(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData) })]
internal static class InventoryMoveItemToThisFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item)
    {
        return FishingOverrideSystem.CanAddToFishingRodBag(__instance, item);
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.MoveItemToThis), new[] { typeof(Inventory), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) })]
internal static class InventoryMoveItemToThisAmountFishingRodBagPatch
{
    private static bool Prefix(Inventory __instance, ItemDrop.ItemData item, ref bool __result)
    {
        if (FishingOverrideSystem.CanAddToFishingRodBag(__instance, item))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
