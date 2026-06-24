using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TrollingFishing;

internal static class ProjectileAccess
{
    private static readonly FieldInfo? VelocityField = AccessTools.Field(typeof(Projectile), "m_vel");
    private static readonly FieldInfo? DidHitField = AccessTools.Field(typeof(Projectile), "m_didHit");
    private static readonly FieldInfo? StayAfterHitStaticField = AccessTools.Field(typeof(Projectile), "m_stayAfterHitStatic");
    private static readonly FieldInfo? StayAfterHitDynamicField = AccessTools.Field(typeof(Projectile), "m_stayAfterHitDynamic");
    private static readonly FieldInfo? AttachToClosestBoneField = AccessTools.Field(typeof(Projectile), "m_attachToClosestBone");
    private static readonly FieldInfo? AttachToRigidBodyField = AccessTools.Field(typeof(Projectile), "m_attachToRigidBody");

    internal static void SetVelocity(Projectile projectile, Vector3 velocity)
    {
        VelocityField?.SetValue(projectile, velocity);
    }

    internal static void SetDidHit(Projectile projectile, bool didHit)
    {
        DidHitField?.SetValue(projectile, didHit);
    }

    internal static bool GetDidHit(Projectile projectile)
    {
        return projectile != null && DidHitField?.GetValue(projectile) is bool didHit && didHit;
    }

    internal static bool WillDestroyAfterHit(Projectile projectile, Collider collider)
    {
        if (projectile == null)
        {
            return false;
        }

        if (collider != null && collider.attachedRigidbody != null)
        {
            bool canAttach = GetBool(projectile, AttachToClosestBoneField) || GetBool(projectile, AttachToRigidBodyField);
            if (canAttach && collider.gameObject.GetComponentInParent<ZNetView>() != null)
            {
                return false;
            }

            return !GetBool(projectile, StayAfterHitDynamicField);
        }

        return !GetBool(projectile, StayAfterHitStaticField);
    }

    private static bool GetBool(Projectile projectile, FieldInfo? field)
    {
        return field?.GetValue(projectile) is bool value && value;
    }
}
