using System;
using HarmonyLib;

namespace TrollingFishing;

[HarmonyPatch(typeof(Fish), nameof(Fish.FindFloat))]
internal static class FishFindFloatFishingSkillPatch
{
    private static bool Prefix(Fish __instance, ref FishingFloat __result)
    {
        __result = FishingOverrideSystem.FindFloatWithSkillChance(__instance)!;
        return false;
    }
}

[HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Catch))]
internal static class FishingFloatCatchExtraDropSkillPatch
{
    private static bool Prefix(Fish fish, Character owner, out FishingOverrideSystem.ExtraDropChanceState __state, ref string __result)
    {
        __state = FishingOverrideSystem.ApplyExtraDropChance(fish, owner);
        if (FishingOverrideSystem.TryCatchFishToFishingRodBag(fish, owner, out string message))
        {
            __result = message;
            return false;
        }

        return true;
    }

    private static Exception? Finalizer(Exception? __exception, FishingOverrideSystem.ExtraDropChanceState __state)
    {
        FishingOverrideSystem.RestoreExtraDropChance(__state);
        return __exception;
    }
}

