using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TrollingFishing;

internal static class MultiLineFishingSystem
{
    private const string FishingRodPrefabName = "FishingRod";
    private static readonly MethodInfo MemberwiseCloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone")!;
    private static readonly ConditionalWeakTable<Player, DrawState> DrawStates = new();
    private static readonly ConditionalWeakTable<Humanoid, AmmoSelectionState> AmmoSelectionStates = new();

    internal static bool IsFishingRod(ItemDrop.ItemData? item)
    {
        return item?.m_dropPrefab != null &&
               string.Equals(item.m_dropPrefab.name, FishingRodPrefabName, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool TryPrepareSecondaryStart(Humanoid humanoid, ItemDrop.ItemData? weapon, ref bool result)
    {
        if (!IsFishingRod(weapon))
        {
            return true;
        }

        if (TrollingFishingPlugin.FishingRodMultiLine.Value.IsOff())
        {
            result = false;
            return false;
        }

        EnsureFishingRodSecondaryAttack(weapon!);
        string ammoType = weapon!.m_shared.m_ammoType;
        if (string.IsNullOrWhiteSpace(ammoType))
        {
            return true;
        }

        int requiredAmmo = ResolveCount();
        if (FishingOverrideSystem.TryResolveFishingRodAmmoSelection(humanoid, weapon, out FishingOverrideSystem.FishingRodAmmoSelection selection) &&
            FishingOverrideSystem.CountAvailableAmmoFromSource(humanoid, weapon, ammoType, selection.AmmoItem, selection.Source) >= requiredAmmo)
        {
            RememberAmmoSelection(humanoid, weapon, selection);
            return true;
        }

        ForgetAmmoSelection(humanoid);
        humanoid.Message(MessageHud.MessageType.Center, "$msg_outof " + ammoType);
        result = false;
        return false;
    }

    internal static bool ShouldHandleFishingRodDraw(ItemDrop.ItemData? weapon)
    {
        return IsFishingRod(weapon) &&
               weapon!.m_shared.m_attack != null &&
               weapon.m_shared.m_attack.m_bowDraw;
    }

    internal static void UpdateFishingRodDraw(
        Player player,
        ItemDrop.ItemData weapon,
        float dt,
        ref float attackDrawTime,
        bool blocking,
        bool attackHold,
        bool secondaryAttackHold,
        bool secondaryAttackPressed,
        ZSyncAnimation zanim)
    {
        DrawState state = DrawStates.GetValue(player, _ => new DrawState());
        if (state.Weapon != weapon)
        {
            state.Weapon = weapon;
            state.PendingSecondary = false;
        }

        bool drawHeld = attackHold || secondaryAttackHold;
        float drawStaminaDrain = GetSkillAdjustedDrawCost(player, weapon, weapon.m_shared.m_attack.m_drawStaminaDrain);
        float drawEitrDrain = weapon.m_shared.m_attack.m_drawEitrDrain;
        bool hasStamina = drawStaminaDrain <= 0f || player.HaveStamina();
        bool hasEitr = drawEitrDrain <= 0f || player.HaveEitr();

        if (blocking || player.InMinorAction() || player.IsAttached())
        {
            attackDrawTime = -1f;
            state.PendingSecondary = false;
            SetDrawAnimation(zanim, weapon, false, player);
            return;
        }

        if (drawHeld && attackDrawTime == 0f)
        {
            state.PendingSecondary = secondaryAttackHold || secondaryAttackPressed;
            if (state.PendingSecondary)
            {
                EnsureFishingRodSecondaryAttack(weapon);
            }
        }
        else if (secondaryAttackPressed && drawHeld && attackDrawTime >= 0f)
        {
            state.PendingSecondary = true;
            EnsureFishingRodSecondaryAttack(weapon);
        }

        if (attackDrawTime < 0f)
        {
            if (!drawHeld)
            {
                attackDrawTime = 0f;
            }

            return;
        }

        if (drawHeld && hasStamina && hasEitr && attackDrawTime >= 0f)
        {
            if (attackDrawTime == 0f)
            {
                if (!weapon.m_shared.m_attack.StartDraw(player, weapon))
                {
                    attackDrawTime = -1f;
                    state.PendingSecondary = false;
                    return;
                }

                weapon.m_shared.m_holdStartEffect.Create(player.transform.position, Quaternion.identity, player.transform);
            }

            attackDrawTime += Time.fixedDeltaTime;
            SetDrawAnimation(zanim, weapon, true, player);
            player.UseStamina(drawStaminaDrain * dt);
            player.UseEitr(drawEitrDrain * dt);
            return;
        }

        if (attackDrawTime > 0f)
        {
            if (hasStamina && hasEitr)
            {
                bool pendingSecondary = state.PendingSecondary;
                float extraStaminaCost = ResolveSecondaryReleaseExtraStamina(player, weapon, drawStaminaDrain, pendingSecondary);
                if (extraStaminaCost <= 0f || player.HaveStamina(extraStaminaCost))
                {
                    bool started = player.StartAttack(null, pendingSecondary);
                    if (started && pendingSecondary && extraStaminaCost > 0f)
                    {
                        player.UseStamina(extraStaminaCost);
                    }
                }
                else
                {
                    Hud.instance?.StaminaBarEmptyFlash();
                }
            }

            SetDrawAnimation(zanim, weapon, false, player);
            attackDrawTime = 0f;
            state.PendingSecondary = false;
        }
    }

    internal static void EnsureFishingRodSecondaryAttack(ItemDrop.ItemData? weapon)
    {
        if (!IsFishingRod(weapon) || weapon!.m_shared.m_attack == null)
        {
            return;
        }

        Attack secondary = CloneAttack(weapon.m_shared.m_attack);
        float multiplier = Mathf.Max(0f, TrollingFishingPlugin.FishingRodMultiLineCastResourceFactor.Value);
        secondary.m_attackStamina *= multiplier;
        secondary.m_attackEitr *= multiplier;
        secondary.m_attackHealth *= multiplier;
        secondary.m_drawStaminaDrain *= multiplier;
        secondary.m_drawEitrDrain *= multiplier;
        weapon.m_shared.m_secondaryAttack = secondary;
    }

    private static float GetSkillAdjustedDrawCost(Player player, ItemDrop.ItemData weapon, float rawDrawCost)
    {
        if (rawDrawCost <= 0f)
        {
            return 0f;
        }

        float skillFactor = player.GetSkillFactor(weapon.m_shared.m_skillType);
        return rawDrawCost - rawDrawCost * 0.33f * skillFactor;
    }

    private static float ResolveSecondaryReleaseExtraStamina(Player player, ItemDrop.ItemData weapon, float drawStaminaDrain, bool pendingSecondary)
    {
        if (!pendingSecondary || drawStaminaDrain <= 0f)
        {
            return 0f;
        }

        float resourceMultiplier = Mathf.Max(0f, TrollingFishingPlugin.FishingRodMultiLineCastResourceFactor.Value);
        if (resourceMultiplier <= 1f)
        {
            return 0f;
        }

        float fullChargeTime = GetSkillAdjustedFullDrawTime(player, weapon);
        return drawStaminaDrain * fullChargeTime * (resourceMultiplier - 1f);
    }

    private static float GetSkillAdjustedFullDrawTime(Player player, ItemDrop.ItemData weapon)
    {
        float baseFullChargeTime = Mathf.Max(0f, weapon.m_shared.m_attack.m_drawDurationMin);
        if (baseFullChargeTime <= 0f)
        {
            return 0f;
        }

        float skillFactor = player.GetSkillFactor(weapon.m_shared.m_skillType);
        return Mathf.Lerp(baseFullChargeTime, baseFullChargeTime * 0.2f, skillFactor);
    }

    private static void RememberAmmoSelection(Humanoid humanoid, ItemDrop.ItemData weapon, FishingOverrideSystem.FishingRodAmmoSelection selection)
    {
        if (humanoid == null || weapon == null || !selection.IsValid)
        {
            return;
        }

        AmmoSelectionState state = AmmoSelectionStates.GetValue(humanoid, _ => new AmmoSelectionState());
        state.Weapon = weapon;
        state.AmmoItem = selection.AmmoItem;
        state.Source = selection.Source;
        state.CreatedAt = Time.time;
    }

    private static void ForgetAmmoSelection(Humanoid humanoid)
    {
        if (humanoid != null)
        {
            AmmoSelectionStates.Remove(humanoid);
        }
    }

    private static bool TryResolvePreparedAmmoSelection(Attack attack, ItemDrop.ItemData? attackAmmo, out FishingOverrideSystem.FishingRodAmmoSelection selection)
    {
        selection = default;
        if (attack?.m_character is Humanoid humanoid &&
            AmmoSelectionStates.TryGetValue(humanoid, out AmmoSelectionState state) &&
            ReferenceEquals(state.Weapon, attack.m_weapon) &&
            !state.IsExpired &&
            state.AmmoItem != null)
        {
            selection = new FishingOverrideSystem.FishingRodAmmoSelection(attackAmmo ?? state.AmmoItem, state.Source);
            AmmoSelectionStates.Remove(humanoid);
            return true;
        }

        if (attack != null &&
            FishingOverrideSystem.TryResolveFishingRodAmmoSelection(attack.m_character, attack.m_weapon, out selection))
        {
            return true;
        }

        return false;
    }

    private static void SetDrawAnimation(ZSyncAnimation zanim, ItemDrop.ItemData weapon, bool value, Player player)
    {
        if (string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
        {
            return;
        }

        zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value);
        if (value)
        {
            zanim.SetFloat("drawpercent", player.GetAttackDrawPercentage());
        }
    }

    internal static bool IsActiveMultiLineAttack(Attack attack)
    {
        return attack != null &&
               IsFishingRod(attack.m_weapon) &&
               attack.m_character is Humanoid humanoid &&
               ReferenceEquals(humanoid.m_currentAttack, attack) &&
               humanoid.m_currentAttackIsSecondary;
    }

    internal static bool TryHandleFireProjectileBurst(Attack attack)
    {
        if (!IsActiveMultiLineAttack(attack))
        {
            return false;
        }

        if (TrollingFishingPlugin.FishingRodMultiLine.Value.IsOff())
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(attack.m_weapon.m_shared.m_ammoType))
        {
            return false;
        }

        int configuredFloatCount = ResolveCount();
        int additionalFloatCount = Mathf.Max(0, configuredFloatCount - 1);
        ItemDrop.ItemData ammoItem = attack.m_ammoItem ?? attack.m_lastUsedAmmo;
        if (!TryResolvePreparedAmmoSelection(attack, ammoItem, out FishingOverrideSystem.FishingRodAmmoSelection selection))
        {
            attack.m_character.Message(MessageHud.MessageType.Center, "$msg_outof " + attack.m_weapon.m_shared.m_ammoType);
            return true;
        }

        ammoItem ??= selection.AmmoItem;
        if (FishingOverrideSystem.CountAvailableAmmoFromSource(attack.m_character, attack.m_weapon, attack.m_weapon.m_shared.m_ammoType, ammoItem, selection.Source) < additionalFloatCount)
        {
            attack.m_character.Message(MessageHud.MessageType.Center, "$msg_outof " + attack.m_weapon.m_shared.m_ammoType);
            return true;
        }

        attack.m_ammoItem = ammoItem;
        attack.m_lastUsedAmmo = ammoItem;
        LaunchData launchData = CreateLaunchData(attack);
        if (!launchData.IsValid)
        {
            return true;
        }

        List<FishingOverrideSystem.MultiLineBaitReservation> baitReservations =
            FishingOverrideSystem.ReserveAdditionalMultiLineFishingBaits(
                attack.m_character,
                attack.m_weapon,
                attack.m_weapon.m_shared.m_ammoType,
                ammoItem,
                selection.Source,
                additionalFloatCount);
        if (baitReservations.Count < additionalFloatCount)
        {
            attack.m_character.Message(MessageHud.MessageType.Center, "$msg_outof " + attack.m_weapon.m_shared.m_ammoType);
            return true;
        }

        int floatCount = Mathf.Max(1, baitReservations.Count + 1);
        int primaryEquivalentLineIndex = GetPrimaryEquivalentLineIndex(floatCount);
        FishingOverrideSystem.MultiLineBaitReservation primaryBaitReturnSource =
            FishingOverrideSystem.ResolveAttackBaitReturnSource(attack, attack.m_lastUsedAmmo);

        FishingOverrideSystem.DestroyExistingFishingFloats(attack.m_character);
        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(spawnPoint, Quaternion.LookRotation(aimDirection));
        }

        Vector3 rotationAxis = attack.m_character.transform.up;
        float spreadAngle = Mathf.Max(0f, TrollingFishingPlugin.FishingRodMultiLineSpreadAngle.Value);
        float startAngle = floatCount > 1 ? -spreadAngle * 0.5f : 0f;
        float angleStep = floatCount > 1 ? spreadAngle / (floatCount - 1) : 0f;
        using (FishingOverrideSystem.BeginMultiLineFishingSetup())
        {
            float speed = ResolveProjectileSpeed(launchData);
            int reservationIndex = 0;
            for (int projectileIndex = 0; projectileIndex < floatCount; projectileIndex++)
            {
                float angleOffset = startAngle + angleStep * projectileIndex;
                Vector3 direction = Quaternion.AngleAxis(angleOffset, rotationAxis) * aimDirection;
                FishingOverrideSystem.MultiLineBaitReservation reservation = default;
                if (projectileIndex != primaryEquivalentLineIndex && reservationIndex < baitReservations.Count)
                {
                    reservation = baitReservations[reservationIndex++];
                }

                GameObject projectileObject = SpawnProjectileObject(attack, launchData, spawnPoint, direction, speed);
                FishingOverrideSystem.MarkMultiLineFishingObject(
                    projectileObject,
                    attack.m_character,
                    projectileIndex,
                    primaryEquivalentLineIndex,
                    reservation,
                    attack.m_weapon,
                    projectileIndex == primaryEquivalentLineIndex ? primaryBaitReturnSource : default);
            }
        }

        return true;
    }

