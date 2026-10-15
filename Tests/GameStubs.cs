// Test doubles only. No Unity runtime, Harmony patching, or networking is simulated.
#nullable disable
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public static int DestroyCalls;
        public static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation) =>
            throw new NotSupportedException("World spawning is outside this harness.");
        public static void Destroy(Object target) => DestroyCalls++;
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    public class GameObject(string objectName = "") : Object
    {
        private readonly Dictionary<Type, Component> _components = new();
        public string name = objectName;
        public readonly Transform transform = new();
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new() { gameObject = this };
            _components[typeof(T)] = component;
            return component;
        }
        public T GetComponent<T>() where T : Component => _components.GetValueOrDefault(typeof(T)) as T;
        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>();
            return component != null;
        }
    }

    public class Transform { public Vector3 position; }
    public struct Vector3 { }
    public struct Quaternion { public static Quaternion Euler(float x, float y, float z) => new(); }
    public class Sprite { }
    public static class Random { public static int Range(int min, int max) => min; }
    public static class Mathf
    {
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
        public static float Clamp01(float value) => Math.Clamp(value, 0, 1);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int FloorToInt(float value) => (int)Math.Floor(value);
        public static int CeilToInt(float value) => (int)Math.Ceiling(value);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}

public record struct Vector2i(int x, int y);
public class Character : Component { }
public class Player : Character
{
    public static Player m_localPlayer;
    public float FishingLevel;
    public ItemDrop.ItemData Weapon;
    public readonly Inventory Inventory = new("player", null, 8, 4);
    public Inventory GetInventory() => Inventory;
    public ItemDrop.ItemData GetCurrentWeapon() => Weapon;
    public float GetSkillLevel(Skills.SkillType skill) => FishingLevel;
    public float GetSkillFactor(Skills.SkillType skill) => FishingLevel / 100;
    public void Message(MessageHud.MessageType type, string message) { }
}
public static class Skills { public enum SkillType { Fishing } }
public static class MessageHud { public enum MessageType { TopLeft } }
public static class Game { public static int m_worldLevel; }
public class Localization
{
    public static readonly Localization instance = new();
    public string Localize(string text) => text;
}
public class Container : Component
{
    public Inventory Inventory;
    public Inventory GetInventory() => Inventory;
}
public class ZNetView : Component
{
    public bool IsValid() => true;
    public void Destroy() => UnityEngine.Object.Destroy(this);
}
public class Fish : Component
{
    public readonly DropTable m_extraDrops = new();
    public GameObject m_pickupItem;
    public int m_pickupItemStackSize = 1;
    public readonly List<BaitSetting> m_baits = new();
    public string GetHoverName() => "fish";
    public class BaitSetting { public GameObject m_bait; }
}
public class DropTable
{
    public bool IsEmpty() => true;
    public IEnumerable<ItemDrop.ItemData> GetDropListItems() => Array.Empty<ItemDrop.ItemData>();
}
public class ZNetScene
{
    public static ZNetScene instance;
    public readonly List<GameObject> m_prefabs = new();
    public readonly List<GameObject> m_nonNetViewPrefabs = new();
}
public class ObjectDB
{
    public static ObjectDB instance = new();
    public readonly List<GameObject> m_items = new();
    public GameObject GetItemPrefab(string name) => m_items.FirstOrDefault(item => item.name == name);
    public GameObject GetItemPrefab(int hash) => m_items.FirstOrDefault(item => TestPrefabHash.Of(item.name) == hash);
}
public class ItemDrop : Component
{
    public ItemData m_itemData = new();
    public void SetStack(int stack) => m_itemData.m_stack = stack;
    public class ItemData
    {
        public static int CloneCalls;
        public GameObject m_dropPrefab;
        public SharedData m_shared = new();
        public int m_stack = 1;
        public int m_quality = 1;
        public Vector2i m_gridPos;
        public byte m_worldLevel;
        public bool m_cheated;
        public Dictionary<string, string> m_customData = new();
        public bool ThrowOnWeight;
        public Sprite GetIcon() => null;
        public ItemData Clone()
        {
            CloneCalls++;
            ItemData clone = (ItemData)MemberwiseClone();
            clone.m_customData = new(m_customData);
            return clone;
        }
        public float GetWeight()
        {
            if (ThrowOnWeight) throw new InvalidOperationException("Injected item weight failure.");
            return m_shared.m_weight * m_stack * (1 + (m_quality - 1) * m_shared.m_scaleWeightByQuality);
        }
        public static (int prefabHash, ItemData itemData) Load(ZPackage package, Version.Item version)
        {
            ItemData item = new();
            _ = package.ReadInt();
            item.m_gridPos = new(package.ReadByte(), package.ReadByte());
            item.m_worldLevel = package.ReadByte();
            byte flags = package.ReadByte();
            item.m_quality = (flags & 4) != 0 ? package.ReadUShort() : 1;
            item.m_stack = (flags & 8) != 0 ? package.ReadUShort() : 1;
            if ((flags & 16) != 0) _ = package.ReadInt();
            if ((flags & 32) != 0) { _ = package.ReadLong(); _ = package.ReadString(); }
            int hash = (flags & 64) != 0 ? package.ReadInt() : 0;
            int count = (flags & 128) != 0 ? package.ReadByte() : 0;
            // Tests use small dictionaries. Real extended counts are covered by the original-DLL check.
            for (int i = 0; i < count; i++) item.m_customData[package.ReadString()] = package.ReadString();
            byte additionalFlags = package.ReadByte();
            item.m_cheated = version >= Version.Item.NewCheats && (additionalFlags & 1) != 0;
            return (hash, item);
        }
        public enum ItemType { Material, Fish }
        public class SharedData
        {
            public string m_name = "item";
            public int m_maxStackSize = 100;
            public float m_weight = 1;
            public float m_scaleWeightByQuality;
            public ItemType m_itemType;
        }
    }
}

