using System.Reflection;
using BepInEx.Bootstrap;
using EpicLoot;
using EpicLoot.ShardStones;
using HarmonyLib;
using TrollingFishing;

internal static class Program
{
    private static readonly InventoryGui Gui = new();
    private static readonly ItemDrop.ItemData Rod = new() { IsRod = true };
    private const string Id = TrollingFishingPlugin.ModGUID;
    private static int checks;

    private static void Main()
    {
        Fresh(false);
        EpicLootBagCompatibility.Initialize();
        Check(!EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.RegisterCalls == 0, "EpicLoot absent: normal bag input remains available");

        Fresh();
        EpicLootBagCompatibility.Initialize();
        Check(API.Registered.ContainsKey(Id) && API.RegisterCalls == 1, "registers exactly one stable mod GUID");
        Func<ItemDrop.ItemData, bool> callback = API.Registered[Id];
        Check(callback(Rod) && !callback(new ItemDrop.ItemData()) && !callback(null!), "registered callback classifies only fishing rods");
        TrollingFishingPlugin.FishingRodBag!.Value = Toggle.Off;
        Check(!callback(Rod), "configuration Off immediately disables the cached predicate");
        TrollingFishingPlugin.FishingRodBag.Value = Toggle.On;
        Check(callback(Rod), "configuration On restores the same predicate");
        TrollingFishingPlugin.FishingRodBag = null;
        Check(!callback(Rod), "uninitialized configuration does not claim containers");
        TrollingFishingPlugin.FishingRodBag = new Setting();
        Check(!EpicLootBagCompatibility.ShouldYieldUse(Gui) && ReferenceEquals(SocketOverlayInput.LastGui, Gui), "idle input stays available and forwards the actual GUI");
        SocketOverlayInput.Pressed = true;
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui), "current shard decision yields the bag gesture");
        SocketOverlayInput.Pressed = false;
        InventoryPromptHost.Open = true;
        int before = SocketOverlayInput.Calls;
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && SocketOverlayInput.Calls == before, "open prompt yields before evaluating shard input");
        InventoryPromptHost.Open = false;
        ShardUI.Configure(true, true); SocketOverlayInput.Gamepad = true;
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui), "focused shard panel close gesture yields even when GUI helper is false");
        ShardUI.Configure(true, false);
        Check(!EpicLootBagCompatibility.ShouldYieldUse(Gui), "unfocused shard panel does not capture unrelated gamepad input");
        ShardUI.Configure(false, false);
        Check(!EpicLootBagCompatibility.ShouldYieldUse(Gui), "closed panel safely short-circuits its null private panel");
        EpicLootBagCompatibility.Reset();
        Check(!API.Registered.ContainsKey(Id) && API.UnregisterCalls == 1 && !callback(Rod), "Reset unregisters and deactivates a retained callback");
        EpicLootBagCompatibility.Reset();
        Check(API.UnregisterCalls == 1, "repeated Reset is idempotent");

        foreach (string missing in new[] { "IsOpen", "IsShardSlotPress", "_panel", "HasFocus", "GamepadPressed", "RegisterItemContainer", "UnregisterItemContainer", "GetRegisteredProviders" })
        {
            Fresh(); AccessTools.Missing.Add(missing);
            EpicLootBagCompatibility.Initialize();
            Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.RegisterCalls == 0 && API.Registered.Count == 0,
                "missing contract " + missing + " fails closed before registration");
        }

        Fresh(); API.MissingProviderCategory = true;
        EpicLootBagCompatibility.Initialize();
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.RegisterCalls == 0, "unrecognized provider snapshot fails closed");
        Fresh(); API.Registered[Id] = API.OtherOwner;
        EpicLootBagCompatibility.Initialize();
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.UnregisterCalls == 0 && API.Registered[Id] == API.OtherOwner,
            "pre-existing stable id is preserved and not silently replaced");
        Fresh(); API.InsertOtherThenReject = true;
        EpicLootBagCompatibility.Initialize();
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.UnregisterCalls == 0 && API.Registered[Id] == API.OtherOwner,
            "registration returning false never unregisters another owner's entry");
        Fresh(); API.ThrowAfterRegister = true;
        EpicLootBagCompatibility.Initialize();
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.Registered.Count == 0 && API.UnregisterCalls == 1,
            "notification failure after committed registration rolls back the owned callback");
        Fresh(); API.ThrowBeforeRegister = true;
        EpicLootBagCompatibility.Initialize();
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.Registered.Count == 0, "exception before registration remains fail closed");

        Fresh(); EpicLootBagCompatibility.Initialize();
        callback = API.Registered[Id]; SocketOverlayInput.Throw = true;
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.Registered.Count == 0 && !callback(Rod), "runtime input failure disables and unregisters the callback");
        int warnings = TrollingFishingPlugin.ModLogger.Warnings.Count;
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && TrollingFishingPlugin.ModLogger.Warnings.Count == warnings,
            "disabled input does not retry failing delegates every frame");

        Fresh(); EpicLootBagCompatibility.Initialize(); callback = API.Registered[Id]; API.ThrowOnUnregister = true;
        EpicLootBagCompatibility.Reset();
        Check(!callback(Rod) && API.Registered.ContainsKey(Id), "cleanup exception leaves retained callback inactive");
        EpicLootBagCompatibility.Initialize();
        Check(EpicLootBagCompatibility.ShouldYieldUse(Gui) && API.RegisterCalls == 1, "pending cleanup prevents duplicate initialization");
        API.ThrowOnUnregister = false;
        EpicLootBagCompatibility.Reset();
        Check(API.Registered.Count == 0, "later Reset retries retained cleanup successfully");
        EpicLootBagCompatibility.Initialize();
        Check(API.Registered.ContainsKey(Id) && API.RegisterCalls == 2, "initialization can recover after cleanup succeeds");

        Fresh(); EpicLootBagCompatibility.Initialize();
        EpicLootBagCompatibility.Initialize();
        Check(API.Registered.Count == 1 && API.RegisterCalls == 2 && API.UnregisterCalls == 1, "reinitialization unregisters before replacing its owned callback");
        Fresh();
        Console.WriteLine("PASS " + checks + " source-linked adapter checks. Environment doubles are used; no Unity GUI input is simulated.");
    }

    private static void Fresh(bool epic = true)
    {
        API.ThrowOnUnregister = false;
        EpicLootBagCompatibility.Reset();
        API.Reset(); AccessTools.Missing.Clear(); Chainloader.PluginInfos.Clear();
        TrollingFishingPlugin.FishingRodBag = new Setting();
        TrollingFishingPlugin.ModLogger.Warnings.Clear(); TrollingFishingPlugin.ModLogger.Infos.Clear(); TrollingFishingPlugin.ModLogger.ThrowOnInfo = false;
        InventoryPromptHost.Open = InventoryPromptHost.Throw = false;
        SocketOverlayInput.Pressed = SocketOverlayInput.Gamepad = SocketOverlayInput.Throw = false;
        SocketOverlayInput.LastGui = null; SocketOverlayInput.Calls = 0; ShardUI.Configure(false, false);
        if (epic) Chainloader.PluginInfos.Add(EpicLootBagCompatibility.PluginGuid, new PluginInfo());
    }
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException("FAIL " + description);
        checks++; Console.WriteLine("PASS " + description);
    }
}