    private static int ResolveCount()
    {
        return Mathf.Clamp(
            TrollingFishingPlugin.FishingRodMultiLineCount.Value,
            TrollingFishingPlugin.FishingRodMultiLineMinCount,
            TrollingFishingPlugin.FishingRodMultiLineMaxCount);
    }

    private static int GetPrimaryEquivalentLineIndex(int floatCount)
    {
        return Mathf.Max(0, (floatCount - 1) / 2);
    }

    private static Attack CloneAttack(Attack? sourceAttack)
    {
        return sourceAttack == null
            ? new Attack()
            : (Attack)MemberwiseCloneMethod.Invoke(sourceAttack, Array.Empty<object>())!;
    }

    private static LaunchData CreateLaunchData(Attack attack)
    {
        ItemDrop.ItemData ammoItem = attack.m_ammoItem;
        GameObject projectilePrefab = attack.m_attackProjectile;
        float projectileVelocity = attack.m_projectileVel;
        float projectileVelocityMin = attack.m_projectileVelMin;
        float projectileAccuracy = attack.m_projectileAccuracy;
        float projectileAccuracyMin = attack.m_projectileAccuracyMin;
        float attackHitNoise = attack.m_attackHitNoise;
        AnimationCurve drawVelocityCurve = attack.m_drawVelocityCurve;

        if (ammoItem != null && ammoItem.m_shared.m_attack.m_attackProjectile != null)
        {
            projectilePrefab = ammoItem.m_shared.m_attack.m_attackProjectile;
            projectileVelocity += ammoItem.m_shared.m_attack.m_projectileVel;
            projectileVelocityMin += ammoItem.m_shared.m_attack.m_projectileVelMin;
            projectileAccuracy += ammoItem.m_shared.m_attack.m_projectileAccuracy;
            projectileAccuracyMin += ammoItem.m_shared.m_attack.m_projectileAccuracyMin;
            attackHitNoise += ammoItem.m_shared.m_attack.m_attackHitNoise;
            drawVelocityCurve = ammoItem.m_shared.m_attack.m_drawVelocityCurve;
        }

        if (projectilePrefab == null)
        {
            return LaunchData.Invalid;
        }

        float damageFactor = attack.m_character.GetRandomSkillFactor(attack.m_weapon.m_shared.m_skillType);
        if (attack.m_bowDraw)
        {
            projectileAccuracy = Mathf.Lerp(projectileAccuracyMin, projectileAccuracy, Mathf.Pow(attack.m_attackDrawPercentage, 0.5f));
            damageFactor *= attack.m_attackDrawPercentage;
            projectileVelocity = Mathf.Lerp(projectileVelocityMin, projectileVelocity, drawVelocityCurve.Evaluate(attack.m_attackDrawPercentage));
            if (attack.m_character is Player)
            {
                Game.instance.IncrementPlayerStat(PlayerStatType.ArrowsShot);
            }
        }
        else if (attack.m_skillAccuracy)
        {
            float skillFactor = attack.m_character.GetSkillFactor(attack.m_weapon.m_shared.m_skillType);
            projectileAccuracy = Mathf.Lerp(projectileAccuracyMin, projectileAccuracy, skillFactor);
        }

        return new LaunchData(
            projectilePrefab,
            ammoItem,
            projectileVelocity,
            projectileVelocityMin,
            projectileAccuracy,
            projectileAccuracyMin,
            attackHitNoise,
            damageFactor,
            attack.m_randomVelocity && !attack.m_bowDraw);
    }

