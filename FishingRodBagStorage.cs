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

internal static partial class FishingOverrideSystem
{
    internal static bool TryCatchFishToFishingRodBag(Fish fish, Character owner, out string message)
    {
        message = "";
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOff() || fish == null || owner is not Player player)
        {
            return false;
        }

        ItemDrop.ItemData rod = player.GetCurrentWeapon();
        if (!IsFishingRod(rod) || !TryCreateFishPickupItem(fish, out ItemDrop.ItemData fishItem))
        {
            return false;
        }

        Inventory bagInventory = LoadFishingRodBagInventory(player, rod, out _, out _);
        if (!TryAddItemToFishingRodBagInventory(bagInventory, fishItem))
        {
            return false;
        }

        Vector3 dropPosition = fish.transform.position;
        message = "$msg_fishing_catched " + fish.GetHoverName();
        if (!fish.m_extraDrops.IsEmpty())
        {
            foreach (ItemDrop.ItemData dropListItem in fish.m_extraDrops.GetDropListItems())
            {
                message = message + " & " + dropListItem.m_shared.m_name;
                ItemDrop.ItemData dropCopy = dropListItem.Clone();
                dropCopy.m_stack = dropListItem.m_stack;
                if (TryAddItemToFishingRodBagInventory(bagInventory, dropCopy))
                {
                    continue;
                }

                Inventory playerInventory = player.GetInventory();
                if (playerInventory != null && playerInventory.CanAddItem(dropListItem.m_dropPrefab, dropListItem.m_stack))
                {
                    playerInventory.AddItem(dropListItem.m_dropPrefab, dropListItem.m_stack);
                }
                else
                {
                    Object.Instantiate(dropListItem.m_dropPrefab, dropPosition, Quaternion.Euler(0f, UnityEngine.Random.Range(0, 360), 0f))
                        .GetComponent<ItemDrop>()
                        .SetStack(dropListItem.m_stack);
                    player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$inventory_full"));
                }
            }
        }

