using System;
using System.Linq;
using System.Reflection;

namespace TrollingFishing;

internal static class AzuCraftyBoxesCompat
{
    private const string AzuCraftyBoxesAssemblyName = "AzuCraftyBoxes";
    private static bool _initialized;
    private static bool _loaded;
    private static bool _loggedAddFailure;
    private static bool _loggedRemoveFailure;
    private static bool _loggedAggressiveUnavailable;
    private static bool _loggedAggressiveFailure;
    private static bool _aggressiveRefreshFailed;
    private static MethodInfo? _addContainerMethod;
    private static MethodInfo? _removeContainerMethod;
    private static MethodInfo? _updateContainersMethod;
    private static FieldInfo? _queryFrameIdField;
    private static FieldInfo? _lastQueryTimeField;

    internal static bool IsLoaded()
    {
        return TryEnsureLoaded();
    }

    internal static void AddContainer(Container container)
    {
        if (container == null ||
            TrollingFishingPlugin.FishingRodBagAzuCraftyBoxesCompatibility.Value.IsOff() ||
            !TryEnsureLoaded())
        {
            return;
        }

        try
        {
            _addContainerMethod?.Invoke(null, new object[] { container });
        }
        catch (Exception ex)
        {
            if (_loggedAddFailure)
            {
                return;
            }

            _loggedAddFailure = true;
            TrollingFishingPlugin.ModLogger.LogWarning($"Could not register fishing rod bag with AzuCraftyBoxes: {ex.GetBaseException().Message}");
            return;
        }

        TryAggressiveRefresh();
    }

    internal static void RemoveContainer(Container container)
    {
        if (container == null || !TryEnsureLoaded())
        {
            return;
        }

        try
        {
            _removeContainerMethod?.Invoke(null, new object[] { container });
        }
        catch (Exception ex)
        {
            if (_loggedRemoveFailure)
            {
                return;
            }

            _loggedRemoveFailure = true;
            TrollingFishingPlugin.ModLogger.LogWarning($"Could not unregister fishing rod bag from AzuCraftyBoxes: {ex.GetBaseException().Message}");
            return;
        }

        TryAggressiveRefresh();
    }

    private static bool TryEnsureLoaded()
    {
        if (_initialized)
        {
            return _loaded;
        }

        _initialized = true;
        Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, AzuCraftyBoxesAssemblyName, StringComparison.OrdinalIgnoreCase));
        Type? apiType = assembly?.GetType("AzuCraftyBoxes.API");
        _addContainerMethod = apiType?.GetMethod(
            "AddContainer",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Container) },
            null);
        _removeContainerMethod = apiType?.GetMethod(
            "RemoveContainer",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Container) },
            null);
        Type? boxesType = assembly?.GetType("AzuCraftyBoxes.Util.Functions.Boxes");
        _updateContainersMethod = boxesType?.GetMethod("UpdateContainers", BindingFlags.Static | BindingFlags.NonPublic);
        Type? queryFrameType = assembly?.GetType("AzuCraftyBoxes.Util.Functions.Boxes+QueryFrame");
        _queryFrameIdField = queryFrameType?.GetField("FrameId", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _lastQueryTimeField = boxesType?.GetField("_lastQueryTime", BindingFlags.Static | BindingFlags.NonPublic);
        _loaded = _addContainerMethod != null && _removeContainerMethod != null;
        if (_loaded)
        {
            TrollingFishingPlugin.ModLogger.LogInfo("AzuCraftyBoxes compatibility enabled for fishing rod bags.");
        }

        return _loaded;
    }

    private static void TryAggressiveRefresh()
    {
        if (TrollingFishingPlugin.FishingRodBagAzuCraftyBoxesAggressiveRefresh.Value.IsOff() ||
            _aggressiveRefreshFailed)
        {
            return;
        }

        if (_updateContainersMethod == null &&
            _queryFrameIdField == null &&
            _lastQueryTimeField == null)
        {
            if (!_loggedAggressiveUnavailable)
            {
                _loggedAggressiveUnavailable = true;
                TrollingFishingPlugin.ModLogger.LogWarning("AzuCraftyBoxes aggressive refresh is enabled, but private refresh hooks were not found. Falling back to public API only.");
            }

            return;
        }

        try
        {
            _updateContainersMethod?.Invoke(null, Array.Empty<object>());
            _queryFrameIdField?.SetValue(null, -1);
            _lastQueryTimeField?.SetValue(null, -999f);
        }
        catch (Exception ex)
        {
            _aggressiveRefreshFailed = true;
            if (_loggedAggressiveFailure)
            {
                return;
            }

            _loggedAggressiveFailure = true;
            TrollingFishingPlugin.ModLogger.LogWarning($"AzuCraftyBoxes aggressive refresh failed and was disabled for this session; public API compatibility remains active: {ex.GetBaseException().Message}");
        }
    }
}
