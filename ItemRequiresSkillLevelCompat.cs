using System;
using System.Reflection;
using HarmonyLib;

namespace TrollingFishing;

internal static class ItemRequiresSkillLevelCompat
{
    private const string StartDrawPatchTypeName = "ItemRequiresSkillLevel.Patches+StartDrawPatch";
    private static bool _loggedBypassFailure;

    internal static bool IsLoaded()
    {
        return FindType(StartDrawPatchTypeName) != null;
    }

    internal static MethodBase? GetStartDrawPatchMethod()
    {
        return FindType(StartDrawPatchTypeName)?.GetMethod(
            "StartDraw",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Humanoid), typeof(ItemDrop.ItemData) },
            null);
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
    private static bool Prepare()
    {
        return ItemRequiresSkillLevelCompat.IsLoaded();
    }

    private static MethodBase TargetMethod()
    {
        return ItemRequiresSkillLevelCompat.GetStartDrawPatchMethod()!;
    }

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        return !ItemRequiresSkillLevelCompat.TryHandleStartDraw(character, weapon, ref __result);
    }
}