    private static GameObject SpawnProjectileObject(Attack attack, LaunchData launchData, Vector3 spawnPoint, Vector3 direction, float speed)
    {
        if (direction == Vector3.zero)
        {
            direction = attack.m_character.transform.forward;
        }

        direction.Normalize();
        GameObject projectileObject = Object.Instantiate(launchData.ProjectilePrefab!, spawnPoint, Quaternion.LookRotation(direction));
        HitData hitData = CreateProjectileHitData(attack, launchData.AmmoItem, launchData.DamageFactor);
        IProjectile projectile = projectileObject.GetComponent<IProjectile>();
        Projectile projectileComponent = projectileObject.GetComponent<Projectile>();
        FishingFloat fishingFloat = projectileObject.GetComponent<FishingFloat>();
        projectile?.Setup(attack.m_character, direction * speed, launchData.AttackHitNoise, hitData, attack.m_weapon, attack.m_lastUsedAmmo);
        if (fishingFloat != null)
        {
            if (projectileComponent != null && !ReferenceEquals(projectile, projectileComponent))
            {
                projectileComponent.Setup(attack.m_character, direction * speed, launchData.AttackHitNoise, hitData, attack.m_weapon, attack.m_lastUsedAmmo);
            }

            if (!ReferenceEquals(projectile, fishingFloat) && attack.m_lastUsedAmmo != null)
            {
                fishingFloat.Setup(attack.m_character, direction * speed, launchData.AttackHitNoise, hitData, attack.m_weapon, attack.m_lastUsedAmmo);
            }
        }

        attack.m_weapon.m_lastProjectile = projectileObject;
        if (attack.m_spawnOnHitChance > 0f && attack.m_spawnOnHit != null && projectile is Projectile baseProjectile)
        {
            baseProjectile.m_spawnOnHit = attack.m_spawnOnHit;
            baseProjectile.m_spawnOnHitChance = attack.m_spawnOnHitChance;
        }

        return projectileObject;
    }

