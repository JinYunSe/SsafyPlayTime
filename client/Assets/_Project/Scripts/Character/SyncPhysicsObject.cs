using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 래그돌 신체 부위의 ConfigurableJoint 목표 회전을
// 애니메이션 Rigidbody의 실제 회전에 맞춰 동기화하는 컴포넌트.
// NetworkPlayer.FixedUpdateNetwork()에서 StateAuthority(서버)만 호출한다.
public class SyncPhysicsObject : MonoBehaviour
{
    // 이 오브젝트의 물리 Rigidbody (자동 할당)
    Rigidbody rigidbody3D;

    // 이 오브젝트의 ConfigurableJoint (자동 할당)
    ConfigurableJoint joint;

    // 애니메이션 결과를 반영하는 참조용 Rigidbody
    // (애니메이터가 움직이는 "애니메이션 복사본"의 Rigidbody)
    [SerializeField]
    Rigidbody animatedRigidbody3D;

    // true이면 UpdateJointFromAnimation()이 실제로 동작한다
    [SerializeField]
    bool syncAnimation = false;

    // 시작 시 로컬 회전을 기록해 둔다 (joint 목표 회전 계산의 기준값)
    Quaternion startLocalRotation;

    void Awake()
    {
        rigidbody3D = GetComponent<Rigidbody>();
        joint = GetComponent<ConfigurableJoint>();

        startLocalRotation = transform.localRotation;
    }

    // 애니메이션 Rigidbody의 현재 회전을 읽어 ConfigurableJoint의 targetRotation에 적용한다.
    // syncAnimation이 false이면 아무 동작도 하지 않는다.
    public void UpdateJointFromAnimation()
    {
        if (!syncAnimation)
            return;

        ConfigurableJointExtensions.SetTargetRotationLocal(joint, animatedRigidbody3D.transform.localRotation, startLocalRotation);
    }
}
