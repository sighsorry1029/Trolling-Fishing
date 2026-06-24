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
    internal static int CountAvailableAmmo(Humanoid humanoid, ItemDrop.ItemData weapon, string ammoType)
    {
        int count = CountAmmo(humanoid.GetInventory(), ammoType);
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOn() && humanoid is Player player && IsFishingRod(weapon))
        {
            count += CountFishingRodBagAmmo(player, weapon, ammoType);
        }

        return count;
    }

    internal static int CountAvailableAmmo(Humanoid humanoid, ItemDrop.ItemData weapon, string ammoType, ItemDrop.ItemData ammoItem)
    {
        int count = CountAmmo(humanoid.GetInventory(), ammoType, ammoItem);
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOn() && humanoid is Player player && IsFishingRod(weapon))
        {
            count += CountFishingRodBagAmmo(player, weapon, ammoType, ammoItem);
        }

        return count;
    }

    internal static int CountAvailableAmmoFromSource(Humanoid humanoid, ItemDrop.ItemData weapon, string ammoType, ItemDrop.ItemData ammoItem, FishingRodAmmoSource source)
    {
        if (humanoid == null || weapon == null || string.IsNullOrWhiteSpace(ammoType) || ammoItem == null)
        {
            return 0;
        }

        return source switch
        {
            FishingRodAmmoSource.Inventory => CountAmmo(humanoid.GetInventory(), ammoType, ammoItem),
            FishingRodAmmoSource.FishingRodBag when TrollingFishingPlugin.FishingRodBag.Value.IsOn() && humanoid is Player player && IsFishingRod(weapon) =>
                CountFishingRodBagAmmo(player, weapon, ammoType, ammoItem),
            _ => 0
        };
    }

    internal static bool TryResolveFishingRodAmmo(Humanoid humanoid, ItemDrop.ItemData weapon, out ItemDrop.ItemData ammoItem)
    {
        ammoItem = null!;
        if (!TryResolveFishingRodAmmoSelection(humanoid, weapon, out FishingRodAmmoSelection selection))
        {
            return false;
        }

        ammoItem = selection.AmmoItem;
        return true;
    }

    internal static bool TryResolveFishingRodAmmoSelection(Humanoid humanoid, ItemDrop.ItemData weapon, out FishingRodAmmoSelection selection)
    {
        selection = default;
        if (humanoid == null || weapon == null || string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType))
        {
            return false;
        }

        string ammoType = weapon.m_shared.m_ammoType;
        if (humanoid is Player player &&
            TryFindEquippedInventoryAmmo(player, ammoType, out ItemDrop.ItemData equippedAmmo))
        {
            selection = new FishingRodAmmoSelection(equippedAmmo, FishingRodAmmoSource.Inventory);
            return true;
        }

        if (TryFindFishingRodAmmo(humanoid, weapon, out ItemDrop.ItemData bagAmmo))
        {
            selection = new FishingRodAmmoSelection(bagAmmo, FishingRodAmmoSource.FishingRodBag);
            return true;
        }

        Inventory inventory = humanoid.GetInventory();
        ItemDrop.ItemData ammoItem = humanoid.GetAmmoItem();
        if (ammoItem != null &&
            inventory.ContainsItem(ammoItem) &&
            IsMatchingAmmo(ammoItem, ammoType))
        {
            selection = new FishingRodAmmoSelection(ammoItem, FishingRodAmmoSource.Inventory);
            return true;
        }

        ammoItem = inventory.GetAmmoItem(ammoType);
        if (ammoItem == null)
        {
            return false;
        }

        selection = new FishingRodAmmoSelection(ammoItem, FishingRodAmmoSource.Inventory);
        return true;
    }

    internal static bool TryFindFishingRodAmmo(Humanoid humanoid, ItemDrop.ItemData weapon, out ItemDrop.ItemData ammoItem)
    {
        ammoItem = null!;
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            humanoid is not Player player ||
            !IsFishingRod(weapon) ||
            string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType) ||
            TryFindEquippedInventoryAmmo(player, weapon.m_shared.m_ammoType, out _))
        {
            return false;
        }

        return TryFindFishingRodBagAmmo(player, weapon, weapon.m_shared.m_ammoType, out ammoItem);
    }

    internal static bool HasFishingRodBagAmmo(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        return TrollingFishingPlugin.FishingRodBag.Value.IsOn() &&
               humanoid is Player player &&
               IsFishingRod(weapon) &&
               !string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType) &&
               !TryFindEquippedInventoryAmmo(player, weapon.m_shared.m_ammoType, out _) &&
               CountFishingRodBagAmmo(player, weapon, weapon.m_shared.m_ammoType) > 0;
    }

    internal static bool TryUseFishingRodBagAmmoForVanillaAttack(Attack attack, ref ItemDrop.ItemData ammoItem, out bool result)
    {
        result = true;
        ammoItem = null!;
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            attack?.m_character is not Player player ||
            !IsFishingRod(attack.m_weapon) ||
            string.IsNullOrWhiteSpace(attack.m_weapon.m_shared.m_ammoType) ||
            TryFindEquippedInventoryAmmo(player, attack.m_weapon.m_shared.m_ammoType, out _) ||
            !TryFindFishingRodBagAmmo(player, attack.m_weapon, attack.m_weapon.m_shared.m_ammoType, out ItemDrop.ItemData bagAmmo))
        {
            return false;
        }

        Inventory inventory = LoadFishingRodBagInventory(player, attack.m_weapon, out _, out _);
        ItemDrop.ItemData ammoInBag = inventory.GetAllItems().FirstOrDefault(item =>
            IsMatchingAmmo(item, attack.m_weapon.m_shared.m_ammoType) &&
            item.m_dropPrefab == bagAmmo.m_dropPrefab) ?? inventory.GetAmmoItem(attack.m_weapon.m_shared.m_ammoType);
        if (ammoInBag == null)
        {
            return false;
        }

        if (ammoInBag.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable)
        {
            if (!player.ConsumeItem(inventory, ammoInBag))
            {
                result = false;
                return true;
            }
        }
        else
        {
            inventory.RemoveItem(ammoInBag, 1);
        }

        SaveFishingRodBagInventory(attack.m_weapon, inventory);
        RegisterAttackBaitReturnSource(attack, player, attack.m_weapon, bagAmmo);
        ammoItem = bagAmmo;
        attack.m_ammoItem = bagAmmo;
        return true;
    }


    internal static bool TryUseFishingRodBagItem(Humanoid humanoid, Inventory inventory, ItemDrop.ItemData item)
    {
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            humanoid is not Player player ||
            inventory == null ||
            item == null ||
            !FishingRodBagStoreState.InventoryOwners.TryGetValue(inventory, out ItemDrop.ItemData rod) ||
            !IsFishingRod(rod))
        {
            return false;
        }

        TryResolveMissingDropPrefab(item);
        if (!string.IsNullOrWhiteSpace(rod.m_shared.m_ammoType) &&
            IsMatchingAmmo(item, rod.m_shared.m_ammoType))
        {
            if (item.m_dropPrefab == null)
            {
                return false;
            }

            if (IsSelectedFishingRodBagBait(rod, item))
            {
                rod.m_customData.Remove(FishingRodBagStoreState.BagSelectedBaitKey);
                NotifyFishingRodBagBaitSelectionChanged(player, inventory, rod, saveBagInventory: true);
                player.Message(MessageHud.MessageType.Center, "$msg_removed " + item.m_shared.m_name);
                TrollingFishingPlugin.LogDebug($"[Fishing bag] unselected bait {item.m_dropPrefab.name} for rod bag.");
                return true;
            }

            UnequipMatchingInventoryAmmo(player, rod.m_shared.m_ammoType);
            rod.m_customData[FishingRodBagStoreState.BagSelectedBaitKey] = item.m_dropPrefab.name;
            NotifyFishingRodBagBaitSelectionChanged(player, inventory, rod, saveBagInventory: true);
            player.Message(MessageHud.MessageType.Center, "$msg_added " + item.m_shared.m_name);
            TrollingFishingPlugin.LogDebug($"[Fishing bag] selected bait {item.m_dropPrefab.name} for rod bag.");
            return true;
        }

        if (IsFishChumItem(item))
        {
            return TryMoveFishChumToPlayerInventoryAndUse(player, inventory, item);
        }

        return false;
    }

    internal static void SyncFishingRodBagBaitAfterInventoryUse(Humanoid humanoid, Inventory inventory, ItemDrop.ItemData item)
    {
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            humanoid is not Player player ||
            inventory == null ||
            item == null ||
            inventory != player.GetInventory())
        {
            return;
        }

        TryResolveMissingDropPrefab(item);
        ClearMatchingFishingRodBagBaitSelections(player, item);
    }

    private static bool IsSelectedFishingRodBagBait(ItemDrop.ItemData rod, ItemDrop.ItemData item)
    {
        return rod != null &&
               item?.m_dropPrefab != null &&
               rod.m_customData.TryGetValue(FishingRodBagStoreState.BagSelectedBaitKey, out string selectedPrefabName) &&
               string.Equals(selectedPrefabName, item.m_dropPrefab.name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindEquippedInventoryAmmo(Player player, string ammoType, out ItemDrop.ItemData ammoItem)
    {
        ammoItem = null!;
        Inventory? inventory = player.GetInventory();
        if (inventory == null || string.IsNullOrWhiteSpace(ammoType))
        {
            return false;
        }

        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (item.m_equipped && IsMatchingAmmo(item, ammoType))
            {
                ammoItem = item;
                return true;
            }
        }

        ItemDrop.ItemData? currentAmmo = player.GetAmmoItem();
        if (currentAmmo != null &&
            inventory.ContainsItem(currentAmmo) &&
            IsMatchingAmmo(currentAmmo, ammoType))
        {
            ammoItem = currentAmmo;
            return true;
        }

        return false;
    }

    private static bool TryFindEquippedInventoryAmmo(Player player, ItemDrop.ItemData sourceAmmo, out ItemDrop.ItemData ammoItem)
    {
        ammoItem = null!;
        Inventory? inventory = player.GetInventory();
        if (inventory == null || sourceAmmo == null)
        {
            return false;
        }

        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (item.m_equipped && IsSameAmmoPrefabOrName(item, sourceAmmo))
            {
                ammoItem = item;
                return true;
            }
        }

        ItemDrop.ItemData? currentAmmo = player.GetAmmoItem();
        if (currentAmmo != null &&
            inventory.ContainsItem(currentAmmo) &&
            IsSameAmmoPrefabOrName(currentAmmo, sourceAmmo))
        {
            ammoItem = currentAmmo;
            return true;
        }

        return false;
    }

    private static void UnequipMatchingInventoryAmmo(Player player, string ammoType)
    {
        Inventory? inventory = player.GetInventory();
        if (inventory == null || string.IsNullOrWhiteSpace(ammoType))
        {
            return;
        }

        bool changed = false;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
        {
            if (!item.m_equipped || !IsMatchingAmmo(item, ammoType))
            {
                continue;
            }

            player.UnequipItem(item, true);
            changed = true;
        }

        ItemDrop.ItemData? currentAmmo = player.GetAmmoItem();
        if (currentAmmo != null &&
            inventory.ContainsItem(currentAmmo) &&
            IsMatchingAmmo(currentAmmo, ammoType))
        {
            player.UnequipItem(currentAmmo, true);
            changed = true;
        }

        if (changed)
        {
            inventory.Changed();
        }
    }

    private static void ClearMatchingFishingRodBagBaitSelections(Player player, ItemDrop.ItemData equippedAmmo)
    {
        Inventory? playerInventory = player.GetInventory();
        if (playerInventory == null || equippedAmmo == null)
        {
            return;
        }

        bool changed = false;
        foreach (ItemDrop.ItemData rod in playerInventory.GetAllItems())
        {
            if (!IsFishingRod(rod) ||
                string.IsNullOrWhiteSpace(rod.m_shared.m_ammoType) ||
                !IsMatchingAmmo(equippedAmmo, rod.m_shared.m_ammoType) ||
                !rod.m_customData.ContainsKey(FishingRodBagStoreState.BagSelectedBaitKey))
            {
                continue;
            }

            rod.m_customData.Remove(FishingRodBagStoreState.BagSelectedBaitKey);
            NotifyOpenFishingRodBagInventoriesChanged(rod, saveBagInventories: false);
            changed = true;
        }

        if (changed)
        {
            playerInventory.Changed();
            TrollingFishingPlugin.LogDebug("[Fishing bag] cleared selected bag bait because an inventory bait was equipped.");
        }
    }

    private static void NotifyFishingRodBagBaitSelectionChanged(Player player, Inventory bagInventory, ItemDrop.ItemData rod, bool saveBagInventory)
    {
        NotifyOpenFishingRodBagInventoriesChanged(rod, saveBagInventory);
        if (saveBagInventory && bagInventory != null)
        {
            bagInventory.Changed();
        }

        player?.GetInventory()?.Changed();
    }

    private static void NotifyOpenFishingRodBagInventoriesChanged(ItemDrop.ItemData rod, bool saveBagInventories)
    {
        if (rod == null)
        {
            return;
        }

        foreach (KeyValuePair<Inventory, ItemDrop.ItemData> pair in FishingRodBagStoreState.InventoryOwners.ToList())
        {
            if (ReferenceEquals(pair.Value, rod))
            {
                Inventory inventory = pair.Key;
                if (inventory == null)
                {
                    continue;
                }

                if (saveBagInventories)
                {
                    inventory.Changed();
                }
                else
                {
                    MarkFishingRodBagInventoryVisualDirty(inventory);
                }
            }
        }
    }

    private static bool IsSameAmmoPrefabOrName(ItemDrop.ItemData item, ItemDrop.ItemData sourceAmmo)
    {
        if (item == null || sourceAmmo == null)
        {
            return false;
        }

        if (!IsAmmoItemType(item.m_shared.m_itemType) || !IsAmmoItemType(sourceAmmo.m_shared.m_itemType))
        {
            return false;
        }

        if (item.m_dropPrefab != null && sourceAmmo.m_dropPrefab != null)
        {
            return string.Equals(item.m_dropPrefab.name, sourceAmmo.m_dropPrefab.name, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(item.m_shared.m_name, sourceAmmo.m_shared.m_name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryMoveFishChumToPlayerInventoryAndUse(Player player, Inventory bagInventory, ItemDrop.ItemData item)
    {
        if (player == null || bagInventory == null || item == null || item.m_dropPrefab == null)
        {
            return false;
        }

        Inventory playerInventory = player.GetInventory();
        if (playerInventory == null)
        {
            return true;
        }

        ItemDrop.ItemData? alreadyEquipped = FindMatchingInventoryItem(playerInventory, item, requireEquipped: true);
        if (!playerInventory.CanAddItem(item, item.m_stack))
        {
            player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$inventory_full"));
            return true;
        }

        ItemDrop.ItemData copy = item.Clone();
        copy.m_equipped = false;
        if (!playerInventory.AddItem(copy))
        {
            player.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$inventory_full"));
            return true;
        }

        bagInventory.RemoveItem(item);
        bagInventory.Changed();

        if (alreadyEquipped != null && playerInventory.ContainsItem(alreadyEquipped))
        {
            player.Message(MessageHud.MessageType.Center, "$msg_added " + copy.m_shared.m_name);
            return true;
        }

        ItemDrop.ItemData? itemToUse = playerInventory.ContainsItem(copy)
            ? copy
            : FindMatchingInventoryItem(playerInventory, copy, requireEquipped: false);
        if (itemToUse == null)
        {
            return true;
        }

        player.UseItem(playerInventory, itemToUse, true);
        return true;
    }

    private static ItemDrop.ItemData? FindMatchingInventoryItem(Inventory inventory, ItemDrop.ItemData source, bool requireEquipped)
    {
        if (inventory == null || source == null)
        {
            return null;
        }

        string? prefabName = source.m_dropPrefab != null ? StripCloneSuffix(source.m_dropPrefab.name) : null;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (requireEquipped && !item.m_equipped)
            {
                continue;
            }

            if (!requireEquipped && item.m_equipped)
            {
                continue;
            }

            if (item.m_quality != source.m_quality || item.m_worldLevel != source.m_worldLevel)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(prefabName) &&
                item.m_dropPrefab != null &&
                string.Equals(StripCloneSuffix(item.m_dropPrefab.name), prefabName, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (string.Equals(item.m_shared.m_name, source.m_shared.m_name, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }


    private static int CountAmmo(Inventory inventory, string ammoType)
    {
        if (inventory == null || string.IsNullOrWhiteSpace(ammoType))
        {
            return 0;
        }

        int count = 0;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (!IsMatchingAmmo(item, ammoType))
            {
                continue;
            }

            count += item.m_stack;
        }

        return count;
    }

    private static int CountAmmo(Inventory inventory, string ammoType, ItemDrop.ItemData targetAmmo)
    {
        if (inventory == null || string.IsNullOrWhiteSpace(ammoType) || targetAmmo == null)
        {
            return 0;
        }

        int count = 0;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (!IsMatchingAmmo(item, ammoType, targetAmmo))
            {
                continue;
            }

            count += item.m_stack;
        }

        return count;
    }

    private static bool IsMatchingAmmo(ItemDrop.ItemData item, string ammoType)
    {
        if (item == null || string.IsNullOrWhiteSpace(ammoType))
        {
            return false;
        }

        if (!IsAmmoItemType(item.m_shared.m_itemType))
        {
            return false;
        }

        return string.Equals(item.m_shared.m_ammoType, ammoType, StringComparison.OrdinalIgnoreCase) ||
               (item.m_dropPrefab != null && string.Equals(item.m_dropPrefab.name, ammoType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMatchingAmmo(ItemDrop.ItemData item, string ammoType, ItemDrop.ItemData targetAmmo)
    {
        if (!IsMatchingAmmo(item, ammoType) || targetAmmo == null)
        {
            return false;
        }

        if (item.m_dropPrefab != null && targetAmmo.m_dropPrefab != null)
        {
            return string.Equals(item.m_dropPrefab.name, targetAmmo.m_dropPrefab.name, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(item.m_shared.m_name, targetAmmo.m_shared.m_name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAmmoItemType(ItemDrop.ItemData.ItemType itemType)
    {
        return itemType == ItemDrop.ItemData.ItemType.Ammo ||
               itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable ||
               itemType == ItemDrop.ItemData.ItemType.Consumable;
    }

    private static List<ItemDrop.ItemData> ResolveInventoryAmmoRemovalOrder(Inventory inventory, string ammoType, ItemDrop.ItemData targetAmmo)
    {
        if (inventory == null || string.IsNullOrWhiteSpace(ammoType) || targetAmmo == null)
        {
            return new List<ItemDrop.ItemData>();
        }

        return inventory.GetAllItems()
            .Where(item => IsMatchingAmmo(item, ammoType, targetAmmo))
            .ToList();
    }

    private static int CountFishingRodBagAmmo(Player player, ItemDrop.ItemData rod, string ammoType)
    {
        Inventory inventory = LoadFishingRodBagInventory(player, rod, out _, out _);
        return inventory.GetAllItems()
            .Where(item => IsMatchingAmmo(item, ammoType))
            .Sum(item => item.m_stack);
    }

    private static int CountFishingRodBagAmmo(Player player, ItemDrop.ItemData rod, string ammoType, ItemDrop.ItemData targetAmmo)
    {
        Inventory inventory = LoadFishingRodBagInventory(player, rod, out _, out _);
        return inventory.GetAllItems()
            .Where(item => IsMatchingAmmo(item, ammoType, targetAmmo))
            .Sum(item => item.m_stack);
    }

    private static bool TryFindFishingRodBagAmmo(Player player, ItemDrop.ItemData rod, string ammoType, out ItemDrop.ItemData ammoItem)
    {
        Inventory inventory = LoadFishingRodBagInventory(player, rod, out _, out _);
        ItemDrop.ItemData found = ResolveSelectedFishingRodBagAmmo(inventory, rod, ammoType) ?? inventory.GetAmmoItem(ammoType);
        if (found == null)
        {
            ammoItem = null!;
            return false;
        }

        ammoItem = found.Clone();
        ammoItem.m_stack = 1;
        return true;
    }

    private static ItemDrop.ItemData? ResolveSelectedFishingRodBagAmmo(Inventory inventory, ItemDrop.ItemData rod, string ammoType)
    {
        if (inventory == null ||
            rod == null ||
            !rod.m_customData.TryGetValue(FishingRodBagStoreState.BagSelectedBaitKey, out string selectedPrefabName) ||
            string.IsNullOrWhiteSpace(selectedPrefabName))
        {
            return null;
        }

        ItemDrop.ItemData selected = inventory.GetAllItems().FirstOrDefault(item =>
            item?.m_dropPrefab != null &&
            IsMatchingAmmo(item, ammoType) &&
            string.Equals(item.m_dropPrefab.name, selectedPrefabName, StringComparison.OrdinalIgnoreCase));
        if (selected == null)
        {
            rod.m_customData.Remove(FishingRodBagStoreState.BagSelectedBaitKey);
        }

        return selected;
    }

    private static List<ItemDrop.ItemData> ResolveFishingRodBagAmmoRemovalOrder(Inventory inventory, ItemDrop.ItemData rod, string ammoType, ItemDrop.ItemData targetAmmo)
    {
        List<ItemDrop.ItemData> ammoItems = inventory.GetAllItems()
            .Where(item => IsMatchingAmmo(item, ammoType, targetAmmo))
            .ToList();
        ItemDrop.ItemData? selected = ResolveSelectedFishingRodBagAmmo(inventory, rod, ammoType);
        if (selected == null)
        {
            return ammoItems;
        }

        return ammoItems
            .OrderByDescending(item => ReferenceEquals(item, selected))
            .ToList();
    }

}

