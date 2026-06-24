using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrollingFishing;

internal static partial class FishingOverrideSystem
{
    internal static IDisposable BeginMultiLineFishingSetup()
    {
        MultiLineFishingCastState.SetupDepth++;
        MultiLineFishingCastState.SetupContexts.Add(MultiLineFishingSetupContext.Empty);
        return new MultiLineFishingSetupScope();
    }

    private static IDisposable BeginMultiLineFishingSetup(MultiLineFishingFloatMarker sourceMarker)
    {
        return BeginMultiLineFishingSetup(sourceMarker, consumeTrackedBaitOnSetup: false);
    }

    private static IDisposable BeginMultiLineFishingSetup(MultiLineFishingFloatMarker sourceMarker, bool consumeTrackedBaitOnSetup)
    {
        MultiLineFishingCastState.SetupDepth++;
        MultiLineFishingCastState.SetupContexts.Add(new MultiLineFishingSetupContext(
            sourceMarker.Owner,
            sourceMarker.Rod,
            sourceMarker.LineIndex,
            sourceMarker.PrimaryEquivalentLineIndex,
            sourceMarker.ReservedBait,
            sourceMarker.BaitReturnSource,
            consumeTrackedBaitOnSetup));
        return new MultiLineFishingSetupScope();
    }

    internal static bool IsMultiLineFishingSetupActive()
    {
        return MultiLineFishingCastState.SetupDepth > 0;
    }

    internal static void MarkMultiLineFishingFloat(FishingFloat fishingFloat, Character owner)
    {
        if (fishingFloat == null || owner == null)
        {
            return;
        }

        MultiLineFishingSetupContext context = CurrentMultiLineSetupContext();
        Character markerOwner = context.Owner ?? owner;
        MarkMultiLineFishingObject(
            fishingFloat.gameObject,
            markerOwner,
            context.LineIndex,
            context.PrimaryEquivalentLineIndex,
            context.ReservedBait,
            context.Rod,
            context.BaitReturnSource);
    }

    internal static void MarkMultiLineFishingObject(
        GameObject projectileObject,
        Character owner,
        int lineIndex = 0,
        int primaryEquivalentLineIndex = 0,
        MultiLineBaitReservation reservedBait = default,
        ItemDrop.ItemData? rod = null,
        MultiLineBaitReservation baitReturnSource = default)
    {
        if (projectileObject == null || owner == null)
        {
            return;
        }

        MultiLineFishingFloatMarker marker = projectileObject.GetComponent<MultiLineFishingFloatMarker>() ??
                                             projectileObject.AddComponent<MultiLineFishingFloatMarker>();
        marker.Initialize(owner, rod, Time.time + MultiLineFishingCastState.AttackGrace, lineIndex, primaryEquivalentLineIndex, reservedBait, baitReturnSource);

        MultiLineBaitReservation trackedBait = reservedBait.IsValid ? reservedBait : baitReturnSource;
        if (trackedBait.IsValid)
        {
            MarkFishingBaitReturnSource(projectileObject, trackedBait, CurrentMultiLineSetupContext().ConsumeTrackedBaitOnSetup);
        }

        TrollingFishingPlugin.LogDebug(
            $"[Fishing multiLine] marked float object={projectileObject.name} line={marker.LineIndex} primaryLine={marker.PrimaryEquivalentLineIndex} extra={marker.IsAdditionalLine} reservedBait={marker.ReservedBait.PrefabName} hasFishingFloat={projectileObject.GetComponent<FishingFloat>() != null} hasProjectile={projectileObject.GetComponent<Projectile>() != null}.");
    }

    private static MultiLineFishingSetupContext CurrentMultiLineSetupContext()
    {
        return MultiLineFishingCastState.SetupContexts.Count > 0 ? MultiLineFishingCastState.SetupContexts[MultiLineFishingCastState.SetupContexts.Count - 1] : MultiLineFishingSetupContext.Empty;
    }

    internal static bool TrySuppressMultiLineFishingFloatInitialUpdate(FishingFloat fishingFloat)
    {
        if (fishingFloat == null)
        {
            return false;
        }

        MultiLineFishingFloatMarker marker = fishingFloat.GetComponent<MultiLineFishingFloatMarker>();
        if (marker == null)
        {
            return false;
        }

        if (marker.ShouldSkipAttackCancellation())
        {
            return true;
        }

        return false;
    }

