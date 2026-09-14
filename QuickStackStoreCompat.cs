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

        Inventory inventory = container.GetInventory();
        if (inventory == null)
        {
            return true;
        }

        if (!TryInvokeQuickStackStoreSortInternal(inventory))
        {
            SortFishingRodBagInventoryFallback(inventory);
            return true;
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
        List<ItemDrop.ItemData> items = new(inventory.GetAllItems());
        items.Sort(CompareFishingRodBagItems);

        int width = Mathf.Max(1, inventory.GetWidth());
        for (int i = 0; i < items.Count; i++)
        {
            items[i].m_gridPos = new Vector2i(i % width, i / width);
        }

        inventory.GetAllItems().Clear();
        inventory.GetAllItems().AddRange(items);
        inventory.NotifyChanged();
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
        Type? sortModuleType = FindType(SortModuleTypeName);
        return sortModuleType == null ? null : FindStaticMethod(sortModuleType, "SortContainer", typeof(Container));
    }

    internal static MethodInfo? GetSortInternalMethod()
    {
        Type? sortModuleType = FindType(SortModuleTypeName);
        if (sortModuleType == null)
        {
            return null;
        }

        Type? userConfigType = sortModuleType.Assembly.GetType("QuickStackStore.UserConfig", throwOnError: false, ignoreCase: false);
        if (userConfigType != null)
        {
            MethodInfo? method = FindStaticMethod(sortModuleType, "SortInternal", typeof(Inventory), userConfigType);
            if (method != null)
            {
                return method;
            }
        }

        return FindStaticMethod(sortModuleType, "SortInternal", typeof(Inventory));
    }

    private static MethodInfo? FindStaticMethod(Type sortModuleType, string methodName, params Type[] parameterTypes)
    {
        foreach (MethodInfo method in sortModuleType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                method.ReturnType != typeof(void) ||
                method.ContainsGenericParameters)
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != parameterTypes.Length)
            {
                continue;
            }

            bool matches = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != parameterTypes[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
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
    private static MethodBase? _targetMethod;

    private static bool Prepare()
    {
        _targetMethod ??= QuickStackStoreCompat.GetSortContainerMethod();
        return _targetMethod != null;
    }

    private static MethodBase TargetMethod()
    {
        return _targetMethod!;
    }

    private static bool Prefix(Container container)
    {
        return !FishingOverrideSystem.TrySortFishingRodBagForQuickStackStore(container);
    }
}
