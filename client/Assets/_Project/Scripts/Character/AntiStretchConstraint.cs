using UnityEngine;
using SSAFYPlayTime.Character;

/// <summary>
/// HFF 스타일 anti-stretch: 손→가슴, 발→엉덩이에 ConfigurableJoint를 추가하여
/// 래그돌 body part 간 거리가 초기 거리 이상으로 벌어지지 않도록 제한한다.
/// 그랩/충돌/빠른 물리에서 팔다리가 늘어나는 것을 방지하는 패시브 안전망.
///
/// GrabAntiStretchController가 있으면 그쪽이 상태별로 더 세밀하게 제어하므로
/// 이 컴포넌트는 조인트를 생성하지 않는다 (이중 제한 방지).
///
/// PuppetMaster 하위 또는 캐릭터 루트에 부착하면 Awake 시 자동 설정.
/// </summary>
public class AntiStretchConstraint : MonoBehaviour
{
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;
    [SerializeField] private Transform leftFoot;
    [SerializeField] private Transform rightFoot;
    [SerializeField] private Transform chest;   // Spine1
    [SerializeField] private Transform hips;

    void Start()
    {
        // GrabAntiStretchController가 있으면 이쪽이 주도 → 패시브 조인트 생성 스킵
        bool hasGrabController = GetComponent<GrabAntiStretchController>() != null
            || GetComponentInChildren<GrabAntiStretchController>(true) != null;

        if (hasGrabController)
        {
            Debug.Log($"[AntiStretch-Passive] {name}: SKIPPED — GrabAntiStretchController가 주도합니다.", this);
            return;
        }

        if (!TryAutoResolve())
            return;

        AddAntiStretch(leftHand, chest);
        AddAntiStretch(rightHand, chest);
        AddAntiStretch(leftFoot, hips);
        AddAntiStretch(rightFoot, hips);
        Debug.Log($"[AntiStretch-Passive] {name}: 패시브 조인트 4개 생성 완료.", this);
    }

    private bool TryAutoResolve()
    {
        // Inspector에서 할당됐으면 그대로 사용
        if (leftHand != null && rightHand != null && leftFoot != null
            && rightFoot != null && chest != null && hips != null)
            return true;

        // 자동 탐색: Rigidbody가 있는 body part만 대상
        var rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rigidbodies)
        {
            var lower = rb.gameObject.name.ToLowerInvariant();
            if (lower == "lefthand" || lower == "left hand") leftHand = rb.transform;
            else if (lower == "righthand" || lower == "right hand") rightHand = rb.transform;
            else if (lower == "leftfoot" || lower == "left foot") leftFoot = rb.transform;
            else if (lower == "rightfoot" || lower == "right foot") rightFoot = rb.transform;
            else if (lower == "spine1" || lower == "chest") chest = rb.transform;
            else if (lower == "hips" && hips == null) hips = rb.transform;
        }

        if (leftHand == null || rightHand == null || leftFoot == null
            || rightFoot == null || chest == null || hips == null)
        {
            Debug.LogWarning($"[AntiStretch] 일부 body part를 찾지 못함 " +
                $"(LH={leftHand != null} RH={rightHand != null} " +
                $"LF={leftFoot != null} RF={rightFoot != null} " +
                $"Chest={chest != null} Hips={hips != null})", this);
            return false;
        }

        return true;
    }

    private static void AddAntiStretch(Transform endpoint, Transform anchor)
    {
        var endRb = endpoint.GetComponent<Rigidbody>();
        var anchorRb = anchor.GetComponent<Rigidbody>();
        if (endRb == null || anchorRb == null) return;

        var cj = endpoint.gameObject.AddComponent<ConfigurableJoint>();
        cj.connectedBody = anchorRb;
        cj.autoConfigureConnectedAnchor = false;
        cj.anchor = Vector3.zero;
        cj.connectedAnchor = Vector3.zero;

        // 3축 모두 Limited — 현재 거리를 한계로 설정
        cj.xMotion = ConfigurableJointMotion.Limited;
        cj.yMotion = ConfigurableJointMotion.Limited;
        cj.zMotion = ConfigurableJointMotion.Limited;

        float distance = (endpoint.position - anchor.position).magnitude;
        cj.linearLimit = new SoftJointLimit
        {
            limit = distance,
            bounciness = 0f,
            contactDistance = 0.01f
        };

        // 회전은 건드리지 않음 (기존 joint이 관리)
        cj.angularXMotion = ConfigurableJointMotion.Free;
        cj.angularYMotion = ConfigurableJointMotion.Free;
        cj.angularZMotion = ConfigurableJointMotion.Free;
    }
}
