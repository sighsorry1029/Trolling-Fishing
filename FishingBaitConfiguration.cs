using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using YamlDotNet.Serialization;
using Object = UnityEngine.Object;

namespace TrollingFishing;

internal static class FishingBaitConfiguration
{
    internal const string FileName = "TrollingFishing.yml";

    private static readonly string FileFullPath = Path.Combine(Paths.ConfigPath, FileName);
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly Dictionary<string, List<Fish.BaitSetting>> OriginalBaits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<BaitRule>> CurrentRulesByFish = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<BaitTooltipEntry>> CurrentTooltipEntriesByBait = new(StringComparer.OrdinalIgnoreCase);
    private static bool _hasAppliedConfiguration;

    internal static void EnsureConfigDirectory()
    {
        Directory.CreateDirectory(Paths.ConfigPath);
    }

    internal static void ReloadAndApply()
    {
        EnsureConfigDirectory();
        if (!File.Exists(FileFullPath) && !TryCreateInitialConfigurationFromLoadedFish())
        {
            return;
        }

        if (!TryReadConfiguration(out BaitConfigurationFile configuration))
        {
            return;
        }

        Apply(configuration);
    }

    internal static void ApplyToFishInstance(Fish fish)
    {
        if (!_hasAppliedConfiguration || fish == null)
        {
            return;
        }

        string fishName = StripCloneSuffix(fish.gameObject.name);
        EnsureOriginalBaits(fishName, fish);
        ApplyRulesToFish(fishName, fish, CurrentRulesByFish);
    }

    private static void Apply(BaitConfigurationFile configuration)
    {
        if (ObjectDB.instance == null || ZNetScene.instance == null)
        {
            TrollingFishingPlugin.ModLogger.LogDebug("Fishing bait YAML is waiting for ObjectDB and ZNetScene.");
            return;
        }

        Dictionary<string, List<BaitRule>> rulesByFish = BuildRulesByFish(configuration);
        CaptureOriginalBaitsFromKnownPrefabs();

        CurrentRulesByFish.Clear();
        foreach (KeyValuePair<string, List<BaitRule>> pair in rulesByFish)
        {
            CurrentRulesByFish[pair.Key] = pair.Value;
        }

        int changedPrefabs = 0;
        foreach (Fish fish in EnumerateKnownFishPrefabs())
        {
            string fishName = StripCloneSuffix(fish.gameObject.name);
            EnsureOriginalBaits(fishName, fish);
            ApplyRulesToFish(fishName, fish, CurrentRulesByFish);
            changedPrefabs++;
        }

        int changedInstances = 0;
        foreach (Fish fish in Object.FindObjectsByType<Fish>(FindObjectsSortMode.None))
        {
            if (fish == null)
            {
                continue;
            }

            string fishName = StripCloneSuffix(fish.gameObject.name);
            EnsureOriginalBaits(fishName, fish);
            ApplyRulesToFish(fishName, fish, CurrentRulesByFish);
            changedInstances++;
        }

        _hasAppliedConfiguration = true;
        RebuildBaitTooltipEntries();
        TrollingFishingPlugin.ModLogger.LogInfo(
            $"Fishing bait YAML applied. configuredFish={CurrentRulesByFish.Count}, prefabs={changedPrefabs}, instances={changedInstances}.");
    }

    internal static void AppendBaitTooltip(ItemDrop.ItemData item, ref string tooltip)
    {
        if (item == null || !TryResolveItemPrefabName(item, out string baitName))
        {
            return;
        }

        if (!CurrentTooltipEntriesByBait.TryGetValue(baitName, out List<BaitTooltipEntry> entries) || entries.Count == 0)
        {
            return;
        }

        StringBuilder builder = new();
        builder.Append("\n\n<color=orange>");
        builder.Append(FishingLocalization.Localize(FishingLocalization.FishingBaitTooltipHeaderKey));
        foreach (BaitTooltipEntry entry in entries)
        {
            builder.Append('\n');
            builder.Append(LocalizeFishName(entry.FishName));
            builder.Append(": ");
            builder.Append(FormatChancePercent(entry.Chance));
        }

        builder.Append("</color>");
        tooltip += builder.ToString();
    }

