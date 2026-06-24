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
    internal static void UpdateFishingRodBagAzuProxyLifetime(Player player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        if (FishingRodBagProxyState.Proxies.Count == 0)
        {
            return;
        }

        if (!ShouldUseFishingRodBagProxies() || Time.time > FishingRodBagProxyState.KeepAliveUntil)
        {
            DestroyFishingRodBagProxies();
        }
    }


    internal static void RefreshFishingRodBagProxiesForAzuContext(Humanoid user)
    {
        if (user is not Player player || player != Player.m_localPlayer || !ShouldUseFishingRodBagProxies())
        {
            return;
        }

        FishingRodBagProxyState.KeepAliveUntil = Time.time + FishingRodBagProxyState.KeepAliveSeconds;
        SyncFishingRodBagProxies(player);
    }

    internal static void ClearFishingRodBagProxiesForAzuContext()
    {
        FishingRodBagProxyState.KeepAliveUntil = -1f;
        DestroyFishingRodBagProxies();
    }

    private static bool ShouldUseFishingRodBagProxies()
    {
        return TrollingFishingPlugin.FishingRodBag.Value.IsOn() &&
               TrollingFishingPlugin.FishingRodBagAzuCraftyBoxesCompatibility.Value.IsOn() &&
               AzuCraftyBoxesCompat.IsLoaded();
    }

    private static void SyncFishingRodBagProxies(Player player)
    {
        Inventory playerInventory = player.GetInventory();
        if (playerInventory == null)
        {
            DestroyFishingRodBagProxies();
            return;
        }

        ItemDrop.ItemData? activeRod = ResolveAzuCraftyBoxesFishingRod(player, playerInventory);

        foreach (ItemDrop.ItemData proxiedRod in FishingRodBagProxyState.Proxies.Keys.ToList())
        {
            if (!ReferenceEquals(proxiedRod, activeRod) || FishingRodBagUiState.OpenRods.Contains(proxiedRod))
            {
                RemoveFishingRodBagProxy(proxiedRod);
            }
        }

        if (activeRod == null || FishingRodBagUiState.OpenRods.Contains(activeRod))
        {
            return;
        }

        if (FishingRodBagProxyState.Proxies.TryGetValue(activeRod, out FishingRodBagProxyContainer proxy))
        {
            proxy.SetPlayer(player);
            return;
        }

        CreateFishingRodBagProxy(player, activeRod);
    }

    private static ItemDrop.ItemData? ResolveAzuCraftyBoxesFishingRod(Player player, Inventory playerInventory)
    {
        ItemDrop.ItemData currentWeapon = player.GetCurrentWeapon();
        if (IsFishingRod(currentWeapon) && playerInventory.ContainsItem(currentWeapon))
        {
            return currentWeapon;
        }

        return playerInventory.GetAllItems().FirstOrDefault(IsFishingRod);
    }


    private static void CreateFishingRodBagProxy(Player player, ItemDrop.ItemData rod)
    {
        int targetSlots = ResolveTargetSlotCount(player, rod);
        ResolveGridSize(targetSlots, out int width, out int height);
        GameObject proxyObject = new("TrollingFishing_FishingRodBagProxy");
        proxyObject.transform.position = player.transform.position;
        FishingRodBagProxyContainer proxy = proxyObject.AddComponent<FishingRodBagProxyContainer>();
        Container container = proxyObject.AddComponent<Container>();
        proxy.Initialize(player, rod, container, targetSlots, width, height);
        FishingRodBagProxyState.Proxies[rod] = proxy;
    }

    private static void RemoveFishingRodBagProxy(ItemDrop.ItemData rod)
    {
        if (rod == null || !FishingRodBagProxyState.Proxies.TryGetValue(rod, out FishingRodBagProxyContainer proxy))
        {
            return;
        }

        FishingRodBagProxyState.Proxies.Remove(rod);
        proxy.CloseAndDestroy();
    }

    private static void RefreshFishingRodBagProxy(ItemDrop.ItemData rod)
    {
        if (rod != null && FishingRodBagProxyState.Proxies.TryGetValue(rod, out FishingRodBagProxyContainer proxy))
        {
            proxy.ReloadFromRod();
        }
    }

    private static void DestroyFishingRodBagProxies()
    {
        foreach (ItemDrop.ItemData rod in FishingRodBagProxyState.Proxies.Keys.ToList())
        {
            RemoveFishingRodBagProxy(rod);
        }
    }

    internal sealed class FishingRodBagProxyContainer : MonoBehaviour
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
            container.m_name = "$item_fishingrod";
            container.m_width = width;
            container.m_height = height;
            container.m_inUse = false;
            rod.m_customData[FishingRodBagStoreState.BagSlotsKey] = slots.ToString(CultureInfo.InvariantCulture);
            LoadInventory(width, height);
            AzuCraftyBoxesCompat.AddContainer(container);
        }

        internal void SetPlayer(Player player)
        {
            _player = player;
        }

        internal void ReloadFromRod()
        {
            if (_closed || _player == null || _rod == null || _container == null)
            {
                return;
            }

            int targetSlots = ResolveTargetSlotCount(_player, _rod);
            ResolveGridSize(targetSlots, out int width, out int height);
            _container.m_width = width;
            _container.m_height = height;
            LoadInventory(width, height);
        }

        private void LoadInventory(int width, int height)
        {
            if (_rod == null || _container == null)
            {
                return;
            }

            if (_inventory != null)
            {
                _inventory.m_onChanged -= Save;
                UnregisterFishingRodBagInventory(_inventory);
            }

            _inventory = _player != null
                ? LoadFishingRodBagInventory(_player, _rod, out width, out height)
                : CreateFishingRodBagInventory(_rod, width, height);

            _inventory.m_onChanged += Save;
            RegisterFishingRodBagInventory(_inventory, _rod);
            _container.m_width = width;
            _container.m_height = height;
            _container.m_inventory = _inventory;
        }

        private void Update()
        {
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

            SaveFishingRodBagInventory(_rod, _inventory, refreshProxy: false);
        }

        internal void CloseAndDestroy()
        {
            if (_closed)
            {
                return;
            }

            Cleanup();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            Save();
            if (_inventory != null)
            {
                _inventory.m_onChanged -= Save;
                UnregisterFishingRodBagInventory(_inventory);
            }

            if (_container != null)
            {
                AzuCraftyBoxesCompat.RemoveContainer(_container);
            }
        }
    }
}
