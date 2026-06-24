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

[HarmonyPatch(typeof(InventoryGui), "SetupRequirementList")]
internal static class InventoryGuiSetupRequirementListFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix()
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(Player.m_localPlayer);
    }
}

[HarmonyPatch(typeof(Player), "HaveRequirementItems", new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) })]
internal static class PlayerHaveRequirementItemsFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player __instance)
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(__instance);
    }
}

[HarmonyPatch(typeof(Player), "ConsumeResources", new[] { typeof(Piece.Requirement[]), typeof(int), typeof(int), typeof(int) })]
internal static class PlayerConsumeResourcesFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Player __instance)
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(__instance);
    }
}

[HarmonyPatch(typeof(CookingStation), "FindCookableItem")]
internal static class CookingStationFindCookableItemFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix()
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(Player.m_localPlayer);
    }
}

[HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
internal static class CraftingStationInteractFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Humanoid user)
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(user);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(bool __result)
    {
        if (!__result)
        {
            FishingOverrideSystem.ClearFishingRodBagProxiesForAzuContext();
        }
    }
}

[HarmonyPatch(typeof(CookingStation), nameof(CookingStation.Interact))]
internal static class CookingStationInteractFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Humanoid user)
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(user);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        FishingOverrideSystem.ClearFishingRodBagProxiesForAzuContext();
    }
}

[HarmonyPatch(typeof(CookingStation), nameof(CookingStation.UseItem))]
internal static class CookingStationUseItemFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Humanoid user)
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(user);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        FishingOverrideSystem.ClearFishingRodBagProxiesForAzuContext();
    }
}

[HarmonyPatch(typeof(CookingStation), "OnAddFuelSwitch")]
internal static class CookingStationAddFuelFishingRodBagProxyPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Humanoid user)
    {
        FishingOverrideSystem.RefreshFishingRodBagProxiesForAzuContext(user);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        FishingOverrideSystem.ClearFishingRodBagProxiesForAzuContext();
    }
}