    private static bool TryReadConfiguration(out BaitConfigurationFile configuration)
    {
        configuration = new BaitConfigurationFile();

        try
        {
            string yaml = File.ReadAllText(FileFullPath);
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return true;
            }

            configuration = Deserializer.Deserialize<BaitConfigurationFile>(yaml) ?? new BaitConfigurationFile();
            configuration.Baits ??= new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
            return true;
        }
        catch (Exception ex)
        {
            TrollingFishingPlugin.ModLogger.LogError($"Failed to read fishing bait YAML '{FileName}': {ex.Message}");
            return false;
        }
    }

    private static Dictionary<string, List<BaitRule>> BuildRulesByFish(BaitConfigurationFile configuration)
    {
        Dictionary<string, List<BaitRule>> rulesByFish = new(StringComparer.OrdinalIgnoreCase);
        if (configuration.Baits == null || configuration.Baits.Count == 0)
        {
            return rulesByFish;
        }

        foreach (KeyValuePair<string, Dictionary<string, float>> baitEntry in configuration.Baits)
        {
            string baitName = StripCloneSuffix((baitEntry.Key ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(baitName))
            {
                continue;
            }

            if (!TryResolveBait(baitName, out ItemDrop bait))
            {
                TrollingFishingPlugin.ModLogger.LogWarning($"Fishing bait YAML skipped unknown bait prefab '{baitName}'.");
                continue;
            }

            if (baitEntry.Value == null)
            {
                continue;
            }

            foreach (KeyValuePair<string, float> fishEntry in baitEntry.Value)
            {
                string configuredFishName = StripCloneSuffix((fishEntry.Key ?? string.Empty).Trim());
                if (string.IsNullOrWhiteSpace(configuredFishName))
                {
                    continue;
                }

                if (!TryResolveFishPrefabName(configuredFishName, out string fishName))
                {
                    TrollingFishingPlugin.ModLogger.LogWarning($"Fishing bait YAML skipped unknown fish prefab '{configuredFishName}'.");
                    continue;
                }

                if (!rulesByFish.TryGetValue(fishName, out List<BaitRule> rules))
                {
                    rules = new List<BaitRule>();
                    rulesByFish[fishName] = rules;
                }

                UpsertRule(rules, new BaitRule(baitName, bait, ClampChance(fishEntry.Value)));
            }
        }

        return rulesByFish;
    }

    private static void CaptureOriginalBaitsFromKnownPrefabs()
    {
        foreach (Fish fish in EnumerateKnownFishPrefabs())
        {
            EnsureOriginalBaits(StripCloneSuffix(fish.gameObject.name), fish);
        }
    }

    private static bool ApplyRulesToFish(string fishName, Fish fish, Dictionary<string, List<BaitRule>> rulesByFish)
    {
        bool hasRules = rulesByFish.TryGetValue(fishName, out List<BaitRule> rules);
        List<Fish.BaitSetting> baits = hasRules ? new List<Fish.BaitSetting>() : CloneOriginalBaits(fishName, fish);

        if (hasRules)
        {
            foreach (BaitRule rule in rules)
            {
                UpsertBait(baits, rule);
            }
        }

        fish.m_baits = baits;
        return hasRules;
    }

    private static void RebuildBaitTooltipEntries()
    {
        CurrentTooltipEntriesByBait.Clear();
        foreach (Fish fish in EnumerateKnownFishPrefabs())
        {
            if (fish == null)
            {
                continue;
            }

            string fishName = string.IsNullOrWhiteSpace(fish.m_name)
                ? StripCloneSuffix(fish.gameObject.name)
                : fish.m_name;
            foreach (Fish.BaitSetting baitSetting in fish.m_baits)
            {
                if (baitSetting?.m_bait == null || baitSetting.m_chance <= 0f)
                {
                    continue;
                }

                string baitName = StripCloneSuffix(baitSetting.m_bait.name);
                if (string.IsNullOrWhiteSpace(baitName))
                {
                    continue;
                }

                if (!CurrentTooltipEntriesByBait.TryGetValue(baitName, out List<BaitTooltipEntry> entries))
                {
                    entries = new List<BaitTooltipEntry>();
                    CurrentTooltipEntriesByBait[baitName] = entries;
                }

                UpsertTooltipEntry(entries, new BaitTooltipEntry(fishName, ClampChance(baitSetting.m_chance)));
            }
        }

        foreach (List<BaitTooltipEntry> entries in CurrentTooltipEntriesByBait.Values)
        {
            entries.Sort((left, right) => string.Compare(left.FishName, right.FishName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void UpsertTooltipEntry(List<BaitTooltipEntry> entries, BaitTooltipEntry entry)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (!string.Equals(entries[i].FishName, entry.FishName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries[i] = entry.Chance > entries[i].Chance ? entry : entries[i];
            return;
        }

        entries.Add(entry);
    }

    private static void EnsureOriginalBaits(string fishName, Fish fish)
    {
        if (OriginalBaits.ContainsKey(fishName))
        {
            return;
        }

        Fish prefabFish = TryResolveFishPrefab(fishName, out Fish resolvedFish) ? resolvedFish : fish;
        OriginalBaits[fishName] = CloneBaits(prefabFish.m_baits);
    }

    private static List<Fish.BaitSetting> CloneOriginalBaits(string fishName, Fish fish)
    {
        if (!OriginalBaits.TryGetValue(fishName, out List<Fish.BaitSetting> original))
        {
            OriginalBaits[fishName] = CloneBaits(fish.m_baits);
            original = OriginalBaits[fishName];
        }

        return CloneBaits(original);
    }

    private static List<Fish.BaitSetting> CloneBaits(IEnumerable<Fish.BaitSetting>? source)
    {
        List<Fish.BaitSetting> clone = new();
        if (source == null)
        {
            return clone;
        }

        foreach (Fish.BaitSetting baitSetting in source)
        {
            if (baitSetting == null)
            {
                continue;
            }

            clone.Add(new Fish.BaitSetting
            {
                m_bait = baitSetting.m_bait,
                m_chance = baitSetting.m_chance
            });
        }

        return clone;
    }

    private static void UpsertRule(List<BaitRule> rules, BaitRule rule)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            if (string.Equals(rules[i].BaitName, rule.BaitName, StringComparison.OrdinalIgnoreCase))
            {
                rules[i] = rule;
                return;
            }
        }

        rules.Add(rule);
    }

    private static void UpsertBait(List<Fish.BaitSetting> baits, BaitRule rule)
    {
        for (int i = 0; i < baits.Count; i++)
        {
            Fish.BaitSetting baitSetting = baits[i];
            if (baitSetting?.m_bait == null)
            {
                continue;
            }

            string existingName = StripCloneSuffix(baitSetting.m_bait.name);
            if (!string.Equals(existingName, rule.BaitName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            baits[i] = rule.ToBaitSetting();
            return;
        }

        baits.Add(rule.ToBaitSetting());
    }

    private static bool TryResolveBait(string baitName, out ItemDrop bait)
    {
        bait = null!;
        GameObject? itemPrefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(baitName) : null;
        if (itemPrefab == null && ZNetScene.instance != null)
        {
            itemPrefab = ZNetScene.instance.GetPrefab(baitName);
        }

        if (itemPrefab == null)
        {
            itemPrefab = FindKnownPrefabByName(baitName);
        }

        if (itemPrefab == null)
        {
            return false;
        }

        ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
        if (itemDrop == null)
        {
            return false;
        }

        bait = itemDrop;
        return true;
    }

    private static bool TryCreateInitialConfigurationFromLoadedFish()
    {
        if (ObjectDB.instance == null || ZNetScene.instance == null)
        {
            TrollingFishingPlugin.ModLogger.LogDebug("Fishing bait YAML generation is waiting for ObjectDB and ZNetScene.");
            return false;
        }

        SortedDictionary<string, SortedDictionary<string, float>> baits = new(StringComparer.OrdinalIgnoreCase);
        foreach (Fish fish in EnumerateKnownFishPrefabs())
        {
            if (fish == null)
            {
                continue;
            }

            string fishName = StripCloneSuffix(fish.gameObject.name);
            foreach (Fish.BaitSetting baitSetting in fish.m_baits)
            {
                if (baitSetting?.m_bait == null)
                {
                    continue;
                }

                string baitName = StripCloneSuffix(baitSetting.m_bait.name);
                if (string.IsNullOrWhiteSpace(baitName))
                {
                    continue;
                }

                if (!baits.TryGetValue(baitName, out SortedDictionary<string, float> fishChances))
                {
                    fishChances = new SortedDictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                    baits[baitName] = fishChances;
                }

                fishChances[fishName] = ClampChance(baitSetting.m_chance);
            }
        }

        string yaml = BuildInitialConfigurationYaml(baits);
        File.WriteAllText(FileFullPath, yaml);
        TrollingFishingPlugin.ModLogger.LogInfo(
            $"Created initial fishing bait YAML '{FileName}' from loaded fish prefabs. baitTypes={baits.Count}.");
        return true;
    }

    private static string BuildInitialConfigurationYaml(SortedDictionary<string, SortedDictionary<string, float>> baits)
    {
        StringBuilder builder = new();
        builder.AppendLine("# TrollingFishing bait map");
        builder.AppendLine("# This file was generated from the currently loaded vanilla and modded Fish components.");
        builder.AppendLine("# baits is bait prefab -> fish prefab -> Fish.BaitSetting.m_chance.");
        builder.AppendLine("#");
        builder.AppendLine("# Bite chance flow:");
        builder.AppendLine("# 1. A fish first tries to target a nearby fishing float using Fish.m_baseHookChance.");
        builder.AppendLine("#    TrollingFishing's Fishing skill bonus modifies this hook chance.");
        builder.AppendLine("# 2. If a float is selected, Valheim checks whether the float's bait is accepted by this fish.");
        builder.AppendLine("#    The numbers below are Fish.BaitSetting.m_chance values for this second bait check.");
        builder.AppendLine("#    1.0 means the fish always accepts this bait, 0.5 means 50%, and 0 means never.");
        builder.AppendLine("#");
        builder.AppendLine("# Example:");
        builder.AppendLine("# If \"Fishing Bite Chance Bonus Factor\" is 0.3 and your Fishing skill is 100,");
        builder.AppendLine("# the hook chance becomes 30%. If Fish1's bait chance for Bait1 is 0.5,");
        builder.AppendLine("# the total chance for Fish1 to bite that float is roughly 0.3 * 0.5 = 0.15, or 15%.");
        builder.AppendLine("#");
        builder.AppendLine("# These values are not item drop chances, fish spawn chances, or the skill-scaled hook chance itself.");
        builder.AppendLine("# Fish listed below use only the YAML entries below; unlisted fish keep their loaded defaults.");

        if (baits.Count == 0)
        {
            builder.AppendLine("baits: {}");
            return builder.ToString();
        }

        builder.AppendLine("baits:");
        foreach (KeyValuePair<string, SortedDictionary<string, float>> baitEntry in baits)
        {
            builder.Append("  ");
            builder.Append(FormatYamlKey(baitEntry.Key));
            builder.AppendLine(":");

            foreach (KeyValuePair<string, float> fishEntry in baitEntry.Value)
            {
                builder.Append("    ");
                builder.Append(FormatYamlKey(fishEntry.Key));
                builder.Append(": ");
                builder.AppendLine(fishEntry.Value.ToString("0.######", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static bool TryResolveFishPrefabName(string configuredFishName, out string fishName)
    {
        fishName = configuredFishName;
        if (TryResolveFishPrefab(configuredFishName, out Fish fish))
        {
            fishName = StripCloneSuffix(fish.gameObject.name);
            return true;
        }

        return false;
    }

    private static bool TryResolveFishPrefab(string fishName, out Fish fish)
    {
        fish = null!;
        GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(fishName) : null!;
        if (prefab != null)
        {
            fish = prefab.GetComponent<Fish>();
            if (fish != null)
            {
                return true;
            }
        }

        foreach (Fish knownFish in EnumerateKnownFishPrefabs())
        {
            if (!string.Equals(StripCloneSuffix(knownFish.gameObject.name), fishName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fish = knownFish;
            return true;
        }

        return false;
    }

    private static IEnumerable<Fish> EnumerateKnownFishPrefabs()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (GameObject prefab in EnumerateKnownPrefabs())
        {
            if (prefab == null || !seen.Add(StripCloneSuffix(prefab.name)))
            {
                continue;
            }

            Fish fish = prefab.GetComponent<Fish>();
            if (fish != null)
            {
                yield return fish;
            }
        }
    }

    private static GameObject? FindKnownPrefabByName(string prefabName)
    {
        foreach (GameObject prefab in EnumerateKnownPrefabs())
        {
            if (prefab != null &&
                string.Equals(StripCloneSuffix(prefab.name), prefabName, StringComparison.OrdinalIgnoreCase))
            {
                return prefab;
            }
        }

        return null;
    }

    private static IEnumerable<GameObject> EnumerateKnownPrefabs()
    {
        if (ZNetScene.instance != null)
        {
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab != null)
                {
                    yield return prefab;
                }
            }

            foreach (GameObject prefab in ZNetScene.instance.m_nonNetViewPrefabs)
            {
                if (prefab != null)
                {
                    yield return prefab;
                }
            }
        }

        if (ObjectDB.instance == null)
        {
            yield break;
        }

        foreach (GameObject prefab in ObjectDB.instance.m_items)
        {
            if (prefab != null)
            {
                yield return prefab;
            }
        }
    }

    private static float ClampChance(float chance)
    {
        if (float.IsNaN(chance) || float.IsInfinity(chance))
        {
            return 0f;
        }

        return Mathf.Clamp01(chance);
    }

    private static bool TryResolveItemPrefabName(ItemDrop.ItemData item, out string prefabName)
    {
        prefabName = string.Empty;
        if (item == null)
        {
            return false;
        }

        GameObject? prefab = item.m_dropPrefab;
        if (prefab == null && ObjectDB.instance != null)
        {
            ObjectDB.instance.TryGetItemPrefab(item.m_shared, out prefab);
        }

        prefabName = prefab != null ? StripCloneSuffix(prefab.name) : string.Empty;
        return !string.IsNullOrWhiteSpace(prefabName);
    }

    private static string LocalizeFishName(string fishName)
    {
        if (string.IsNullOrWhiteSpace(fishName) || Localization.instance == null)
        {
            return fishName;
        }

        string localized = Localization.instance.Localize(fishName);
        return localized.Contains('$') ? fishName : localized;
    }

    private static string FormatChancePercent(float chance)
    {
        float percent = ClampChance(chance) * 100f;
        string format = Math.Abs(percent - Math.Round(percent)) < 0.01f ? "0" : "0.##";
        return percent.ToString(format, CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatYamlKey(string key)
    {
        if (!string.IsNullOrEmpty(key) && IsPlainYamlKey(key))
        {
            return key;
        }

        return "'" + key.Replace("'", "''") + "'";
    }

    private static bool IsPlainYamlKey(string key)
    {
        foreach (char c in key)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string StripCloneSuffix(string name)
    {
        const string cloneSuffix = "(Clone)";
        return name.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - cloneSuffix.Length)
            : name;
    }

    private sealed class BaitConfigurationFile
    {
        [YamlMember(Alias = "baits")]
        public Dictionary<string, Dictionary<string, float>>? Baits { get; set; }
    }

    private readonly struct BaitRule
    {
        internal readonly string BaitName;
        private readonly ItemDrop _bait;
        private readonly float _chance;

        internal BaitRule(string baitName, ItemDrop bait, float chance)
        {
            BaitName = baitName;
            _bait = bait;
            _chance = chance;
        }

        internal Fish.BaitSetting ToBaitSetting()
        {
            return new Fish.BaitSetting
            {
                m_bait = _bait,
                m_chance = _chance
            };
        }
    }

    private readonly struct BaitTooltipEntry
    {
        internal readonly string FishName;
        internal readonly float Chance;

        internal BaitTooltipEntry(string fishName, float chance)
        {
            FishName = fishName;
            Chance = chance;
        }
    }

}

[HarmonyPatch(typeof(ObjectDB), "Awake")]
internal static class ObjectDBAwakeFishingBaitConfigurationPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        FishingBaitConfiguration.ReloadAndApply();
    }
}

[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class ZNetSceneAwakeFishingBaitConfigurationPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        FishingBaitConfiguration.ReloadAndApply();
    }
}

[HarmonyPatch(typeof(Fish), "OnEnable")]
internal static class FishOnEnableFishingBaitConfigurationPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Fish __instance)
    {
        FishingBaitConfiguration.ApplyToFishInstance(__instance);
    }
}
