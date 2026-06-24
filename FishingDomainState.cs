using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TrollingFishing;

internal sealed class FishingRodBagInventoryState
{
    internal readonly List<ItemDrop.ItemData> OverflowItems;
    internal readonly int VisibleSlots;

    internal FishingRodBagInventoryState(List<ItemDrop.ItemData> overflowItems, int visibleSlots)
    {
        OverflowItems = overflowItems;
        VisibleSlots = visibleSlots;
    }
}

internal sealed class BagWeightCacheEntry
{
    internal readonly string RawData;
    internal readonly float Weight;

    internal BagWeightCacheEntry(string rawData, float weight)
    {
        RawData = rawData;
        Weight = weight;
    }
}

internal sealed class AttackBaitReturnSourceState
{
    internal readonly FishingOverrideSystem.MultiLineBaitReservation Source;

    internal AttackBaitReturnSourceState(FishingOverrideSystem.MultiLineBaitReservation source)
    {
        Source = source;
    }
}

internal static class FishingRodBagStoreState
{
    internal const string FishingRodPrefabName = "FishingRod";
    internal const string BagDataKey = "TrollingFishing.FishingRodBag.Data";
    internal const string BagSlotsKey = "TrollingFishing.FishingRodBag.Slots";
    internal const string BagSelectedBaitKey = "TrollingFishing.FishingRodBag.SelectedBait";
    internal const int FixedSlotCount = 32;
    internal const int MinSlots = 8;
    internal const int MaxSlots = 80;

    internal static readonly HashSet<Inventory> Inventories = new();
    internal static readonly Dictionary<Inventory, ItemDrop.ItemData> InventoryOwners = new();
    internal static readonly ConditionalWeakTable<Inventory, FishingRodBagInventoryState> InventoryStates = new();
    internal static readonly Dictionary<ItemDrop.ItemData, BagWeightCacheEntry> WeightCache = new();
}

internal static class FishingRodBagUiState
{
    internal static readonly HashSet<ItemDrop.ItemData> OpenRods = new();
}

internal static class FishingRodBagProxyState
{
    internal const float KeepAliveSeconds = 2f;

    internal static readonly Dictionary<ItemDrop.ItemData, FishingOverrideSystem.FishingRodBagProxyContainer> Proxies = new();
    internal static float KeepAliveUntil = -1f;
}

internal static class FishingRodBagRulesState
{
    internal static readonly HashSet<string> AllowedPrefabNames = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly HashSet<string> FishChumPrefabNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "FishChumMeadows",
        "FishChumBlackforest",
        "FishChumSwamps",
        "FishChumMountains",
        "FishChumPlains",
        "FishChumMistlands",
        "FishChumAshlands",
        "FishChumDeepnorth",
        "FishChumOcean",
        "SerpentChum",
        "LeviathanChum",
    };

    internal static ZNetScene? CachedScene;
}

internal static class MultiLineFishingCastState
{
    internal const float AttackGrace = 8f;

    internal static readonly List<FishingFloat> FloatBuffer = new();
    internal static readonly List<FishingOverrideSystem.MultiLineFishingSetupContext> SetupContexts = new();
    internal static readonly List<FishingOverrideSystem.MultiLineFishingUpdateContext> UpdateContexts = new();
    internal static int SetupDepth;
}

internal static class BaitSourceTrackerState
{
    internal static readonly List<FishingOverrideSystem.MultiLineBaitReservation> SetupContexts = new();
    internal static readonly ConditionalWeakTable<Attack, AttackBaitReturnSourceState> AttackSources = new();
}