    internal static bool TrySuppressMultiLineFishingFindFloat(out FishingFloat result)
    {
        result = null!;
        return IsMultiLineFishingSetupActive();
    }

    internal static IDisposable? BeginAdditionalMultiLineFishingFloatUpdate(FishingFloat fishingFloat)
    {
        if (fishingFloat == null)
        {
            return null;
        }

        MultiLineFishingFloatMarker marker = fishingFloat.GetComponent<MultiLineFishingFloatMarker>();
        if (marker == null || !marker.IsAdditionalLine)
        {
            return null;
        }

        Character owner = marker.Owner ?? fishingFloat.GetOwner();
        if (owner == null)
        {
            return null;
        }

        MultiLineFishingCastState.UpdateContexts.Add(new MultiLineFishingUpdateContext(
            owner,
            Mathf.Max(0f, TrollingFishingPlugin.FishingRodMultiLineExtraPullStaminaFactor.Value),
            Mathf.Max(0f, TrollingFishingPlugin.FishingRodMultiLineSkillRaiseFactor.Value)));
        return new MultiLineFishingUpdateScope();
    }

    internal static void AdjustMultiLineFishingStaminaUse(Character character, ref float stamina)
    {
        if (stamina <= 0f || !TryGetCurrentMultiLineFishingUpdateContext(character, out MultiLineFishingUpdateContext context))
        {
            return;
        }

        stamina *= context.StaminaFactor;
    }

    internal static void AdjustMultiLineFishingSkillRaise(Character character, Skills.SkillType skillType, ref float factor)
    {
        if (factor <= 0f ||
            skillType != Skills.SkillType.Fishing ||
            !TryGetCurrentMultiLineFishingUpdateContext(character, out MultiLineFishingUpdateContext context))
        {
            return;
        }

        factor *= context.SkillRaiseFactor;
    }

    private static bool TryGetCurrentMultiLineFishingUpdateContext(Character character, out MultiLineFishingUpdateContext context)
    {
        if (character != null)
        {
            for (int i = MultiLineFishingCastState.UpdateContexts.Count - 1; i >= 0; i--)
            {
                MultiLineFishingUpdateContext candidate = MultiLineFishingCastState.UpdateContexts[i];
                if (ReferenceEquals(candidate.Owner, character))
                {
                    context = candidate;
                    return true;
                }
            }
        }

        context = default;
        return false;
    }

    internal static bool TrySettleMultiLineFishingProjectileOnWater(Projectile projectile, Vector3 hitPoint, bool water)
    {
        if (!water || projectile == null)
        {
            return false;
        }

        MultiLineFishingFloatMarker marker = projectile.GetComponent<MultiLineFishingFloatMarker>();
        FishingFloat fishingFloat = projectile.GetComponent<FishingFloat>();
        if (marker == null || fishingFloat == null)
        {
            if (fishingFloat != null)
            {
                TrollingFishingPlugin.LogDebug($"[Fishing multiLine] water hit not intercepted object={projectile.gameObject.name} hasMarker={marker != null}.");
            }

            return false;
        }

        TrollingFishingPlugin.LogDebug($"[Fishing multiLine] water hit intercepted object={projectile.gameObject.name} point={hitPoint}.");
        projectile.transform.position = hitPoint;
        ProjectileAccess.SetVelocity(projectile, Vector3.zero);
        ProjectileAccess.SetDidHit(projectile, true);
        projectile.m_ttl = 0f;
        projectile.m_hitWaterEffects.Create(hitPoint, Quaternion.identity);
        Rigidbody body = projectile.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        projectile.enabled = false;
        return true;
    }

