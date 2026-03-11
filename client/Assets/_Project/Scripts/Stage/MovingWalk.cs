using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovingWalk : MonoBehaviour
{
    public Vector3 direction = Vector3.forward; // 이동 방향
    public float speed = 2.0f; // 이동 속도

    private void OnCollisionStay(Collision collision)
    {
        // 닿아 있는 오브젝트의 Rigidbody를 가져옵니다.
        Rigidbody rb = collision.rigidbody;

        if (rb != null)
        {
            // 현재 위치에서 방향 * 속도 * 시간만큼 이동시킵니다.
            // MovePosition을 써야 물리 엔진이 자연스럽게 인식합니다.
            Vector3 moveAmount = direction.normalized * speed * Time.fixedDeltaTime;
            rb.MovePosition(rb.position + moveAmount);
        }
    }
}