// BinaryReader/Writer model the package primitives exercised here. The inventory double
// reads the game's format-109/110 field order inspected in original DLLs, but only keeps
// fields needed by these storage tests. It deliberately skips unknown item prefabs, as the
// installed game's Inventory.Load does, so production must reject the partial result.
public class ZPackage
{
    private readonly MemoryStream _stream;
    private readonly BinaryReader _reader;
    private readonly BinaryWriter _writer;
    public ZPackage() : this(Array.Empty<byte>()) { }
    public ZPackage(string data) : this(Convert.FromBase64String(data)) { }
    private ZPackage(byte[] data)
    {
        _stream = new();
        _stream.Write(data);
        _stream.Position = 0;
        _reader = new(_stream);
        _writer = new(_stream);
    }
    public int ReadInt() => _reader.ReadInt32();
    public ushort ReadUShort() => _reader.ReadUInt16();
    public byte ReadByte() => _reader.ReadByte();
    public float ReadSingle() => _reader.ReadSingle();
    public Vector2i ReadVector2i() => new(ReadInt(), ReadInt());
    public bool ReadBool() => _reader.ReadBoolean();
    public long ReadLong() => _reader.ReadInt64();
    public string ReadString() => _reader.ReadString();
    public void Write(int value) => _writer.Write(value);
    public void Write(ushort value) => _writer.Write(value);
    public void Write(byte value) => _writer.Write(value);
    public void Write(float value) => _writer.Write(value);
    public void Write(bool value) => _writer.Write(value);
    public void Write(long value) => _writer.Write(value);
    public void Write(string value) => _writer.Write(value);
    public void SetPos(int position) => _stream.Position = position;
    public int GetPos() => (int)_stream.Position;
    public int Size() => (int)_stream.Length;
    public string GetBase64() => Convert.ToBase64String(_stream.ToArray());
}

public class Inventory(string name, Sprite icon, int width, int height)
{
    public string Name { get; } = name;
    public Sprite Icon { get; } = icon;
    public static int LoadCalls;
    public static bool ThrowOnSave;
    public readonly List<ItemDrop.ItemData> m_inventory = new();
    public int ChangedCalls;
    public List<ItemDrop.ItemData> GetAllItems() => m_inventory;
    public int GetWidth() => width;
    public int GetHeight() => height;
    public void Changed() => ChangedCalls++;
    public void NotifyChanged() => Changed();
    public bool CanAddItem(ItemDrop.ItemData item) => m_inventory.Count < width * height;
    public bool CanAddItem(GameObject prefab, int stack) => m_inventory.Count < width * height;
    public bool AddItem(ItemDrop.ItemData item)
    {
        if (!CanAddItem(item)) return false;
        m_inventory.Add(item);
        Changed();
        return true;
    }
    public bool AddItem(GameObject prefab, int stack)
    {
        ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_stack = stack;
        return AddItem(item);
    }
    public void Save(ZPackage package)
    {
        if (ThrowOnSave) throw new InvalidOperationException("Injected serialization failure.");
        WriteFixture(package, m_inventory, 110);
    }
    public static void WriteFixture(ZPackage package, IReadOnlyCollection<ItemDrop.ItemData> items, int version)
    {
        package.Write(version);
        package.Write((ushort)items.Count);
        foreach (ItemDrop.ItemData item in items)
        {
            package.Write(10000); // durability * 100
            package.Write((byte)item.m_gridPos.x);
            package.Write((byte)item.m_gridPos.y);
            package.Write(item.m_worldLevel);
            int flags = 4 | 8 | 64 | 128;
            package.Write((byte)flags);
            package.Write((ushort)item.m_quality);
            package.Write((ushort)item.m_stack);
            package.Write(TestPrefabHash.Of(item.m_dropPrefab.name));
            package.Write((byte)item.m_customData.Count); // small WriteNumItems case
            foreach (var pair in item.m_customData) { package.Write(pair.Key); package.Write(pair.Value); }
            package.Write((byte)(item.m_cheated ? 1 : 0));
        }
    }
    public void Load(ZPackage package)
    {
        LoadCalls++;
        int version = package.ReadInt();
        int count = package.ReadUShort();
        m_inventory.Clear();
        for (int i = 0; i < count; i++)
        {
            (int hash, ItemDrop.ItemData data) = ItemDrop.ItemData.Load(package, (Version.Item)version);
            GameObject prefab = ObjectDB.instance.GetItemPrefab(hash);
            if (prefab == null) continue;
            ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = Math.Min(data.m_stack, item.m_shared.m_maxStackSize);
            item.m_quality = data.m_quality;
            item.m_gridPos = data.m_gridPos;
            item.m_customData = data.m_customData;
            item.m_worldLevel = data.m_worldLevel;
            item.m_cheated = data.m_cheated;
            m_inventory.Add(item);
        }
        Changed();
    }
}