    internal static bool TryBeginFishingProjectileHit(Projectile projectile, Collider collider, bool water, out IDisposable? scope)
    {
        scope = null;
        if (projectile == null ||
            projectile.GetComponent<FishingFloat>() != null)
        {
            return false;
        }

        MultiLineFishingFloatMarker marker = projectile.GetComponent<MultiLineFishingFloatMarker>();
        if (marker != null)
        {
            bool consumeTrackedBaitOnSetup = marker.IsAdditionalLine && !water && ProjectileAccess.WillDestroyAfterHit(projectile, collider);
            scope = BeginMultiLineFishingSetup(marker, consumeTrackedBaitOnSetup);
            TrollingFishingPlugin.LogDebug($"[Fishing multiLine] hit spawn scope opened projectile={projectile.gameObject.name} water={water} consumeTrackedBaitOnSetup={consumeTrackedBaitOnSetup}.");
            return true;
        }

        FishingBaitReturnTracker tracker = projectile.GetComponent<FishingBaitReturnTracker>();
        if (tracker == null || !tracker.Source.IsValid || tracker.IsSettled)
        {
            return false;
        }

        scope = BeginBaitReturnSourceSetup(tracker.Source);
        TrollingFishingPlugin.LogDebug($"[Fishing bait] hit spawn source scope opened projectile={projectile.gameObject.name} prefab={tracker.Source.PrefabName} fromRodBag={tracker.Source.FromRodBag}.");
        return true;
    }

    internal static void ReturnFishingFloatBaitBeforeDestroy(FishingFloat fishingFloat)
    {
        if (fishingFloat == null || IsFishingFloatBaitConsumed(fishingFloat))
        {
            return;
        }

        if (TryReturnBaitToOriginalSource(fishingFloat))
        {
            return;
        }

        fishingFloat.ReturnBait();
    }

    internal static void LogMultiLineFishingFloatSetup(FishingFloat fishingFloat, Character owner, string phase)
    {
        if (fishingFloat == null)
        {
            return;
        }

        TrollingFishingPlugin.LogDebug(
            $"[Fishing multiLine] setup {phase} object={fishingFloat.gameObject.name} setupActive={IsMultiLineFishingSetupActive()} owner={(owner != null ? owner.name : "null")} hasMarker={fishingFloat.GetComponent<MultiLineFishingFloatMarker>() != null}.");
    }

    internal static void LogMultiLineFishingFloatDestroyed(FishingFloat fishingFloat)
    {
        if (fishingFloat == null)
        {
            return;
        }

        MultiLineFishingFloatMarker marker = fishingFloat.GetComponent<MultiLineFishingFloatMarker>();
        if (marker == null)
        {
            return;
        }

        Character? owner = marker.Owner;
        TrollingFishingPlugin.LogDebug(
            $"[Fishing multiLine] float destroyed object={fishingFloat.gameObject.name} owner={(owner != null ? owner.name : "null")} ownerInAttack={(owner != null && owner.InAttack())} ownerDrawing={(owner != null && owner.IsDrawingBow())} inWater={fishingFloat.IsInWater()}.");
    }

    internal static void RegisterAttackBaitReturnSource(Attack attack, Player player, ItemDrop.ItemData rod, ItemDrop.ItemData bait)
    {
        if (attack == null || player == null || bait?.m_dropPrefab == null)
        {
            return;
        }

        MultiLineBaitReservation source = new(bait.m_dropPrefab.name, fromRodBag: true, rod);
        BaitSourceTrackerState.AttackSources.Remove(attack);
        BaitSourceTrackerState.AttackSources.Add(attack, new AttackBaitReturnSourceState(source));
        TrollingFishingPlugin.LogDebug($"[Fishing bait] registered attack bait return source prefab={bait.m_dropPrefab.name} fromRodBag=true.");
    }

    internal static MultiLineBaitReservation ResolveAttackBaitReturnSource(Attack attack, ItemDrop.ItemData? ammo)
    {
        if (!TryGetAttackBaitReturnSource(attack, ammo, out MultiLineBaitReservation source))
        {
            return default;
        }

        BaitSourceTrackerState.AttackSources.Remove(attack);
        return source;
    }

    internal static IDisposable? BeginAttackBaitReturnSourceSetup(Attack attack)
    {
        if (attack == null ||
            !TryGetAttackBaitReturnSource(attack, attack.m_lastUsedAmmo ?? attack.m_ammoItem, out MultiLineBaitReservation source))
        {
            return null;
        }

        return BeginBaitReturnSourceSetup(source, attack);
    }

