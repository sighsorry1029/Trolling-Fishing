using System;
using System.Collections.Generic;
using System.Reflection;

public sealed class InventoryGui { public string Name = "test-gui"; }
public sealed class ItemDrop { public sealed class ItemData { public bool IsRod; } }

namespace UnityEngine { public class Object { } }
namespace UnityEngine.UI { }

namespace BepInEx.Bootstrap
{
    public sealed class PluginInfo { public object Instance = new PluginMarker(); }
    public sealed class PluginMarker { }
    public static class Chainloader
    {
        public static readonly Dictionary<string, PluginInfo> PluginInfos = new();
    }
}

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static readonly HashSet<string> Missing = new();
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        public static MethodInfo? DeclaredMethod(Type type, string name, Type[] parameters) =>
            Missing.Contains(name) ? null : type.GetMethod(name, Flags, null, parameters, null);
        public static FieldInfo? DeclaredField(Type type, string name) => Missing.Contains(name) ? null : type.GetField(name, Flags);
        public static MethodInfo? PropertyGetter(Type type, string name) =>
            Missing.Contains(name) ? null : type.GetProperty(name, Flags)?.GetGetMethod(true);
    }
}

namespace TrollingFishing
{
    public enum Toggle { Off, On }
    public static class ToggleExtensions { public static bool IsOff(this Toggle value) => value == Toggle.Off; }
    public sealed class Setting { public Toggle Value = Toggle.On; }
    public sealed class TestLogger
    {
        public readonly List<string> Warnings = new();
        public readonly List<string> Infos = new();
        public bool ThrowOnInfo;
        public void LogWarning(string text) => Warnings.Add(text);
        public void LogInfo(string text) { if (ThrowOnInfo) throw new InvalidOperationException("info observer failure"); Infos.Add(text); }
    }
    public static class TrollingFishingPlugin
    {
        internal const string ModGUID = "sighsorry.TrollingFishing";
        public static Setting? FishingRodBag = new();
        public static readonly TestLogger ModLogger = new();
    }
    public static class FishingOverrideSystem
    {
        public static bool IsFishingRod(ItemDrop.ItemData? item) => item?.IsRod ?? false;
    }
}

namespace EpicLoot
{
    public static class InventoryPromptHost
    {
        public static bool Open;
        public static bool Throw;
        public static bool IsOpen => Throw ? throw new InvalidOperationException("prompt failure") : Open;
    }
    public static class API
    {
        public static readonly Dictionary<string, Func<ItemDrop.ItemData, bool>> Registered = new();
        public static int RegisterCalls;
        public static int UnregisterCalls;
        public static bool RejectRegistration;
        public static bool InsertOtherThenReject;
        public static bool ThrowBeforeRegister;
        public static bool ThrowAfterRegister;
        public static bool ThrowOnUnregister;
        public static bool MissingProviderCategory;
        public static bool RegisterItemContainer(string id, Func<ItemDrop.ItemData, bool> predicate)
        {
            RegisterCalls++;
            if (ThrowBeforeRegister) throw new InvalidOperationException("before insert");
            if (InsertOtherThenReject) { Registered[id] = OtherOwner; return false; }
            if (RejectRegistration || Registered.ContainsKey(id)) return false;
            Registered.Add(id, predicate);
            if (ThrowAfterRegister) throw new InvalidOperationException("after insert notification");
            return true;
        }
        public static bool UnregisterItemContainer(string id)
        {
            UnregisterCalls++;
            if (ThrowOnUnregister) throw new InvalidOperationException("unregister failure");
            return Registered.Remove(id);
        }
        public static Dictionary<string, List<string>> GetRegisteredProviders() => MissingProviderCategory ? new() :
            new() { ["ItemContainer"] = new List<string>(Registered.Keys) };
        public static bool OtherOwner(ItemDrop.ItemData item) => true;
        public static void Reset()
        {
            Registered.Clear(); RegisterCalls = UnregisterCalls = 0;
            RejectRegistration = InsertOtherThenReject = ThrowBeforeRegister = ThrowAfterRegister = ThrowOnUnregister = MissingProviderCategory = false;
        }
    }
}

namespace EpicLoot.ShardStones
{
    public sealed class ShardSlotPanel { public bool Focus; public bool HasFocus => Focus; }
    public static class ShardUI
    {
        private static ShardSlotPanel? _panel;
        private static bool _open;
        public static bool IsOpen => _open && _panel != null;
        public static void Configure(bool open, bool focus) { _open = open; _panel = open ? new ShardSlotPanel { Focus = focus } : null; }
    }
    public static class SocketOverlayInput
    {
        public static bool Pressed;
        public static bool Gamepad;
        public static bool Throw;
        public static InventoryGui? LastGui;
        public static int Calls;
        public static bool IsShardSlotPress(InventoryGui gui)
        {
            Calls++; LastGui = gui;
            if (Throw) throw new InvalidOperationException("shard decision failure");
            return Pressed;
        }
        internal static bool GamepadPressed() => Gamepad;
    }
}
