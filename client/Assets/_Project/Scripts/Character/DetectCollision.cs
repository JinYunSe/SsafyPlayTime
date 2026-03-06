using UnityEngine;

/// <summary>
/// 래그돌 신체 부위에 부착되어, 일정 이상의 충격(CauseDamage 태그)을 받으면
/// NetworkPlayer.OnPlayerBodyPartHit()를 호출하여 기절 상태로 만드는 컴포넌트.
///
/// CSV(CombatTable)에서 knockoutThreshold / maxKnockbackForce를 자동으로 읽어온다.
/// CombatSettings 싱글턴이 없으면 Inspector의 fallback 값 사용.
/// </summary>
public class DetectCollision : MonoBehaviour
{
    [Header("Fallback Settings (CombatSettings 없을 때)")]
    [Tooltip("이 수치 이상의 충격을 받아야 기절 판정됨")]
    [SerializeField] private float fallbackKnockoutThreshold = 15f;

    [Tooltip("피격 시 추가 넉백 힘의 최대 크기")]
    [SerializeField] private float fallbackMaxKnockbackForce = 30f;

    NetworkPlayer networkPlayer;
    Rigidbody hitRigidbody;

    // GC 줄이기 위해 미리 할당
    ContactPoint[] contactPoints = new ContactPoint[5];

    private float KnockoutThreshold =>
        CombatSettings.Instance != null ? CombatSettings.Instance.knockoutThreshold : fallbackKnockoutThreshold;

    private float MaxKnockbackForce => fallbackMaxKnockbackForce;

    void Awake()
    {
        networkPlayer = GetComponentInParent<NetworkPlayer>();
        hitRigidbody = GetComponent<Rigidbody>();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (networkPlayer == null) return;

        // StateAuthority(호스트)에서만 판정
        if (networkPlayer.Object != null && networkPlayer.Object.IsValid
            && !networkPlayer.HasStateAuthority)
            return;

        // 이미 기절 상태이면 무시
        if (!networkPlayer.IsActiveRagdoll)
            return;

        // "CauseDamage" 태그가 붙은 물체만 판정
        if (!collision.collider.CompareTag("CauseDamage"))
            return;

        // 자기 자신의 공격은 무시
        if (collision.collider.transform.root == networkPlayer.transform)
            return;

        int numberOfContacts = collision.GetContacts(contactPoints);

        for (int i = 0; i < numberOfContacts; i++)
        {
            ContactPoint contactPoint = contactPoints[i];

            // 접촉 충격량 계산
            Vector3 contactImpulse = contactPoint.impulse / Time.fixedDeltaTime;

            // 임계값 미만이면 무시
            if (contactImpulse.magnitude < KnockoutThreshold)
                continue;

            // 기절 처리
            networkPlayer.OnPlayerBodyPartHit();

            // 넉백 방향: 충격 방향 + 약간 위쪽
            Vector3 forceDirection = (contactImpulse + Vector3.up) * 0.5f;
            forceDirection = Vector3.ClampMagnitude(forceDirection, MaxKnockbackForce);

            Debug.DrawRay(hitRigidbody.position, forceDirection * 40, Color.red, 4);

            // 피격 부위에 추가 넉백 힘 적용
            if (hitRigidbody != null)
                hitRigidbody.AddForce(forceDirection, ForceMode.Impulse);

            break; // 첫 번째 유효 충격만 처리
        }
    }
}
