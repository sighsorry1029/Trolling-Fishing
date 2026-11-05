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
    [HarmonyAfter(PathOfValheimanCompatibility.PluginGuid)]
    private static bool Prefix(Attack __instance, ref ItemDrop.ItemData ammoItem, ref bool __result, bool __runOriginal)
    {
        if (!MultiLineFishingSystem.PrepareNativeBaitAmmo(__instance, ammoItem, __runOriginal, ref __result))
        {
            return false;
        }

        // HarmonyX still runs this Prefix when another Prefix owns the transaction.
        if (!__runOriginal) return true;
        ItemDrop.ItemData selectedAmmo = ammoItem;
        if (!FishingOverrideSystem.TryUseFishingRodBagAmmoForVanillaAttack(__instance, ref selectedAmmo, out bool result))
        {
            return true;
        }

        ammoItem = selectedAmmo;
        __result = result;
        return false;
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
