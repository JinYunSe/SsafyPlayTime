using UnityEngine;
using RootMotion.Dynamics;

/// <summary>
/// 펀치 시 물리 hand rigidbody에 짧은 force를 가해
/// "팔이 툭 뻗었다 돌아오는" active ragdoll 연출을 만든다.
/// ProceduralGrabArm과 독립적으로 동작하며, grab 중이면 자동 억제.
/// </summary>
public class ProceduralPunchArm : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;

    [Header("Punch Timing")]
    [SerializeField] float windUpDuration = 0.17f;
    [SerializeField] float impactDuration = 0.09f;
    [SerializeField] float recoveryDuration = 0.27f;

    [Header("Punch Reach")]
    [SerializeField] float punchReach = 0.7f;
    [SerializeField] float punchSideOffset = 0.10f;
    [SerializeField] float punchHeight = 0.85f;
    [SerializeField] float windUpPullBack = 0.15f;
    [SerializeField] float impactOvershoot = 0.14f;

    [Header("Forces")]
    [SerializeField] float handPunchForce = 180f;
    [SerializeField] float handReturnForce = 100f;
    [SerializeField] float handDamping = 10f;
    [SerializeField] float torsoForwardReaction = 0.25f;
    [SerializeField] float torsoCounterReaction = 0.15f;

    enum PunchPhase { None, WindUp, Impact, Recovery }

    // Left punch state
    PunchPhase _leftPhase;
    float _leftPhaseStartTime;
    Vector3 _leftTargetWorld;

    // Right punch state
    PunchPhase _rightPhase;
    float _rightPhaseStartTime;
    Vector3 _rightTargetWorld;

    // Cached references
    NetworkPlayer _networkPlayer;
    Rigidbody _leftHandRb;
    Rigidbody _rightHandRb;
    Rigidbody _hipsRb;
    Transform _torsoRef;
    Vector3 _leftHandRestLocalOffset;
    Vector3 _rightHandRestLocalOffset;
    bool _hasLeftHandRestOffset;
    bool _hasRightHandRestOffset;

    void Awake()
    {
        _networkPlayer = GetComponent<NetworkPlayer>();
        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);
    }

    void Start()
    {
        FindPhysicsReferences();
    }

    void FindPhysicsReferences()
    {
        if (puppetMaster == null || puppetMaster.muscles == null) return;

        // Hips rigidbody
        if (puppetMaster.muscles.Length > 0)
        {
            var hipsMuscle = puppetMaster.muscles[0];
            _hipsRb = hipsMuscle.rigidbody ?? hipsMuscle.joint?.GetComponent<Rigidbody>();
        }

        // Torso reference
        var animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            _torsoRef = animator.GetBoneTransform(HumanBodyBones.Chest)
                     ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        }

        // Hand rigidbodies from PuppetMaster muscles
        foreach (var muscle in puppetMaster.muscles)
        {
            if (muscle.transform == null) continue;
            if (muscle.transform.name == "LeftHand")
            {
                _leftHandRb = muscle.transform.GetComponent<Rigidbody>();
                _leftHandRestLocalOffset = ResolveBodyRoot().InverseTransformPoint(muscle.transform.position);
                _hasLeftHandRestOffset = true;
            }
            else if (muscle.transform.name == "RightHand")
            {
                _rightHandRb = muscle.transform.GetComponent<Rigidbody>();
                _rightHandRestLocalOffset = ResolveBodyRoot().InverseTransformPoint(muscle.transform.position);
                _hasRightHandRestOffset = true;
            }
        }
    }

    /// <summary>
    /// 외부에서 호출: 왼손 펀치 시작.
    /// </summary>
    public float TotalPunchDuration => windUpDuration + impactDuration + recoveryDuration;

    public void TriggerLeftPunch(Vector3 forward)
    {
        TryTriggerLeftPunch(forward);
    }

    /// <summary>
    /// 외부에서 호출: 오른손 펀치 시작.
    /// </summary>
    public void TriggerRightPunch(Vector3 forward)
    {
        TryTriggerRightPunch(forward);
    }

    public bool TryTriggerLeftPunch(Vector3 forward)
    {
        return TryStartPunch(true, forward);
    }

    public bool TryTriggerRightPunch(Vector3 forward)
    {
        return TryStartPunch(false, forward);
    }

    /// <summary>
    /// 현재 펀치 연출이 진행 중인지.
    /// </summary>
    public bool IsLeftPunching => _leftPhase != PunchPhase.None;
    public bool IsRightPunching => _rightPhase != PunchPhase.None;
    public bool IsAnyPunching => _leftPhase != PunchPhase.None || _rightPhase != PunchPhase.None;

    bool TryStartPunch(bool isLeft, Vector3 forward)
    {
        if (IsGrabSuppressed())
            return false;

        if (isLeft)
        {
            if (_leftPhase != PunchPhase.None)
                return false;

            _leftPhase = PunchPhase.WindUp;
            _leftPhaseStartTime = Time.time;
            _leftTargetWorld = ComputePunchTarget(true, forward);
            return true;
        }

        if (_rightPhase != PunchPhase.None)
            return false;

        _rightPhase = PunchPhase.WindUp;
        _rightPhaseStartTime = Time.time;
        _rightTargetWorld = ComputePunchTarget(false, forward);
        return true;
    }

    void FixedUpdate()
    {
        if (puppetMaster == null) return;

        TickPunchHand(ref _leftPhase, ref _leftPhaseStartTime, _leftHandRb, _leftTargetWorld, true);
        TickPunchHand(ref _rightPhase, ref _rightPhaseStartTime, _rightHandRb, _rightTargetWorld, false);
    }

    void TickPunchHand(ref PunchPhase phase, ref float phaseStart, Rigidbody handRb, Vector3 targetWorld, bool isLeft)
    {
        if (phase == PunchPhase.None || handRb == null) return;

        float elapsed = Time.time - phaseStart;

        switch (phase)
        {
            case PunchPhase.WindUp:
                if (elapsed >= windUpDuration)
                {
                    phase = PunchPhase.Impact;
                    phaseStart = Time.time;
                    ApplyTorsoForwardReaction(isLeft);
                }
                else
                {
                    // 손을 살짝 뒤로 당김
                    var bodyRoot = ResolveBodyRoot();
                    var sideDirection = bodyRoot.right * (isLeft ? -1f : 1f);
                    var pullTarget = handRb.position
                        - bodyRoot.forward * windUpPullBack
                        + sideDirection * (windUpPullBack * 0.35f);
                    var pullDir = (pullTarget - handRb.position).normalized;
                    handRb.AddForce(pullDir * handPunchForce * 0.4f, ForceMode.Acceleration);
                    handRb.AddForce(-handRb.velocity * handDamping, ForceMode.Acceleration);
                }
                break;

            case PunchPhase.Impact:
                if (elapsed >= impactDuration)
                {
                    phase = PunchPhase.Recovery;
                    phaseStart = Time.time;
                }
                else
                {
                    // 목표 지점으로 강하게 밀기
                    var impactTarget = targetWorld + ResolveBodyRoot().forward * impactOvershoot;
                    var toTarget = impactTarget - handRb.position;
                    var dist = toTarget.magnitude;
                    if (dist > 0.01f)
                    {
                        var force = toTarget / dist * handPunchForce * Mathf.Clamp(dist * 3f, 0.5f, 2f);
                        handRb.AddForce(force, ForceMode.Acceleration);
                    }
                    handRb.AddForce(-handRb.velocity * handDamping * 0.5f, ForceMode.Acceleration);
                }
                break;

            case PunchPhase.Recovery:
                if (elapsed >= recoveryDuration)
                {
                    phase = PunchPhase.None;
                    ApplyTorsoCounterReaction(isLeft);
                }
                else
                {
                    // 원위치로 부드럽게 복귀 — PuppetMaster 스프링이 자연 복원하도록 약한 댐핑만
                    handRb.AddForce(-handRb.velocity * handDamping * 1.2f, ForceMode.Acceleration);

                    // 복귀 보조: 현재 위치가 타겟보다 멀리 나갔으면 약한 리턴 포스
                    var restPos = ResolveHandRestWorldPosition(isLeft);
                    var toRest = restPos - handRb.position;
                    if (toRest.sqrMagnitude > 0.01f)
                        handRb.AddForce(toRest.normalized * handReturnForce * (elapsed / recoveryDuration), ForceMode.Acceleration);
                }
                break;
        }
    }

    Vector3 ComputePunchTarget(bool isLeft, Vector3 forward)
    {
        var bodyRoot = ResolveBodyRoot();
        var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = bodyRoot.forward;
        planarForward.Normalize();

        var right = Vector3.Cross(Vector3.up, planarForward);
        float side = isLeft ? -1f : 1f;

        return bodyRoot.position
            + planarForward * (punchReach + impactOvershoot)
            + right * (side * punchSideOffset)
            + Vector3.up * punchHeight;
    }

    void ApplyTorsoForwardReaction(bool isLeft)
    {
        if (_hipsRb == null || _hipsRb.isKinematic) return;
        var bodyRoot = ResolveBodyRoot();
        _hipsRb.AddForce(bodyRoot.forward * handPunchForce * torsoForwardReaction, ForceMode.Acceleration);
        var side = isLeft ? -1f : 1f;
        _hipsRb.AddTorque(
            bodyRoot.forward * side * handPunchForce * torsoCounterReaction * 0.08f,
            ForceMode.Acceleration);
    }

    void ApplyTorsoCounterReaction(bool isLeft)
    {
        if (_hipsRb == null || _hipsRb.isKinematic) return;
        var bodyRoot = ResolveBodyRoot();
        float side = isLeft ? 1f : -1f;
        _hipsRb.AddTorque(bodyRoot.up * side * handPunchForce * torsoCounterReaction * 0.3f, ForceMode.Acceleration);
    }

    Transform ResolveBodyRoot()
    {
        if (_torsoRef != null) return _torsoRef;
        if (puppetMaster != null && puppetMaster.targetRoot != null) return puppetMaster.targetRoot;
        return transform;
    }

    Vector3 ResolveHandRestWorldPosition(bool isLeft)
    {
        var bodyRoot = ResolveBodyRoot();
        if (isLeft && _hasLeftHandRestOffset)
            return bodyRoot.TransformPoint(_leftHandRestLocalOffset);

        if (!isLeft && _hasRightHandRestOffset)
            return bodyRoot.TransformPoint(_rightHandRestLocalOffset);

        var sideOffset = bodyRoot.right * (isLeft ? -punchSideOffset : punchSideOffset);
        return bodyRoot.position + sideOffset + Vector3.up * punchHeight;
    }

    bool IsGrabSuppressed()
    {
        if (_networkPlayer == null) return false;
        var phase = _networkPlayer.GetPhysicalPhase();
        return phase == NetworkPlayer.PhysicalPhase.GrabIntent
            || phase == NetworkPlayer.PhysicalPhase.Holding
            || phase == NetworkPlayer.PhysicalPhase.CarryingStunned
            || phase == NetworkPlayer.PhysicalPhase.WeaponEquipped
            || NetworkPlayer.UsesPhysicsPosePresentation(phase);
    }
}
