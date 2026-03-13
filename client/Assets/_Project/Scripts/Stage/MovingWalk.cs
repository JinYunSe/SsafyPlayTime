using Fusion;
using UnityEngine;

public class MovingWalk : NetworkBehaviour
{
    public Vector3 direction = Vector3.forward; // 이동 방향
    public float speed = 2.0f; // 이동 속도

    private void OnCollisionStay(Collision collision)
    {
        Rigidbody rb = collision.rigidbody;
        if (rb == null)
            return;

        // 클라이언트도 동일한 DeltaTime으로 힘을 예측해야 호스트 보정 없이 부드러운 이동이 가능
        // HasStateAuthority 체크를 하면 클라이언트가 예측을 못해 위치가 뚝뚝 튀게 됨
        var dt = Runner != null ? Runner.DeltaTime : Time.fixedDeltaTime;
        Vector3 moveAmount = direction.normalized * speed * dt;
        rb.MovePosition(rb.position + moveAmount);
    }
}
