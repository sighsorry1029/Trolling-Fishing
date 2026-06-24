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

internal static partial class FishingOverrideSystem
{
    private static readonly Dictionary<FishingFloat, float> FishingFloatOwnerSkillFactorCache = new();
    private static int _fishingFloatOwnerSkillFactorCacheFrame = -1;

    internal readonly struct ExtraDropChanceState
    {
        internal readonly DropTable? DropTable;
        internal readonly float OriginalDropChance;

        internal ExtraDropChanceState(DropTable dropTable, float originalDropChance)
        {
            DropTable = dropTable;
            OriginalDropChance = originalDropChance;
        }
    }

    internal static FishingFloat? FindFloatWithSkillChance(Fish fish)
    {
        if (fish == null)
        {
            return null;
        }

        Vector3 fishPosition = fish.transform.position;
        float baseChance = Mathf.Clamp01(fish.m_baseHookChance);
        float targetChance = Mathf.Max(
            baseChance,
            Mathf.Clamp(TrollingFishingPlugin.FishingOverrideBiteChanceBonusFactor.Value, 0.1f, 1f));

        foreach (FishingFloat fishingFloat in FishingFloat.GetAllInstances())
        {
            if (fishingFloat == null ||
                !fishingFloat.IsInWater())
            {
                continue;
            }

            float range = Mathf.Max(0f, fishingFloat.m_range);
            if ((fishPosition - fishingFloat.transform.position).sqrMagnitude > range * range ||
                fishingFloat.GetCatch() != null)
            {
                continue;
            }

            float chance = ResolveBiteChance(baseChance, targetChance, GetFishingFloatOwnerSkillFactor(fishingFloat));
            if (UnityEngine.Random.value < chance)
            {
                return fishingFloat;
            }
        }

        return null;
    }

    internal static ExtraDropChanceState ApplyExtraDropChance(Fish fish, Character character)
    {
        if (fish == null ||
            character is not Player player ||
            fish.m_extraDrops == null ||
            fish.m_extraDrops.IsEmpty())
        {
            return default;
        }

        DropTable dropTable = fish.m_extraDrops;
        float originalChance = dropTable.m_dropChance;
        dropTable.m_dropChance = ResolveExtraDropChance(originalChance, player);
        return new ExtraDropChanceState(dropTable, originalChance);
    }

    internal static void RestoreExtraDropChance(ExtraDropChanceState state)
    {
        if (state.DropTable != null)
        {
            state.DropTable.m_dropChance = state.OriginalDropChance;
        }
    }

    private static float ResolveBiteChance(float baseChance, float targetChance, float skillFactor)
    {
        if (skillFactor <= 0f)
        {
            return baseChance;
        }

        return Mathf.Clamp01(Mathf.Lerp(baseChance, targetChance, Mathf.Clamp01(skillFactor)));
    }

    private static float GetFishingFloatOwnerSkillFactor(FishingFloat fishingFloat)
    {
        int frame = Time.frameCount;
        if (_fishingFloatOwnerSkillFactorCacheFrame != frame)
        {
            _fishingFloatOwnerSkillFactorCacheFrame = frame;
            FishingFloatOwnerSkillFactorCache.Clear();
        }

        if (FishingFloatOwnerSkillFactorCache.TryGetValue(fishingFloat, out float skillFactor))
        {
            return skillFactor;
        }

        skillFactor = fishingFloat.GetOwner() is Player player
            ? Mathf.Clamp01(player.GetSkillFactor(Skills.SkillType.Fishing))
            : 0f;
        FishingFloatOwnerSkillFactorCache[fishingFloat] = skillFactor;
        return skillFactor;
    }

    private static float ResolveExtraDropChance(float baseChance, Player? player)
    {
        float chance = Mathf.Clamp01(baseChance);
        if (player == null)
        {
            return chance;
        }

        float skillFactor = Mathf.Clamp01(player.GetSkillFactor(Skills.SkillType.Fishing));
        float bonusFactor = Mathf.Clamp(TrollingFishingPlugin.FishingOverrideExtraDropChanceBonusFactor.Value, 0f, 2f);
        return Mathf.Clamp01(chance * (1f + bonusFactor * skillFactor));
    }
}
