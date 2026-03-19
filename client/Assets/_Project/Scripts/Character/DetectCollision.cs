using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts high-energy collisions from "CauseDamage" objects into stun damage.
/// Extra launch impulse is disabled by default because the collision response has
/// already been applied by Unity physics before this callback runs.
/// </summary>
public class DetectCollision : MonoBehaviour
{
    [Header("Fallback Settings")]
    [SerializeField] private float fallbackKnockoutThreshold = 18f;
    [SerializeField] private float fallbackMaxKnockbackForce = 30f;
    [SerializeField] private float fallbackStunEntryNudgeForce = 0f;
    [SerializeField] private float fallbackMaxImpact = 55f;
    [SerializeField] private float fallbackMinStunDamage = 3f;
    [SerializeField] private float fallbackMaxStunDamage = 9f;
    [SerializeField] private float fallbackMinHealthDamage = 0f;
    [SerializeField] private float fallbackMaxHealthDamage = 14f;

    private NetworkPlayer networkPlayer;
    private Rigidbody hitRigidbody;

    private readonly ContactPoint[] contactPoints = new ContactPoint[5];

    private float KnockoutThreshold =>
        CombatSettings.Instance != null ? CombatSettings.Instance.environmentCollisionMinImpact : fallbackKnockoutThreshold;

    private float MaxImpact =>
        CombatSettings.Instance != null ? CombatSettings.Instance.environmentCollisionMaxImpact : fallbackMaxImpact;

    private float MinStunDamage =>
        CombatSettings.Instance != null ? CombatSettings.Instance.environmentCollisionMinStunDamage : fallbackMinStunDamage;

    private float MaxStunDamage =>
        CombatSettings.Instance != null ? CombatSettings.Instance.environmentCollisionMaxStunDamage : fallbackMaxStunDamage;

    private float MinHealthDamage =>
        CombatSettings.Instance != null ? CombatSettings.Instance.environmentCollisionMinHealthDamage : fallbackMinHealthDamage;

    private float MaxHealthDamage =>
        CombatSettings.Instance != null ? CombatSettings.Instance.environmentCollisionMaxHealthDamage : fallbackMaxHealthDamage;

    private float StunEntryNudgeForce =>
        Mathf.Max(0f, Mathf.Min(fallbackStunEntryNudgeForce, fallbackMaxKnockbackForce));

    private void Awake()
    {
        networkPlayer = GetComponentInParent<NetworkPlayer>();
        hitRigidbody = GetComponent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (networkPlayer == null)
            return;

        if (networkPlayer.Object != null && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return;

        if (!networkPlayer.IsActiveRagdoll)
            return;

        if (!collision.collider.CompareTag("CauseDamage"))
            return;

        if (collision.collider.transform.root == networkPlayer.transform)
            return;

        var numberOfContacts = collision.GetContacts(contactPoints);
        for (var i = 0; i < numberOfContacts; i++)
        {
            var contactPoint = contactPoints[i];
            var impactMagnitude = contactPoint.impulse.magnitude;
            if (impactMagnitude < KnockoutThreshold)
                continue;

            networkPlayer.ArmStunForceDiagnostics("DetectCollision", $"impact={impactMagnitude:F2}");
            networkPlayer.TraceStunCollisionImpact(
                "DetectCollision",
                impactMagnitude,
                contactPoint.impulse,
                contactPoint.normal);

            var maxImpact = Mathf.Max(KnockoutThreshold + 0.01f, MaxImpact);
            var impactRatio = Mathf.InverseLerp(KnockoutThreshold, maxImpact, impactMagnitude);
            var stunDamage = Mathf.Lerp(MinStunDamage, MaxStunDamage, impactRatio);
            var healthDamage = Mathf.Lerp(MinHealthDamage, MaxHealthDamage, impactRatio);
            networkPlayer.ApplyCombinedDamage(
                healthDamage,
                stunDamage,
                "EnvironmentCollision",
                0f,
                impactMagnitude);

            if (hitRigidbody != null && !hitRigidbody.isKinematic && StunEntryNudgeForce > 0f)
            {
                var forceDirection = Vector3.ProjectOnPlane(contactPoint.normal, Vector3.up);
                if (forceDirection.sqrMagnitude <= 0.0001f)
                    forceDirection = contactPoint.normal.sqrMagnitude > 0.0001f
                        ? contactPoint.normal.normalized
                        : transform.forward;

                forceDirection = forceDirection.normalized;
                Debug.DrawRay(hitRigidbody.position, forceDirection * 8f, Color.red, 2f);
                hitRigidbody.AddForce(forceDirection * StunEntryNudgeForce, ForceMode.Impulse);
            }

            break;
        }
    }
}
