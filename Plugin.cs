using System;
using System.IO;
using System.Reflection;
using System.Timers;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace TrollingFishing;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInDependency("WackyMole.ItemRequiresSkillLevel", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("Detalhes.ItemRequiresSkillLevel", BepInDependency.DependencyFlags.SoftDependency)]
public class TrollingFishingPlugin : BaseUnityPlugin
{
    internal const string ModName = "TrollingFishing";
    internal const string ModVersion = "1.0.8";
    internal const string Author = "sighsorry";
    internal const int FishingRodMultiLineMinCount = 2;
    internal const int FishingRodMultiLineMaxCount = 10;
    private const string ModGUID = $"{Author}.{ModName}";
    private static readonly string ConfigFileName = $"{ModGUID}.cfg";
    private static readonly string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    internal static string ConnectionError = "";
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    internal static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    internal static ConfigEntry<float> FishingOverrideBiteChanceBonusFactor = null!;
    internal static ConfigEntry<float> FishingOverrideExtraDropChanceBonusFactor = null!;
    internal static ConfigEntry<Toggle> FishingRodBag = null!;
    internal static ConfigEntry<Toggle> FishingRodMultiLine = null!;
    internal static ConfigEntry<int> FishingRodMultiLineCount = null!;
    internal static ConfigEntry<float> FishingRodMultiLineCastResourceFactor = null!;
    internal static ConfigEntry<float> FishingRodMultiLineSpreadAngle = null!;
    internal static ConfigEntry<float> FishingRodMultiLineExtraPullStaminaFactor = null!;
    internal static ConfigEntry<float> FishingRodMultiLineSkillRaiseFactor = null!;
    internal static ConfigEntry<Toggle> FishingRodBagScalesWithFishingSkill = null!;
    internal static ConfigEntry<Toggle> FishingRodBagCountsWeight = null!;
    internal static ConfigEntry<int> FishingRodBagWeightAtMaxSkillPercent = null!;
    internal static ConfigEntry<Toggle> FishingRodBagAzuCraftyBoxesCompatibility = null!;
    internal static ConfigEntry<Toggle> FishingRodBagAzuCraftyBoxesAggressiveRefresh = null!;
    internal static ConfigEntry<Toggle> FishingDebugLogging = null!;

    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _baitConfigWatcher;
    private readonly object _reloadLock = new();
    private DateTime _lastConfigReloadTime;
    private DateTime _lastBaitConfigReloadTime;
    private string? _lastConfigFileText;
    private string? _lastBaitConfigFileText;
    private const long ReloadDelayTicks = 10000000;

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    public void Awake()
    {
        bool saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;

        _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
        FishingDebugLogging = config("1 - General", "Fishing Debug Logging", Toggle.Off, "If on, logs verbose fishing diagnostics such as multi-line float setup, water hits, bait reservations, and bag bait selection. Leave off for normal play.", synchronizedSetting: false);

        FishingOverrideBiteChanceBonusFactor = config("2 - Fishing Skill", "Fishing Bite Chance Bonus Factor", 0.3f, new ConfigDescription("Target bite chance at Fishing skill 100. Fish hook chance scales from vanilla baseHookChance at Fishing 0 toward max(baseHookChance, this value) at Fishing 100. Vanilla base chance is usually 0.10, so 0.30 makes eligible fish bite at up to 30% at Fishing 100 and 1.00 makes eligible fish always bite at Fishing 100.", new AcceptableValueRange<float>(0.1f, 1f)), synchronizedSetting: true);
        FishingOverrideExtraDropChanceBonusFactor = config("2 - Fishing Skill", "Fishing Extra Drop Chance Bonus Factor", 1f, new ConfigDescription("Extra-drop chance multiplier at Fishing skill 100. Final chance = vanillaChance * (1 + factor * FishingSkillFactor). 0 keeps vanilla, 1 changes a 10% drop to 20% at Fishing 100, and 4 changes it to 50%.", new AcceptableValueRange<float>(0f, 4f)), synchronizedSetting: true);

        FishingRodBag = config("3 - Fishing Rod Bag", "Fishing Rod Bag", Toggle.On, "If on, pressing the Use key while hovering FishingRod in the player inventory opens an item-bound bag that accepts fish pickup items, fishing bait, and supported fish chum items.", synchronizedSetting: true);
        FishingRodBagScalesWithFishingSkill = config("3 - Fishing Rod Bag", "Fishing Rod Bag Scales With Fishing Skill", Toggle.On, "If on, FishingRod bag size unlocks one tier per 10 Fishing levels: 4x2, 4x4, 6x4, then 8x4 through 8x10. If off, FishingRod bags use a fixed 8x4 size. If the bag shrinks, overflow items remain stored on the rod and reappear when enough slots are available again.", synchronizedSetting: true);
        FishingRodBagCountsWeight = config("3 - Fishing Rod Bag", "Fishing Rod Bag Counts Weight", Toggle.On, "If on, items stored inside FishingRod bags count toward the player's inventory weight while the rod is in the player's inventory.", synchronizedSetting: true);
        FishingRodBagWeightAtMaxSkillPercent = config("3 - Fishing Rod Bag", "Fishing Rod Bag Weight At Fishing 100 Percent", 50, new ConfigDescription("FishingRod bag item weight percent at Fishing skill 100. Fishing skill linearly scales bag item weight from 100% at skill 0 to this value at skill 100. 0 makes bag contents weightless at Fishing 100; 100 keeps full weight.", new AcceptableValueRange<int>(0, 100)), synchronizedSetting: true);
        FishingRodBagAzuCraftyBoxesCompatibility = config("3 - Fishing Rod Bag", "Fishing Rod Bag AzuCraftyBoxes Compatibility", Toggle.On, "If on and AzuCraftyBoxes is installed, FishingRod bags in the player's inventory are exposed as nearby containers for crafting pulls. FishingRods stored inside external chests are not recursively exposed.", synchronizedSetting: true);
        FishingRodBagAzuCraftyBoxesAggressiveRefresh = config("3 - Fishing Rod Bag", "Fishing Rod Bag AzuCraftyBoxes Aggressive Refresh", Toggle.On, "If on, AzuCraftyBoxes integration also refreshes its private container registry/cache immediately after FishingRod bag proxy changes. If off, only the public AzuCraftyBoxes API is used, which is safer across AzuCraftyBoxes updates but may refresh later.", synchronizedSetting: true);

        FishingRodMultiLine = config("4 - Multi Line Fishing", "Fishing Rod Multi Line", Toggle.On, "If on, FishingRod secondary casts multiple fishing floats. If off, the fishing secondary attack is blocked before ammo/resource use.", synchronizedSetting: true);
        FishingRodMultiLineCount = config("4 - Multi Line Fishing", "Fishing Rod Multi Line Count", 3, new ConfigDescription("Number of fishing floats fired by FishingRod secondary while Fishing Rod Multi Line is on. Higher values create more networked fishing floats, so the range is capped for multiplayer stability.", new AcceptableValueRange<int>(FishingRodMultiLineMinCount, FishingRodMultiLineMaxCount)), synchronizedSetting: true);
        FishingRodMultiLineCastResourceFactor = config("4 - Multi Line Fishing", "Fishing Rod Multi Line Cast Resource Factor", 2f, new ConfigDescription("Multiplier applied to the initial cast/draw stamina, eitr, and health costs for the multi-line secondary cast. Reeling costs are controlled by Extra Pull Stamina Factor.", new AcceptableValueRange<float>(0f, 5f)), synchronizedSetting: true);
        FishingRodMultiLineSpreadAngle = config("4 - Multi Line Fishing", "Fishing Rod Multi Line Spread Angle", 30f, new ConfigDescription("Total horizontal spread angle in degrees across all floats fired by multi-line fishing.", new AcceptableValueRange<float>(0f, 90f)), synchronizedSetting: true);
        FishingRodMultiLineExtraPullStaminaFactor = config("4 - Multi Line Fishing", "Fishing Rod Multi Line Extra Pull Stamina Factor", 0.5f, new ConfigDescription("Multiplier applied to stamina consumed while reeling each additional multi-line fishing float. The primary-equivalent float always uses vanilla stamina cost. With 3 floats and 0.5, total reel stamina is roughly 1 + 2 * 0.5 = 2x vanilla.", new AcceptableValueRange<float>(0f, 5f)), synchronizedSetting: true);
        FishingRodMultiLineSkillRaiseFactor = config("4 - Multi Line Fishing", "Fishing Rod Multi Line Extra Skill Raise Factor", 0.5f, new ConfigDescription("Multiplier applied to Fishing skill progress while reeling each additional multi-line fishing float. The primary-equivalent float always uses vanilla skill progress. With 3 floats and 0.5, total reel skill progress is roughly 1 + 2 * 0.5 = 2x vanilla.", new AcceptableValueRange<float>(0f, 5f)), synchronizedSetting: true);

        Assembly assembly = Assembly.GetExecutingAssembly();
        _harmony.PatchAll(assembly);
        FishingLocalization.Register();
        SetupWatcher();

        Config.Save();
        _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
        _lastBaitConfigFileText = ReadFileTextIfExists(GetBaitConfigFileFullPath());
        if (saveOnSet)
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    private void OnDestroy()
    {
        SaveWithRespectToConfigSet();
        _watcher?.Dispose();
        _baitConfigWatcher?.Dispose();
        _harmony.UnpatchSelf();
    }

    private void SetupWatcher()
    {
        _watcher = new FileSystemWatcher(Paths.ConfigPath, ConfigFileName);
        _watcher.Changed += ReadConfigValues;
        _watcher.Created += ReadConfigValues;
        _watcher.Renamed += ReadConfigValues;
        _watcher.IncludeSubdirectories = true;
        _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _watcher.EnableRaisingEvents = true;

        FishingBaitConfiguration.EnsureConfigDirectory();
        _baitConfigWatcher = new FileSystemWatcher(Paths.ConfigPath, FishingBaitConfiguration.FileName);
        _baitConfigWatcher.Changed += ReadBaitConfigValues;
        _baitConfigWatcher.Created += ReadBaitConfigValues;
        _baitConfigWatcher.Deleted += ReadBaitConfigValues;
        _baitConfigWatcher.Renamed += ReadBaitConfigValues;
        _baitConfigWatcher.IncludeSubdirectories = false;
        _baitConfigWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _baitConfigWatcher.EnableRaisingEvents = true;
    }

    private void ReadConfigValues(object sender, FileSystemEventArgs e)
    {
        DateTime now = DateTime.Now;
        long time = now.Ticks - _lastConfigReloadTime.Ticks;
        if (time < ReloadDelayTicks)
        {
            return;
        }

        lock (_reloadLock)
        {
            if (!File.Exists(ConfigFileFullPath))
            {
                ModLogger.LogWarning("Config file does not exist. Skipping reload.");
                return;
            }

            try
            {
                string configFileText = File.ReadAllText(ConfigFileFullPath);
                if (string.Equals(_lastConfigFileText, configFileText, StringComparison.Ordinal))
                {
                    return;
                }

                ModLogger.LogDebug("Reloading configuration...");
                SaveWithRespectToConfigSet(true);
                _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
                ModLogger.LogInfo("Configuration reload complete.");
            }
            catch (Exception ex)
            {
                ModLogger.LogError($"Error reloading configuration: {ex.Message}");
            }
        }

        _lastConfigReloadTime = now;
    }

    private void ReadBaitConfigValues(object sender, FileSystemEventArgs e)
    {
        DateTime now = DateTime.Now;
        long time = now.Ticks - _lastBaitConfigReloadTime.Ticks;
        if (time < ReloadDelayTicks)
        {
            return;
        }

        lock (_reloadLock)
        {
            try
            {
                string baitConfigPath = GetBaitConfigFileFullPath();
                string? baitConfigText = ReadFileTextIfExists(baitConfigPath);
                if (string.Equals(_lastBaitConfigFileText, baitConfigText, StringComparison.Ordinal))
                {
                    return;
                }

                ModLogger.LogDebug("Reloading fishing bait YAML...");
                FishingBaitConfiguration.ReloadAndApply();
                _lastBaitConfigFileText = ReadFileTextIfExists(baitConfigPath);
                ModLogger.LogInfo("Fishing bait YAML reload complete.");
            }
            catch (Exception ex)
            {
                ModLogger.LogError($"Error reloading fishing bait YAML: {ex.Message}");
            }
        }

        _lastBaitConfigReloadTime = now;
    }

    private static string GetBaitConfigFileFullPath()
    {
        return Path.Combine(Paths.ConfigPath, FishingBaitConfiguration.FileName);
    }

    private static string? ReadFileTextIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void SaveWithRespectToConfigSet(bool reload = false)
    {
        bool originalSaveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        if (reload)
        {
            Config.Reload();
        }

        Config.Save();
        if (originalSaveOnSet)
        {
            Config.SaveOnConfigSet = originalSaveOnSet;
        }
    }

    internal static void LogDebug(string message)
    {
        if (FishingDebugLogging != null && FishingDebugLogging.Value.IsOn())
        {
            ModLogger.LogInfo(message);
        }
    }

    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);
        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    #endregion
}

public static class ToggleExtentions
{
    extension(TrollingFishingPlugin.Toggle value)
    {
        public bool IsOn()
        {
            return value == TrollingFishingPlugin.Toggle.On;
        }

        public bool IsOff()
        {
            return value == TrollingFishingPlugin.Toggle.Off;
        }
    }
}
