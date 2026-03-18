using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 신체 콜라이더가 공격 판정(CauseDamage 태그)과 충돌했을 때
/// NetworkPlayer.OnPlayerBodyPartHit()를 호출해 기절/피격 판정을 전달한다.
/// 같은 공격자가 짧은 시간 안에 여러 바디파트를 연속 충돌해도 1회만 처리한다.
/// </summary>
public class DetectCollision : MonoBehaviour
{
    private readonly struct RecentHitKey
    {
        public RecentHitKey(int victimId, int attackerId)
        {
            VictimId = victimId;
            AttackerId = attackerId;
        }

        public int VictimId { get; }
        public int AttackerId { get; }
    }

    [Header("Fallback Settings")]
    [Tooltip("CombatSettings가 없을 때 사용할 기절 임계치")]
    [SerializeField] private float fallbackKnockoutThreshold = 15f;

    [Tooltip("피격 시 가할 최대 넉백 힘")]
    [SerializeField] private float fallbackMaxKnockbackForce = 30f;

    [Header("Hit Filtering")]
    [Tooltip("같은 공격자가 여러 바디파트를 연속 타격해도 1회로만 처리하는 최소 간격")]
    [SerializeField] private float repeatedHitCooldown = 0.2f;

    private static readonly Dictionary<RecentHitKey, float> RecentHits = new();

    private NetworkPlayer networkPlayer;
    private Rigidbody hitRigidbody;

    private readonly ContactPoint[] contactPoints = new ContactPoint[5];

    private float KnockoutThreshold =>
        CombatSettings.Instance != null ? CombatSettings.Instance.knockoutThreshold : fallbackKnockoutThreshold;

    private float MaxKnockbackForce => fallbackMaxKnockbackForce;

    private void Awake()
    {
        networkPlayer = GetComponentInParent<NetworkPlayer>();
        hitRigidbody = GetComponent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (networkPlayer == null || collision == null || collision.collider == null)
            return;

        if (networkPlayer.Object != null && networkPlayer.Object.IsValid && !networkPlayer.HasStateAuthority)
            return;

        if (!networkPlayer.IsActiveRagdoll)
            return;

        if (!collision.collider.CompareTag("CauseDamage"))
            return;

        var attackerRoot = collision.collider.transform.root;
        if (attackerRoot == null || attackerRoot == networkPlayer.transform)
            return;

        if (!CanAcceptHitFrom(attackerRoot))
            return;

        var numberOfContacts = collision.GetContacts(contactPoints);
        for (var i = 0; i < numberOfContacts; i++)
        {
            var contactPoint = contactPoints[i];
            var contactImpulse = contactPoint.impulse / Time.fixedDeltaTime;

            if (contactImpulse.magnitude < KnockoutThreshold)
                continue;

            networkPlayer.OnPlayerBodyPartHit();
            RegisterRecentHit(attackerRoot);

            var forceDirection = (contactImpulse + Vector3.up) * 0.5f;
            forceDirection = Vector3.ClampMagnitude(forceDirection, MaxKnockbackForce);

            if (hitRigidbody != null)
                hitRigidbody.AddForce(forceDirection, ForceMode.Impulse);

            Debug.DrawRay(transform.position, forceDirection * 40f, Color.red, 4f);
            break;
        }
    }

    private bool CanAcceptHitFrom(Transform attackerRoot)
    {
        var key = new RecentHitKey(networkPlayer.GetInstanceID(), attackerRoot.GetInstanceID());
        if (!RecentHits.TryGetValue(key, out var lastHitTime))
            return true;

        if (Time.time - lastHitTime >= Mathf.Max(0.01f, repeatedHitCooldown))
        {
            RecentHits.Remove(key);
            return true;
        }

        return false;
    }

    private void RegisterRecentHit(Transform attackerRoot)
    {
        var key = new RecentHitKey(networkPlayer.GetInstanceID(), attackerRoot.GetInstanceID());
        RecentHits[key] = Time.time;
    }
}
