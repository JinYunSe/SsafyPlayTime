using UnityEngine;

// 래그돌 신체 부위의 ConfigurableJoint 목표 회전을
// 애니메이션 Rigidbody의 실제 회전에 맞춰 동기화하는 컴포넌트.
// NetworkPlayer.FixedUpdateNetwork()에서 StateAuthority(서버)만 호출한다.
//
// [Phase 2] MakeRagdoll / MakeActiveRagdoll 추가:
// 관절의 slerpDrive.positionSpring 값을 조작하여
// 기절(완전 래그돌) ↔ 액티브 래그돌 전환을 지원한다.
public class SyncPhysicsObject : MonoBehaviour
{
    public enum BodyPartType
    {
        Core,   // Hip, Waist, Chest — 잡혔을 때 spring 증가 (형태 유지)
        Head,   // Head — 약간 증가
        Limb    // UpperArm, ForeArm, Leg — 잡혔을 때 spring 감소 (느슨)
    }

    [SerializeField]
    BodyPartType bodyPartType = BodyPartType.Limb;

    public void SetBodyPartType(BodyPartType type) => bodyPartType = type;

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

    // [Phase 2] 시작 시 관절의 스프링 값을 기억해 복원에 사용
    float startSlerpPositionSpring;

    void Awake()
    {
        rigidbody3D = GetComponent<Rigidbody>();
        joint = GetComponent<ConfigurableJoint>();

        startLocalRotation = transform.localRotation;

        // ConfigurableJoint가 있는 경우에만 스프링 값 기록
        if (joint != null)
            startSlerpPositionSpring = joint.slerpDrive.positionSpring;
    }

    // 애니메이션 Rigidbody의 현재 회전을 읽어 ConfigurableJoint의 targetRotation에 적용한다.
    // syncAnimation이 false이면 아무 동작도 하지 않는다.
    public void UpdateJointFromAnimation()
    {
        if (!syncAnimation || animatedRigidbody3D == null || joint == null)
            return;

        ConfigurableJointExtensions.SetTargetRotationLocal(joint, animatedRigidbody3D.transform.localRotation, startLocalRotation);
    }

    public void SetSyncAnimationEnabled(bool enabled)
    {
        syncAnimation = enabled;
    }

    // [Phase 2] 관절 스프링을 거의 0으로 만들어
    // 물리 시뮬레이션에 의해 흐느적거리는 완전 래그돌 상태로 전환한다.
    // 타격을 받아 기절할 때 호출된다.
    public void MakeRagdoll()
    {
        if (joint == null) return;

        JointDrive jointDrive = joint.slerpDrive;
        jointDrive.positionSpring = 1f; // 거의 힘 없음
        joint.slerpDrive = jointDrive;
    }

    // [Phase 2] 관절 스프링을 원래 값으로 복원하여
    // 애니메이션 기반 액티브 래그돌 상태로 돌아간다.
    // 부활(Revive) 시 호출된다.
    public void MakeActiveRagdoll()
    {
        if (joint == null) return;

        JointDrive jointDrive = joint.slerpDrive;
        jointDrive.positionSpring = startSlerpPositionSpring;
        joint.slerpDrive = jointDrive;
    }

    // 다른 플레이어에게 잡혔을 때: 부위별로 spring 조정
    public void MakeGrabbed()
    {
        if (joint == null) return;

        var cs = CombatSettings.Instance;
        float multiplier = bodyPartType switch
        {
            BodyPartType.Core => cs != null ? cs.grabbedCoreSpringMultiplier : 2.5f,
            BodyPartType.Head => cs != null ? cs.grabbedHeadSpringMultiplier : 1.5f,
            BodyPartType.Limb => cs != null ? cs.grabbedLimbSpringMultiplier : 0.3f,
            _ => 1f
        };

        JointDrive jointDrive = joint.slerpDrive;
        jointDrive.positionSpring = startSlerpPositionSpring * multiplier;
        joint.slerpDrive = jointDrive;
    }

    // 놓였을 때: 원래 spring으로 복원
    public void MakeUnGrabbed()
    {
        if (joint == null) return;

        JointDrive jointDrive = joint.slerpDrive;
        jointDrive.positionSpring = startSlerpPositionSpring;
        joint.slerpDrive = jointDrive;
    }

    /// <summary>
    /// 스프링을 0 → startSlerpPositionSpring 사이에서 점진적으로 복원.
    /// t=0이면 스프링 0, t=1이면 원래 값.
    /// ForceRecover 안정화 단계에서 매 틱 호출된다.
    /// </summary>
    public void SetSpringLerp(float t)
    {
        if (joint == null) return;

        JointDrive jointDrive = joint.slerpDrive;
        jointDrive.positionSpring = Mathf.Lerp(0f, startSlerpPositionSpring, t);
        joint.slerpDrive = jointDrive;
    }

    // ── 기절 중 부위별 최소 스프링 유지 ──

    private const float StunnedGroundedCoreScale = 0.12f;
    private const float StunnedGroundedHeadScale = 0.06f;
    private const float StunnedGroundedLimbScale = 0.02f;

    private const float StunnedCarriedCoreScale = 0.38f;
    private const float StunnedCarriedHeadScale = 0.22f;
    private const float StunnedCarriedLimbScale = 0.08f;

    /// <summary>
    /// 기절 + 바닥에 누운 상태: 완전 래그돌보다 약간 높은 스프링으로
    /// 코어 본의 최소 형태를 유지한다.
    /// </summary>
    public void MakeStunnedGrounded()
    {
        if (joint == null) return;

        float scale = bodyPartType switch
        {
            BodyPartType.Core => StunnedGroundedCoreScale,
            BodyPartType.Head => StunnedGroundedHeadScale,
            _ => StunnedGroundedLimbScale
        };

        JointDrive jointDrive = joint.slerpDrive;
        jointDrive.positionSpring = Mathf.Max(1f, startSlerpPositionSpring * scale);
        joint.slerpDrive = jointDrive;
    }

    /// <summary>
    /// 기절 + 운반 중: 코어 본에 충분한 스프링을 유지하여
    /// 들린 상태에서 몸체가 구겨지지 않도록 한다.
    /// </summary>
    public void MakeStunnedCarried()
    {
        if (joint == null) return;

        float scale = bodyPartType switch
        {
            BodyPartType.Core => StunnedCarriedCoreScale,
            BodyPartType.Head => StunnedCarriedHeadScale,
            _ => StunnedCarriedLimbScale
        };

        JointDrive jointDrive = joint.slerpDrive;
        jointDrive.positionSpring = Mathf.Max(1f, startSlerpPositionSpring * scale);
        joint.slerpDrive = jointDrive;
    }
}