public static class Version { public enum Item { ChunksNCheats = 109, NewCheats = 110 } }
public static class TestPrefabHash
{
    public static int Of(string name)
    {
        unchecked
        {
            int first = 5381, second = first;
            for (int i = 0; i < name.Length && name[i] != '\0'; i += 2)
            {
                first = ((first << 5) + first) ^ name[i];
                if (i == name.Length - 1 || name[i + 1] == '\0') break;
                second = ((second << 5) + second) ^ name[i + 1];
            }
            return first + second * 1566083941;
        }
    }
}
namespace TrollingFishing
{
    public enum Toggle { Off, On }
    public static class ToggleExtensions
    {
        public static bool IsOn(this Toggle toggle) => toggle == Toggle.On;
        public static bool IsOff(this Toggle toggle) => toggle == Toggle.Off;
    }
    public class Setting<T>(T value) { public T Value = value; }
    public class TestLogger
    {
        public readonly List<string> Warnings = new();
        public void LogWarning(string message) => Warnings.Add(message);
    }
    public static class TrollingFishingPlugin
    {
        public static readonly Setting<Toggle> FishingRodBag = new(Toggle.On);
        public static readonly Setting<Toggle> FishingRodBagCountsWeight = new(Toggle.On);
        public static readonly Setting<Toggle> FishingRodBagScalesWithFishingSkill = new(Toggle.On);
        public static readonly Setting<int> FishingRodBagWeightAtMaxSkillPercent = new(50);
        public static readonly TestLogger ModLogger = new();
    }
    internal static partial class FishingOverrideSystem
    {
        private static void RecordFishingBagCatchStatistics(Player player, string fishName, int? quality) { }
        internal static int TestProxyRefreshCalls;
        private static void RefreshFishingRodBagProxy(ItemDrop.ItemData rod) => TestProxyRefreshCalls++;
        private static bool IsFishingRod(ItemDrop.ItemData item) => item?.m_dropPrefab?.name == "FishingRod";
        private static bool TryResolveMissingDropPrefab(ItemDrop.ItemData item) => item?.m_dropPrefab != null;
        private static string StripCloneSuffix(string name) => name.Replace("(Clone)", "");
        internal sealed class FishingRodBagContainer : Component { internal void Save() { } }
        internal sealed class FishingRodBagProxyContainer : Component { internal void Save() { } }

        // Thin entry points to actual private production methods; no storage logic is copied.
        internal static Inventory TestLoad(Player player, ItemDrop.ItemData rod) =>
            LoadFishingRodBagInventory(player, rod, out _, out _);
        internal static void TestSave(ItemDrop.ItemData rod, Inventory inventory, bool refreshProxy = true) =>
            SaveFishingRodBagInventory(rod, inventory, refreshProxy);
        internal static bool TestAdd(Inventory inventory, ItemDrop.ItemData item) =>
            TryAddItemToFishingRodBagInventory(inventory, item);
        internal static bool TestAddToRod(Player player, ItemDrop.ItemData rod, ItemDrop.ItemData item) =>
            TryAddItemToFishingRodBag(player, rod, item);
        internal static float TestWeight(ItemDrop.ItemData rod) => GetFishingRodBagWeight(rod);
    }
}
