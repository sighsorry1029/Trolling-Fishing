using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using Item = ItemDrop.ItemData;

namespace TrollingFishing;

internal static partial class FishingOverrideSystem
{
    internal const string CraftFromGuid = "sighsorry.CraftFrom";
    private delegate IDisposable RegisterMaterialSource(string id, Func<Player, Item?> select, Func<Player, Item, Inventory?> read,
        Func<Player, Item, Inventory, bool> current, Action<Player, Item, Inventory> save);

    private sealed class CraftFromRodView
    {
        internal string? Payload;
        internal int Slots;
        internal WeakReference<Inventory>? Inventory;
        internal int FailedPrefabCount = -1;
        internal WeakReference<ObjectDB>? Database;
        internal bool HasCurrentDatabase() => Database != null && Database.TryGetTarget(out var database) &&
            ReferenceEquals(database, ObjectDB.instance);
    }

    private static readonly ConditionalWeakTable<Item, CraftFromRodView> CraftFromRodViews = new();

    internal static IDisposable? RegisterCraftFromRodBag()
    {
        if (!Chainloader.PluginInfos.TryGetValue(CraftFromGuid, out var plugin)) return null;
        try
        {
            var api = plugin.Instance.GetType().Assembly.GetType("CraftFrom.API", true)!;
            var parameters = typeof(RegisterMaterialSource).GetMethod("Invoke")!.GetParameters().Select(p => p.ParameterType).ToArray();
            var method = api.GetMethod("RegisterCarriedMaterialSource", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                null, parameters, null);
            if (method == null || method.ReturnType != typeof(IDisposable))
                throw new MissingMethodException(api.FullName, "RegisterCarriedMaterialSource");
            var register = (RegisterMaterialSource)Delegate.CreateDelegate(typeof(RegisterMaterialSource), method);
            return register(TrollingFishingPlugin.ModGUID, SelectCraftFromRod, ReadCraftFromRod, IsCraftFromRodCurrent, SaveCraftFromRod);
        }
        catch (Exception error)
        {
            TrollingFishingPlugin.ModLogger.LogWarning($"CraftFrom fishing bag integration unavailable: {error.GetBaseException().Message}");
            return null;
        }
    }

    internal static Item? SelectCraftFromRod(Player player)
    {
        if (player == null || player != Player.m_localPlayer || TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            player.GetInventory() is not Inventory inventory) return null;
        Item? rod = ResolveCarriedFishingRod(player, inventory);
        return rod != null && !FishingRodBagUiState.OpenRods.Contains(rod) ? rod : null;
    }

    private static string? ReadRodPayload(Item rod) =>
        rod.m_customData != null && rod.m_customData.TryGetValue(FishingRodBagStoreState.BagDataKey, out string? data) ? data : null;

    internal static Inventory? ReadCraftFromRod(Player player, Item rod)
    {
        if (rod == null || ObjectDB.instance == null || !ReferenceEquals(SelectCraftFromRod(player), rod) ||
            ReadRodPayload(rod) is not string raw) return null;
        int slots = ResolveTargetSlotCount(player);
        var stamp = CraftFromRodViews.GetValue(rod, _ => new CraftFromRodView());
        var database = ObjectDB.instance;
        if (stamp.FailedPrefabCount == database.m_items.Count && stamp.Payload == raw &&
            stamp.Slots == slots && stamp.HasCurrentDatabase()) return null;

        // CraftFrom keeps the current view. A re-read after retirement must detach from any earlier transaction.
        var view = LoadFishingRodBagInventory(player, rod, out _, out _);
        if (!ReferenceEquals(database, ObjectDB.instance) || !ReferenceEquals(SelectCraftFromRod(player), rod) ||
            ReadRodPayload(rod) != raw || ResolveTargetSlotCount(player) != slots)
            return null;
        stamp.Payload = raw;
        stamp.Slots = slots;
        stamp.Database = new WeakReference<ObjectDB>(database);
        bool failed = IsFishingRodBagInventoryLoadFailed(view);
        stamp.FailedPrefabCount = failed ? database.m_items.Count : -1;
        stamp.Inventory = failed ? null : new WeakReference<Inventory>(view);
        return failed ? null : view;
    }

    internal static bool IsCraftFromRodCurrent(Player player, Item rod, Inventory inventory) =>
        rod != null && ReferenceEquals(SelectCraftFromRod(player), rod) &&
        CraftFromRodViews.TryGetValue(rod, out var stamp) && stamp.Inventory != null &&
        stamp.Inventory.TryGetTarget(out var current) && ReferenceEquals(current, inventory) &&
        stamp.HasCurrentDatabase() && stamp.Slots == ResolveTargetSlotCount(player) && stamp.Payload == ReadRodPayload(rod);

    internal static void SaveCraftFromRod(Player player, Item rod, Inventory inventory)
    {
        void RequireCurrent()
        {
            if (!IsCraftFromRodCurrent(player, rod, inventory))
                throw new InvalidOperationException("Fishing rod bag changed before CraftFrom could save it.");
        }

        RequireCurrent();
        SaveFishingRodBagStoredItems(rod, CollectFishingRodBagStoredItems(inventory), RequireCurrent);
        // A notification may throw or write a different bag. Rollback can only reuse our own committed payload.
        CraftFromRodViews.GetValue(rod, _ => new CraftFromRodView()).Payload = ReadRodPayload(rod);
        player.GetInventory().NotifyChanged();
    }
}
