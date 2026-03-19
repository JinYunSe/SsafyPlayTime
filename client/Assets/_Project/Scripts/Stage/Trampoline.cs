using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Trampoline : MonoBehaviour
{
    public float bounceForce = 20.0f;

    private void OnCollisionEnter(Collision collision)
    {
        // 1. 충돌 지점 정보 중 첫 번째 정보를 가져옵니다.
        ContactPoint contact = collision.contacts[0];

        // 2. 법선 벡터(Normal) 확인 
        // contact.normal은 충돌면이 바라보는 방향입니다.
        // 플레이어가 위에서 아래로 밟았다면, 충돌면의 법선은 위(Vector3.up)를 향합니다.
        // 하지만 유니티 물리 연산에서 상대 물체의 방향에 따라 값이 다를 수 있으므로
        // y값이 양수(위쪽 방향)인지 확인하는 것이 가장 확실합니다.
        if (contact.normal.y < -0.5f)
        {
            var networkPlayer = collision.transform.root.GetComponent<NetworkPlayer>();
            if (networkPlayer != null && (networkPlayer.IsStunned || networkPlayer.IsRecovering))
            {
                var skippedRb = collision.transform.root.GetComponent<Rigidbody>();
                var velocity = skippedRb != null ? skippedRb.velocity : Vector3.zero;
                networkPlayer.TraceStunForceEvent(
                    "Trampoline",
                    skippedRb,
                    Vector3.up * bounceForce,
                    ForceMode.Impulse,
                    velocity,
                    velocity,
                    false,
                    "skipped=stunned_or_recovering");
                return;
            }

            Rigidbody rb = networkPlayer != null
                ? collision.transform.root.GetComponent<Rigidbody>()
                : collision.rigidbody != null
                    ? collision.rigidbody
                    : collision.gameObject.GetComponent<Rigidbody>();

            if (rb != null)
            {
                // 아래로 떨어지던 속도를 초기화하여 팅겨나가는 힘을 일정하게 유지
                Vector3 vel = rb.velocity;
                vel.y = 0;
                rb.velocity = vel;

                // 위로 쏘아 올리기
                var velocityBefore = rb.velocity;
                rb.AddForce(Vector3.up * bounceForce, ForceMode.Impulse);
                networkPlayer?.TraceStunForceEvent(
                    "Trampoline",
                    rb,
                    Vector3.up * bounceForce,
                    ForceMode.Impulse,
                    velocityBefore,
                    rb.velocity,
                    true,
                    "applied");
            }
        }
    }
}