    private static HitData CreateProjectileHitData(Attack attack, ItemDrop.ItemData? ammoItem, float damageFactor)
    {
        HitData hitData = new();
        hitData.m_toolTier = (short)attack.m_weapon.m_shared.m_toolTier;
        hitData.m_pushForce = attack.m_weapon.m_shared.m_attackForce * attack.m_forceMultiplier;
        hitData.m_backstabBonus = attack.m_weapon.m_shared.m_backstabBonus;
        hitData.m_staggerMultiplier = attack.m_staggerMultiplier;
        hitData.m_damage.Add(attack.m_weapon.GetDamage());
        hitData.m_statusEffectHash = attack.m_weapon.m_shared.m_attackStatusEffect != null &&
                                     (attack.m_weapon.m_shared.m_attackStatusEffectChance == 1f || UnityEngine.Random.Range(0f, 1f) < attack.m_weapon.m_shared.m_attackStatusEffectChance)
            ? attack.m_weapon.m_shared.m_attackStatusEffect.NameHash()
            : 0;
        hitData.m_skillLevel = attack.m_character.GetSkillLevel(attack.m_weapon.m_shared.m_skillType);
        hitData.m_itemLevel = (short)attack.m_weapon.m_quality;
        hitData.m_itemWorldLevel = (byte)attack.m_weapon.m_worldLevel;
        hitData.m_blockable = attack.m_weapon.m_shared.m_blockable;
        hitData.m_dodgeable = attack.m_weapon.m_shared.m_dodgeable;
        hitData.m_skill = attack.m_weapon.m_shared.m_skillType;
        hitData.m_skillRaiseAmount = attack.m_raiseSkillAmount;
        hitData.SetAttacker(attack.m_character);
        hitData.m_hitType = hitData.GetAttacker() is Player ? HitData.HitType.PlayerHit : HitData.HitType.EnemyHit;
        hitData.m_healthReturn = attack.m_attackHealthReturnHit;

        if (ammoItem != null)
        {
            hitData.m_damage.Add(ammoItem.GetDamage());
            hitData.m_pushForce += ammoItem.m_shared.m_attackForce;
            if (ammoItem.m_shared.m_attackStatusEffect != null &&
                (ammoItem.m_shared.m_attackStatusEffectChance == 1f || UnityEngine.Random.Range(0f, 1f) < ammoItem.m_shared.m_attackStatusEffectChance))
            {
                hitData.m_statusEffectHash = ammoItem.m_shared.m_attackStatusEffect.NameHash();
            }

            if (!ammoItem.m_shared.m_blockable)
            {
                hitData.m_blockable = false;
            }

            if (!ammoItem.m_shared.m_dodgeable)
            {
                hitData.m_dodgeable = false;
            }
        }

        hitData.m_pushForce *= damageFactor;
        attack.ModifyDamage(hitData, damageFactor);
        attack.m_character.GetSEMan().ModifyAttack(attack.m_weapon.m_shared.m_skillType, ref hitData);
        return hitData;
    }

