using UnityEngine;
using RootMotion.Dynamics;
using RootMotion.FinalIK;
using SSAFYPlayTime.Character;

/// <summary>
/// Drives grab reach poses with FinalIK while the PuppetMaster muscles stay authoritative.
/// The IK targets are updated during OnRead so the physical rig can keep up with grab intent.
/// </summary>
public class ProceduralGrabArm : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;

    [Header("IK")]
    [SerializeField] LimbIK leftArmIK;
    [SerializeField] LimbIK rightArmIK;

    [Header("Reach Settings")]
    [SerializeField] float blendSpeed = 6f;
    [SerializeField] float targetScanRadius = 2f;
    [SerializeField] float reachDistance = 0.6f;

    [Header("Hand Physics Force")]
    [SerializeField] float handReachForce = 115f;
    [SerializeField] float handDamping = 8f;
    [Tooltip("조인트가 활성인 hold 상태에서 손 힘 스케일 (0=조인트만, 1=기존과 동일)")]
    [SerializeField, Range(0f, 1f)] float holdForceScale = 0.15f;

    [Header("IK Rotation")]
    [SerializeField, Range(0f, 1f)] float holdIKRotationWeight = 0.5f;

    [Header("Hold Pose")]
    [SerializeField, Range(0f, 1f)] float anchorBlend = 0.6f;
    [SerializeField] float holdForwardOffset = 0.45f;
    [SerializeField] float holdSideOffset = 0.24f;
    [SerializeField] float holdHeightOffset = 0.82f;
    [SerializeField] float holdVerticalClamp = 0.55f;
    [SerializeField] float holdLateralClamp = 0.35f;
    [SerializeField] float anchorAssistScale = 0.4f;
    [SerializeField] float torsoReactionScale = 0.3f;
    [SerializeField] float behindBackThreshold = -0.08f;
    [SerializeField] float behindBackForce = 90f;
    [SerializeField] float behindBackTurnTorque = 20f;
    [SerializeField, Range(0f, 1f)] float chestReactionShare = 0.4f;
    [SerializeField] float carryingTorsoReactionMultiplier = 1.15f;
    [SerializeField] float dualCarryTorsoReactionMultiplier = 1.35f;
    [SerializeField] float carryingTurnAssistMultiplier = 1.1f;
    [SerializeField] float dualCarryTurnAssistMultiplier = 1.25f;

    [Header("Carry Pose Profile")]
    [SerializeField] CarryPoseProfile carryPoseProfile;

    [Header("Debug")]
    [SerializeField] bool debugLog = true;
    float _debugLogTimer;
    float _debugPhysicsLogTimer;
    float _debugCarryLogTimer;

    float _leftBlend;
    float _rightBlend;

    // 오버헤드 캐리 포즈 전환 블렌드 (0=frontCarry, 1=overheadCarry)
    float _overheadBlend;

    NetworkPlayer _networkPlayer;

    // Cache handlers by side so array order does not matter.
    HandGrabHandler _leftHandler;
    HandGrabHandler _rightHandler;

    // IK targets (created at runtime)
    Transform _leftIKTarget;
    Transform _rightIKTarget;

    // Physics hands
    Rigidbody _leftPhysicsHandRb;
    Rigidbody _rightPhysicsHandRb;
    Rigidbody _hipsBodyRb;
    Rigidbody _chestBodyRb;
    Transform _leftPhysicsHand;
    Transform _rightPhysicsHand;
    Transform _torsoReference;

    // CarrySolveFrame: carrier anchor 참조
    CarryRig _carryRig;
    CarryPhysicsProfile _carryPhysicsProfile;

    Vector3 _leftReachDir;
    Vector3 _rightReachDir;

    void Awake()
    {
        debugLog = true; // 디버그 진단용 강제 활성화
        _networkPlayer = GetComponent<NetworkPlayer>();

        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);

        // CarrySolveFrame: CarryRig/Profile 참조 캐시
        _carryRig = GetComponentInChildren<SSAFYPlayTime.Character.CarryRig>(true);
        if (_networkPlayer != null)
        {
            _carryPhysicsProfile = _networkPlayer.GetCarryPhysicsProfile();
            if (_carryRig == null)
                _carryRig = _networkPlayer.GetCarryRig();
        }

        FindPhysicsHands();
    }

    void Start()
    {
        var handlers = GetComponentsInChildren<HandGrabHandler>(true);
        foreach (var h in handlers)
        {
            if (h.Side == HandGrabHandler.HandSide.Left)
                _leftHandler = h;
            else
                _rightHandler = h;
        }

        _leftIKTarget = CreateIKTarget("LeftArm_IKTarget");
        _rightIKTarget = CreateIKTarget("RightArm_IKTarget");

        if (leftArmIK != null)
        {
            leftArmIK.solver.target = _leftIKTarget;
            leftArmIK.solver.SetIKPositionWeight(0f);
            leftArmIK.solver.SetIKRotationWeight(0f);
            EnsureSolverReady(leftArmIK);
            leftArmIK.enabled = false;
        }

        if (rightArmIK != null)
        {
            rightArmIK.solver.target = _rightIKTarget;
            rightArmIK.solver.SetIKPositionWeight(0f);
            rightArmIK.solver.SetIKRotationWeight(0f);
            EnsureSolverReady(rightArmIK);
            rightArmIK.enabled = false;
        }

        if (puppetMaster != null)
        {
            puppetMaster.OnRead += OnPuppetMasterRead;
            puppetMaster.OnFixTransforms += OnPuppetMasterFixTransforms;
        }
    }

    Transform CreateIKTarget(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.forward;
        return go.transform;
    }

    static void EnsureSolverReady(LimbIK limbIK)
    {
        if (limbIK == null)
            return;

        if (!limbIK.solver.initiated)
            limbIK.solver.Initiate(limbIK.transform);
    }

    void FindPhysicsHands()
    {
        if (puppetMaster == null) return;

        if (puppetMaster.muscles != null && puppetMaster.muscles.Length > 0)
        {
            var hipsMuscle = puppetMaster.muscles[0];
            _hipsBodyRb = hipsMuscle.rigidbody != null
                ? hipsMuscle.rigidbody
                : hipsMuscle.joint != null ? hipsMuscle.joint.GetComponent<Rigidbody>() : null;
        }

        var animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            _torsoReference = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (_torsoReference == null)
                _torsoReference = animator.GetBoneTransform(HumanBodyBones.Spine);

            _chestBodyRb = FindNearestRigidbody(_torsoReference, 3);
        }

        foreach (var muscle in puppetMaster.muscles)
        {
            if (muscle.transform == null) continue;
            if (muscle.transform.name == "LeftHand")
            {
                _leftPhysicsHand = muscle.transform;
                _leftPhysicsHandRb = muscle.transform.GetComponent<Rigidbody>();
            }
            else if (muscle.transform.name == "RightHand")
            {
                _rightPhysicsHand = muscle.transform;
                _rightPhysicsHandRb = muscle.transform.GetComponent<Rigidbody>();
            }
        }

        if (_chestBodyRb == null && puppetMaster.muscles != null)
        {
            foreach (var muscle in puppetMaster.muscles)
            {
                if (muscle == null || muscle.transform == null)
                    continue;

                if (muscle.transform.name != "Chest" && muscle.transform.name != "Spine2")
                    continue;

                _chestBodyRb = muscle.rigidbody != null
                    ? muscle.rigidbody
                    : muscle.joint != null ? muscle.joint.GetComponent<Rigidbody>() : null;
                if (_chestBodyRb != null)
                    break;
            }
        }
    }

    static Rigidbody FindNearestRigidbody(Transform start, int maxDepth)
    {
        var current = start;
        for (var depth = 0; current != null && depth <= maxDepth; depth++)
        {
            var rb = current.GetComponent<Rigidbody>();
            if (rb != null)
                return rb;

            current = current.parent;
        }

        return null;
    }

    void Update()
    {
        if (puppetMaster == null) return;

        var phase = _networkPlayer != null ? _networkPlayer.GetPhysicalPhase() : NetworkPlayer.PhysicalPhase.Stable;
        bool grabActive = phase == NetworkPlayer.PhysicalPhase.GrabIntent || phase == NetworkPlayer.PhysicalPhase.Holding || phase == NetworkPlayer.PhysicalPhase.CarryingStunned;
        bool weaponEquipped = phase == NetworkPlayer.PhysicalPhase.WeaponEquipped;
        bool suppressReach = NetworkPlayer.UsesPhysicsPosePresentation(phase);
        bool leftHolding = IsHandHolding(_leftHandler);
        bool rightHolding = IsHandHolding(_rightHandler);

        bool leftShouldReach = (grabActive && !weaponEquipped || leftHolding) && !suppressReach;
        bool rightShouldReach = (grabActive && !weaponEquipped || rightHolding) && !suppressReach;

        float dt = Time.deltaTime * blendSpeed;
        _leftBlend = Mathf.MoveTowards(_leftBlend, leftShouldReach ? 1f : 0f, dt);
        _rightBlend = Mathf.MoveTowards(_rightBlend, rightShouldReach ? 1f : 0f, dt);

        // 오버헤드 캐리 포즈 전환: 한 손이든 양 손이든 기절자 잡으면 오버헤드
        float overheadTarget = (phase == NetworkPlayer.PhysicalPhase.CarryingStunned && _networkPlayer != null && _networkPlayer.IsAnyHandHoldingStunnedPlayer) ? 1f : 0f;
        float overheadSpeed = carryPoseProfile != null ? carryPoseProfile.overheadBlendSpeed : 4f;
        _overheadBlend = Mathf.MoveTowards(_overheadBlend, overheadTarget, Time.deltaTime * overheadSpeed);

        // B2: 1초 간격 상태 로그
        if (debugLog && (leftHolding || rightHolding || grabActive))
        {
            _debugLogTimer -= Time.deltaTime;
            if (_debugLogTimer <= 0f)
            {
                _debugLogTimer = 1f;
                Debug.Log($"[Arm] phase={phase}, grabActive={grabActive}, " +
                    $"L_hold={leftHolding}, R_hold={rightHolding}, " +
                    $"L_blend={_leftBlend:F2}, R_blend={_rightBlend:F2}, " +
                    $"overheadBlend={_overheadBlend:F2}, carryProfile={(carryPoseProfile != null ? "OK" : "NULL")}, " +
                    $"isDual={(_networkPlayer != null ? _networkPlayer.IsDualGrabbingStunnedPlayer.ToString() : "N/A")}, " +
                    $"isAnyStunned={(_networkPlayer != null ? _networkPlayer.IsAnyHandHoldingStunnedPlayer.ToString() : "N/A")}", this);
            }
        }

        if (_networkPlayer != null)
        {
            bool carryTrackingActive = phase == NetworkPlayer.PhysicalPhase.CarryingStunned ||
                                      _overheadBlend > 0.01f ||
                                      _networkPlayer.IsAnyHandHoldingStunnedPlayer;
            bool carryStateMismatch = phase == NetworkPlayer.PhysicalPhase.CarryingStunned &&
                                      !_networkPlayer.IsAnyHandHoldingStunnedPlayer &&
                                      !leftHolding &&
                                      !rightHolding;

            if (carryTrackingActive)
            {
                _debugCarryLogTimer -= Time.deltaTime;
                if (carryStateMismatch || _debugCarryLogTimer <= 0f)
                {
                    _debugCarryLogTimer = carryStateMismatch ? 0.15f : 0.5f;
                    EmitCarryArmDiagnostics(
                        phase,
                        grabActive,
                        suppressReach,
                        leftHolding,
                        rightHolding,
                        overheadTarget,
                        carryStateMismatch);
                }
            }
        }

        if (weaponEquipped && !leftHolding && !rightHolding)
        {
            // 무기 장착 시 양손 모두 twoHandWeapon 포즈로
            var leftTarget = ResolveWeaponPoseTarget(true);
            var rightTarget = ResolveWeaponPoseTarget(false);
            _leftIKTarget.position = leftTarget;
            _rightIKTarget.position = rightTarget;
            _rightIKTarget.rotation = puppetMaster.targetRoot.rotation;
            _leftReachDir = (leftTarget - puppetMaster.targetRoot.position).normalized;
            _rightReachDir = (rightTarget - puppetMaster.targetRoot.position).normalized;
        }
        else
        {
            if (leftHolding)
            {
                var anchorWorld = _leftHandler.GetGrabAnchorWorldPosition();
                _leftIKTarget.position = ResolveHoldTarget(true, anchorWorld);
                _leftReachDir = (_leftIKTarget.position - puppetMaster.targetRoot.position).normalized;
                // 손바닥이 앵커(잡힌 대상 표면)를 향하도록 IK rotation 설정
                OrientIKTargetToAnchor(_leftIKTarget, anchorWorld, true);
            }
            else
            {
                // 앵커 기반 reach: 펜딩 앵커가 있으면 그립 포인트를 직접 타겟팅
                var leftPendingAnchor = _leftHandler != null ? _leftHandler.AttachedAnchorPoint : null;
                if (leftPendingAnchor == null && _leftHandler != null && _leftHandler.IsReaching)
                {
                    var sensor = _leftHandler.Sensor;
                    if (sensor != null && sensor.HasOverlappingAnchor)
                        leftPendingAnchor = sensor.GetBestOverlappingAnchor();
                }

                if (leftPendingAnchor != null)
                {
                    var gripPos = leftPendingAnchor.GetGripWorldPosition();
                    _leftReachDir = (gripPos - puppetMaster.targetRoot.position).normalized;
                    _leftIKTarget.position = gripPos;
                }
                else
                {
                    _leftReachDir = GetReachDirection(_leftPhysicsHand, true);
                    Vector3 charPos = puppetMaster.targetRoot.position;
                    // reach 방향의 Y가 크게 음수면 IK 높이도 낮춤 (바닥 기절자 대응)
                    float ikHeight = Mathf.Lerp(0.8f, 0.15f, Mathf.Clamp01(-_leftReachDir.y * 1.5f));
                    _leftIKTarget.position = charPos + Vector3.up * ikHeight + _leftReachDir * reachDistance;
                }
            }

            if (rightHolding)
            {
                var anchorWorld = _rightHandler.GetGrabAnchorWorldPosition();
                _rightIKTarget.position = ResolveHoldTarget(false, anchorWorld);
                _rightReachDir = (_rightIKTarget.position - puppetMaster.targetRoot.position).normalized;
                OrientIKTargetToAnchor(_rightIKTarget, anchorWorld, false);
            }
            else
            {
                // 앵커 기반 reach: 펜딩 앵커가 있으면 그립 포인트를 직접 타겟팅
                var rightPendingAnchor = _rightHandler != null ? _rightHandler.AttachedAnchorPoint : null;
                if (rightPendingAnchor == null && _rightHandler != null && _rightHandler.IsReaching)
                {
                    var sensor = _rightHandler.Sensor;
                    if (sensor != null && sensor.HasOverlappingAnchor)
                        rightPendingAnchor = sensor.GetBestOverlappingAnchor();
                }

                if (rightPendingAnchor != null)
                {
                    var gripPos = rightPendingAnchor.GetGripWorldPosition();
                    _rightReachDir = (gripPos - puppetMaster.targetRoot.position).normalized;
                    _rightIKTarget.position = gripPos;
                }
                else
                {
                    _rightReachDir = GetReachDirection(_rightPhysicsHand, false);
                    Vector3 charPos = puppetMaster.targetRoot.position;
                    float ikHeight = Mathf.Lerp(0.8f, 0.15f, Mathf.Clamp01(-_rightReachDir.y * 1.5f));
                    _rightIKTarget.position = charPos + Vector3.up * ikHeight + _rightReachDir * reachDistance;
                }
            }
        }
    }

    void FixedUpdate()
    {
        if (puppetMaster == null) return;

        // OwnerProxy에서는 물리 힘 적용 불필요 (호스트가 처리)
        if (_networkPlayer != null && _networkPlayer.IsNetworkReady && !_networkPlayer.HasStateAuthority)
            return;

        var phase = _networkPlayer != null ? _networkPlayer.GetPhysicalPhase() : NetworkPlayer.PhysicalPhase.Stable;
        bool grabActive = phase == NetworkPlayer.PhysicalPhase.GrabIntent || phase == NetworkPlayer.PhysicalPhase.Holding || phase == NetworkPlayer.PhysicalPhase.CarryingStunned;
        bool weaponEquipped = phase == NetworkPlayer.PhysicalPhase.WeaponEquipped;
        bool suppressReach = NetworkPlayer.UsesPhysicsPosePresentation(phase);
        bool leftHolding = IsHandHolding(_leftHandler);
        bool rightHolding = IsHandHolding(_rightHandler);

        if (!suppressReach && (grabActive || leftHolding || weaponEquipped))
        {
            var anchorWorld = leftHolding ? _leftHandler.GetGrabAnchorWorldPosition() : Vector3.zero;
            PushPhysicsHand(_leftPhysicsHandRb, _leftReachDir, leftHolding || weaponEquipped, _leftIKTarget.position, anchorWorld, true);
        }

        if (!suppressReach && (grabActive || rightHolding || weaponEquipped))
        {
            var anchorWorld = rightHolding ? _rightHandler.GetGrabAnchorWorldPosition() : Vector3.zero;
            PushPhysicsHand(_rightPhysicsHandRb, _rightReachDir, rightHolding || weaponEquipped, _rightIKTarget.position, anchorWorld, false);
        }
    }

    void OnPuppetMasterRead()
    {
        if (!enabled) return;

        bool leftHolding = IsHandHolding(_leftHandler);
        bool rightHolding = IsHandHolding(_rightHandler);

        if (leftArmIK != null)
        {
            EnsureSolverReady(leftArmIK);
            leftArmIK.solver.SetIKPositionWeight(_leftBlend);
            leftArmIK.solver.SetIKRotationWeight(leftHolding ? _leftBlend * holdIKRotationWeight : 0f);
            leftArmIK.solver.Update();
        }

        if (rightArmIK != null)
        {
            EnsureSolverReady(rightArmIK);
            rightArmIK.solver.SetIKPositionWeight(_rightBlend);
            var phase = _networkPlayer != null ? _networkPlayer.GetPhysicalPhase() : NetworkPlayer.PhysicalPhase.Stable;
            bool weaponEquipped = phase == NetworkPlayer.PhysicalPhase.WeaponEquipped;
            rightArmIK.solver.SetIKRotationWeight((rightHolding || weaponEquipped) ? _rightBlend * holdIKRotationWeight : 0f);
            rightArmIK.solver.Update();
        }
    }

    void OnPuppetMasterFixTransforms()
    {
        if (!enabled) return;
        if (leftArmIK != null && leftArmIK.fixTransforms)
            leftArmIK.solver.FixTransforms();
        if (rightArmIK != null && rightArmIK.fixTransforms)
            rightArmIK.solver.FixTransforms();
    }

    void PushPhysicsHand(Rigidbody handRb, Vector3 reachDir, bool isHolding, Vector3 targetWorld, Vector3 anchorWorld, bool isLeft)
    {
        if (handRb == null) return;

        if (isHolding)
        {
            // 기절자 운반 중인지 판별
            var handler = isLeft ? _leftHandler : _rightHandler;
            bool isCarryingStunned = handler != null &&
                handler.GrabbedTargetKind == GrabDriveProfile.GrabTargetType.StunnedPlayer;

            // 조인트가 주도: 손 힘은 holdForceScale로 축소하여 보조 역할만
            var scale = holdForceScale;

            var toTarget = targetWorld - handRb.position;
            var targetDistance = toTarget.magnitude;
            float appliedForceMag = 0f;
            if (targetDistance > 0.01f)
            {
                var targetForce = toTarget / targetDistance * handReachForce * scale * Mathf.Clamp(targetDistance * 2f, 0.4f, 2.5f);
                appliedForceMag = targetForce.magnitude;
                handRb.AddForce(targetForce, ForceMode.Acceleration);
                ApplyTorsoReaction(targetForce * torsoReactionScale);
            }

            // 기절자 운반: carry pose(targetWorld)가 주도하므로 anchorAssist를 약화
            // 일반 잡기: anchor에도 동시에 끌어 안정화
            float anchorScale = isCarryingStunned ? 0.15f : 1f;
            var toAnchor = anchorWorld - handRb.position;
            if (toAnchor.sqrMagnitude > 0.0001f)
                handRb.AddForce(toAnchor.normalized * handReachForce * anchorAssistScale * scale * anchorScale, ForceMode.Acceleration);

            // B1: 0.5초 간격 hold 힘 로그
            if (debugLog)
            {
                _debugPhysicsLogTimer -= Time.fixedDeltaTime;
                if (_debugPhysicsLogTimer <= 0f)
                {
                    _debugPhysicsLogTimer = 0.5f;
                    Debug.Log($"[Arm] {(isLeft ? "L" : "R")} hold force: scale={scale:F2}, " +
                        $"targetDist={targetDistance:F3}, appliedForce={appliedForceMag:F1}, " +
                        $"anchorDist={toAnchor.magnitude:F3}, vel={handRb.velocity.magnitude:F2}, " +
                        $"handPos={handRb.position}, targetPos={targetWorld}" +
                        $"{(isCarryingStunned ? " [stun-carry]" : "")}", this);
                }
            }

            // 오버헤드 캐리 시 추가 수직 리프트 보조 (블렌드로 부드럽게 적용)
            if (carryPoseProfile != null && _overheadBlend > 0.01f)
            {
                handRb.AddForce(Vector3.up * carryPoseProfile.overheadLiftForce * _overheadBlend, ForceMode.Acceleration);
            }

            // 기절자 운반 중에는 뒤로 말림 보정 끔 (carry pose 방향과 충돌)
            if (!isCarryingStunned)
                ApplyBehindBackCorrection(handRb, isLeft, scale);

            // 댐핑은 유지 — 조인트 진동 억제에 필요
            handRb.AddForce(-handRb.velocity * handDamping * 1.5f, ForceMode.Acceleration);
        }
        else
        {
            handRb.AddForce(reachDir * handReachForce, ForceMode.Acceleration);
            handRb.AddForce(-handRb.velocity * handDamping, ForceMode.Acceleration);
        }
    }

    /// <summary>핸들러의 GrabbedTargetKind에 따라 적절한 PoseAnchor를 선택</summary>
    CarryPoseProfile.PoseAnchor ResolvePoseAnchor(HandGrabHandler handler)
    {
        if (carryPoseProfile == null)
            return default;

        var kind = handler != null ? handler.GrabbedTargetKind : GrabDriveProfile.GrabTargetType.Default;

        /*

        // OwnerProxy 폴백: 로컬 핸들러에 타겟 타입이 없지만 페이즈가 CarryingStunned이면 기절자
        if (kind == GrabDriveProfile.GrabTargetType.Default && _networkPlayer != null &&
        */
        if (kind == GrabDriveProfile.GrabTargetType.Default &&
            _networkPlayer != null &&
            _networkPlayer.GetPhysicalPhase() == NetworkPlayer.PhysicalPhase.CarryingStunned)
        {
            kind = GrabDriveProfile.GrabTargetType.StunnedPlayer;
        }

        switch (kind)
        {
            case GrabDriveProfile.GrabTargetType.StunnedPlayer:
                // frontCarry → overheadCarry를 _overheadBlend로 부드럽게 전환
                return LerpPoseAnchor(carryPoseProfile.frontCarry, carryPoseProfile.overheadCarry, _overheadBlend);
            case GrabDriveProfile.GrabTargetType.Weapon:
                return carryPoseProfile.twoHandWeapon;
            default:
                return carryPoseProfile.frontGrab;
        }
    }

    static CarryPoseProfile.PoseAnchor LerpPoseAnchor(CarryPoseProfile.PoseAnchor a, CarryPoseProfile.PoseAnchor b, float t)
    {
        return new CarryPoseProfile.PoseAnchor
        {
            sideOffset = Mathf.Lerp(a.sideOffset, b.sideOffset, t),
            forwardOffset = Mathf.Lerp(a.forwardOffset, b.forwardOffset, t),
            heightOffset = Mathf.Lerp(a.heightOffset, b.heightOffset, t),
            verticalClamp = Mathf.Lerp(a.verticalClamp, b.verticalClamp, t),
            lateralClamp = Mathf.Lerp(a.lateralClamp, b.lateralClamp, t),
            anchorBlend = Mathf.Lerp(a.anchorBlend, b.anchorBlend, t)
        };
    }

    /// <summary>무기 장착 시 양손 IK 타겟 위치 계산 (CarryPoseProfile.twoHandWeapon 사용)</summary>
    // Carry diagnostics: correlate arm pose state with live hold/joint state.
    void EmitCarryArmDiagnostics(
        NetworkPlayer.PhysicalPhase phase,
        bool grabActive,
        bool suppressReach,
        bool leftHolding,
        bool rightHolding,
        float overheadTarget,
        bool forceSample)
    {
        if (_networkPlayer == null)
            return;

        var details =
            $"grabActive={grabActive} suppressReach={suppressReach} overheadTarget={overheadTarget:F2} overheadBlend={_overheadBlend:F2} " +
            $"leftLocal={leftHolding} rightLocal={rightHolding} " +
            $"leftNet={_networkPlayer.IsHandHoldingNetworked(HandGrabHandler.HandSide.Left)} " +
            $"rightNet={_networkPlayer.IsHandHoldingNetworked(HandGrabHandler.HandSide.Right)} " +
            $"anyStunned={_networkPlayer.IsAnyHandHoldingStunnedPlayer} dual={_networkPlayer.IsDualGrabbingStunnedPlayer} " +
            $"left={DescribeHandState(_leftHandler)} right={DescribeHandState(_rightHandler)}";

        if (debugLog)
            Debug.Log($"[ArmDiag] phase={phase} {details}", this);

        _networkPlayer.TraceCarryDebugSample("ProceduralGrabArm", $"phase={phase} {details}", forceSample);
    }

    string DescribeHandState(HandGrabHandler handler)
    {
        if (handler == null)
            return "null";

        if (handler.IsHolding)
            return handler.BuildGrabDiagnosticsSummary();

        return $"hand={handler.Side} holding=false reaching={handler.IsReaching} pending={(handler.PendingReachTarget != null ? handler.PendingReachTarget.name : "null")}";
    }

    /// <summary>무기 장착 시 양손 IK 목표 위치 계산 (CarryPoseProfile.twoHandWeapon 사용)</summary>
    Vector3 ResolveWeaponPoseTarget(bool isLeft)
    {
        Transform bodyRoot = ResolveBodyReference();
        float side = isLeft ? -1f : 1f;

        // 1-handed (right) low-hip carry pose for flamethrower
        float useHeight = -0.55f; 
        float useForward = 0.45f;
        float useSide = 0.65f;

        return bodyRoot.TransformPoint(new Vector3(side * useSide, useHeight, useForward));
    }

    Vector3 ResolveHoldTarget(bool isLeft, Vector3 anchorWorld)
    {
        return ResolveHoldTargetForHandler(isLeft, anchorWorld, isLeft ? _leftHandler : _rightHandler);
    }

    Vector3 ResolveHoldTargetForHandler(bool isLeft, Vector3 anchorWorld, HandGrabHandler handler)
    {
        // CarrySolveFrame: 기절자 운반 중이면 carrier anchor 기준으로 target 계산
        if (_carryRig != null && _networkPlayer != null && handler != null &&
            handler.GrabbedTargetKind == GrabDriveProfile.GrabTargetType.StunnedPlayer)
        {
            var carryMode = _networkPlayer.GetLocalCarryMode();
            if (carryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry ||
                carryMode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry)
            {
                if (_carryRig.TryGetCarrierAnchorWorld(carryMode, out var carrierPos, out var carrierFwd))
                {
                    // carrier anchor 기준으로 pose offset 적용
                    var carrierPose = ResolvePoseAnchor(handler);
                    var carrySide = isLeft ? -1f : 1f;
                    var carrierRot = Quaternion.LookRotation(carrierFwd, Vector3.up);
                    var poseOffset = new Vector3(carrySide * carrierPose.sideOffset, carrierPose.heightOffset, carrierPose.forwardOffset);
                    var carrierTarget = carrierPos + carrierRot * poseOffset;

                    // anchor blend: carry 중에는 anchor(실제 잡힌 위치)보다 carrier target 중심
                    var carryBlend = Mathf.Lerp(0f, carrierPose.anchorBlend, 0.3f);
                    return Vector3.Lerp(carrierTarget, anchorWorld, carryBlend);
                }
            }
        }

        Transform bodyRoot = ResolveBodyReference();

        // CarryPoseProfile이 있으면 프로파일에서 오프셋을 가져옴
        float useSide, useForward, useHeight, useVClamp, useLClamp, useBlend;
        if (carryPoseProfile != null && handler != null && IsHandHolding(handler))
        {
            var pose = ResolvePoseAnchor(handler);
            useSide = pose.sideOffset;
            useForward = pose.forwardOffset;
            useHeight = pose.heightOffset;
            useVClamp = pose.verticalClamp;
            useLClamp = pose.lateralClamp;
            useBlend = pose.anchorBlend;
        }
        else
        {
            // 프로파일 미설정 시 기존 Inspector 값 폴백
            useSide = holdSideOffset;
            useForward = holdForwardOffset;
            useHeight = holdHeightOffset;
            useVClamp = holdVerticalClamp;
            useLClamp = holdLateralClamp;
            useBlend = anchorBlend;
        }

        float side = isLeft ? -1f : 1f;
        var poseTarget = bodyRoot.TransformPoint(new Vector3(side * useSide, useHeight, useForward));

        var localAnchor = bodyRoot.InverseTransformPoint(anchorWorld);
        var forwardRange = Mathf.Max(0.01f, useForward - behindBackThreshold);
        var behindAmount = Mathf.Clamp01((behindBackThreshold - localAnchor.z) / forwardRange);
        var effectiveBlend = Mathf.Lerp(useBlend, 0.2f, behindAmount);

        localAnchor.x = Mathf.Lerp(side * useSide, localAnchor.x, effectiveBlend);
        localAnchor.x = Mathf.Clamp(
            localAnchor.x,
            side * useSide - useLClamp,
            side * useSide + useLClamp);
        localAnchor.y = Mathf.Clamp(localAnchor.y, useHeight - useVClamp, useHeight + useVClamp);
        localAnchor.z = Mathf.Max(localAnchor.z, useForward * 0.35f);

        var constrainedAnchor = bodyRoot.TransformPoint(localAnchor);
        return Vector3.Lerp(poseTarget, constrainedAnchor, effectiveBlend);
    }

    void ApplyBehindBackCorrection(Rigidbody handRb, bool isLeft, float forceScale = 1f)
    {
        Transform bodyRoot = ResolveBodyReference();

        var localHand = bodyRoot.InverseTransformPoint(handRb.position);
        if (localHand.z >= behindBackThreshold)
            return;

        float side = isLeft ? -1f : 1f;
        float depth = behindBackThreshold - localHand.z;
        var correctiveForce = bodyRoot.forward * depth * behindBackForce * forceScale;
        correctiveForce += bodyRoot.right * side * behindBackForce * 0.15f * forceScale;

        handRb.AddForce(correctiveForce, ForceMode.Acceleration);
        ApplyTorsoReaction(correctiveForce * torsoReactionScale);
        ApplyTorsoFacingAssist(bodyRoot, handRb.position, depth);
    }

    void ApplyTorsoReaction(Vector3 reactionForce)
    {
        if (reactionForce.sqrMagnitude <= 0f)
            return;

        var scaledForce = reactionForce * ResolveTorsoReactionMultiplier();

        // CarrySolveFrame: carry 중 chest/hips 반작용 share를 키워 "몸이 따라옴" 효과 강화
        float effectiveChestShare = chestReactionShare;
        if (_networkPlayer != null)
        {
            var mode = _networkPlayer.GetLocalCarryMode();
            if (mode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedDualCarry)
                effectiveChestShare = Mathf.Min(chestReactionShare * 1.5f, 0.65f);
            else if (mode == SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry)
                effectiveChestShare = Mathf.Min(chestReactionShare * 1.25f, 0.55f);
        }

        var chestShare = _chestBodyRb != null && !_chestBodyRb.isKinematic ? effectiveChestShare : 0f;
        var hipsShare = 1f - chestShare;

        if (_hipsBodyRb != null && !_hipsBodyRb.isKinematic && hipsShare > 0.0001f)
            _hipsBodyRb.AddForce(scaledForce * hipsShare, ForceMode.Acceleration);

        if (_chestBodyRb != null && !_chestBodyRb.isKinematic && chestShare > 0.0001f)
            _chestBodyRb.AddForce(scaledForce * chestShare, ForceMode.Acceleration);
    }

    Transform ResolveBodyReference()
    {
        if (_torsoReference != null)
            return _torsoReference;

        if (puppetMaster != null && puppetMaster.targetRoot != null)
            return puppetMaster.targetRoot;

        return transform;
    }

    void ApplyTorsoFacingAssist(Transform bodyRoot, Vector3 handWorld, float depth)
    {
        if ((_hipsBodyRb == null || _hipsBodyRb.isKinematic) &&
            (_chestBodyRb == null || _chestBodyRb.isKinematic))
        {
            return;
        }

        if (bodyRoot == null || depth <= 0f)
            return;

        var planarHand = Vector3.ProjectOnPlane(handWorld - bodyRoot.position, bodyRoot.up);
        if (planarHand.sqrMagnitude <= 0.0001f)
            return;

        var signedAngle = Vector3.SignedAngle(bodyRoot.forward, planarHand.normalized, bodyRoot.up);
        var turnAssist = Mathf.Clamp(signedAngle / 90f, -1f, 1f)
            * behindBackTurnTorque
            * Mathf.Clamp01(depth * 4f)
            * ResolveTurnAssistMultiplier();
        var assistTorque = bodyRoot.up * turnAssist;
        var chestShare = _chestBodyRb != null && !_chestBodyRb.isKinematic ? chestReactionShare : 0f;
        var hipsShare = 1f - chestShare;

        if (_hipsBodyRb != null && !_hipsBodyRb.isKinematic && hipsShare > 0.0001f)
            _hipsBodyRb.AddTorque(assistTorque * hipsShare, ForceMode.Acceleration);

        if (_chestBodyRb != null && !_chestBodyRb.isKinematic && chestShare > 0.0001f)
            _chestBodyRb.AddTorque(assistTorque * chestShare, ForceMode.Acceleration);
    }

    float ResolveTorsoReactionMultiplier()
    {
        if (_networkPlayer == null)
            return 1f;

        // CarrySolveFrame: CarryPhysicsProfile 기반 배율 우선
        if (_carryPhysicsProfile != null)
        {
            var mode = _networkPlayer.GetLocalCarryMode();
            if (mode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
                return _carryPhysicsProfile.GetSettings(mode).carrierTorsoReactionMultiplier;
        }

        // 폴백: 기존 Inspector 값
        if (_networkPlayer.IsDualGrabbingStunnedPlayer)
            return dualCarryTorsoReactionMultiplier;

        return _networkPlayer.GetPhysicalPhase() == NetworkPlayer.PhysicalPhase.CarryingStunned
            ? carryingTorsoReactionMultiplier
            : 1f;
    }

    float ResolveTurnAssistMultiplier()
    {
        if (_networkPlayer == null)
            return 1f;

        // CarrySolveFrame: CarryPhysicsProfile 기반 배율 우선
        if (_carryPhysicsProfile != null)
        {
            var mode = _networkPlayer.GetLocalCarryMode();
            if (mode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
                return _carryPhysicsProfile.GetSettings(mode).carrierTurnAssistMultiplier;
        }

        // 폴백: 기존 Inspector 값
        if (_networkPlayer.IsDualGrabbingStunnedPlayer)
            return dualCarryTurnAssistMultiplier;

        return _networkPlayer.GetPhysicalPhase() == NetworkPlayer.PhysicalPhase.CarryingStunned
            ? carryingTurnAssistMultiplier
            : 1f;
    }

    Vector3 GetReachDirection(Transform physicsHand, bool isLeft)
    {
        Transform charRoot = puppetMaster.targetRoot;
        Vector3 baseDir = charRoot.forward;

        if (physicsHand != null)
        {
            // 손 중심 + 캐릭터 중심 두 군데서 스캔 (바닥 기절자 감지용)
            Collider[] hits = Physics.OverlapSphere(physicsHand.position, targetScanRadius);
            float bestDist = float.MaxValue;
            Rigidbody bestTarget = null;
            bool bestIsStunned = false;

            System.Action<Collider[]> scanHits = (Collider[] scanResult) =>
            {
                foreach (var hit in scanResult)
                {
                    Rigidbody rb = hit.attachedRigidbody;
                    if (rb == null || rb.isKinematic) continue;
                    if (rb.transform.root == transform) continue;

                    float dist = Vector3.Distance(charRoot.position, rb.position);
                    // 기절 플레이어 부위는 우선순위 보너스
                    var targetNp = rb.transform.root.GetComponent<NetworkPlayer>();
                    bool isStunned = targetNp != null && !targetNp.IsActiveRagdoll;
                    if (isStunned) dist *= 0.5f; // 기절자 거리 가중치 절반 (우선 탐지)

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestTarget = rb;
                        bestIsStunned = isStunned;
                    }
                }
            };

            scanHits(hits);

            // 캐릭터 발 아래쪽도 스캔 (바닥에 누운 기절자 감지)
            Vector3 groundCenter = charRoot.position + Vector3.down * 0.3f;
            Collider[] groundHits = Physics.OverlapSphere(groundCenter, targetScanRadius * 1.2f);
            scanHits(groundHits);

            if (bestTarget != null)
            {
                Vector3 toTarget = (bestTarget.position - charRoot.position).normalized;
                // 기절자(바닥)는 Y 클램프 완화 — 아래로 더 뻗을 수 있도록
                float yClampMin = bestIsStunned ? -0.8f : -0.3f;
                toTarget.y = Mathf.Clamp(toTarget.y, yClampMin, 0.3f);
                toTarget.Normalize();

                float spread = isLeft ? -0.15f : 0.15f;
                toTarget += charRoot.right * spread;
                return toTarget.normalized;
            }
        }

        float defaultSpread = isLeft ? -0.25f : 0.25f;
        return (baseDir + charRoot.right * defaultSpread).normalized;
    }

    /// <summary>
    /// IK 타겟 회전을 설정하여 손바닥이 앵커 방향을 향하도록 합니다.
    /// forward = 손바닥→앵커 방향, up = 캐릭터 up 기준
    /// </summary>
    void OrientIKTargetToAnchor(Transform ikTarget, Vector3 anchorWorld, bool isLeft)
    {
        var toAnchor = anchorWorld - ikTarget.position;
        if (toAnchor.sqrMagnitude < 0.001f)
            return;

        // 손바닥이 앵커를 향하도록: forward를 앵커 방향으로
        var charUp = puppetMaster.targetRoot != null ? puppetMaster.targetRoot.up : Vector3.up;
        ikTarget.rotation = Quaternion.LookRotation(toAnchor.normalized, charUp);
    }

    bool IsHandHolding(HandGrabHandler handler)
    {
        if (handler == null) return false;

        // 로컬 조인트가 있으면 확정
        if (handler.IsHolding) return true;

        // OwnerProxy 폴백: 네트워크 동기화된 GrabConfirmed 사용
        if (_networkPlayer != null)
            return _networkPlayer.IsHandHoldingNetworked(handler.Side);

        return false;
    }

    void OnDestroy()
    {
        if (puppetMaster != null)
        {
            puppetMaster.OnRead -= OnPuppetMasterRead;
            puppetMaster.OnFixTransforms -= OnPuppetMasterFixTransforms;
        }
    }
}
