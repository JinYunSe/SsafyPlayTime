using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class BounceForce2 : NetworkBehaviour
{
    [SerializeField] private float bounceForce = 15.0f;
    [SerializeField, Range(0f, 0.5f)] private float verticalLift = 0.14f;
    [SerializeField, Range(0f, 1f)] private float recoveringBounceScale = 0.45f;

    // Fusion의 물리 충돌은 호스트(서버)에서만 판정하는 것이 가장 깔끔합니다.
    public override void FixedUpdateNetwork()
    {
        // 맵 기믹 자체가 움직이는 해머라면, 여기서 충돌 체크를 직접 하거나
        // OnCollisionEnter에서 권한 체크 후 처리합니다.
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 1. 호스트(서버)에서만 물리 판정을 처리하도록 제한 (중요!)
        if (Object != null && !HasStateAuthority)
            return;

        // 2. 캐싱 및 빠른 리턴
        var rootTransform = collision.transform.root;
        if (!rootTransform.TryGetComponent<NetworkPlayer>(out var networkPlayer))
            return;
        if (!rootTransform.TryGetComponent<Rigidbody>(out var playerRb))
            return;

        // 3. 상태 체크 (스턴 상태일 때 스킵 로직)
        if (networkPlayer.IsStunned)
        {
            LogStunEvent(networkPlayer, playerRb, "skipped=stunned");
            return;
        }

        // 4. 힘 계산
        float forceScale = networkPlayer.IsRecovering ? recoveringBounceScale : 1f;

        // 방향 계산: 해머 중심에서 플레이어로 향하는 방향
        Vector3 planarPush = Vector3.ProjectOnPlane(rootTransform.position - transform.position, Vector3.up);
        if (planarPush.sqrMagnitude <= 0.0001f)
            planarPush = transform.forward;

        Vector3 pushDir = (planarPush.normalized + Vector3.up * verticalLift).normalized;
        Vector3 appliedForce = pushDir * bounceForce * forceScale;

        // 5. 힘 적용 (서버에서 적용하면 Fusion의 NetworkRigidbody가 동기화함)
        playerRb.AddForce(appliedForce, ForceMode.Impulse);

        // 6. 로그 기록
        LogStunEvent(networkPlayer, playerRb, $"recoveringScale={forceScale:F2}", appliedForce);
    }

    private void LogStunEvent(NetworkPlayer player, Rigidbody rb, string message, Vector3? force = null)
    {
        Vector3 applied = force ?? Vector3.zero;
        player.TraceStunForceEvent(
            "BounceForce",
            rb,
            applied,
            ForceMode.Impulse,
            rb.velocity, // AddForce 직후라 정확한 측정은 다음 틱에 반영됨
            rb.velocity,
            force.HasValue,
            message);
    }
}