    private static float ResolveProjectileSpeed(LaunchData launchData)
    {
        float speed = launchData.UseRandomVelocity
            ? UnityEngine.Random.Range(launchData.ProjectileVelocityMin, launchData.ProjectileVelocity)
            : launchData.ProjectileVelocity;
        return Mathf.Max(0.01f, speed);
    }

    private static Vector3 ApplyLaunchAngle(Attack attack, Vector3 aimDirection)
    {
        if (attack.m_launchAngle == 0f)
        {
            return aimDirection;
        }

        Vector3 axis = Vector3.Cross(Vector3.up, aimDirection);
        if (axis == Vector3.zero)
        {
            axis = attack.m_character.transform.right;
        }

        return Quaternion.AngleAxis(attack.m_launchAngle, axis) * aimDirection;
    }

    private static void PrepareCustomProjectileBurst(Attack attack)
    {
        if (attack.m_destroyPreviousProjectile && attack.m_weapon.m_lastProjectile != null)
        {
            ZNetScene.instance.Destroy(attack.m_weapon.m_lastProjectile);
            attack.m_weapon.m_lastProjectile = null;
        }
    }

    private readonly struct LaunchData
    {
        public static readonly LaunchData Invalid = new(null, null, 0f, 0f, 0f, 0f, 0f, 0f, false);

        public LaunchData(
            GameObject? projectilePrefab,
            ItemDrop.ItemData? ammoItem,
            float projectileVelocity,
            float projectileVelocityMin,
            float projectileAccuracy,
            float projectileAccuracyMin,
            float attackHitNoise,
            float damageFactor,
            bool useRandomVelocity)
        {
            ProjectilePrefab = projectilePrefab;
            AmmoItem = ammoItem;
            ProjectileVelocity = projectileVelocity;
            ProjectileVelocityMin = projectileVelocityMin;
            ProjectileAccuracy = projectileAccuracy;
            ProjectileAccuracyMin = projectileAccuracyMin;
            AttackHitNoise = attackHitNoise;
            DamageFactor = damageFactor;
            UseRandomVelocity = useRandomVelocity;
        }

        public GameObject? ProjectilePrefab { get; }

        public ItemDrop.ItemData? AmmoItem { get; }

        public float ProjectileVelocity { get; }

        public float ProjectileVelocityMin { get; }

        public float ProjectileAccuracy { get; }

        public float ProjectileAccuracyMin { get; }

        public float AttackHitNoise { get; }

        public float DamageFactor { get; }

        public bool UseRandomVelocity { get; }

        public bool IsValid => ProjectilePrefab != null;
    }

