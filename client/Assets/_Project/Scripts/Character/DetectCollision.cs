using UnityEngine;

/// <summary>
/// Converts high-energy collisions from "CauseDamage" objects into stun damage.
/// Extra launch impulse is disabled by default because the collision response has
/// already been applied by Unity physics before this callback runs.
/// </summary>
public class DetectCollision : MonoBehaviour
{
    [Header("Fallback Settings")]
    [SerializeField] private float fallbackKnockoutThreshold = 15f;
    [SerializeField] private float fallbackMaxKnockbackForce = 30f;
    [SerializeField] private float fallbackStunEntryNudgeForce = 0f;

    private NetworkPlayer networkPlayer;
    private Rigidbody hitRigidbody;

    private readonly ContactPoint[] contactPoints = new ContactPoint[5];

    private float KnockoutThreshold =>
        CombatSettings.Instance != null ? CombatSettings.Instance.knockoutThreshold : fallbackKnockoutThreshold;

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
            networkPlayer.OnPlayerBodyPartHit();

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