    private static IDisposable BeginBaitReturnSourceSetup(MultiLineBaitReservation source, Attack? attack = null)
    {
        BaitSourceTrackerState.SetupContexts.Add(source);
        return new BaitReturnSetupScope(attack);
    }

    internal static void MarkFishingFloatBaitReturnSource(FishingFloat fishingFloat, Character owner, ItemDrop.ItemData? ammo)
    {
        MultiLineBaitReservation source = CurrentBaitReturnSetupContext();
        if (fishingFloat == null || !source.IsValid || !IsMatchingBaitReturnAmmo(source, ammo))
        {
            return;
        }

        MarkFishingBaitReturnSource(fishingFloat.gameObject, source);
        TrollingFishingPlugin.LogDebug($"[Fishing bait] marked bait return source prefab={source.PrefabName} fromRodBag={source.FromRodBag}.");
    }

    internal static void MarkProjectileBaitReturnSource(Projectile projectile, ItemDrop.ItemData? ammo)
    {
        MultiLineBaitReservation source = CurrentBaitReturnSetupContext();
        if (projectile == null || !source.IsValid || !IsMatchingBaitReturnAmmo(source, ammo))
        {
            return;
        }

        MarkFishingBaitReturnSource(projectile.gameObject, source);
        TrollingFishingPlugin.LogDebug($"[Fishing bait] marked projectile bait return source prefab={source.PrefabName} fromRodBag={source.FromRodBag}.");
    }

    private static void MarkFishingBaitReturnSource(GameObject sourceObject, MultiLineBaitReservation baitReturnSource, bool settled = false)
    {
        if (sourceObject == null || !baitReturnSource.IsValid)
        {
            return;
        }

        FishingBaitReturnTracker tracker = sourceObject.GetComponent<FishingBaitReturnTracker>() ??
                                           sourceObject.AddComponent<FishingBaitReturnTracker>();
        tracker.Initialize(baitReturnSource, settled);
    }

    private static bool TryGetAttackBaitReturnSource(Attack attack, ItemDrop.ItemData? ammo, out MultiLineBaitReservation source)
    {
        source = default;
        if (attack == null || !BaitSourceTrackerState.AttackSources.TryGetValue(attack, out AttackBaitReturnSourceState state))
        {
            return false;
        }

        if (!IsMatchingBaitReturnAmmo(state.Source, ammo))
        {
            return false;
        }

        source = state.Source;
        return true;
    }

    private static bool IsMatchingBaitReturnAmmo(MultiLineBaitReservation source, ItemDrop.ItemData? ammo)
    {
        return source.IsValid &&
               ammo?.m_dropPrefab != null &&
               string.Equals(source.PrefabName, ammo.m_dropPrefab.name, StringComparison.OrdinalIgnoreCase);
    }

    private static MultiLineBaitReservation CurrentBaitReturnSetupContext()
    {
        return BaitSourceTrackerState.SetupContexts.Count > 0 ? BaitSourceTrackerState.SetupContexts[BaitSourceTrackerState.SetupContexts.Count - 1] : default;
    }

    internal static bool TryReturnBaitToOriginalSource(FishingFloat fishingFloat)
    {
        if (fishingFloat == null)
        {
            return false;
        }

        if (TryReturnTrackedBaitToOriginalSource(fishingFloat.gameObject, fishingFloat))
        {
            return true;
        }

        MultiLineFishingFloatMarker marker = fishingFloat.GetComponent<MultiLineFishingFloatMarker>();
        if (marker == null)
        {
            return false;
        }

        MultiLineBaitReservation source = marker.ReservedBait.IsValid ? marker.ReservedBait : marker.BaitReturnSource;
        if (source.IsValid)
        {
            MarkFishingBaitReturnSource(fishingFloat.gameObject, source);
            return TryReturnTrackedBaitToOriginalSource(fishingFloat.gameObject, fishingFloat);
        }

        return marker.IsAdditionalLine;
    }

