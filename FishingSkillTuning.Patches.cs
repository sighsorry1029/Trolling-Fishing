using System;
using HarmonyLib;
using System.Text;
using UnityEngine;

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

internal static class FishingSkillTooltipText
{
    internal static string Append(string? original)
    {
        original ??= string.Empty;
        string heading = FishingLocalization.Localize(FishingLocalization.FishingSkillTooltipHeadingKey);
        if (original.IndexOf(heading, StringComparison.Ordinal) >= 0)
        {
            return original;
        }

        StringBuilder section = new(heading);
        float biteTargetPercent = Mathf.Clamp(
            TrollingFishingPlugin.FishingOverrideBiteChanceBonusFactor.Value,
            0.1f,
            1f) * 100f;
        section.Append('\n').Append(FishingLocalization.Format(
            FishingLocalization.FishingSkillTooltipBiteChanceKey,
            biteTargetPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));

        float extraDropFactor = Mathf.Clamp(
            TrollingFishingPlugin.FishingOverrideExtraDropChanceBonusFactor.Value,
            0f,
            4f);
        if (extraDropFactor > 0f)
        {
            section.Append('\n').Append(FishingLocalization.Format(
                FishingLocalization.FishingSkillTooltipExtraDropsKey,
                (1f + extraDropFactor).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (TrollingFishingPlugin.FishingRodBag.Value.IsOn() &&
            TrollingFishingPlugin.FishingRodBagScalesWithFishingSkill.Value.IsOn())
        {
            section.Append('\n').Append(FishingLocalization.Localize(
                FishingLocalization.FishingSkillTooltipBagSlotsKey));
        }

        if (TrollingFishingPlugin.FishingRodBag.Value.IsOn() &&
            TrollingFishingPlugin.FishingRodBagCountsWeight.Value.IsOn() &&
            TrollingFishingPlugin.FishingRodBagWeightAtMaxSkillPercent.Value < 100)
        {
            section.Append('\n').Append(FishingLocalization.Format(
                FishingLocalization.FishingSkillTooltipBagWeightKey,
                Mathf.Clamp(TrollingFishingPlugin.FishingRodBagWeightAtMaxSkillPercent.Value, 0, 100)));
        }

        return original.Length > 0
            ? original + "\n\n" + section
            : section.ToString();
    }

    internal static bool MatchesSkillDescription(string? tooltipText, string? skillDescription)
    {
        if (string.IsNullOrWhiteSpace(tooltipText) || string.IsNullOrWhiteSpace(skillDescription))
        {
            return false;
        }

        if (tooltipText!.IndexOf(skillDescription!, StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        if (Localization.instance == null)
        {
            return false;
        }

        string localizedDescription = Localization.instance.Localize(skillDescription!);
        return !string.IsNullOrWhiteSpace(localizedDescription) &&
               !string.Equals(localizedDescription, skillDescription, StringComparison.Ordinal) &&
               tooltipText.IndexOf(localizedDescription, StringComparison.Ordinal) >= 0;
    }
}

[HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
internal static class FishingSkillTooltipPatch
{
    private static bool _failureLogged;

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("randyknapp.mods.epicloot")]
    private static void Postfix(SkillsDialog __instance, Player player)
    {
        if (__instance == null || player == null)
        {
            return;
        }

        try
        {
            var skills = player.GetSkills()?.GetSkillList();
            if (skills == null)
            {
                return;
            }

            Skills.Skill? fishingSkill = null;
            int fishingIndex = -1;
            for (int index = 0; index < skills.Count; index++)
            {
                Skills.Skill skill = skills[index];
                if (skill?.m_info?.m_skill == Skills.SkillType.Fishing)
                {
                    fishingSkill = skill;
                    fishingIndex = index;
                    break;
                }
            }

            if (fishingSkill?.m_info == null)
            {
                return;
            }

            UITooltip? tooltip = FindFishingTooltip(
                __instance,
                fishingIndex,
                fishingSkill.m_info.m_description);
            if (tooltip == null)
            {
                return;
            }

            string text = FishingSkillTooltipText.Append(tooltip.m_text);
            if (!string.Equals(text, tooltip.m_text, StringComparison.Ordinal))
            {
                tooltip.Set(tooltip.m_topic, text, tooltip.m_anchor, tooltip.m_fixedPosition);
            }
        }
        catch (Exception exception)
        {
            if (_failureLogged)
            {
                return;
            }

            _failureLogged = true;
            TrollingFishingPlugin.ModLogger.LogWarning(
                "Could not extend the Fishing skill tooltip: " +
                exception.GetBaseException().Message);
        }
    }

    private static UITooltip? FindFishingTooltip(
        SkillsDialog dialog,
        int fishingIndex,
        string fishingDescription)
    {
        if (dialog.m_elements != null &&
            fishingIndex >= 0 &&
            fishingIndex < dialog.m_elements.Count)
        {
            GameObject? indexedElement = dialog.m_elements[fishingIndex];
            UITooltip? indexedTooltip = indexedElement != null
                ? indexedElement.GetComponentInChildren<UITooltip>(true)
                : null;
            if (indexedTooltip != null &&
                FishingSkillTooltipText.MatchesSkillDescription(indexedTooltip.m_text, fishingDescription))
            {
                return indexedTooltip;
            }
        }

        InventoryGui? inventory = dialog.GetComponentInParent<InventoryGui>();
        if (inventory == null)
        {
            return null;
        }

        UITooltip[] candidates = inventory.GetComponentsInChildren<UITooltip>(true);
        foreach (UITooltip candidate in candidates)
        {
            if (candidate != null &&
                candidate.gameObject.activeInHierarchy &&
                FishingSkillTooltipText.MatchesSkillDescription(candidate.m_text, fishingDescription))
            {
                return candidate;
            }
        }

        foreach (UITooltip candidate in candidates)
        {
            if (candidate != null &&
                FishingSkillTooltipText.MatchesSkillDescription(candidate.m_text, fishingDescription))
            {
                return candidate;
            }
        }

        return null;
    }
}

