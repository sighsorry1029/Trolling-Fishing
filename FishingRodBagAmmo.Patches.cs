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

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
internal static class HumanoidUseItemFishingRodBagPatch
{
    private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
    {
        return !FishingOverrideSystem.TryUseFishingRodBagItem(__instance, inventory, item);
    }

    private static void Postfix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
    {
        FishingOverrideSystem.SyncFishingRodBagBaitAfterInventoryUse(__instance, inventory, item);
    }
}

[HarmonyPatch(typeof(Attack), "UseAmmo")]
internal static class AttackUseAmmoFishingRodBagPatch
{
    private static bool Prefix(Attack __instance, ref ItemDrop.ItemData ammoItem, ref bool __result)
    {
        return !FishingOverrideSystem.TryUseFishingRodBagAmmoForVanillaAttack(__instance, ref ammoItem, out __result);
    }
}

[HarmonyPatch(typeof(Attack), "HaveAmmo")]
internal static class AttackHaveAmmoFishingRodBagPatch
{
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        if (!FishingOverrideSystem.HasFishingRodBagAmmo(character, weapon))
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Attack), "EquipAmmoItem")]
internal static class AttackEquipAmmoItemFishingRodBagPatch
{
    private static bool Prefix(Humanoid character, ItemDrop.ItemData weapon, ref bool __result)
    {
        if (!FishingOverrideSystem.HasFishingRodBagAmmo(character, weapon))
        {
            return true;
        }

        __result = true;
        return false;
    }
}
