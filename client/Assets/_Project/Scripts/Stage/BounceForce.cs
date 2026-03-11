using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BounceForce : MonoBehaviour
{
    public float bounceForce = 15.0f; // 튕겨나가는 힘의 세기

    private void OnCollisionEnter(Collision collision)
    {
        // 부딪힌 물체(플레이어)의 Rigidbody를 가져옵니다.
        Rigidbody playerRb = collision.gameObject.GetComponent<Rigidbody>();

        if (playerRb != null)
        {
            // 1. 튕겨나갈 방향 계산 (나(회전체)의 중심에서 플레이어 방향으로)
            Vector3 pushDir = (collision.transform.position - transform.position).normalized;
            pushDir.y = 0.5f; // 약간 위쪽으로 띄워주면 더 효과적입니다.

            // 2. 순간적인 힘(Impulse)을 가합니다.
            playerRb.AddForce(pushDir * bounceForce, ForceMode.Impulse);
        }
    }
}
