// Managed boundary doubles. No Unity scene, Harmony dispatch, real serializer, or PoV DLL is run.
#nullable disable
using System.Reflection;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class HarmonyPatch(Type type, string method) : Attribute { public Type Type = type; public string Method = method; }
    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyAfter(string owner) : Attribute { public string Owner = owner; }
    public static class AccessTools
    {
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
namespace UnityEngine.UI { public class Image { } }
namespace UnityEngine
{
    public class Object
    {
        public static int InstantiateCalls;
        public static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            InstantiateCalls++;
            return new GameObject();
        }
    }
    public class Component : Object
    {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
    }
    public class GameObject : Object
    {
        public Transform transform = new();
        public T GetComponent<T>() where T : class => null;
    }
    public class Transform { public Vector3 position, up, forward, right; }
    public record struct Vector3(float x, float y, float z)
    {
        public static Vector3 zero => default;
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 Cross(Vector3 a, Vector3 b) => default;
        public static Vector3 operator *(Vector3 v, float f) => new(v.x * f, v.y * f, v.z * f);
        public void Normalize() { }
    }
    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion LookRotation(Vector3 v) => default;
        public static Quaternion AngleAxis(float angle, Vector3 axis) => default;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }
    public class AnimationCurve { public float Evaluate(float v) => v; }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Clamp(int x, int min, int max) => Math.Clamp(x, min, max);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
    }
    public static class Time { public static float time, fixedDeltaTime = 0.02f; }
    public static class Random { public static float Range(float min, float max) => min; }
}
public class Inventory { }
public class Character : Component
{
    public List<string> Messages = new();
    public void Message(MessageHud.MessageType type, string text) => Messages.Add(text);
    public float GetSkillLevel(Skills.SkillType skill) => 0;
    public float GetRandomSkillFactor(Skills.SkillType skill) => 0;
    public long GetZDOID() => 0;
    public SEMan GetSEMan() => new();
}
public class Humanoid : Character
{
    public ItemDrop.ItemData Weapon;
    public Attack CurrentAttack;
    public bool Secondary = true;
    public ItemDrop.ItemData GetCurrentWeapon() => Weapon;
    public bool StartAttack(Character target, bool secondary) => true;
    public void UseItem() { }
}
public class Player : Humanoid
{
    public static Player m_localPlayer;
    public float m_blockReload;
    public bool HaveStamina(float v = 0) => true;
    public bool HaveEitr() => true;
    public bool InMinorAction() => false;
    public bool IsAttached() => false;
    public void UseStamina(float v) { }
    public void UseEitr(float v) { }
    public float GetSkillFactor(Skills.SkillType skill) => 0;
    public float GetAttackDrawPercentage() => 1;
}
public static class MessageHud { public enum MessageType { Center } }
public static class Skills { public enum SkillType { Fishing } }
public class ZSyncAnimation { public void SetBool(string name, bool value) { } public void SetFloat(string name, float value) { } }
public class Hud { public static Hud instance; public void StaminaBarEmptyFlash() { } }
public class EffectList
{
    public bool HasEffects() => false;
    public void Create(Vector3 v, Quaternion q, Transform t = null, float scale = 1, int variant = -1, long id = 0) { }
}
public class StatusEffect { public int NameHash() => 0; }
public class SEMan { public void ModifyAttack(Skills.SkillType skill, ref HitData hit) { } }
public class HitData
{
    public enum HitType { PlayerHit, EnemyHit }
    public class DamageTypes { public void Add(DamageTypes damage) { } }
    private Character attacker;
    public short m_toolTier, m_itemLevel;
    public byte m_itemWorldLevel;
    public float m_pushForce, m_backstabBonus, m_staggerMultiplier, m_skillLevel, m_skillRaiseAmount, m_healthReturn, m_eitrAdd;
    public int m_statusEffectHash, m_variant;
    public bool m_blockable, m_dodgeable;
    public Skills.SkillType m_skill;
    public HitType m_hitType;
    public DamageTypes m_damage = new();
    public void SetAttacker(Character who) => attacker = who;
    public Character GetAttacker() => attacker;
}
public class ItemDrop
{
    public class ItemData
    {
        public bool IsRod;
        public SharedData m_shared = new();
        public int m_quality = 1, m_worldLevel;
        public GameObject m_lastProjectile;
        public float GetDrawStaminaDrain() => m_shared.m_attack.m_drawStaminaDrain;
        public HitData.DamageTypes GetDamage() => new();
    }
    public class SharedData
    {
        public string m_ammoType = "bait";
        public Attack m_attack = new(), m_secondaryAttack;
        public Skills.SkillType m_skillType;
        public EffectList m_holdStartEffect = new();
        public int m_toolTier, m_hitVariant;
        public float m_attackForce, m_backstabBonus, m_attackStatusEffectChance;
        public StatusEffect m_attackStatusEffect;
        public bool m_blockable, m_dodgeable;
    }
}
public class Attack
{
    public ItemDrop.ItemData Weapon, Ammo, LastAmmo;
    public Humanoid Owner;
    public float m_attackStamina, m_attackEitr, m_attackHealth, m_drawStaminaDrain, m_drawEitrDrain, m_drawDurationMin;
    public float m_projectileVel, m_projectileVelMin, m_attackHitNoise, m_blockReloadTime, m_spawnOnHitChance, m_forceMultiplier, m_staggerMultiplier;
    public float m_raiseSkillAmount, m_attackHealthReturnHit, m_attackEitrAdd, m_launchAngle;
    public bool m_bowDraw, m_randomVelocity, m_destroyPreviousProjectile;
    public string m_drawAnimationState;
    public GameObject m_attackProjectile, m_spawnOnHit;
    public EffectList m_burstEffect = new();
    public AnimationCurve m_drawVelocityCurve = new();
    public ItemDrop.ItemData GetWeapon() => Weapon;
    public bool StartDraw(Player player, ItemDrop.ItemData weapon) => true;
}
public interface IProjectile { void Setup(Character owner, Vector3 velocity, float noise, HitData hit, ItemDrop.ItemData weapon, ItemDrop.ItemData ammo); }
public class Projectile : IProjectile
{
    public GameObject m_spawnOnHit;
    public float m_spawnOnHitChance;
    public void Setup(Character owner, Vector3 velocity, float noise, HitData hit, ItemDrop.ItemData weapon, ItemDrop.ItemData ammo) { }
}
public class FishingFloat : IProjectile
{
    public void Setup(Character owner, Vector3 velocity, float noise, HitData hit, ItemDrop.ItemData weapon, ItemDrop.ItemData ammo) { }
}
public class ZNetScene { public static ZNetScene instance = new(); public void Destroy(GameObject go) { } }
public enum PlayerStatType { ArrowsShot }
public class Game { public static Game instance = new(); public void IncrementPlayerStat(PlayerStatType stat) { } }

