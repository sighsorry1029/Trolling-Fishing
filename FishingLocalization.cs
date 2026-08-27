using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using YamlDotNet.Serialization;

namespace TrollingFishing;

// Localization loading is adapted from AzumattDev/LocalizationManager.
internal static class FishingLocalization
{
    internal const string FishingRodBagOpenHintKey = "$tf_fishingrod_bag_open_hint";
    internal const string FishingRodMultiLineHintKey = "$tf_fishingrod_multi_line_hint";
    internal const string FishingBaitTooltipHeaderKey = "$tf_fishing_bait_tooltip_header";
    internal const string FishingSkillTooltipHeadingKey = "$tf_skill_fishing_heading";
    internal const string FishingSkillTooltipBiteChanceKey = "$tf_skill_fishing_bite_chance";
    internal const string FishingSkillTooltipExtraDropsKey = "$tf_skill_fishing_extra_drops";
    internal const string FishingSkillTooltipBagSlotsKey = "$tf_skill_fishing_bag_slots";
    internal const string FishingSkillTooltipBagWeightKey = "$tf_skill_fishing_bag_weight";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreFields()
        .Build();
    private static readonly string[] FileExtensions = { ".json", ".yml" };
    private static BaseUnityPlugin? _plugin;

    private static BaseUnityPlugin Plugin =>
        _plugin ?? throw new InvalidOperationException("TrollingFishing localization is not initialized.");

    internal static void Initialize(BaseUnityPlugin plugin)
    {
        _plugin = plugin;
    }

    internal static void Shutdown()
    {
        _plugin = null;
    }

    internal static void LoadLocalizationLater()
    {
        if (Localization.instance != null)
        {
            LoadLocalization(Localization.instance, Localization.instance.GetSelectedLanguage());
        }
    }

    internal static void LoadLocalization(Localization localization, string language)
    {
        if (localization == null)
        {
            return;
        }

        Dictionary<string, string> localizationFiles = FindExternalLocalizationFiles();
        Dictionary<string, string> localizationTexts = LoadEmbeddedLocalization("English")
            ?? throw new InvalidOperationException(
                $"Found no English localizations in mod {Plugin.Info.Metadata.Name}. " +
                "Expected an embedded translations/English.json or translations/English.yml resource.");

        string? localizationData = null;
        if (!language.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            if (localizationFiles.TryGetValue(language, out string externalLanguageFile))
            {
                localizationData = File.ReadAllText(externalLanguageFile);
            }
            else
            {
                localizationData = ReadEmbeddedLocalizationText(language);
            }
        }

        if (localizationData == null && localizationFiles.TryGetValue("English", out string externalEnglishFile))
        {
            localizationData = File.ReadAllText(externalEnglishFile);
        }

        if (localizationData != null)
        {
            foreach (KeyValuePair<string, string> entry in Deserialize(localizationData))
            {
                localizationTexts[entry.Key] = entry.Value;
            }
        }

        foreach (KeyValuePair<string, string> entry in localizationTexts)
        {
            localization.AddWord(entry.Key, entry.Value);
        }
    }

    internal static string Localize(string key)
    {
        if (Localization.instance == null)
        {
            return key;
        }

        string localized = Localization.instance.Localize(key);
        return localized.Contains('$') ? Localization.instance.Localize(localized) : localized;
    }

    internal static string Format(string key, params object[] args)
    {
        string format = Localize(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException exception)
        {
            TrollingFishingPlugin.ModLogger.LogWarning(
                $"Could not format localization key {key}: {exception.GetBaseException().Message}");
            return format;
        }
    }

    private static Dictionary<string, string> FindExternalLocalizationFiles()
    {
        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
        string pattern = $"{Plugin.Info.Metadata.Name}.*";
        foreach (string file in Directory.GetFiles(Paths.BepInExRootPath, pattern, SearchOption.AllDirectories)
                     .Where(file => FileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
        {
            string[] parts = Path.GetFileNameWithoutExtension(file).Split('.');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                continue;
            }

            string language = parts[1];
            if (!files.TryAdd(language, file))
            {
                TrollingFishingPlugin.ModLogger.LogWarning(
                    $"Duplicate TrollingFishing localization for {language} found at {file}; the duplicate was skipped.");
            }
        }

        return files;
    }

    private static Dictionary<string, string>? LoadEmbeddedLocalization(string language)
    {
        string? text = ReadEmbeddedLocalizationText(language);
        return text == null ? null : Deserialize(text);
    }

    private static string? ReadEmbeddedLocalizationText(string language)
    {
        foreach (string extension in FileExtensions)
        {
            byte[]? data = ReadEmbeddedFileBytes($"translations.{language}{extension}");
            if (data != null)
            {
                return Encoding.UTF8.GetString(data);
            }
        }

        return null;
    }

    private static Dictionary<string, string> Deserialize(string text)
    {
        return Deserializer.Deserialize<Dictionary<string, string>?>(text)
               ?? new Dictionary<string, string>();
    }

    private static byte[]? ReadEmbeddedFileBytes(string resourceFileName)
    {
        Assembly assembly = typeof(FishingLocalization).Assembly;
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(resourceFileName, StringComparison.Ordinal));
        if (resourceName == null)
        {
            return null;
        }

        using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
        if (resourceStream == null)
        {
            return null;
        }

        using MemoryStream output = new();
        resourceStream.CopyTo(output);
        return output.Length == 0 ? null : output.ToArray();
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class LocalizationSetupLanguageFishingPatch
{
    private static void Postfix(Localization __instance, string language)
    {
        FishingLocalization.LoadLocalization(__instance, language);
    }
}

[HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.SetupGui))]
internal static class LocalizationSetupGuiFishingPatch
{
    private static void Postfix()
    {
        FishingLocalization.LoadLocalizationLater();
    }
}
