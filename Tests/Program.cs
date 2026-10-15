using TrollingFishing;
using UnityEngine;
using Bag = TrollingFishing.FishingOverrideSystem;
using Item = ItemDrop.ItemData;

internal static class Program
{
    private static Player _player = null!;
    private static Item _rod = null!;
    private static GameObject _fishPrefab = null!;

    private static int Main()
    {
        List<(string Name, Action Run)> tests = new();
        foreach (int version in new[] { 109, 110 })
        {
            tests.Add(($"version {version} load and round trip", () => RoundTrip(version)));
            tests.Add(($"version {version} weight query does not instantiate inventory", () => WeightMetadata(version)));
            foreach (bool cheated in new[] { false, true })
                tests.Add(($"version {version} cheated={cheated} follows game load and survives resave", () => CheatedRoundTrip(version, cheated)));
        }
        foreach (int version in new[] { 100, 106, 107, 108 })
        {
            int unsupported = version;
            tests.Add(($"unsupported bag format {version} is preserved", () => AssertLoadFailure(Header(unsupported, 0))));
        }
        tests.AddRange(new (string, Action)[]
        {
            ("absent data creates an editable empty bag", NewBag),
            ("valid empty data is distinct from failed data", EmptyBag),
            ("invalid base64 remains intact", () => AssertLoadFailure("not base64!")),
            ("empty stored value remains intact", () => AssertLoadFailure("")),
            ("whitespace stored value remains intact", () => AssertLoadFailure(" \t")),
            ("truncated item remains intact", TruncatedItem),
            ("missing prefab cannot become a partial saved bag", MissingPrefab),
            ("trailing bytes are rejected", TrailingBytes),
            ("unsupported old version remains intact", () => AssertLoadFailure(Header(99, 0))),
            ("unsupported future version remains intact", () => AssertLoadFailure(Header(111, 0))),
            ("maximum ushort item count remains intact", () => AssertLoadFailure(Header(109, 65535))),
            ("oversized item count remains intact", () => AssertLoadFailure(Header(109, 81))),
            ("failed automatic catch falls through without destroying fish", FailedCatch),
            ("shrink/save/expand preserves overflow and clones once", OverflowRoundTrip),
            ("saving repairs positions without changing live items", RepairPositions),
            ("overcapacity save preserves existing metadata", Overcapacity),
            ("serialization exception preserves existing metadata", SerializationFailure),
            ("weight exception preserves existing metadata", WeightFailure),
            ("normal bag rules remain distinct from player inventory", AllowedItemRules),
            ("normal empty save clears data and weight only", SaveEmpty),
        });

        int failures = 0;
        foreach ((string name, Action run) in tests)
        {
            Reset();
            try { run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
        }
        Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed; managed storage tests, not a game runtime check.");
        return failures == 0 ? 0 : 1;
    }

    private static void Reset()
    {
        ObjectDB.instance = new();
        ZNetScene.instance = new();
        FishingRodBagStoreState.Inventories.Clear();
        FishingRodBagStoreState.InventoryOwners.Clear();
        FishingRodBagRulesState.AllowedPrefabNames.Clear();
        FishingRodBagRulesState.CachedScene = null;
        TrollingFishingPlugin.FishingRodBag.Value = Toggle.On;
        TrollingFishingPlugin.FishingRodBagScalesWithFishingSkill.Value = Toggle.On;
        TrollingFishingPlugin.ModLogger.Warnings.Clear();
        _player = new GameObject("player").AddComponent<Player>();
        Player.m_localPlayer = _player;
        _fishPrefab = Prefab("FishTest", Item.ItemType.Fish);
        _rod = Prefab("FishingRod", Item.ItemType.Material).GetComponent<ItemDrop>().m_itemData.Clone();
        _player.Weapon = _rod;
        _player.Inventory.m_inventory.Add(_rod);
        Inventory.LoadCalls = 0;
        Inventory.ThrowOnSave = false;
        Item.CloneCalls = 0;
        UnityEngine.Object.DestroyCalls = 0;
        Bag.TestProxyRefreshCalls = 0;
    }

    private static GameObject Prefab(string name, Item.ItemType type)
    {
        GameObject prefab = new(name);
        Item item = prefab.AddComponent<ItemDrop>().m_itemData;
        item.m_dropPrefab = prefab;
        item.m_shared.m_name = name;
        item.m_shared.m_itemType = type;
        ObjectDB.instance.m_items.Add(prefab);
        return prefab;
    }

    private static Item FishItem(int index = 0)
    {
        Item item = _fishPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
        item.m_stack = index % 3 + 1;
        item.m_quality = index % 2 + 1;
        item.m_gridPos = new(index % 8, index / 8);
        item.m_worldLevel = 2;
        item.m_customData["test.id"] = index.ToString();
        return item;
    }

    private static string Fixture(IReadOnlyCollection<Item> items, int version = 110)
    {
        ZPackage package = new();
        Inventory.WriteFixture(package, items, version);
        return package.GetBase64();
    }
    private static string Header(int version, int count)
    {
        ZPackage package = new();
        package.Write(version);
        package.Write((ushort)count);
        return package.GetBase64();
    }
    private static void SetExistingData(string raw)
    {
        _rod.m_customData[FishingRodBagStoreState.BagDataKey] = raw;
        _rod.m_customData[FishingRodBagStoreState.BagWeightKey] = "23.5";
        _rod.m_customData[FishingRodBagStoreState.BagSelectedBaitKey] = "FishingBait";
        _rod.m_customData["other.mod"] = "keep";
    }
    private static Dictionary<string, string> Snapshot() => new(_rod.m_customData);
    private static void Unchanged(Dictionary<string, string> before)
    {
        Check(before.Count == _rod.m_customData.Count &&
              before.All(pair => _rod.m_customData.TryGetValue(pair.Key, out string? value) && value == pair.Value),
            "Rod metadata changed.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected InvalidOperationException.");
    }

    private static void RoundTrip(int version)
    {
        Item[] source = [FishItem(0), FishItem(1)];
        SetExistingData(Fixture(source, version));
        var before = Snapshot();
        Inventory inventory = Bag.TestLoad(_player, _rod);
        Check(!Bag.IsFishingRodBagInventoryLoadFailed(inventory), "Valid data was rejected.");
        Check(inventory.GetAllItems().Count == 2, "Item count changed.");
        Unchanged(before);
        Bag.TestSave(_rod, inventory);
        Check(new ZPackage(_rod.m_customData[FishingRodBagStoreState.BagDataKey]).ReadInt() == 110, "Save did not use current game format.");
        Inventory reloaded = Bag.TestLoad(_player, _rod);
        Check(reloaded.GetAllItems().Sum(item => item.m_stack) == 3, "Stacks changed on round trip.");
        Item second = reloaded.GetAllItems()[1];
        Check(second.m_quality == 2, "Stored quality changed.");
        Check(second.m_customData.Count == 1, "Stored custom data changed.");
        Check(second.m_worldLevel == 2, "Stored world level changed.");
        Check(_rod.m_customData["other.mod"] == "keep", "Unrelated metadata changed.");
    }

    private static void CheatedRoundTrip(int version, bool cheated)
    {
        Item source = FishItem();
        source.m_cheated = cheated;
        SetExistingData(Fixture([source], version));
        Inventory inventory = Bag.TestLoad(_player, _rod);
        Check(!Bag.IsFishingRodBagInventoryLoadFailed(inventory), "Supported data was rejected.");
        bool expected = version == 110 && cheated;
        Check(inventory.GetAllItems().Single().m_cheated == expected, "Game clean-slate interpretation changed.");
        Bag.TestSave(_rod, inventory);
        Check(Bag.TestLoad(_player, _rod).GetAllItems().Single().m_cheated == expected, "Cheated state changed after save/reopen.");
    }

    private static void NewBag()
    {
        Inventory bag = Bag.TestLoad(_player, _rod);
        Check(!Bag.IsFishingRodBagInventoryLoadFailed(bag), "Absent data must be editable.");
        Check(Bag.TestAdd(bag, FishItem()), "Valid fish rejected.");
        Bag.TestSave(_rod, bag);
        Check(Bag.TestLoad(_player, _rod).GetAllItems().Count == 1, "New bag was not saved.");
    }
    private static void EmptyBag()
    {
        SetExistingData(Header(109, 0));
        var before = Snapshot();
        Inventory bag = Bag.TestLoad(_player, _rod);
        Check(!Bag.IsFishingRodBagInventoryLoadFailed(bag), "Valid empty package was rejected.");
        Unchanged(before);
        Check(Bag.TestAdd(bag, FishItem()), "Valid empty bag must accept items.");
    }

    private static void AssertLoadFailure(string raw)
    {
        SetExistingData(raw);
        var before = Snapshot();
        Inventory bag = Bag.TestLoad(_player, _rod);
        Check(Bag.IsFishingRodBagInventoryLoadFailed(bag), "Failure was not recorded.");
        Check(bag.GetAllItems().Count == 0, "Partial items were exposed.");
        Item fish = FishItem();
        Item.CloneCalls = 0;
        Check(!Bag.TestAdd(bag, fish), "Failed bag accepted an item.");
        Check(!Bag.CanAddToFishingRodBag(bag, fish), "Item rule did not fail closed.");
        Check(!Bag.CanAddToFishingRodBag(bag, _fishPrefab), "Prefab rule did not fail closed.");
        Bag.TestSave(_rod, bag);
        Unchanged(before);
        Check(Item.CloneCalls == 0, "Blocked operation cloned items.");
        Check(_player.Inventory.ChangedCalls == 0 && Bag.TestProxyRefreshCalls == 0, "Blocked save notified listeners.");
        Check(!Bag.TestAddToRod(_player, _rod, fish), "Automatic bag add did not fall through.");
        Unchanged(before);
    }
    private static void TruncatedItem()
    {
        byte[] bytes = Convert.FromBase64String(Fixture([FishItem()]));
        AssertLoadFailure(Convert.ToBase64String(bytes[..^1]));
    }
    private static void MissingPrefab()
    {
        Item missing = FishItem(1);
        missing.m_dropPrefab = new("UnavailableOptionalModFish");
        AssertLoadFailure(Fixture([FishItem(), missing]));
        Check(Inventory.LoadCalls > 0, "The partial-load path was not exercised.");
    }
    private static void TrailingBytes()
    {
        ZPackage package = new();
        Inventory.WriteFixture(package, [FishItem()], 109);
        package.Write(42);
        AssertLoadFailure(package.GetBase64());
    }
    private static void FailedCatch()
    {
        SetExistingData("broken!");
        var before = Snapshot();
        Fish fish = new GameObject("live fish").AddComponent<Fish>();
        fish.m_pickupItem = _fishPrefab;
        Check(!Bag.TryCatchFishToFishingRodBag(fish, _player, out _), "Failed bag catch did not fall through.");
        Check(UnityEngine.Object.DestroyCalls == 0, "Fallback destroyed the fish.");
        Unchanged(before);
    }

    private static void OverflowRoundTrip()
    {
        List<Item> source = Enumerable.Range(0, 80).Select(FishItem).ToList();
        SetExistingData(Fixture(source));
        Inventory small = Bag.TestLoad(_player, _rod);
        Check(small.GetAllItems().Count == 8, "Skill zero visible slots changed.");
        Check(FishingRodBagStoreState.InventoryStates.TryGetValue(small, out var state) &&
              state.OverflowItems.Count == 72, "Overflow was not retained.");
        Item[] liveItems = small.GetAllItems().Concat(state!.OverflowItems).ToArray();
        Vector2i[] livePositions = liveItems.Select(item => item.m_gridPos).ToArray();
        string[] liveIds = liveItems.Select(item => item.m_customData["test.id"]).ToArray();
        Item.CloneCalls = 0;
        Bag.TestSave(_rod, small);
        Check(Item.CloneCalls == 80, $"Expected one clone per stored item, got {Item.CloneCalls}.");
        Check(liveItems.Select(item => item.m_gridPos).SequenceEqual(livePositions), "Save changed live grid positions.");
        Check(liveItems.Select(item => item.m_customData["test.id"]).SequenceEqual(liveIds), "Save changed live custom data.");
        _player.FishingLevel = 90;
        Inventory expanded = Bag.TestLoad(_player, _rod);
        Check(expanded.GetAllItems().Count == 80, "Expanded bag lost overflow.");
        Check(expanded.GetAllItems().Sum(item => item.m_stack) == source.Sum(item => item.m_stack), "Total quantity changed.");
        Check(expanded.GetAllItems().Select(item => item.m_customData["test.id"]).Distinct().Count() == 80,
            "Item identities were lost or duplicated.");
    }
    private static void RepairPositions()
    {
        Inventory bag = Bag.TestLoad(_player, _rod);
        Item first = FishItem(0);
        Item second = FishItem(1);
        first.m_gridPos = second.m_gridPos = new(-1, -1);
        bag.m_inventory.AddRange([first, second]);
        Bag.TestSave(_rod, bag);
        Check(first.m_gridPos == new Vector2i(-1, -1) && second.m_gridPos == first.m_gridPos,
            "Save repaired live items instead of storage clones.");
        Inventory loaded = Bag.TestLoad(_player, _rod);
        Check(loaded.GetAllItems().Count == 2 && loaded.GetAllItems().Select(item => item.m_gridPos).Distinct().Count() == 2,
            "Saved positions were not repaired.");
    }
    private static void Overcapacity()
    {
        SetExistingData(Fixture([FishItem()]));
        var before = Snapshot();
        Inventory bag = Bag.TestLoad(_player, _rod);
        bag.m_inventory.Clear();
        bag.m_inventory.AddRange(Enumerable.Range(0, 81).Select(FishItem));
        Throws(() => Bag.TestSave(_rod, bag));
        Unchanged(before);
    }
    private static void SerializationFailure()
    {
        SetExistingData(Fixture([FishItem()]));
        var before = Snapshot();
        Inventory bag = Bag.TestLoad(_player, _rod);
        Inventory.ThrowOnSave = true;
        Throws(() => Bag.TestSave(_rod, bag));
        Unchanged(before);
    }
    private static void WeightFailure()
    {
        SetExistingData(Fixture([FishItem()]));
        var before = Snapshot();
        Inventory bag = Bag.TestLoad(_player, _rod);
        bag.GetAllItems()[0].ThrowOnWeight = true;
        Throws(() => Bag.TestSave(_rod, bag));
        Unchanged(before);
        Check(_player.Inventory.ChangedCalls == 0 && Bag.TestProxyRefreshCalls == 0, "Failed commit notified listeners.");
    }
    private static void WeightMetadata(int version)
    {
        _rod.m_customData[FishingRodBagStoreState.BagDataKey] = Fixture([FishItem(1)], version);
        Item.CloneCalls = 0;
        Inventory.LoadCalls = 0;
        Check(Bag.TestWeight(_rod) == 2, "Decoded weight is incorrect.");
        Check(Item.CloneCalls == 0 && Inventory.LoadCalls == 0, "Weight decoding loaded or cloned inventory items.");
        Check(_rod.m_customData[FishingRodBagStoreState.BagWeightKey] == "2", "Weight metadata was not persisted.");
        Check(Bag.TestWeight(_rod) == 2 && Inventory.LoadCalls == 0, "Cached weight unexpectedly loaded inventory.");
    }
    private static void AllowedItemRules()
    {
        Inventory bag = Bag.TestLoad(_player, _rod);
        Bag.RegisterFishingRodBagInventory(bag, _rod);
        Item stone = Prefab("Stone", Item.ItemType.Material).GetComponent<ItemDrop>().m_itemData;
        Check(Bag.CanAddToFishingRodBag(bag, FishItem()), "Fish rejected from normal bag.");
        Check(!Bag.CanAddToFishingRodBag(bag, stone), "Unrelated material accepted into bag.");
        Check(Bag.CanAddToFishingRodBag(_player.Inventory, stone), "Ordinary inventory was restricted.");
        Bag.UnregisterFishingRodBagInventory(bag);
    }
    private static void SaveEmpty()
    {
        SetExistingData(Fixture([FishItem()]));
        Inventory bag = Bag.TestLoad(_player, _rod);
        bag.m_inventory.Clear();
        Bag.TestSave(_rod, bag);
        Check(!_rod.m_customData.ContainsKey(FishingRodBagStoreState.BagDataKey) &&
              !_rod.m_customData.ContainsKey(FishingRodBagStoreState.BagWeightKey), "Empty save retained payload or weight.");
        Check(_rod.m_customData[FishingRodBagStoreState.BagSelectedBaitKey] == "FishingBait" &&
              _rod.m_customData["other.mod"] == "keep", "Empty save changed unrelated metadata.");
    }
}
