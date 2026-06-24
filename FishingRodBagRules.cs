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
    internal static bool IsFishingRodBagInventory(Inventory inventory)
    {
        return inventory != null && FishingRodBagStoreState.Inventories.Contains(inventory);
    }

    internal static bool CanAddToFishingRodBag(Inventory inventory, ItemDrop.ItemData item)
    {
        return !IsFishingRodBagInventory(inventory) || IsAllowedFishingRodBagItem(item);
    }

    internal static bool CanAddToFishingRodBag(Inventory inventory, GameObject itemPrefab)
    {
        return !IsFishingRodBagInventory(inventory) || IsAllowedFishingRodBagPrefab(itemPrefab);
    }


    private static bool IsAllowedFishingRodBagItem(ItemDrop.ItemData item)
    {
        if (item == null)
        {
            return false;
        }

        TryResolveMissingDropPrefab(item);
        return item.m_dropPrefab != null &&
               IsAllowedFishingRodBagPrefab(item.m_dropPrefab);
    }

    private static bool IsAllowedFishingRodBagPrefab(GameObject itemPrefab)
    {
        if (itemPrefab == null)
        {
            return false;
        }

        ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
        if (itemDrop?.m_itemData?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Fish)
        {
            return true;
        }

        if (IsCompatibleFishingItemDropPrefab(itemPrefab, itemDrop))
        {
            return true;
        }

        if (IsFishChumPrefab(itemPrefab))
        {
            return true;
        }

        EnsureAllowedFishingItemCache();
        return FishingRodBagRulesState.AllowedPrefabNames.Contains(itemPrefab.name);
    }

    private static bool IsFishChumItem(ItemDrop.ItemData item)
    {
        if (item == null)
        {
            return false;
        }

        TryResolveMissingDropPrefab(item);
        if (item.m_dropPrefab != null && IsFishChumPrefab(item.m_dropPrefab))
        {
            return true;
        }

        return false;
    }

    private static bool IsFishChumPrefab(GameObject itemPrefab)
    {
        if (itemPrefab == null)
        {
            return false;
        }

        return FishingRodBagRulesState.FishChumPrefabNames.Contains(StripCloneSuffix(itemPrefab.name));
    }

    private static bool IsCompatibleFishingItemDropPrefab(GameObject itemPrefab, ItemDrop? itemDrop)
    {
        if (itemPrefab == null || itemDrop == null)
        {
            return false;
        }

        string prefabName = StripCloneSuffix(itemPrefab.name);
        return itemPrefab.GetComponent<Fish>() != null ||
               prefabName.IndexOf("Starfish", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string StripCloneSuffix(string name)
    {
        const string cloneSuffix = "(Clone)";
        if (name.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            return name.Substring(0, name.Length - cloneSuffix.Length);
        }

        return name;
    }

    private static bool TryResolveMissingDropPrefab(ItemDrop.ItemData item)
    {
        if (item == null || item.m_dropPrefab != null || ObjectDB.instance == null)
        {
            return item?.m_dropPrefab != null;
        }

        if (ObjectDB.instance.TryGetItemPrefab(item.m_shared, out GameObject itemPrefab))
        {
            item.m_dropPrefab = itemPrefab;
            return true;
        }

        return false;
    }

    private static void EnsureAllowedFishingItemCache()
    {
        ZNetScene? scene = ZNetScene.instance;
        if (scene == null || scene == FishingRodBagRulesState.CachedScene)
        {
            return;
        }

        FishingRodBagRulesState.CachedScene = scene;
        FishingRodBagRulesState.AllowedPrefabNames.Clear();
        AddAllowedFishingItemsFromPrefabs(scene.m_prefabs);
        AddAllowedFishingItemsFromPrefabs(scene.m_nonNetViewPrefabs);

        if (ObjectDB.instance != null)
        {
            foreach (GameObject itemPrefab in ObjectDB.instance.m_items)
            {
                if (itemPrefab != null && itemPrefab.name.IndexOf("FishingBait", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FishingRodBagRulesState.AllowedPrefabNames.Add(itemPrefab.name);
                }
            }
        }
    }

    private static void AddAllowedFishingItemsFromPrefabs(IEnumerable<GameObject> prefabs)
    {
        foreach (GameObject prefab in prefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            Fish fish = prefab.GetComponent<Fish>();
            if (fish == null)
            {
                continue;
            }

            if (fish.m_pickupItem != null)
            {
                FishingRodBagRulesState.AllowedPrefabNames.Add(fish.m_pickupItem.name);
            }

            foreach (Fish.BaitSetting baitSetting in fish.m_baits)
            {
                if (baitSetting?.m_bait != null)
                {
                    FishingRodBagRulesState.AllowedPrefabNames.Add(baitSetting.m_bait.name);
                }
            }
        }
    }

}