        SaveFishingRodBagInventory(rod, bagInventory);
        DestroyCaughtFish(fish);
        return true;
    }


    internal static void RegisterFishingRodBagInventory(Inventory inventory, ItemDrop.ItemData rod)
    {
        if (inventory != null)
        {
            FishingRodBagStoreState.Inventories.Add(inventory);
            if (rod != null)
            {
                FishingRodBagStoreState.InventoryOwners[inventory] = rod;
            }
        }
    }

    internal static void UnregisterFishingRodBagInventory(Inventory inventory)
    {
        if (inventory != null)
        {
            FishingRodBagStoreState.Inventories.Remove(inventory);
            FishingRodBagStoreState.InventoryOwners.Remove(inventory);
            FishingRodBagStoreState.InventoryStates.Remove(inventory);
        }
    }

    internal static bool IsFishingRodBagContainer(Container container)
    {
        return container != null &&
               (container.GetComponent<FishingRodBagContainer>() != null ||
                container.GetComponent<FishingRodBagProxyContainer>() != null);
    }

    internal static bool TrySaveFishingRodBagContainer(Container container)
    {
        if (container == null)
        {
            return false;
        }

        FishingRodBagContainer bag = container.GetComponent<FishingRodBagContainer>();
        if (bag != null)
        {
            bag.Save();
            return true;
        }

        FishingRodBagProxyContainer proxy = container.GetComponent<FishingRodBagProxyContainer>();
        if (proxy != null)
        {
            proxy.Save();
            return true;
        }

        return false;
    }

    internal static float GetFishingRodBagExtraWeight(Inventory inventory)
    {
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            TrollingFishingPlugin.FishingRodBagCountsWeight.Value.IsOff() ||
            inventory == null ||
            Player.m_localPlayer == null ||
            inventory != Player.m_localPlayer.GetInventory())
        {
            return 0f;
        }

        float weight = 0f;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (IsFishingRod(item))
            {
                weight += GetFishingRodBagWeight(item);
            }
        }

        return weight * ResolveFishingRodBagWeightMultiplier(Player.m_localPlayer);
    }

    internal static bool TryGetFishingRodBagContainerDisplayWeight(Container container, Player player, out float weight)
    {
        weight = 0f;
        if (container == null ||
            player == null ||
            !IsFishingRodBagContainer(container) ||
            container.GetInventory() is not Inventory inventory ||
            !FishingRodBagStoreState.InventoryOwners.TryGetValue(inventory, out ItemDrop.ItemData rod))
        {
            return false;
        }

        weight = GetFishingRodBagWeight(rod) * ResolveFishingRodBagWeightMultiplier(player);
        return true;
    }

    private static float ResolveFishingRodBagWeightMultiplier(Player player)
    {
        if (player == null)
        {
            return 1f;
        }

        float targetMultiplier = Mathf.Clamp(TrollingFishingPlugin.FishingRodBagWeightAtMaxSkillPercent.Value, 0, 100) / 100f;
        float skillFactor = Mathf.Clamp01(player.GetSkillFactor(Skills.SkillType.Fishing));
        return Mathf.Lerp(1f, targetMultiplier, skillFactor);
    }

    private static int ResolveTargetSlotCount(Player player, ItemDrop.ItemData rod)
    {
        int resolvedSlots = NormalizeFishingRodBagSlotCount(ResolveConfiguredSlotCount(player));
        rod.m_customData[FishingRodBagStoreState.BagSlotsKey] = resolvedSlots.ToString(CultureInfo.InvariantCulture);
        return resolvedSlots;
    }

    private static int ResolveConfiguredSlotCount(Player player)
    {
        return TrollingFishingPlugin.FishingRodBagScalesWithFishingSkill.Value.IsOn()
            ? ResolveSkillSlotCount(player.GetSkillLevel(Skills.SkillType.Fishing))
            : FishingRodBagStoreState.FixedSlotCount;
    }

    private static int ResolveSkillSlotCount(float fishingLevel)
    {
        int tier = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(0f, fishingLevel) / 10f), 0, 9);
        return (tier + 1) * 8;
    }

    private static Inventory CreateFishingRodBagInventory(ItemDrop.ItemData rod, int width, int height)
    {
        return new Inventory("$item_fishingrod", rod.GetIcon(), width, height);
    }

    private static void ResolveGridSize(int slots, out int width, out int height)
    {
        int normalizedSlots = NormalizeFishingRodBagSlotCount(slots);
        switch (normalizedSlots)
        {
            case <= 8:
                width = 4;
                height = 2;
                return;
            case <= 16:
                width = 4;
                height = 4;
                return;
            case <= 24:
                width = 6;
                height = 4;
                return;
            default:
                width = 8;
                height = normalizedSlots / 8;
                return;
        }
    }

    private static int NormalizeFishingRodBagSlotCount(int slots)
    {
        int clampedSlots = Mathf.Clamp(slots, FishingRodBagStoreState.MinSlots, FishingRodBagStoreState.MaxSlots);
        return Mathf.CeilToInt(clampedSlots / 8f) * 8;
    }

    private static bool TryAddItemToFishingRodBag(Player player, ItemDrop.ItemData rod, ItemDrop.ItemData item)
    {
        Inventory inventory = LoadFishingRodBagInventory(player, rod, out _, out _);
        if (!TryAddItemToFishingRodBagInventory(inventory, item))
        {
            return false;
        }

        SaveFishingRodBagInventory(rod, inventory);
        return true;
    }

    private static bool TryAddItemToFishingRodBagInventory(Inventory inventory, ItemDrop.ItemData item)
    {
        if (inventory == null || !IsAllowedFishingRodBagItem(item))
        {
            return false;
        }

        ItemDrop.ItemData copy = item.Clone();
        return inventory.CanAddItem(copy) && inventory.AddItem(copy);
    }

    private static Inventory LoadFishingRodBagInventory(Player player, ItemDrop.ItemData rod, out int width, out int height)
    {
        int slots = ResolveTargetSlotCount(player, rod);
        ResolveGridSize(slots, out width, out height);
        Inventory inventory = CreateFishingRodBagInventory(rod, width, height);
        if (rod.m_customData.TryGetValue(FishingRodBagStoreState.BagDataKey, out string rawData) && !string.IsNullOrWhiteSpace(rawData))
        {
            LoadFishingRodBagVisibleInventory(rod, inventory, rawData, slots);
        }
        else
        {
            SetFishingRodBagInventoryState(inventory, new FishingRodBagInventoryState(new List<ItemDrop.ItemData>(), slots));
        }

        return inventory;
    }

    private static void SaveFishingRodBagInventory(ItemDrop.ItemData rod, Inventory inventory, bool refreshProxy = true)
    {
        List<ItemDrop.ItemData> allItems = CollectFishingRodBagStoredItems(inventory);
        SaveFishingRodBagStoredItems(rod, allItems);
        InvalidateFishingRodBagWeight(rod);
        if (refreshProxy)
        {
            RefreshFishingRodBagProxy(rod);
        }

        Player.m_localPlayer?.GetInventory()?.Changed();
    }

    private static float GetFishingRodBagWeight(ItemDrop.ItemData rod)
    {
        if (rod == null)
        {
            return 0f;
        }

        if (!rod.m_customData.TryGetValue(FishingRodBagStoreState.BagDataKey, out string rawData) ||
            string.IsNullOrWhiteSpace(rawData))
        {
            FishingRodBagStoreState.WeightCache.Remove(rod);
            return 0f;
        }

        if (FishingRodBagStoreState.WeightCache.TryGetValue(rod, out BagWeightCacheEntry cache) &&
            string.Equals(cache.RawData, rawData, StringComparison.Ordinal))
        {
            return cache.Weight;
        }

        if (!TryLoadFishingRodBagInventoryForReadOnly(rod, rawData, out Inventory inventory))
        {
            FishingRodBagStoreState.WeightCache.Remove(rod);
            return 0f;
        }

        float weight = inventory.GetAllItems().Sum(item => item.GetWeight());
        FishingRodBagStoreState.WeightCache[rod] = new BagWeightCacheEntry(rawData, weight);
        return weight;
    }

    private static bool TryLoadFishingRodBagInventoryForReadOnly(ItemDrop.ItemData rod, string rawData, out Inventory inventory)
    {
        ResolveGridSize(FishingRodBagStoreState.MaxSlots, out int width, out int height);
        inventory = CreateFishingRodBagInventory(rod, width, height);
        try
        {
            inventory.Load(new ZPackage(rawData));
            return true;
        }
        catch (Exception exception)
        {
            TrollingFishingPlugin.ModLogger.LogWarning($"Could not read FishingRod bag weight: {exception.GetBaseException().Message}");
            return false;
        }
    }

    private static void InvalidateFishingRodBagWeight(ItemDrop.ItemData rod)
    {
        if (rod != null)
        {
            FishingRodBagStoreState.WeightCache.Remove(rod);
        }
    }

    private static void LoadFishingRodBagVisibleInventory(ItemDrop.ItemData rod, Inventory visibleInventory, string rawData, int visibleSlots)
    {
        if (!TryLoadFishingRodBagInventoryForReadOnly(rod, rawData, out Inventory storedInventory))
        {
            SetFishingRodBagInventoryState(visibleInventory, new FishingRodBagInventoryState(new List<ItemDrop.ItemData>(), visibleSlots));
            return;
        }

        List<ItemDrop.ItemData> overflowItems = MaterializeFishingRodBagVisibleItems(storedInventory.GetAllItems(), visibleInventory, visibleSlots);
        SetFishingRodBagInventoryState(visibleInventory, new FishingRodBagInventoryState(overflowItems, visibleSlots));
        visibleInventory.Changed();
    }

    private static List<ItemDrop.ItemData> MaterializeFishingRodBagVisibleItems(IEnumerable<ItemDrop.ItemData> sourceItems, Inventory visibleInventory, int visibleSlots)
    {
        List<ItemDrop.ItemData> overflow = new();
        HashSet<Vector2i> occupied = new();
        List<ItemDrop.ItemData> deferred = new();

        foreach (ItemDrop.ItemData sourceItem in sourceItems.OrderBy(item => item.m_gridPos.y).ThenBy(item => item.m_gridPos.x))
        {
            ItemDrop.ItemData item = CloneFishingRodBagItem(sourceItem);
            if (IsGridPositionInInventory(item.m_gridPos, visibleInventory) &&
                occupied.Add(item.m_gridPos) &&
                visibleInventory.m_inventory.Count < visibleSlots)
            {
                visibleInventory.m_inventory.Add(item);
                continue;
            }

            deferred.Add(item);
        }

        foreach (ItemDrop.ItemData item in deferred)
        {
            if (visibleInventory.m_inventory.Count < visibleSlots &&
                TryFindNextFreeFishingRodBagSlot(visibleInventory, occupied, out Vector2i slot))
            {
                item.m_gridPos = slot;
                occupied.Add(slot);
                visibleInventory.m_inventory.Add(item);
                continue;
            }

            overflow.Add(item);
        }

        return overflow;
    }

    private static List<ItemDrop.ItemData> CollectFishingRodBagStoredItems(Inventory inventory)
    {
        List<ItemDrop.ItemData> allItems = inventory.GetAllItems()
            .OrderBy(item => item.m_gridPos.y)
            .ThenBy(item => item.m_gridPos.x)
            .Select(CloneFishingRodBagItem)
            .ToList();

        if (FishingRodBagStoreState.InventoryStates.TryGetValue(inventory, out FishingRodBagInventoryState state))
        {
            allItems.AddRange(state.OverflowItems.Select(CloneFishingRodBagItem));
        }

        return allItems;
    }

    private static void SaveFishingRodBagStoredItems(ItemDrop.ItemData rod, List<ItemDrop.ItemData> allItems)
    {
        ResolveGridSize(FishingRodBagStoreState.MaxSlots, out int width, out int height);
        Inventory storageInventory = CreateFishingRodBagInventory(rod, width, height);
        HashSet<Vector2i> occupied = new();

        foreach (ItemDrop.ItemData item in allItems)
        {
            ItemDrop.ItemData copy = CloneFishingRodBagItem(item);
            if (!IsGridPositionInInventory(copy.m_gridPos, storageInventory) || !occupied.Add(copy.m_gridPos))
            {
                if (!TryFindNextFreeFishingRodBagSlot(storageInventory, occupied, out Vector2i slot))
                {
                    break;
                }

                copy.m_gridPos = slot;
                occupied.Add(slot);
            }

            storageInventory.m_inventory.Add(copy);
        }

        ZPackage package = new();
        storageInventory.Save(package);
        rod.m_customData[FishingRodBagStoreState.BagDataKey] = package.GetBase64();
    }

    private static void SetFishingRodBagInventoryState(Inventory inventory, FishingRodBagInventoryState state)
    {
        FishingRodBagStoreState.InventoryStates.Remove(inventory);
        FishingRodBagStoreState.InventoryStates.Add(inventory, state);
    }

    private static ItemDrop.ItemData CloneFishingRodBagItem(ItemDrop.ItemData item)
    {
        ItemDrop.ItemData clone = item.Clone();
        TryResolveMissingDropPrefab(clone);
        return clone;
    }

    private static bool IsGridPositionInInventory(Vector2i position, Inventory inventory)
    {
        return position.x >= 0 &&
               position.y >= 0 &&
               position.x < inventory.GetWidth() &&
               position.y < inventory.GetHeight();
    }

    private static bool TryFindNextFreeFishingRodBagSlot(Inventory inventory, HashSet<Vector2i> occupied, out Vector2i slot)
    {
        for (int y = 0; y < inventory.GetHeight(); y++)
        {
            for (int x = 0; x < inventory.GetWidth(); x++)
            {
                Vector2i candidate = new(x, y);
                if (!occupied.Contains(candidate))
                {
                    slot = candidate;
                    return true;
                }
            }
        }

        slot = new Vector2i(-1, -1);
        return false;
    }

    private static bool TryCreateFishPickupItem(Fish fish, out ItemDrop.ItemData fishItem)
    {
        ItemDrop itemDrop = fish.GetComponent<ItemDrop>();
        if (itemDrop != null)
        {
            fishItem = itemDrop.m_itemData.Clone();
            if (fishItem.m_dropPrefab == null)
            {
                fishItem.m_dropPrefab = itemDrop.gameObject;
            }

            return true;
        }

        fishItem = null!;
        if (fish.m_pickupItem == null)
        {
            return false;
        }

        ItemDrop pickupItemDrop = fish.m_pickupItem.GetComponent<ItemDrop>();
        if (pickupItemDrop == null)
        {
            return false;
        }

        fishItem = pickupItemDrop.m_itemData.Clone();
        fishItem.m_dropPrefab = fish.m_pickupItem;
        fishItem.m_stack = Mathf.Clamp(fish.m_pickupItemStackSize, 1, fishItem.m_shared.m_maxStackSize);
        fishItem.m_worldLevel = (byte)Game.m_worldLevel;
        return true;
    }

    private static void DestroyCaughtFish(Fish fish)
    {
        ZNetView zNetView = fish.GetComponent<ZNetView>();
        if (zNetView != null && zNetView.IsValid())
        {
            zNetView.Destroy();
            return;
        }

        Object.Destroy(fish.gameObject);
    }
}