namespace TrollingFishing
{
    internal enum Toggle { Off, On }
    internal static class Toggles { internal static bool IsOff(this Toggle x) => x == Toggle.Off; }
    internal sealed class Config<T>(T initial) { internal T Value = initial; }
    internal static class TrollingFishingPlugin
    {
        internal static Config<Toggle> FishingRodMultiLine = new(Toggle.On);
        internal static Config<int> FishingRodMultiLineCount = new(3);
        internal const int FishingRodMultiLineMinCount = 2, FishingRodMultiLineMaxCount = 10;
        internal static Config<float> FishingRodMultiLineCastResourceFactor = new(1), FishingRodMultiLineSpreadAngle = new(20);
    }
    internal static class PathOfValheimanCompatibility
    {
        internal const string PluginGuid = "treextr.pathofvalheiman";
        internal static bool Known = true, Free, Valid = true;
        internal static ItemDrop.ItemData NativeAmmo;
        internal static bool TryGetFreeCast(Humanoid owner, ItemDrop.ItemData rod, out bool free) { free = Free; return Known; }
        internal static bool ValidateNativeBaitPatches() => Valid;
        internal static bool IsNativeAmmo(Attack attack, ItemDrop.ItemData ammo) => ammo != null && ReferenceEquals(ammo, NativeAmmo);
    }
    internal static class FishingGameAccess
    {
        internal static Humanoid AttackCharacter(Attack attack) => attack.Owner;
        internal static Attack CurrentAttack(Humanoid who) => who.CurrentAttack;
        internal static bool IsSecondaryAttack(Humanoid who) => who.Secondary;
        internal static ref ItemDrop.ItemData AttackAmmo(Attack attack) => ref attack.Ammo;
        internal static ref ItemDrop.ItemData LastAmmo(Attack attack) => ref attack.LastAmmo;
        internal static float DrawPercentage(Attack attack) => 1;
        internal static void GetProjectileSpawnPoint(Attack attack, out Vector3 point, out Vector3 direction) { point = default; direction = new(0, 0, 1); }
        internal static void ModifyDamage(Attack attack, HitData hit, float factor) { }
    }
    internal static class FishingOverrideSystem
    {
        internal enum FishingRodAmmoSource { Inventory, FishingRodBag }
        internal readonly record struct FishingRodAmmoSelection(ItemDrop.ItemData AmmoItem, FishingRodAmmoSource Source) { internal bool IsValid => AmmoItem != null; }
        internal readonly record struct MultiLineBaitReservation(string PrefabName) { internal bool IsValid => PrefabName != null; }
        internal static ItemDrop.ItemData SelectedAmmo;
        internal static int Available, CountCalls, ReserveCalls, Reserved, BagUseCalls, DestroyCalls, ScopeDepth;
        internal static bool BagHandled, BagResult;
        internal static List<(bool Free, bool Reserved, int Index)> Marks = new();
        internal static List<bool> Scopes = new();
        internal static bool IsFishingRod(ItemDrop.ItemData weapon) => weapon?.IsRod == true;
        internal static bool TryResolveFishingRodAmmoSelection(Humanoid owner, ItemDrop.ItemData rod, out FishingRodAmmoSelection selection)
        { selection = new(SelectedAmmo, FishingRodAmmoSource.Inventory); return selection.IsValid; }
        internal static int CountAvailableAmmoFromSource(Humanoid owner, ItemDrop.ItemData rod, string type, ItemDrop.ItemData item, FishingRodAmmoSource source)
        { CountCalls++; return Available; }
        internal static List<MultiLineBaitReservation> ReserveAdditionalMultiLineFishingBaits(Humanoid owner, ItemDrop.ItemData rod, string type, ItemDrop.ItemData item, FishingRodAmmoSource source, int amount)
        { ReserveCalls++; Reserved += amount; Available -= amount; return Enumerable.Repeat(new MultiLineBaitReservation("bait"), amount).ToList(); }
        internal static MultiLineBaitReservation ResolveAttackBaitReturnSource(Attack attack, ItemDrop.ItemData ammo) => new("primary");
        internal static void DestroyExistingFishingFloats(Character owner) => DestroyCalls++;
        internal static IDisposable BeginMultiLineFishingSetup(bool freeBait = false)
        { Scopes.Add(freeBait); ScopeDepth++; return new Scope(); }
        private sealed class Scope : IDisposable { public void Dispose() => ScopeDepth--; }
        internal static IDisposable BeginAttackBaitReturnSourceSetup(Attack attack) => new Scope();
        internal static void MarkMultiLineFishingObject(GameObject go, Character owner, int lineIndex, int primaryEquivalentLineIndex, MultiLineBaitReservation trackedBait, ItemDrop.ItemData rod, bool freeBait)
        { Marks.Add((freeBait, trackedBait.IsValid, lineIndex)); }
        internal static bool TryUseFishingRodBagAmmoForVanillaAttack(Attack attack, ref ItemDrop.ItemData ammo, out bool result)
        { BagUseCalls++; ammo = BagHandled ? SelectedAmmo : null; result = BagResult; return BagHandled; }
        internal static bool TryUseFishingRodBagItem(Humanoid who, Inventory inventory, ItemDrop.ItemData item) => false;
        internal static void SyncFishingRodBagBaitAfterInventoryUse(Humanoid who, Inventory inventory, ItemDrop.ItemData item) { }
        internal static bool HasFishingRodBagAmmo(Humanoid who, ItemDrop.ItemData weapon) => false;
    }
}
