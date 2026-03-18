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

    Vector3 _leftReachDir;
    Vector3 _rightReachDir;

    void Awake()
    {
        _networkPlayer = GetComponent<NetworkPlayer>();

        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);

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
            leftArmIK.enabled = false;
        }

        if (rightArmIK != null)
        {
            rightArmIK.solver.target = _rightIKTarget;
            rightArmIK.solver.SetIKPositionWeight(0f);
            rightArmIK.solver.SetIKRotationWeight(0f);
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

        bool leftShouldReach = (grabActive || leftHolding || weaponEquipped) && !suppressReach;
        bool rightShouldReach = (grabActive || rightHolding || weaponEquipped) && !suppressReach;

        float dt = Time.deltaTime * blendSpeed;
        _leftBlend = Mathf.MoveTowards(_leftBlend, leftShouldReach ? 1f : 0f, dt);
        _rightBlend = Mathf.MoveTowards(_rightBlend, rightShouldReach ? 1f : 0f, dt);

        // 오버헤드 캐리 포즈 전환 부드러운 블렌드
        float overheadTarget = (phase == NetworkPlayer.PhysicalPhase.CarryingStunned) ? 1f : 0f;
        float overheadSpeed = carryPoseProfile != null ? carryPoseProfile.overheadBlendSpeed : 4f;
        _overheadBlend = Mathf.MoveTowards(_overheadBlend, overheadTarget, Time.deltaTime * overheadSpeed);

        if (weaponEquipped && !leftHolding && !rightHolding)
        {
            // 무기 장착 시 양손 모두 twoHandWeapon 포즈로
            var leftTarget = ResolveWeaponPoseTarget(true);
            var rightTarget = ResolveWeaponPoseTarget(false);
            _leftIKTarget.position = leftTarget;
            _rightIKTarget.position = rightTarget;
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
                _leftReachDir = GetReachDirection(_leftPhysicsHand, true);
                Vector3 charPos = puppetMaster.targetRoot.position;
                _leftIKTarget.position = charPos + Vector3.up * 0.8f + _leftReachDir * reachDistance;
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
                _rightReachDir = GetReachDirection(_rightPhysicsHand, false);
                Vector3 charPos = puppetMaster.targetRoot.position;
                _rightIKTarget.position = charPos + Vector3.up * 0.8f + _rightReachDir * reachDistance;
            }
        }
    }

    void FixedUpdate()
    {
        if (puppetMaster == null) return;

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
            leftArmIK.solver.SetIKPositionWeight(_leftBlend);
            leftArmIK.solver.SetIKRotationWeight(leftHolding ? _leftBlend * holdIKRotationWeight : 0f);
            leftArmIK.solver.Update();
        }

        if (rightArmIK != null)
        {
            rightArmIK.solver.SetIKPositionWeight(_rightBlend);
            rightArmIK.solver.SetIKRotationWeight(rightHolding ? _rightBlend * holdIKRotationWeight : 0f);
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
            var toTarget = targetWorld - handRb.position;
            var targetDistance = toTarget.magnitude;
            if (targetDistance > 0.01f)
            {
                var targetForce = toTarget / targetDistance * handReachForce * Mathf.Clamp(targetDistance * 2f, 0.4f, 2.5f);
                handRb.AddForce(targetForce, ForceMode.Acceleration);
                ApplyTorsoReaction(targetForce * torsoReactionScale);
            }

            var toAnchor = anchorWorld - handRb.position;
            if (toAnchor.sqrMagnitude > 0.0001f)
                handRb.AddForce(toAnchor.normalized * handReachForce * anchorAssistScale, ForceMode.Acceleration);

            // 오버헤드 캐리 시 추가 수직 리프트 보조 (블렌드로 부드럽게 적용)
            if (carryPoseProfile != null && _overheadBlend > 0.01f)
            {
                handRb.AddForce(Vector3.up * carryPoseProfile.overheadLiftForce * _overheadBlend, ForceMode.Acceleration);
            }

            ApplyBehindBackCorrection(handRb, isLeft);
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
    Vector3 ResolveWeaponPoseTarget(bool isLeft)
    {
        Transform bodyRoot = ResolveBodyReference();
        float side = isLeft ? -1f : 1f;

        float useSide, useForward, useHeight;
        if (carryPoseProfile != null)
        {
            var pose = carryPoseProfile.twoHandWeapon;
            useSide = pose.sideOffset;
            useForward = pose.forwardOffset;
            useHeight = pose.heightOffset;
        }
        else
        {
            useSide = holdSideOffset;
            useForward = holdForwardOffset;
            useHeight = holdHeightOffset;
        }

        return bodyRoot.TransformPoint(new Vector3(side * useSide, useHeight, useForward));
    }

    Vector3 ResolveHoldTarget(bool isLeft, Vector3 anchorWorld)
    {
        return ResolveHoldTargetForHandler(isLeft, anchorWorld, isLeft ? _leftHandler : _rightHandler);
    }

    Vector3 ResolveHoldTargetForHandler(bool isLeft, Vector3 anchorWorld, HandGrabHandler handler)
    {
        Transform bodyRoot = ResolveBodyReference();

        // CarryPoseProfile이 있으면 프로파일에서 오프셋을 가져옴
        float useSide, useForward, useHeight, useVClamp, useLClamp, useBlend;
        if (carryPoseProfile != null && handler != null && handler.IsHolding)
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

    void ApplyBehindBackCorrection(Rigidbody handRb, bool isLeft)
    {
        Transform bodyRoot = ResolveBodyReference();

        var localHand = bodyRoot.InverseTransformPoint(handRb.position);
        if (localHand.z >= behindBackThreshold)
            return;

        float side = isLeft ? -1f : 1f;
        float depth = behindBackThreshold - localHand.z;
        var correctiveForce = bodyRoot.forward * depth * behindBackForce;
        correctiveForce += bodyRoot.right * side * behindBackForce * 0.15f;

        handRb.AddForce(correctiveForce, ForceMode.Acceleration);
        ApplyTorsoReaction(correctiveForce * torsoReactionScale);
        ApplyTorsoFacingAssist(bodyRoot, handRb.position, depth);
    }

    void ApplyTorsoReaction(Vector3 reactionForce)
    {
        if (reactionForce.sqrMagnitude <= 0f)
            return;

        var scaledForce = reactionForce * ResolveTorsoReactionMultiplier();
        var chestShare = _chestBodyRb != null && !_chestBodyRb.isKinematic ? chestReactionShare : 0f;
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
            Collider[] hits = Physics.OverlapSphere(physicsHand.position, targetScanRadius);
            float bestDist = float.MaxValue;
            Rigidbody bestTarget = null;

            foreach (var hit in hits)
            {
                Rigidbody rb = hit.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (rb.transform.root == transform) continue;

                float dist = Vector3.Distance(physicsHand.position, rb.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = rb;
                }
            }

            if (bestTarget != null)
            {
                Vector3 toTarget = (bestTarget.position - charRoot.position).normalized;
                toTarget.y = Mathf.Clamp(toTarget.y, -0.3f, 0.3f);
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

    static bool IsHandHolding(HandGrabHandler handler)
    {
        return handler != null && handler.IsHolding;
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
