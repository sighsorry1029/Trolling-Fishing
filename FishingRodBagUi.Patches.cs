using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx.Bootstrap;
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

    private static void Postfix(InventoryGui __instance,
        FishingOverrideSystem.FishingRodBagContainer __state, bool __runOriginal)
    {
        // Other mods can cancel closing or reopen a container before this postfix.
        if (!__runOriginal || FishingGameAccess.CurrentContainer(__instance) != null)
        {
            return;
        }

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

    private static void Postfix(InventoryGui __instance, Animator ___m_animator,
        FishingOverrideSystem.FishingRodBagContainer __state, bool __runOriginal)
    {
        // IsVisible stays true during the hide animation; the animator records Hide's result.
        if (!__runOriginal || ___m_animator.GetBool("visible") ||
            FishingGameAccess.CurrentContainer(__instance) != null)
        {
            return;
        }

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
    private static Func<InventoryGui, bool>? _shardSlotPress;
    private static Func<bool>? _focusedPanelGamepadPress;
    private static Func<string, bool>? _unregisterContainer;
    private static bool _containerRegistered;
    private static bool _unavailable;

    internal static void Initialize()
    {
        Reset();
        if (_unregisterContainer != null)
        {
            _unavailable = true;
            return;
        }
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin)) return;
        try
        {
            Assembly assembly = plugin.Instance.GetType().Assembly;
            _promptOpen = ResolveFlag(assembly, "EpicLoot.InventoryPromptHost", "IsOpen");
            Type input = assembly.GetType("EpicLoot.ShardStones.SocketOverlayInput", throwOnError: true)!;
            // The public ItemData overload does not check the active gamepad UI group.
            _shardSlotPress = Bind<Func<InventoryGui, bool>>(input, "IsShardSlotPress", typeof(InventoryGui));
            _focusedPanelGamepadPress = BindFocusedPanelGamepadPress(assembly, input);
            Type api = assembly.GetType("EpicLoot.API", throwOnError: true)!;
            var register = Bind<Func<string, Func<ItemDrop.ItemData, bool>, bool>>(api,
                "RegisterItemContainer", typeof(string), typeof(Func<ItemDrop.ItemData, bool>));
            var unregister = Bind<Func<string, bool>>(api, "UnregisterItemContainer", typeof(string));
            var providers = Bind<Func<Dictionary<string, List<string>>>>(api, "GetRegisteredProviders")();
            if (!providers.TryGetValue("ItemContainer", out var containers) || containers.Contains(TrollingFishingPlugin.ModGUID))
                throw new InvalidOperationException("EpicLoot item-container registration is unavailable or already owned.");

            // Register can throw from OnReload after inserting our callback. Remember cleanup
            // before calling it, but never remove an entry whose registration returned false.
            _unregisterContainer = unregister;
            if (!register(TrollingFishingPlugin.ModGUID, IsFishingBag))
            {
                _unregisterContainer = null;
                throw new InvalidOperationException("EpicLoot rejected the fishing bag item-container registration.");
            }
            _containerRegistered = true;
            TrollingFishingPlugin.ModLogger.LogInfo("EpicLoot fishing bag input compatibility enabled (current shard input, item-container registration, priority 801).");
        }
        catch (Exception error)
        {
            Disable(error);
        }
    }

    private static T Bind<T>(Type type, string name, params Type[] parameters) where T : Delegate
    {
        MethodInfo method = AccessTools.DeclaredMethod(type, name, parameters)
            ?? throw new MissingMethodException(type.FullName, name);
        return (T)Delegate.CreateDelegate(typeof(T), method);
    }

    private static Func<bool> ResolveFlag(Assembly assembly, string typeName, string propertyName)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        MethodInfo getter = AccessTools.PropertyGetter(type, propertyName)
            ?? throw new MissingMethodException(typeName, "get_" + propertyName);
        return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
    }

    private static Func<bool> BindFocusedPanelGamepadPress(Assembly assembly, Type input)
    {
        Type ui = assembly.GetType("EpicLoot.ShardStones.ShardUI", throwOnError: true)!;
        MethodInfo isOpen = AccessTools.PropertyGetter(ui, "IsOpen")
            ?? throw new MissingMethodException(ui.FullName, "get_IsOpen");
        FieldInfo panel = AccessTools.DeclaredField(ui, "_panel")
            ?? throw new MissingFieldException(ui.FullName, "_panel");
        MethodInfo hasFocus = AccessTools.PropertyGetter(panel.FieldType, "HasFocus")
            ?? throw new MissingMethodException(panel.FieldType.FullName, "get_HasFocus");
        MethodInfo gamepadPressed = AccessTools.DeclaredMethod(input, "GamepadPressed", Type.EmptyTypes)
            ?? throw new MissingMethodException(input.FullName, "GamepadPressed");
        // Match ShardUI's separate close gesture before our JoyButtonY closes the whole bag.
        // Compile the private field/getter access once; no reflection search/invoke per frame.
        return Expression.Lambda<Func<bool>>(Expression.AndAlso(Expression.Call(isOpen),
            Expression.AndAlso(Expression.Call(Expression.Field(null, panel), hasFocus),
                Expression.Call(gamepadPressed)))).Compile();
    }

    private static bool IsFishingBag(ItemDrop.ItemData item) => _containerRegistered && !_unavailable &&
        TrollingFishingPlugin.FishingRodBag != null && !TrollingFishingPlugin.FishingRodBag.Value.IsOff() &&
        FishingOverrideSystem.IsFishingRod(item);

    internal static bool ShouldYieldUse(InventoryGui inventoryGui)
    {
        if (_unavailable) return true;
        try
        {
            return (_promptOpen?.Invoke() ?? false) || (_focusedPanelGamepadPress?.Invoke() ?? false) ||
                (_shardSlotPress?.Invoke(inventoryGui) ?? false);
        }
        catch (Exception error)
        {
            Disable(error);
            return true;
        }
    }

    private static void Disable(Exception error)
    {
        Reset();
        _unavailable = true;
        TrollingFishingPlugin.ModLogger.LogWarning($"EpicLoot bag input compatibility is unavailable; leaving input to EpicLoot. {error.Message}");
    }

    internal static void Reset()
    {
        _promptOpen = null;
        _shardSlotPress = null;
        _focusedPanelGamepadPress = null;
        _containerRegistered = false;
        _unavailable = false;
        if (_unregisterContainer == null) return;
        try
        {
            _unregisterContainer(TrollingFishingPlugin.ModGUID);
            _unregisterContainer = null;
        }
        catch (Exception error)
        {
            // The callback is already inactive; retain cleanup for a later Reset/OnDestroy.
            TrollingFishingPlugin.ModLogger.LogWarning($"EpicLoot fishing bag registration cleanup failed. {error.Message}");
        }
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
