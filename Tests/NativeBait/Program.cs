using System.Reflection;
using TrollingFishing;
using UnityEngine;
using Flow = TrollingFishing.MultiLineFishingSystem;
using Boundary = TrollingFishing.FishingOverrideSystem;
using Compatibility = TrollingFishing.PathOfValheimanCompatibility;

internal static class Program
{
    private static readonly MethodInfo AmmoPrefix = typeof(AttackUseAmmoFishingRodBagPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static Attack _attack = null!;
    private static Player _player = null!;
    private static ItemDrop.ItemData _rod = null!;

    private static int Main()
    {
        List<(string Name, Action Run)> tests = new();
        foreach (int count in new[] { 2, 3, 10 })
        {
            tests.Add(($"free {count} lines reserve no bait and propagate free receipt", () => FreeCast(count)));
            tests.Add(($"paid {count} lines retain primary plus additional consumption", () => PaidCast(count)));
        }
        tests.AddRange(new (string, Action)[]
        {
            ("unhandled bag lookup preserves incoming result and ammo", PassThrough),
            ("another successful prefix retains result and ammo", OtherPrefix),
            ("another rejected prefix retains rejection and ammo", OtherRejection),
            ("bag transaction publishes its own result and ammo", BagTransaction),
            ("planned free changing to paid is rejected before bag consumption", FreeToPaid),
            ("paid plan changing to native free commits free receipt", PaidToFree),
            ("free native prefab creation failure rejects before bag consumption", NativeCreationFailure),
            ("unknown contract rejects secondary preflight without consumption", UnknownPreflight),
            ("partial native patch set rejects preflight without consumption", PartialPreflight),
            ("partial native patch set rejects use receipt before bag consumption", PartialReceipt),
            ("contract loss before burst preserves existing floats and inventory", PartialBurst),
            ("non rod keeps foreign prefix outputs", NonRod),
            ("new paid attack cannot inherit previous free receipt", NewAttackReceipt),
        });

        int failures = 0;
        foreach (var test in tests)
        {
            Reset();
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception e) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + e); }
        }
        Console.WriteLine($"{tests.Count - failures}/{tests.Count} passed; linked casting flow with managed boundary doubles, not Harmony/Unity/PoV execution.");
        return failures == 0 ? 0 : 1;
    }

    private static void Reset()
    {
        Flow.ResetNativeBaitCasts();
        Time.time = 0;
        UnityEngine.Object.InstantiateCalls = 0;
        Compatibility.Known = Compatibility.Valid = true;
        Compatibility.Free = false;
        Compatibility.NativeAmmo = null!;
        TrollingFishingPlugin.FishingRodMultiLine.Value = Toggle.On;
        TrollingFishingPlugin.FishingRodMultiLineCount.Value = 3;
        _rod = new() { IsRod = true };
        _player = new() { Weapon = _rod };
        Player.m_localPlayer = _player;
        _attack = NewAttack();
        Boundary.SelectedAmmo = new();
        Boundary.Available = 100;
        Boundary.CountCalls = Boundary.ReserveCalls = Boundary.Reserved = Boundary.BagUseCalls = Boundary.DestroyCalls = Boundary.ScopeDepth = 0;
        Boundary.BagHandled = Boundary.BagResult = false;
        Boundary.Marks.Clear();
        Boundary.Scopes.Clear();
    }

    private static Attack NewAttack()
    {
        var attack = new Attack { Weapon = _rod, Owner = _player, m_attackProjectile = new GameObject() };
        _player.CurrentAttack = attack;
        return attack;
    }

    private static void Prepare(bool free)
    {
        Compatibility.Free = free;
        bool result = true;
        Check(Flow.TryPrepareSecondaryStart(_player, _rod, ref result), "Secondary preflight unexpectedly failed.");
    }

    private static (bool RunOriginal, ItemDrop.ItemData? Ammo, bool Result) Use(ItemDrop.ItemData? ammo, bool result, bool runOriginal)
    {
        object?[] args = [_attack, ammo, result, runOriginal];
        bool run = (bool)AmmoPrefix.Invoke(null, args)!;
        return (run, (ItemDrop.ItemData?)args[1], (bool)args[2]!);
    }

    private static void Receipt()
    {
        Compatibility.NativeAmmo = new();
        _attack.Ammo = Compatibility.NativeAmmo;
        var output = Use(Compatibility.NativeAmmo, true, false);
        Check(output.RunOriginal && output.Result && ReferenceEquals(output.Ammo, Compatibility.NativeAmmo), "Native output was overwritten.");
    }

    private static void AssertFreeBurst(int count)
    {
        int before = Boundary.Available;
        int countCalls = Boundary.CountCalls;
        Check(Flow.TryHandleFireProjectileBurst(_attack), "Free burst was not handled.");
        Check(UnityEngine.Object.InstantiateCalls == count && Boundary.Marks.Count == count, "Incorrect float count.");
        Check(Boundary.Marks.All(m => m.Free && !m.Reserved), "A free line acquired a physical reservation.");
        Check(Boundary.ReserveCalls == 0 && Boundary.Reserved == 0 && Boundary.Available == before, "Free cast consumed physical bait.");
        Check(Boundary.CountCalls == countCalls, "Free burst queried physical stock.");
        Check(Boundary.Scopes.SequenceEqual([true]) && Boundary.ScopeDepth == 0, "Free setup scope was not propagated or disposed.");
    }

    private static void FreeCast(int count)
    {
        TrollingFishingPlugin.FishingRodMultiLineCount.Value = count;
        Boundary.SelectedAmmo = null!;
        Boundary.Available = 0;
        Prepare(true);
        Receipt();
        Compatibility.Free = false; // A committed shot remains free if the talent changes before its burst.
        AssertFreeBurst(count);
        Check(Boundary.BagUseCalls == 0, "Native cast reached the bag consumer.");
    }

    private static void PaidCast(int count)
    {
        TrollingFishingPlugin.FishingRodMultiLineCount.Value = count;
        Boundary.Available = count;
        Prepare(false);
        var output = Use(null, false, true);
        Check(output.RunOriginal, "Ordinary ammo use did not fall through.");
        // The external vanilla UseAmmo boundary consumes the first bait before FireProjectileBurst.
        Boundary.Available--;
        _attack.Ammo = _attack.LastAmmo = Boundary.SelectedAmmo;
        Check(Flow.TryHandleFireProjectileBurst(_attack), "Paid burst was not handled.");
        Check(Boundary.ReserveCalls == 1 && Boundary.Reserved == count - 1 && Boundary.Available == 0, "Additional paid reservation changed.");
        Check(Boundary.Marks.Count == count && Boundary.Marks.All(m => !m.Free && m.Reserved), "Paid lines were not tracked.");
        Check(Boundary.Scopes.SequenceEqual([false]) && Boundary.ScopeDepth == 0, "Paid setup scope leaked.");
    }

    private static void PassThrough()
    {
        var original = new ItemDrop.ItemData();
        var output = Use(original, true, true);
        Check(output.RunOriginal && output.Result && ReferenceEquals(output.Ammo, original), "Unhandled bag helper reset an incoming out/ref value.");
    }

    private static void OtherPrefix()
    {
        var original = new ItemDrop.ItemData();
        var output = Use(original, true, false);
        Check(output.RunOriginal && output.Result && ReferenceEquals(output.Ammo, original) && Boundary.BagUseCalls == 0, "Another prefix's success was replaced or consumed twice.");
    }

    private static void OtherRejection()
    {
        var original = new ItemDrop.ItemData();
        var output = Use(original, false, false);
        Check(output.RunOriginal && !output.Result && ReferenceEquals(output.Ammo, original) && Boundary.BagUseCalls == 0, "Another prefix's cancellation was replaced.");
    }

    private static void BagTransaction()
    {
        Boundary.BagHandled = Boundary.BagResult = true;
        var output = Use(null, false, true);
        Check(!output.RunOriginal && output.Result && ReferenceEquals(output.Ammo, Boundary.SelectedAmmo), "Handled bag transaction did not publish its result.");
    }

    private static void FreeToPaid()
    {
        Prepare(true);
        Compatibility.Free = false;
        var output = Use(null, false, true);
        Check(!output.RunOriginal && !output.Result && Boundary.BagUseCalls == 0, "Free plan silently changed into a paid transaction.");
    }

    private static void PaidToFree() { Prepare(false); Compatibility.Free = true; Receipt(); AssertFreeBurst(3); }
    private static void NativeCreationFailure()
    {
        Prepare(true);
        var output = Use(null, false, true);
        Check(!output.RunOriginal && !output.Result && Boundary.BagUseCalls == 0, "Missing generated native bait consumed physical bait.");
    }
    private static void UnknownPreflight() { Compatibility.Known = false; AssertRejectedPreflight(); }
    private static void PartialPreflight() { Compatibility.Free = true; Compatibility.Valid = false; AssertRejectedPreflight(); }
    private static void AssertRejectedPreflight()
    {
        bool result = true;
        Check(!Flow.TryPrepareSecondaryStart(_player, _rod, ref result) && !result, "Unsafe preflight was accepted.");
        Check(Boundary.CountCalls == 0 && Boundary.Reserved == 0 && Boundary.BagUseCalls == 0, "Rejected preflight touched inventory.");
    }
    private static void PartialReceipt()
    {
        Prepare(true);
        Compatibility.Valid = false;
        Compatibility.NativeAmmo = new();
        var output = Use(Compatibility.NativeAmmo, true, false);
        Check(!output.RunOriginal && !output.Result && Boundary.BagUseCalls == 0, "Invalid active contract was accepted at receipt.");
    }
    private static void PartialBurst()
    {
        Prepare(true);
        Receipt();
        Compatibility.Valid = false;
        Check(Flow.TryHandleFireProjectileBurst(_attack), "Unsafe burst was passed to the original.");
        Check(Boundary.ReserveCalls == 0 && Boundary.DestroyCalls == 0 && UnityEngine.Object.InstantiateCalls == 0 && Boundary.Marks.Count == 0,
            "Contract failure mutated inventory or replaced existing floats.");
    }
    private static void NonRod()
    {
        _rod.IsRod = false;
        Compatibility.Known = false;
        OtherPrefix();
    }
    private static void NewAttackReceipt()
    {
        Prepare(true);
        Receipt();
        _attack = NewAttack();
        PaidCast(3);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
