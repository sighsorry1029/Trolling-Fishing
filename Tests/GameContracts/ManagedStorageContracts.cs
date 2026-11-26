using System.Reflection;
using System.Runtime.Loader;

internal static class ManagedStorageContracts
{
    internal static void Run(string gameDir)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(gameDir, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDir, name.Name + ".dll")) : null;
        var game = Assembly.LoadFrom(Path.Combine(gameDir, "assembly_valheim.dll"));
        var packageType = game.GetType("ZPackage", true)!;
        var itemType = game.GetType("ItemDrop+ItemData", true)!;
        var itemVersion = game.GetType("Version+Item", true)!;
        var load = itemType.GetMethod("Load", new[] { packageType, itemVersion })!;
        var inventoryType = game.GetType("Inventory", true)!;
        var inventory = Activator.CreateInstance(inventoryType, new object?[] { "contract", null, 8, 10 })!;
        int notifications = 0;
        inventoryType.GetField("m_onChanged")!.SetValue(inventory, (Action)(() => notifications++));
        inventoryType.GetMethod("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inventory, new object[] { false, false });
        if (notifications != 1) throw new Exception("Inventory.Changed did not notify exactly once");
        int count = 0;
        foreach (int version in new[] { 109, 110 })
        foreach (bool cheated in new[] { false, true })
        foreach (bool expanded in new[] { false, true })
        {
            // Independent binary fixtures exercise compact defaults and all optional fields.
            // This invokes the original reader only; no prefab or Unity object is constructed.
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(version); writer.Write((ushort)1);
                writer.Write(12345); writer.Write((byte)3); writer.Write((byte)7); writer.Write((byte)2);
                writer.Write(expanded ? (byte)255 : (byte)64);
                if (expanded) { writer.Write((ushort)4); writer.Write((ushort)12); writer.Write(2); writer.Write(123456789L); writer.Write("Crafter 한글"); }
                writer.Write(7654321);
                if (expanded) { writer.Write((byte)1); writer.Write("other.mod"); writer.Write("metadata"); }
                writer.Write(cheated ? (byte)1 : (byte)0);
            }
            var package = Activator.CreateInstance(packageType, Convert.ToBase64String(stream.ToArray()))!;
            object Read(string name) => packageType.GetMethod(name, Type.EmptyTypes)!.Invoke(package, null)!;
            if ((int)Read("ReadInt") != version || (ushort)Read("ReadUShort") != 1) throw new Exception("Header mismatch");
            var pair = load.Invoke(null, new[] { package, Enum.ToObject(itemVersion, version) })!;
            var item = pair.GetType().GetField("Item2")!.GetValue(pair)!;
            object Field(string name) => itemType.GetField(name)!.GetValue(item)!;
            void Require(bool condition, string field) { if (!condition) throw new Exception($"Item {version}/{expanded}/{cheated}: {field}"); }
            Require((int)pair.GetType().GetField("Item1")!.GetValue(pair)! == 7654321, "prefab");
            Require(((float)Field("m_durability")).Equals(12345 * 0.01f), "durability");
            Require((int)Field("m_stack") == (expanded ? 12 : 1) && (int)Field("m_quality") == (expanded ? 4 : 1), "stack/quality");
            Require((int)Field("m_variant") == (expanded ? 2 : 0), "variant");
            Require(Convert.ToInt32(Field("m_worldLevel")) == 2, "worldLevel");
            Require((long)Field("m_crafterID") == (expanded ? 123456789L : 0) && (string)Field("m_crafterName") == (expanded ? "Crafter 한글" : ""), "crafter");
            Require((bool)Field("m_pickedUp") == expanded && (bool)Field("m_equipped") == expanded, "flags");
            Require((bool)Field("m_cheated") == (version == 110 && cheated), "version-specific cheated flag");
            var position = Field("m_gridPos");
            Require((int)position.GetType().GetField("x")!.GetValue(position)! == 3 && (int)position.GetType().GetField("y")!.GetValue(position)! == 7, "position");
            var custom = (Dictionary<string, string>)Field("m_customData");
            Require(custom.Count == (expanded ? 1 : 0) && (!expanded || custom["other.mod"] == "metadata"), "customData");
            Require(Read("GetPos").Equals(Read("Size")), "unread bytes");
            count++;
        }
        Console.WriteLine($"PASS original private Inventory.Changed callback and {count} original ItemData.Load fixtures (managed-only, not Unity/Mono gameplay)");
    }
}
