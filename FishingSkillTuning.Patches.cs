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

    private static void Postfix(FishingOverrideSystem.ExtraDropChanceState __state)
    {
        FishingOverrideSystem.RestoreExtraDropChance(__state);
    }
}