    internal static void TrySettleBaitAfterProjectileGroundHit(Projectile projectile, Collider collider, bool water)
    {
        if (water ||
            projectile == null ||
            !ProjectileAccess.GetDidHit(projectile) ||
            !ProjectileAccess.WillDestroyAfterHit(projectile, collider))
        {
            return;
        }

        MultiLineFishingFloatMarker marker = projectile.GetComponent<MultiLineFishingFloatMarker>();
        if (marker == null || !marker.IsAdditionalLine)
        {
            return;
        }

        FishingBaitReturnTracker tracker = projectile.GetComponent<FishingBaitReturnTracker>();
        if (tracker != null && tracker.TrySettle())
        {
            TrollingFishingPlugin.LogDebug($"[Fishing multiLine] settled tracked bait after projectile ground hit object={projectile.gameObject.name} prefab={tracker.Source.PrefabName}.");
        }
    }

    private static bool TryReturnTrackedBaitToOriginalSource(GameObject sourceObject, FishingFloat? fishingFloat)
    {
        if (sourceObject == null)
        {
            return false;
        }

        FishingBaitReturnTracker tracker = sourceObject.GetComponent<FishingBaitReturnTracker>();
        if (tracker == null || !tracker.Source.IsValid)
        {
            return false;
        }

        if (tracker.IsSettled)
        {
            return true;
        }

        if (fishingFloat != null && IsFishingFloatBaitConsumed(fishingFloat))
        {
            tracker.TrySettle();
            return true;
        }

        Character? owner = fishingFloat != null ? fishingFloat.GetOwner() : sourceObject.GetComponent<MultiLineFishingFloatMarker>()?.Owner;
        if (owner is not Player player)
        {
            return false;
        }

        MultiLineBaitReservation source = tracker.Source;
        ItemDrop.ItemData rod = source.Rod ?? sourceObject.GetComponent<MultiLineFishingFloatMarker>()?.Rod ?? player.GetCurrentWeapon();
        if (!TryReturnBaitToRecordedSource(player, rod, source))
        {
            return false;
        }

        tracker.TrySettle();
        TrollingFishingPlugin.LogDebug($"[Fishing bait] returned tracked bait {source.PrefabName} to recorded source fromRodBag={source.FromRodBag}.");
        return true;
    }

    internal static int ResolveMultiLineFishingCount()
    {
        return Mathf.Clamp(
            TrollingFishingPlugin.FishingRodMultiLineCount.Value,
            TrollingFishingPlugin.FishingRodMultiLineMinCount,
            TrollingFishingPlugin.FishingRodMultiLineMaxCount);
    }

    internal static List<MultiLineBaitReservation> ReserveAdditionalMultiLineFishingBaits(Humanoid humanoid, ItemDrop.ItemData weapon, string ammoType, ItemDrop.ItemData targetAmmo, FishingRodAmmoSource source, int amount)
    {
        List<MultiLineBaitReservation> reservations = new();
        if (amount <= 0 ||
            humanoid is not Player player ||
            !IsFishingRod(weapon) ||
            string.IsNullOrWhiteSpace(ammoType) ||
            targetAmmo == null)
        {
            return reservations;
        }

        if (source == FishingRodAmmoSource.FishingRodBag && TrollingFishingPlugin.FishingRodBag.Value.IsOn())
        {
            Inventory bagInventory = LoadFishingRodBagInventory(player, weapon, out _, out _);
            foreach (ItemDrop.ItemData item in ResolveFishingRodBagAmmoRemovalOrder(bagInventory, weapon, ammoType, targetAmmo))
            {
                while (reservations.Count < amount && item.m_stack > 0)
                {
                    string prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
                    if (string.IsNullOrWhiteSpace(prefabName))
                    {
                        break;
                    }

                    bagInventory.RemoveItem(item, 1);
                    reservations.Add(new MultiLineBaitReservation(prefabName, fromRodBag: true));
                }

                if (reservations.Count >= amount)
                {
                    break;
                }
            }

            if (reservations.Any(reservation => reservation.FromRodBag))
            {
                SaveFishingRodBagInventory(weapon, bagInventory);
            }
        }

        if (source == FishingRodAmmoSource.Inventory)
        {
            Inventory playerInventory = player.GetInventory();
            foreach (ItemDrop.ItemData item in ResolveInventoryAmmoRemovalOrder(playerInventory, ammoType, targetAmmo))
            {
                while (reservations.Count < amount && item.m_stack > 0)
                {
                    string prefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
                    if (string.IsNullOrWhiteSpace(prefabName))
                    {
                        break;
                    }

                    playerInventory.RemoveItem(item, 1);
                    reservations.Add(new MultiLineBaitReservation(prefabName, fromRodBag: false));
                }

                if (reservations.Count >= amount)
                {
                    break;
                }
            }
        }

        return reservations;
    }

