using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

// Original EpicLoot DLL only: no rewritten/publicized assembly, Unity scene or input simulation.
internal static class EpicLootContractProbe
{
    private const BindingFlags AllStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static string[] directories;
    private static string pluginRoot;
    private static object acceptedItem;
    private static int callbackCalls;
    private static int checks;

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            System.Console.Error.WriteLine("Usage: probe <original EpicLoot.dll> [Valheim game directory] [final TrollingFishing.dll]");
            return 2;
        }
        string epicPath = Path.GetFullPath(args[0]);
        string game = args.Length > 1 ? args[1] : @"C:\Program Files (x86)\Steam\steamapps\common\Valheim";
        directories = new[] { Path.GetDirectoryName(epicPath), Path.Combine(game, "BepInEx", "core"), Path.Combine(game, "valheim_Data", "Managed") };
        pluginRoot = Path.GetDirectoryName(Path.GetDirectoryName(epicPath));
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            Run(Assembly.LoadFrom(epicPath), args.Length > 2 ? Path.GetFullPath(args[2]) : null);
            System.Console.WriteLine("PASS " + checks + " original EpicLoot metadata/managed registry checks.");
            System.Console.WriteLine("No EpicLoot plugin Awake, Unity scene, GUI input or actual item inventory was executed.");
            return 0;
        }
        catch (Exception error)
        {
            System.Console.Error.WriteLine(error);
            return 1;
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= Resolve; }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs request)
    {
        string file = new AssemblyName(request.Name).Name + ".dll";
        foreach (string directory in directories)
        {
            string path = Path.Combine(directory, file);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        string[] installed = Directory.GetFiles(pluginRoot, file, SearchOption.AllDirectories);
        string exact = installed.FirstOrDefault(path => AssemblyName.GetAssemblyName(path).FullName == request.Name);
        if (exact != null) return Assembly.LoadFrom(exact);
        if (installed.Length == 1) return Assembly.LoadFrom(installed[0]);
        return null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(Assembly epic, string modPath)
    {
        Type plugin = epic.GetType("EpicLoot.EpicLoot", true);
        var metadata = CustomAttributeData.GetCustomAttributes(plugin).Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)metadata.ConstructorArguments[0].Value == "randyknapp.mods.epicloot", "actual BepInPlugin GUID");
        Check((string)metadata.ConstructorArguments[2].Value == "0.15.6", "supplied BepInPlugin version 0.15.6");
        Type gameItem = Assembly.Load("assembly_valheim").GetType("ItemDrop+ItemData", true);
        Type gui = Assembly.Load("assembly_valheim").GetType("InventoryGui", true);
        MethodInfo prompt = Require(epic.GetType("EpicLoot.InventoryPromptHost", true), "get_IsOpen", typeof(bool));
        MethodInfo press = Require(epic.GetType("EpicLoot.ShardStones.SocketOverlayInput", true), "IsShardSlotPress", typeof(bool), gui);
        Delegate.CreateDelegate(typeof(Func<bool>), prompt);
        Type shardDelegate = typeof(Func<,>).MakeGenericType(gui, typeof(bool));
        Delegate.CreateDelegate(shardDelegate, press);
        Check(true, "actual prompt and shard-input delegates bind with exact signatures");
        Type api = epic.GetType("EpicLoot.API", true);
        Type callbackType = typeof(Func<,>).MakeGenericType(gameItem, typeof(bool));
        MethodInfo register = Require(api, "RegisterItemContainer", typeof(bool), typeof(string), callbackType);
        MethodInfo unregister = Require(api, "UnregisterItemContainer", typeof(bool), typeof(string));
        MethodInfo contains = Require(api, "IsRegisteredItemContainer", typeof(bool), gameItem);
        MethodInfo foreign = Require(epic.GetType("EpicLoot.Compatibility.ForeignItemContainers", true), "HasContainer", typeof(bool), gameItem);
        MethodInfo providers = Require(api, "GetRegisteredProviders", typeof(Dictionary<string, List<string>>));
        Check(register.IsPublic && unregister.IsPublic, "container registration and unregister are public APIs");
        acceptedItem = Activator.CreateInstance(gameItem);
        Delegate callback = Delegate.CreateDelegate(callbackType, typeof(EpicLootContractProbe).GetMethod("Accept", AllStatic));
        Delegate reject = Delegate.CreateDelegate(callbackType, typeof(EpicLootContractProbe).GetMethod("Reject", AllStatic));
        string id = "sighsorry.TrollingFishing.contractprobe." + Guid.NewGuid().ToString("N");
        try
        {
            Check((bool)register.Invoke(null, new object[] { id, callback }), "actual API registers a container predicate");
            Check(((Dictionary<string, List<string>>)providers.Invoke(null, null))["ItemContainer"].Contains(id),
                "actual provider snapshot exposes the registered ItemContainer id");
            Check((bool)contains.Invoke(null, new[] { acceptedItem }) && callbackCalls == 1, "actual registry dispatch forwards the original ItemData");
            Check((bool)foreign.Invoke(null, new[] { acceptedItem }), "actual ForeignItemContainers recognizes registered items");
            Check(!(bool)register.Invoke(null, new object[] { id, reject }), "duplicate registration returns false");
            Check((bool)contains.Invoke(null, new[] { acceptedItem }), "duplicate failure preserves the original predicate");
            Check(!(bool)contains.Invoke(null, new object[] { null }), "null item is not a registered container");
            Check((bool)unregister.Invoke(null, new object[] { id }), "actual unregister removes the registered id");
            Check(!(bool)contains.Invoke(null, new[] { acceptedItem }), "unregistered predicate is no longer consulted");
            Check(!(bool)unregister.Invoke(null, new object[] { id }), "repeated unregister returns false");

            FieldInfo reload = api.GetField("OnReload", AllStatic);
            Delegate oldReload = (Delegate)reload.GetValue(null);
            Action<string> throwing = message => { throw new InvalidOperationException("probe notification failure"); };
            reload.SetValue(null, Delegate.Combine(oldReload, throwing));
            try
            {
                bool threw = false;
                try { register.Invoke(null, new object[] { id, callback }); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { threw = true; }
                Check(threw, "actual register propagates an OnReload observer exception");
                Check((bool)contains.Invoke(null, new[] { acceptedItem }), "registration is already committed when OnReload throws");
            }
            finally { reload.SetValue(null, oldReload); }
            Check((bool)unregister.Invoke(null, new object[] { id }), "explicit rollback removes an entry committed before notification failure");
        }
        finally { unregister.Invoke(null, new object[] { id }); }

        if (modPath != null)
        {
            Type adapter = Assembly.LoadFrom(modPath).GetType("TrollingFishing.EpicLootBagCompatibility", true);
            MethodInfo bind = adapter.GetMethod("BindFocusedPanelGamepadPress", AllStatic);
            var predicate = (Func<bool>)bind.Invoke(null, new object[] { epic, press.DeclaringType });
            Check(!predicate(), "actual production private-access expression binds and returns false for the closed panel");
            FieldInfo panel = epic.GetType("EpicLoot.ShardStones.ShardUI", true).GetField("_panel", AllStatic);
            Check(panel != null && panel.IsPrivate, "original EpicLoot _panel is private");
            Func<object> readPrivatePanel = Expression.Lambda<Func<object>>(Expression.Convert(Expression.Field(null, panel), typeof(object))).Compile();
            Check(readPrivatePanel() == null, "compiled expression executes the original private _panel read on Unity Mono");
        }
    }

    private static MethodInfo Require(Type type, string name, Type result, params Type[] arguments)
    {
        MethodInfo method = type.GetMethod(name, AllStatic, null, arguments, null);
        if (method == null || !method.IsStatic || method.ReturnType != result || method.ContainsGenericParameters)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    private static bool Accept(object item) { callbackCalls++; return ReferenceEquals(item, acceptedItem); }
    private static bool Reject(object item) => false;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + description);
        checks++;
        System.Console.WriteLine("PASS " + description);
    }
}
