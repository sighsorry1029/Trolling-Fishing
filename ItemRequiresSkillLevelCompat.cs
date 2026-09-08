using System;
using System.Reflection;
using HarmonyLib;

namespace TrollingFishing;

internal static class ItemRequiresSkillLevelCompat
{
    private const string StartDrawPatchTypeName = "ItemRequiresSkillLevel.Patches+StartDrawPatch";
    private static bool _loggedBypassFailure;

    internal static MethodBase? GetStartDrawPatchMethod()
    {
        try
        {
            Type? patchType = FindType(StartDrawPatchTypeName);
            if (patchType == null)
            {
                TrollingFishingPlugin.LogDebug("ItemRequiresSkillLevel StartDraw compatibility skipped because the optional patch type is not loaded.");
                return null;
            }

            MethodInfo? method = patchType.GetMethod(
                "StartDraw",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) },
                null);
            if (method != null && method.ReturnType == typeof(bool) && !method.ContainsGenericParameters)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(Humanoid) &&
                    parameters[1].ParameterType == typeof(ItemDrop.ItemData))
                {
                    return method;
                }
            }

            TrollingFishingPlugin.ModLogger.LogWarning("ItemRequiresSkillLevel StartDraw compatibility skipped: expected static bool StartDraw(Humanoid, ItemDrop.ItemData) was not found.");
        }
        catch (Exception exception)
        {
            TrollingFishingPlugin.ModLogger.LogWarning($"ItemRequiresSkillLevel StartDraw compatibility skipped because its patch target could not be resolved: {exception.GetBaseException().Message}");
        }

        return null;
    }

    internal static bool TryHandleStartDraw(Humanoid character, ItemDrop.ItemData weapon, ref bool result)
    {
        if (TrollingFishingPlugin.FishingRodBag.Value.IsOff() ||
            character is not Player ||
            !FishingOverrideSystem.IsFishingRod(weapon) ||
            string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType))
        {
            return false;
        }

        try
        {
            if (!FishingOverrideSystem.TryResolveFishingRodAmmoSelection(character, weapon, out FishingOverrideSystem.FishingRodAmmoSelection selection) ||
                selection.Source != FishingOverrideSystem.FishingRodAmmoSource.FishingRodBag ||
                selection.AmmoItem == null)
            {
                return false;
            }

            result = selection.AmmoItem.IsEquipable();
            TrollingFishingPlugin.LogDebug($"[Fishing bag] ItemRequiresSkillLevel StartDraw compatibility handled bag bait result={result}.");
            return true;
        }
        catch (Exception exception)
        {
            if (!_loggedBypassFailure)
            {
                _loggedBypassFailure = true;
                TrollingFishingPlugin.ModLogger.LogWarning($"ItemRequiresSkillLevel fishing bag compatibility failed; falling back to the original ammo check: {exception.GetBaseException().Message}");
            }

            return false;
        }
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
internal static class ItemRequiresSkillLevelStartDrawFishingRodBagPatch
{
    private static MethodBase? _targetMethod;
    private static bool _targetResolved;

    private static bool Prepare()
    {
        if (!_targetResolved)
        {
            _targetMethod = ItemRequiresSkillLevelCompat.GetStartDrawPatchMethod();
            _targetResolved = true;
        }

        return _targetMethod != null;
    }

    private static MethodBase TargetMethod()
    {
        return _targetMethod!;
    }

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        return !ItemRequiresSkillLevelCompat.TryHandleStartDraw(character, weapon, ref __result);
    }
}