    internal static bool TryConsumeMultiLineFishingBaitOnCatch(FishingFloat fishingFloat, Fish fish)
    {
        if (fishingFloat == null || fish == null)
        {
            return false;
        }

        FishingBaitReturnTracker tracker = fishingFloat.GetComponent<FishingBaitReturnTracker>();
        if (tracker == null)
        {
            return false;
        }

        if (tracker.TrySettle())
        {
            TrollingFishingPlugin.LogDebug($"[Fishing bait] confirmed tracked bait {tracker.Source.PrefabName} on catch.");
            return true;
        }

        return false;
    }

    private static bool IsFishingFloatBaitConsumed(FishingFloat fishingFloat)
    {
        return FishingFloatBaitConsumedField?.GetValue(fishingFloat) is bool consumed && consumed;
    }

    private static bool TryReturnBaitToRecordedSource(Player player, ItemDrop.ItemData rod, MultiLineBaitReservation reservation)
    {
        if (player == null || !reservation.IsValid || !TryCreateItemFromPrefabName(reservation.PrefabName, out ItemDrop.ItemData baitItem))
        {
            return false;
        }

        if (reservation.FromRodBag &&
            IsFishingRod(rod) &&
            TryAddItemToFishingRodBag(player, rod, baitItem))
        {
            return true;
        }

        Inventory inventory = player.GetInventory();
        if (inventory != null && inventory.AddItem(baitItem))
        {
            return true;
        }

        if (baitItem.m_dropPrefab != null)
        {
            ItemDrop.DropItem(baitItem, 1, player.transform.position + player.transform.forward, player.transform.rotation);
        }

        return true;
    }


    internal readonly struct MultiLineBaitReservation
    {
        internal readonly string PrefabName;
        internal readonly bool FromRodBag;
        internal readonly ItemDrop.ItemData? Rod;

        internal MultiLineBaitReservation(string prefabName, bool fromRodBag, ItemDrop.ItemData? rod = null)
        {
            PrefabName = prefabName ?? "";
            FromRodBag = fromRodBag;
            Rod = rod;
        }

        internal bool IsValid => !string.IsNullOrWhiteSpace(PrefabName);
    }

    internal readonly struct MultiLineFishingSetupContext
    {
        internal static readonly MultiLineFishingSetupContext Empty = new(null, null, 0, 0, default, default);

        internal readonly Character? Owner;
        internal readonly ItemDrop.ItemData? Rod;
        internal readonly int LineIndex;
        internal readonly int PrimaryEquivalentLineIndex;
        internal readonly MultiLineBaitReservation ReservedBait;
        internal readonly MultiLineBaitReservation BaitReturnSource;
        internal readonly bool ConsumeTrackedBaitOnSetup;

        internal MultiLineFishingSetupContext(Character? owner, ItemDrop.ItemData? rod, int lineIndex, int primaryEquivalentLineIndex, MultiLineBaitReservation reservedBait, MultiLineBaitReservation baitReturnSource, bool consumeTrackedBaitOnSetup = false)
        {
            Owner = owner;
            Rod = rod;
            LineIndex = Mathf.Max(0, lineIndex);
            PrimaryEquivalentLineIndex = Mathf.Max(0, primaryEquivalentLineIndex);
            ReservedBait = reservedBait;
            BaitReturnSource = baitReturnSource;
            ConsumeTrackedBaitOnSetup = consumeTrackedBaitOnSetup;
        }
    }

    internal readonly struct MultiLineFishingUpdateContext
    {
        internal readonly Character Owner;
        internal readonly float StaminaFactor;
        internal readonly float SkillRaiseFactor;

        internal MultiLineFishingUpdateContext(Character owner, float staminaFactor, float skillRaiseFactor)
        {
            Owner = owner;
            StaminaFactor = staminaFactor;
            SkillRaiseFactor = skillRaiseFactor;
        }
    }

