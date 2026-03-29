using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class Trampoline2 : NetworkBehaviour
{
    public float bounceForce = 20.0f;

    private void OnCollisionEnter(Collision collision)
    {
        // 1. 호스트 권한 체크
        if (Object == null || !HasStateAuthority)
            return;

        if (collision.contacts.Length == 0)
            return;

        ContactPoint contact = collision.contacts[0];

        // 2. 판정 방향 수정 (중요: > 0.5f 여야 플레이어가 위에서 밟은 것)
        if (contact.normal.y < -0.5f)
        {
            var rootTransform = collision.transform.root;

            if (rootTransform.TryGetComponent<Rigidbody>(out var rb))
            {
                var networkPlayer = rootTransform.GetComponent<NetworkPlayer>();

                // 스턴/회복 상태 체크
                if (networkPlayer != null && (networkPlayer.IsStunned || networkPlayer.IsRecovering))
                {
                    return;
                }

                // 3. 점프 높이를 일정하게 만드는 핵심 로직
                // 이전 속도를 완전히 무시하고 새로운 속도로 덮어씁니다 (VelocityChange)
                // 이렇게 해야 떨어지는 속도에 상관없이 항상 bounceForce만큼의 높이로 뜁니다.
                Vector3 velocityBefore = rb.velocity;

                // 수직 속도만 bounceForce로 즉시 교체 (낙하 속도 상쇄 포함)
                rb.velocity = new Vector3(rb.velocity.x, bounceForce, rb.velocity.z);

                // 로그 기록
                networkPlayer?.TraceStunForceEvent("Trampoline", rb, Vector3.up * bounceForce, ForceMode.VelocityChange, velocityBefore, rb.velocity, true, "applied_fixed_velocity");
            }
        }
    }
}
