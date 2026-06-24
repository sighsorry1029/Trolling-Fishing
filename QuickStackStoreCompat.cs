using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TrollingFishing;

internal static partial class FishingOverrideSystem
{
    internal static bool TrySortFishingRodBagForQuickStackStore(Container container)
    {
        if (!IsFishingRodBagContainer(container))
        {
            return false;
        }

        Inventory inventory = container.m_inventory;
        if (inventory == null)
        {
            return true;
        }

        if (!TryInvokeQuickStackStoreSortInternal(inventory))
        {
            SortFishingRodBagInventoryFallback(inventory);
        }

        TrySaveFishingRodBagContainer(container);
        return true;
    }

    private static bool TryInvokeQuickStackStoreSortInternal(Inventory inventory)
    {
        try
        {
            MethodInfo? sortInternalMethod = QuickStackStoreCompat.GetSortInternalMethod();
            if (sortInternalMethod == null)
            {
                return false;
            }

            ParameterInfo[] parameters = sortInternalMethod.GetParameters();
            object?[] args = parameters.Length == 1
                ? new object?[] { inventory }
                : new object?[] { inventory, null };
            sortInternalMethod.Invoke(null, args);
            return true;
        }
        catch (Exception exception)
        {
            TrollingFishingPlugin.ModLogger.LogWarning($"QuickStackStore FishingRod bag sort compatibility fell back to local sort: {exception.GetBaseException().Message}");
            return false;
        }
    }

    private static void SortFishingRodBagInventoryFallback(Inventory inventory)
    {
        List<ItemDrop.ItemData> items = new(inventory.m_inventory);
        items.Sort(CompareFishingRodBagItems);

        int width = Mathf.Max(1, inventory.GetWidth());
        for (int i = 0; i < items.Count; i++)
        {
            items[i].m_gridPos = new Vector2i(i % width, i / width);
        }

        inventory.m_inventory.Clear();
        inventory.m_inventory.AddRange(items);
        inventory.Changed();
    }

    private static int CompareFishingRodBagItems(ItemDrop.ItemData left, ItemDrop.ItemData right)
    {
        string leftName = ResolveSortName(left);
        string rightName = ResolveSortName(right);
        int result = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        if (result != 0)
        {
            return result;
        }

        result = right.m_quality.CompareTo(left.m_quality);
        if (result != 0)
        {
            return result;
        }

        return right.m_stack.CompareTo(left.m_stack);
    }

    private static string ResolveSortName(ItemDrop.ItemData item)
    {
        return item?.m_shared?.m_name ??
               item?.m_dropPrefab?.name ??
               string.Empty;
    }
}

internal static class QuickStackStoreCompat
{
    private const string SortModuleTypeName = "QuickStackStore.SortModule";

    internal static MethodInfo? GetSortContainerMethod()
    {
        return FindStaticMethod("SortContainer");
    }

    internal static MethodInfo? GetSortInternalMethod()
    {
        return FindStaticMethod("SortInternal");
    }

    private static MethodInfo? FindStaticMethod(string methodName)
    {
        Type? sortModuleType = FindType(SortModuleTypeName);
        if (sortModuleType == null)
        {
            return null;
        }

        foreach (MethodInfo method in sortModuleType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (string.Equals(method.Name, methodName, StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    private static Type? FindType(string typeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}

[HarmonyPatch]
internal static class QuickStackStoreSortFishingRodBagPatch
{
    private static bool Prepare()
    {
        return QuickStackStoreCompat.GetSortContainerMethod() != null;
    }

    private static MethodBase TargetMethod()
    {
        return QuickStackStoreCompat.GetSortContainerMethod()!;
    }

    private static bool Prefix(Container container)
    {
        return !FishingOverrideSystem.TrySortFishingRodBagForQuickStackStore(container);
    }
}
