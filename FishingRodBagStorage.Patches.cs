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

[HarmonyPatch(typeof(Inventory), nameof(Inventory.GetTotalWeight))]
internal static class InventoryGetTotalWeightFishingRodBagPatch
{
    private static void Postfix(Inventory __instance, ref float __result)
    {
        __result += FishingOverrideSystem.GetFishingRodBagExtraWeight(__instance);
    }
}

