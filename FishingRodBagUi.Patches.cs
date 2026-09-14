using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrollingFishing;

[HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
internal static class InventoryGuiCloseFishingRodBagPatch
{
    private static void Prefix(InventoryGui __instance, out FishingOverrideSystem.FishingRodBagContainer __state)
    {
        __state = FishingGameAccess.CurrentContainer(__instance) != null
            ? FishingGameAccess.CurrentContainer(__instance).GetComponent<FishingOverrideSystem.FishingRodBagContainer>()
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
        __state = FishingGameAccess.CurrentContainer(__instance) != null
            ? FishingGameAccess.CurrentContainer(__instance).GetComponent<FishingOverrideSystem.FishingRodBagContainer>()
            : null!;
    }

    private static void Postfix(FishingOverrideSystem.FishingRodBagContainer __state)
    {
        __state?.CloseAndDestroy();
        FishingOverrideSystem.ClearFishingRodBagProxiesForAzuContext();
    }
}

[HarmonyPatch(typeof(InventoryGui), "Update")]
internal static class InventoryGuiUpdateFishingRodBagInputPatch
{
    // Method-level priority survives the installed HarmonyX attribute merge.
    [HarmonyPriority(Priority.First + 1)]
    [HarmonyBefore(EpicLootBagCompatibility.PluginGuid)]
    private static void Prefix(InventoryGui __instance)
    {
        FishingOverrideSystem.TryHandleInventoryGuiUseInput(__instance);
    }

    private static void Postfix()
    {
        FishingOverrideSystem.UpdateFishingRodBagAzuProxyLifetime(Player.m_localPlayer);
    }
}

internal static class EpicLootBagCompatibility
{
    internal const string PluginGuid = "randyknapp.mods.epicloot";
    private static Func<bool>? _promptOpen;
    private static Func<bool>? _socketGridOpen;
    private static ConfigEntry<KeyCode>? _socketModifier;
    private static bool _unavailable;

    internal static void Initialize()
    {
        Reset();
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin)) return;
        try
        {
            Assembly assembly = plugin.Instance.GetType().Assembly;
            _promptOpen = ResolveFlag(assembly, "EpicLoot.InventoryPromptHost", "IsOpen");
            _socketGridOpen = ResolveFlag(assembly, "EpicLoot.ShardStones.SocketsUI", "IsSocketGridOpen");
            Type config = assembly.GetType("EpicLoot.Config.ELConfig", throwOnError: true)!;
            _socketModifier = AccessTools.Field(config, "SocketOverlayModifier")?.GetValue(null) as ConfigEntry<KeyCode>
                ?? throw new MissingFieldException(config.FullName, "SocketOverlayModifier");
            TrollingFishingPlugin.ModLogger.LogInfo("EpicLoot fishing bag input compatibility enabled (method priority 801).");
        }
        catch (Exception error)
        {
            Reset();
            _unavailable = true;
            TrollingFishingPlugin.ModLogger.LogWarning($"EpicLoot bag input compatibility is unavailable; leaving input to EpicLoot. {error.Message}");
        }
    }

    private static Func<bool> ResolveFlag(Assembly assembly, string typeName, string propertyName)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        MethodInfo getter = AccessTools.PropertyGetter(type, propertyName)
            ?? throw new MissingMethodException(typeName, "get_" + propertyName);
        return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
    }

    internal static bool ShouldYieldUse => _unavailable || (_promptOpen?.Invoke() ?? false) ||
        (_socketModifier != null && Input.GetKey(_socketModifier.Value));
    internal static bool IsSocketGridOpen => _socketGridOpen?.Invoke() ?? false;

    internal static void Reset()
    {
        _promptOpen = null;
        _socketGridOpen = null;
        _socketModifier = null;
        _unavailable = false;
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

[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
internal static class ItemDataGetTooltipFishingRodBagPatch
{
    private static void Postfix(ItemDrop.ItemData item, ref string __result)
    {
        FishingOverrideSystem.AppendFishingRodTooltipHints(item, ref __result);
        FishingBaitConfiguration.AppendBaitTooltip(item, ref __result);
    }
}

[HarmonyPatch(typeof(InventoryGui), "UpdateInventoryWeight")]
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
