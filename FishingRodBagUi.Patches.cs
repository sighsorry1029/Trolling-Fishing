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

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.CloseContainer))]
internal static class InventoryGuiCloseFishingRodBagPatch
{
    private static void Prefix(InventoryGui __instance, out FishingOverrideSystem.FishingRodBagContainer __state)
    {
        __state = __instance.m_currentContainer != null
            ? __instance.m_currentContainer.GetComponent<FishingOverrideSystem.FishingRodBagContainer>()
            : null!;
    }

    private static void Postfix(FishingOverrideSystem.FishingRodBagContainer __state)
    {
        __state?.CloseAndDestroy();
        FishingOverrideSystem.ClearFishingRodBagProxiesForAzuContext();
    }
}

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
internal static class InventoryGuiHideFishingRodBagPatch
{
    private static void Prefix(InventoryGui __instance, out FishingOverrideSystem.FishingRodBagContainer __state)
    {
        __state = __instance.m_currentContainer != null
            ? __instance.m_currentContainer.GetComponent<FishingOverrideSystem.FishingRodBagContainer>()
            : null!;
    }

    private static void Postfix(FishingOverrideSystem.FishingRodBagContainer __state)
    {
        __state?.CloseAndDestroy();
        FishingOverrideSystem.ClearFishingRodBagProxiesForAzuContext();
    }
}

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
internal static class InventoryGuiUpdateFishingRodBagInputPatch
{
    private static void Prefix(InventoryGui __instance)
    {
        FishingOverrideSystem.TryHandleInventoryGuiUseInput(__instance);
    }

    private static void Postfix()
    {
        FishingOverrideSystem.UpdateFishingRodBagAzuProxyLifetime(Player.m_localPlayer);
    }
}

[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateInventory))]
internal static class InventoryGridUpdateFishingRodBagSelectedBaitPatch
{
    private static void Postfix(InventoryGrid __instance, Inventory inventory)
    {
        FishingOverrideSystem.UpdateFishingRodBagSelectedBaitVisual(__instance, inventory);
    }
}

[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
internal static class ItemDataGetTooltipFishingRodBagPatch
{
    private static void Postfix(ItemDrop.ItemData item, ref string __result)
    {
        FishingOverrideSystem.AppendFishingRodTooltipHints(item, ref __result);
        FishingBaitConfiguration.AppendBaitTooltip(item, ref __result);
    }
}

[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateInventoryWeight))]
internal static class InventoryGuiUpdateInventoryWeightFishingRodBagPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(InventoryGui __instance, Player player)
    {
        FishingOverrideSystem.UpdateInventoryWeightDisplay(__instance, player);
    }
}

[HarmonyPatch(typeof(InventoryGui), "UpdateContainerWeight")]
internal static class InventoryGuiUpdateContainerWeightFishingRodBagPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(InventoryGui __instance)
    {
        FishingOverrideSystem.UpdateFishingRodBagContainerWeightDisplay(__instance, Player.m_localPlayer);
    }
}
