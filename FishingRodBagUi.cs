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
    internal static void UpdateFishingRodBagSelectedBaitVisual(InventoryGrid grid, Inventory inventory)
    {
        if (grid == null ||
            inventory == null ||
            !FishingRodBagStoreState.InventoryOwners.TryGetValue(inventory, out ItemDrop.ItemData rod) ||
            InventoryGridElementsField?.GetValue(grid) is not IEnumerable elements)
        {
            return;
        }

        HashSet<string> selectedPrefabNames = new(StringComparer.OrdinalIgnoreCase);
        if (rod.m_customData.TryGetValue(FishingRodBagStoreState.BagSelectedBaitKey, out string selectedBaitPrefabName) &&
            !string.IsNullOrWhiteSpace(selectedBaitPrefabName))
        {
            selectedPrefabNames.Add(selectedBaitPrefabName);
        }

        foreach (object element in elements)
        {
            Type elementType = element.GetType();
            FieldInfo? positionField = AccessTools.Field(elementType, "m_pos");
            FieldInfo? equippedField = AccessTools.Field(elementType, "m_equiped");
            if (positionField?.GetValue(element) is not Vector2i position ||
                equippedField?.GetValue(element) is not Image equippedImage)
            {
                continue;
            }

            equippedImage.enabled = false;
            ItemDrop.ItemData item = inventory.GetItemAt(position.x, position.y);
            if (item != null)
            {
                TryResolveMissingDropPrefab(item);
            }

            string? itemName = item?.m_dropPrefab?.name;
            if (string.IsNullOrWhiteSpace(itemName))
            {
                continue;
            }

            equippedImage.enabled = selectedPrefabNames.Contains(itemName!);
        }
    }

    private static void MarkFishingRodBagInventoryVisualDirty(Inventory inventory)
    {
        if (inventory == null ||
            InventoryGui.instance == null ||
            InventoryGui.instance.m_containerGrid == null)
        {
            return;
        }

        InventoryGui.instance.m_containerGrid.UpdateInventory(inventory, Player.m_localPlayer, null);
    }

    internal static void DestroyExistingFishingFloats(Character owner, FishingFloat? except = null)
    {
        if (owner == null)
        {
            return;
        }

        MultiLineFishingCastState.FloatBuffer.Clear();
        foreach (FishingFloat fishingFloat in FishingFloat.GetAllInstances())
        {
            if (fishingFloat != null && fishingFloat != except && fishingFloat.GetOwner() == owner)
            {
                MultiLineFishingCastState.FloatBuffer.Add(fishingFloat);
            }
        }

        foreach (FishingFloat fishingFloat in MultiLineFishingCastState.FloatBuffer)
        {
            if (fishingFloat == null)
            {
                continue;
            }

            FishingOverrideSystem.ReturnFishingFloatBaitBeforeDestroy(fishingFloat);
            GameObject go = fishingFloat.gameObject;
            if (ZNetScene.instance != null)
            {
                ZNetScene.instance.Destroy(go);
            }
            else
            {
                Object.Destroy(go);
            }
        }

        MultiLineFishingCastState.FloatBuffer.Clear();
    }

    internal static void TryOpenFishingRodBagFromUseInput(Player player)
    {
        if (player == null ||
            player != Player.m_localPlayer ||
            TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            InventoryGui.instance == null)
        {
            return;
        }

        TryCloseFishingRodBagFromInput();
    }


    internal static bool TryHandleInventoryGuiUseInput(InventoryGui inventoryGui)
    {
        if (inventoryGui == null ||
            TrollingFishingPlugin.FishingRodBag.Value.IsOff())
        {
            return false;
        }

        if (TryCloseFishingRodBagFromInput())
        {
            return true;
        }

        Player? player = Player.m_localPlayer;
        if (player == null ||
            inventoryGui.m_currentContainer != null ||
            !ZInput.GetButtonDown("Use"))
        {
            return false;
        }

        Vector2 mousePosition = Input.mousePosition;
        ItemDrop.ItemData hoveredItem = inventoryGui.m_playerGrid.GetItem(new Vector2i(Mathf.RoundToInt(mousePosition.x), Mathf.RoundToInt(mousePosition.y)));
        if (!IsFishingRod(hoveredItem))
        {
            return false;
        }

        OpenFishingRodBag(player, hoveredItem);
        return true;
    }


    internal static void UpdateInventoryWeightDisplay(InventoryGui inventoryGui, Player player)
    {
        if (inventoryGui == null || player == null)
        {
            return;
        }

        Inventory inventory = player.GetInventory();
        if (inventory == null)
        {
            return;
        }

        float totalWeight = inventory.m_totalWeight + GetFishingRodBagExtraWeight(inventory);
        int currentWeight = Mathf.CeilToInt(totalWeight);
        int maxWeight = Mathf.CeilToInt(player.GetMaxCarryWeight());
        if (currentWeight > maxWeight && Mathf.Sin(Time.time * 10f) > 0f)
        {
            inventoryGui.m_weight.text = $"<color=red>{currentWeight}</color>/{maxWeight}";
            return;
        }

        inventoryGui.m_weight.text = $"{currentWeight}/{maxWeight}";
    }

    internal static void UpdateFishingRodBagContainerWeightDisplay(InventoryGui inventoryGui, Player player)
    {
        if (inventoryGui == null ||
            player == null ||
            inventoryGui.m_containerWeight == null ||
            !TryGetFishingRodBagContainerDisplayWeight(inventoryGui.m_currentContainer, player, out float weight))
        {
            return;
        }

        inventoryGui.m_containerWeight.text = Mathf.CeilToInt(weight).ToString(CultureInfo.InvariantCulture);
    }

    internal static void AppendFishingRodTooltipHints(ItemDrop.ItemData item, ref string tooltip)
    {
        if (item == null || !IsFishingRod(item))
        {
            return;
        }

        List<string> hints = new();
        if (TrollingFishingPlugin.FishingRodBag != null &&
            TrollingFishingPlugin.FishingRodBag.Value.IsOn())
        {
            hints.Add(FishingLocalization.Localize(FishingLocalization.FishingRodBagOpenHintKey));
        }

        if (TrollingFishingPlugin.FishingRodMultiLine != null &&
            TrollingFishingPlugin.FishingRodMultiLine.Value.IsOn())
        {
            hints.Add(FishingLocalization.Localize(FishingLocalization.FishingRodMultiLineHintKey));
        }

        if (hints.Count > 0)
        {
            tooltip += "\n\n<color=orange>" + string.Join("\n", hints) + "</color>";
        }
    }

    private static void OpenFishingRodBag(Player player, ItemDrop.ItemData rod)
    {
        FishingRodBagUiState.OpenRods.Add(rod);
        RemoveFishingRodBagProxy(rod);
        int targetSlots = ResolveTargetSlotCount(player, rod);
        ResolveGridSize(targetSlots, out int width, out int height);
        GameObject bagObject = new("TrollingFishing_FishingRodBag");
        bagObject.transform.position = player.transform.position;
        FishingRodBagContainer bag = bagObject.AddComponent<FishingRodBagContainer>();
        Container container = bagObject.AddComponent<Container>();
        bag.Initialize(player, rod, container, targetSlots, width, height);
        InventoryGui.instance.Show(container, 1);
        ZInput.ResetButtonStatus("Use");
    }

    private static bool TryCloseFishingRodBagFromInput()
    {
        InventoryGui? inventoryGui = InventoryGui.instance;
        if (inventoryGui == null ||
            !IsFishingRodBagContainer(inventoryGui.m_currentContainer))
        {
            return false;
        }

        if (ZInput.GetButtonDown("Use"))
        {
            ZInput.ResetButtonStatus("Use");
            CloseFishingRodBagContainerOnly(inventoryGui);
            return true;
        }

        if (!IsFishingRodBagFullCloseInput())
        {
            return false;
        }

        ResetFishingRodBagFullCloseInput();
        inventoryGui.Hide();
        return true;
    }

    private static void CloseFishingRodBagContainerOnly(InventoryGui inventoryGui)
    {
        if (InventoryGuiCloseContainerMethod == null)
        {
            TrollingFishingPlugin.ModLogger.LogWarning("Could not close the fishing bag without closing the inventory because InventoryGui.CloseContainer was not found.");
            return;
        }

        InventoryGuiCloseContainerMethod.Invoke(inventoryGui, Array.Empty<object>());
    }

    private static bool IsFishingRodBagFullCloseInput()
    {
        return ZInput.GetButtonDown("Inventory") ||
               ZInput.GetButtonDown("JoyButtonB") ||
               ZInput.GetButtonDown("JoyButtonY") ||
               ZInput.GetKeyDown(KeyCode.Escape);
    }

    private static void ResetFishingRodBagFullCloseInput()
    {
        ZInput.ResetButtonStatus("Inventory");
        ZInput.ResetButtonStatus("JoyButtonB");
        ZInput.ResetButtonStatus("JoyButtonY");
    }

    internal sealed class FishingRodBagContainer : MonoBehaviour
    {
        private Player? _player;
        private ItemDrop.ItemData? _rod;
        private Inventory? _inventory;
        private Container? _container;
        private bool _closed;

        internal void Initialize(Player player, ItemDrop.ItemData rod, Container container, int slots, int width, int height)
        {
            _player = player;
            _rod = rod;
            _container = container;
            _inventory = LoadFishingRodBagInventory(player, rod, out width, out height);

            _inventory.m_onChanged += Save;
            RegisterFishingRodBagInventory(_inventory, rod);
            container.m_name = "$item_fishingrod";
            container.m_width = width;
            container.m_height = height;
            container.m_inventory = _inventory;
            container.m_inUse = true;
            AzuCraftyBoxesCompat.AddContainer(container);
            rod.m_customData[FishingRodBagStoreState.BagSlotsKey] = slots.ToString(CultureInfo.InvariantCulture);
            Save();
        }

        private void Update()
        {
            if (!_closed &&
                _container != null &&
                InventoryGui.instance != null &&
                InventoryGui.instance.m_currentContainer != _container)
            {
                CloseAndDestroy();
                return;
            }

            if (_player != null)
            {
                transform.position = _player.transform.position;
            }
        }

        internal void Save()
        {
            if (_rod == null || _inventory == null)
            {
                return;
            }

            SaveFishingRodBagInventory(_rod, _inventory);
        }

        internal void CloseAndDestroy()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            Save();
            if (_rod != null)
            {
                FishingRodBagUiState.OpenRods.Remove(_rod);
            }

            if (_inventory != null)
            {
                _inventory.m_onChanged -= Save;
                UnregisterFishingRodBagInventory(_inventory);
            }

            if (_container != null)
            {
                AzuCraftyBoxesCompat.RemoveContainer(_container);
            }

            Destroy(gameObject);
        }
    }
}
