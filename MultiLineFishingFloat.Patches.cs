using System;
using HarmonyLib;
using UnityEngine;

namespace TrollingFishing;

[HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.FindFloat), new[] { typeof(Character) })]
internal static class FishingFloatFindFloatMultiLineFishingPatch
{
    private static bool Prefix(ref FishingFloat __result)
    {
        return !FishingOverrideSystem.TrySuppressMultiLineFishingFindFloat(out __result);
    }
}

[HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Setup))]
internal static class FishingFloatSetupMultiLineFishingPatch
{
    private static void Postfix(FishingFloat __instance, Character owner, ItemDrop.ItemData ammo)
    {
        if (!FishingOverrideSystem.IsMultiLineFishingSetupActive())
        {
            FishingOverrideSystem.MarkFishingFloatBaitReturnSource(__instance, owner, ammo);
            return;
        }

        FishingOverrideSystem.MarkMultiLineFishingFloat(__instance, owner);
        FishingOverrideSystem.LogMultiLineFishingFloatSetup(__instance, owner, "postfix");
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
internal static class ProjectileSetupBaitReturnSourcePatch
{
    private static void Postfix(Projectile __instance, ItemDrop.ItemData ammo)
    {
        FishingOverrideSystem.MarkProjectileBaitReturnSource(__instance, ammo);
    }
}

[HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.FixedUpdate))]
internal static class FishingFloatFixedUpdateMultiLineFishingPatch
{
    private static bool Prefix(FishingFloat __instance, out IDisposable? __state)
    {
        __state = null;
        if (FishingOverrideSystem.TrySuppressMultiLineFishingFloatInitialUpdate(__instance))
        {
            return false;
        }

        __state = FishingOverrideSystem.BeginAdditionalMultiLineFishingFloatUpdate(__instance);
        return true;
    }

    private static void Postfix(IDisposable? __state)
    {
        __state?.Dispose();
    }
}

[HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.ReturnBait))]
internal static class FishingFloatReturnBaitOriginalSourcePatch
{
    private static bool Prefix(FishingFloat __instance)
    {
        return !FishingOverrideSystem.TryReturnBaitToOriginalSource(__instance);
    }
}

[HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.SetCatch))]
internal static class FishingFloatSetCatchMultiLineFishingPatch
{
    private static void Postfix(FishingFloat __instance, Fish fish)
    {
        FishingOverrideSystem.TryConsumeMultiLineFishingBaitOnCatch(__instance, fish);
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
internal static class ProjectileOnHitMultiLineFishingPatch
{
    private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water, out IDisposable? __state)
    {
        __state = null;
        if (FishingOverrideSystem.TrySettleMultiLineFishingProjectileOnWater(__instance, hitPoint, water))
        {
            return false;
        }

        if (FishingOverrideSystem.TryBeginFishingProjectileHit(__instance, collider, water, out IDisposable? scope))
        {
            __state = scope;
        }

        return true;
    }

    private static void Postfix(Projectile __instance, Collider collider, bool water, IDisposable? __state)
    {
        FishingOverrideSystem.TrySettleBaitAfterProjectileGroundHit(__instance, collider, water);
        __state?.Dispose();
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
internal static class PlayerUseStaminaMultiLineFishingPatch
{
    private static void Prefix(Player __instance, ref float v)
    {
        FishingOverrideSystem.AdjustMultiLineFishingStaminaUse(__instance, ref v);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.RaiseSkill))]
internal static class PlayerRaiseSkillMultiLineFishingPatch
{
    private static void Prefix(Player __instance, Skills.SkillType skill, ref float value)
    {
        FishingOverrideSystem.AdjustMultiLineFishingSkillRaise(__instance, skill, ref value);
    }
}