    private sealed class DrawState
    {
        public ItemDrop.ItemData? Weapon;
        public bool PendingSecondary;
    }

    private sealed class AmmoSelectionState
    {
        public ItemDrop.ItemData? Weapon;
        public ItemDrop.ItemData? AmmoItem;
        public FishingOverrideSystem.FishingRodAmmoSource Source;
        public float CreatedAt;

        public bool IsExpired => Time.time - CreatedAt > 10f;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdateAttackBowDraw))]
internal static class PlayerUpdateAttackBowDrawMultiLineFishingPatch
{
    private static bool Prefix(
        Player __instance,
        ItemDrop.ItemData weapon,
        float dt,
        ref float ___m_attackDrawTime,
        bool ___m_blocking,
        bool ___m_attackHold,
        bool ___m_secondaryAttackHold,
        bool ___m_secondaryAttack,
        ZSyncAnimation ___m_zanim)
    {
        if (!MultiLineFishingSystem.ShouldHandleFishingRodDraw(weapon))
        {
            return true;
        }

        MultiLineFishingSystem.UpdateFishingRodDraw(
            __instance,
            weapon,
            dt,
            ref ___m_attackDrawTime,
            ___m_blocking,
            ___m_attackHold,
            ___m_secondaryAttackHold,
            ___m_secondaryAttack,
            ___m_zanim);
        return false;
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class HumanoidStartAttackMultiLineFishingPatch
{
    private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result)
    {
        if (!secondaryAttack)
        {
            return true;
        }

        ItemDrop.ItemData currentWeapon = __instance.GetCurrentWeapon();
        return MultiLineFishingSystem.TryPrepareSecondaryStart(__instance, currentWeapon, ref __result);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
internal static class AttackFireProjectileBurstMultiLineFishingPatch
{
    private static bool Prefix(Attack __instance, out IDisposable? __state)
    {
        __state = null;
        if (MultiLineFishingSystem.TryHandleFireProjectileBurst(__instance))
        {
            return false;
        }

        __state = FishingOverrideSystem.BeginAttackBaitReturnSourceSetup(__instance);
        return true;
    }

    private static void Postfix(IDisposable? __state)
    {
        __state?.Dispose();
    }
}
