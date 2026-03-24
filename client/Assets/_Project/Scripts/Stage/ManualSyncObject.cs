using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class ManualSyncObject : NetworkBehaviour
{
    // 1. 위치와 회전을 모두 네트워크 변수로 선언
    [Networked] public Vector3 NetPos { get; set; }
    [Networked] public Quaternion NetRot { get; set; }

    private Rigidbody rb;

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody>();

        // 2. 권한 설정: 클라이언트는 물리 엔진이 간섭하지 못하게 kinematic을 켭니다.
        if (rb != null)
        {
            rb.isKinematic = !HasStateAuthority;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // 3. 호스트(State Authority)만 실제 물리/회전 값을 기록합니다.
        if (HasStateAuthority)
        {
            NetPos = transform.position;
            NetRot = transform.rotation;
        }
    }

    public override void Render()
    {
        // 4. 클라이언트(Proxy)는 호스트가 보낸 값을 부드럽게 따라갑니다.
        if (!HasStateAuthority)
        {
            // 위치 보간 (Lerp)
            transform.position = Vector3.Lerp(transform.position, NetPos, Time.deltaTime * 15f);
            // 회전 보간 (Slerp - 쿼터니언 전용)
            transform.rotation = Quaternion.Slerp(transform.rotation, NetRot, Time.deltaTime * 15f);
        }
    }
}