    private sealed class MultiLineFishingSetupScope : IDisposable
    {
        public void Dispose()
        {
            MultiLineFishingCastState.SetupDepth = Mathf.Max(0, MultiLineFishingCastState.SetupDepth - 1);
            if (MultiLineFishingCastState.SetupContexts.Count > 0)
            {
                MultiLineFishingCastState.SetupContexts.RemoveAt(MultiLineFishingCastState.SetupContexts.Count - 1);
            }
        }
    }

    private sealed class BaitReturnSetupScope : IDisposable
    {
        private readonly Attack? _attack;

        internal BaitReturnSetupScope(Attack? attack)
        {
            _attack = attack;
        }

        public void Dispose()
        {
            if (BaitSourceTrackerState.SetupContexts.Count > 0)
            {
                BaitSourceTrackerState.SetupContexts.RemoveAt(BaitSourceTrackerState.SetupContexts.Count - 1);
            }

            if (_attack != null)
            {
                BaitSourceTrackerState.AttackSources.Remove(_attack);
            }
        }
    }

    private sealed class MultiLineFishingUpdateScope : IDisposable
    {
        public void Dispose()
        {
            if (MultiLineFishingCastState.UpdateContexts.Count > 0)
            {
                MultiLineFishingCastState.UpdateContexts.RemoveAt(MultiLineFishingCastState.UpdateContexts.Count - 1);
            }
        }
    }

    private sealed class MultiLineFishingFloatMarker : MonoBehaviour
    {
        private Character? _owner;
        private ItemDrop.ItemData? _rod;
        private float _ignoreAttackUntil;
        private int _lineIndex;
        private int _primaryEquivalentLineIndex;
        private MultiLineBaitReservation _reservedBait;
        private MultiLineBaitReservation _baitReturnSource;
        private bool _initialAttackEnded;
        private bool _loggedInitialAttackSkip;

        internal Character? Owner => _owner;
        internal ItemDrop.ItemData? Rod => _rod;
        internal int LineIndex => _lineIndex;
        internal int PrimaryEquivalentLineIndex => _primaryEquivalentLineIndex;
        internal bool IsAdditionalLine => _lineIndex != _primaryEquivalentLineIndex;
        internal MultiLineBaitReservation ReservedBait => _reservedBait;
        internal MultiLineBaitReservation BaitReturnSource => _baitReturnSource;

        internal void Initialize(Character owner, ItemDrop.ItemData? rod, float ignoreAttackUntil, int lineIndex, int primaryEquivalentLineIndex, MultiLineBaitReservation reservedBait, MultiLineBaitReservation baitReturnSource)
        {
            _owner = owner;
            _rod = rod;
            _ignoreAttackUntil = ignoreAttackUntil;
            _lineIndex = Mathf.Max(0, lineIndex);
            _primaryEquivalentLineIndex = Mathf.Max(0, primaryEquivalentLineIndex);
            _reservedBait = reservedBait;
            _baitReturnSource = baitReturnSource;
            _initialAttackEnded = false;
            _loggedInitialAttackSkip = false;
        }

        internal bool ShouldSkipAttackCancellation()
        {
            if (_owner == null)
            {
                return false;
            }

            bool ownerStillCasting = _owner.InAttack() || _owner.IsDrawingBow();
            if (!ownerStillCasting)
            {
                _initialAttackEnded = true;
                return false;
            }

            if (_initialAttackEnded || Time.time > _ignoreAttackUntil)
            {
                return false;
            }

            if (!_loggedInitialAttackSkip)
            {
                _loggedInitialAttackSkip = true;
                TrollingFishingPlugin.LogDebug($"[Fishing multiLine] suppressing initial cast cancellation object={gameObject.name}.");
            }

            return true;
        }
    }

    private sealed class FishingBaitReturnTracker : MonoBehaviour
    {
        private MultiLineBaitReservation _source;
        private bool _settled;

        internal MultiLineBaitReservation Source => _source;
        internal bool IsSettled => _settled;

        internal void Initialize(MultiLineBaitReservation source, bool settled = false)
        {
            _source = source;
            _settled = !source.IsValid || settled;
        }

        internal bool TrySettle()
        {
            if (_settled)
            {
                return false;
            }

            _settled = true;
            return true;
        }
    }
}

