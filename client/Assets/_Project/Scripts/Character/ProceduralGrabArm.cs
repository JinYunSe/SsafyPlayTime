using UnityEngine;
using RootMotion.Dynamics;
using RootMotion.FinalIK;

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

    float _leftBlend;
    float _rightBlend;

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
    }

    void Update()
    {
        if (puppetMaster == null) return;

        var phase = _networkPlayer != null ? _networkPlayer.GetPhysicalPhase() : NetworkPlayer.PhysicalPhase.Stable;
        bool grabActive = phase == NetworkPlayer.PhysicalPhase.GrabIntent || phase == NetworkPlayer.PhysicalPhase.Holding;
        bool suppressReach = NetworkPlayer.UsesPhysicsPosePresentation(phase);
        bool leftHolding = IsHandHolding(_leftHandler);
        bool rightHolding = IsHandHolding(_rightHandler);

        bool leftShouldReach = (grabActive || leftHolding) && !suppressReach;
        bool rightShouldReach = (grabActive || rightHolding) && !suppressReach;

        float dt = Time.deltaTime * blendSpeed;
        _leftBlend = Mathf.MoveTowards(_leftBlend, leftShouldReach ? 1f : 0f, dt);
        _rightBlend = Mathf.MoveTowards(_rightBlend, rightShouldReach ? 1f : 0f, dt);

        if (leftHolding)
        {
            var anchorWorld = _leftHandler.GetGrabAnchorWorldPosition();
            _leftIKTarget.position = ResolveHoldTarget(true, anchorWorld);
            _leftReachDir = (_leftIKTarget.position - puppetMaster.targetRoot.position).normalized;
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
        }
        else
        {
            _rightReachDir = GetReachDirection(_rightPhysicsHand, false);
            Vector3 charPos = puppetMaster.targetRoot.position;
            _rightIKTarget.position = charPos + Vector3.up * 0.8f + _rightReachDir * reachDistance;
        }
    }

    void FixedUpdate()
    {
        if (puppetMaster == null) return;

        var phase = _networkPlayer != null ? _networkPlayer.GetPhysicalPhase() : NetworkPlayer.PhysicalPhase.Stable;
        bool grabActive = phase == NetworkPlayer.PhysicalPhase.GrabIntent || phase == NetworkPlayer.PhysicalPhase.Holding;
        bool suppressReach = NetworkPlayer.UsesPhysicsPosePresentation(phase);
        bool leftHolding = IsHandHolding(_leftHandler);
        bool rightHolding = IsHandHolding(_rightHandler);

        if (!suppressReach && (grabActive || leftHolding))
        {
            var anchorWorld = leftHolding ? _leftHandler.GetGrabAnchorWorldPosition() : Vector3.zero;
            PushPhysicsHand(_leftPhysicsHandRb, _leftReachDir, leftHolding, _leftIKTarget.position, anchorWorld, true);
        }

        if (!suppressReach && (grabActive || rightHolding))
        {
            var anchorWorld = rightHolding ? _rightHandler.GetGrabAnchorWorldPosition() : Vector3.zero;
            PushPhysicsHand(_rightPhysicsHandRb, _rightReachDir, rightHolding, _rightIKTarget.position, anchorWorld, false);
        }
    }

    void OnPuppetMasterRead()
    {
        if (!enabled) return;

        if (leftArmIK != null)
        {
            leftArmIK.solver.SetIKPositionWeight(_leftBlend);
            leftArmIK.solver.Update();
        }

        if (rightArmIK != null)
        {
            rightArmIK.solver.SetIKPositionWeight(_rightBlend);
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

            ApplyBehindBackCorrection(handRb, isLeft);
            handRb.AddForce(-handRb.velocity * handDamping * 1.5f, ForceMode.Acceleration);
        }
        else
        {
            handRb.AddForce(reachDir * handReachForce, ForceMode.Acceleration);
            handRb.AddForce(-handRb.velocity * handDamping, ForceMode.Acceleration);
        }
    }

    Vector3 ResolveHoldTarget(bool isLeft, Vector3 anchorWorld)
    {
        Transform bodyRoot = ResolveBodyReference();

        float side = isLeft ? -1f : 1f;
        var poseTarget = bodyRoot.TransformPoint(new Vector3(side * holdSideOffset, holdHeightOffset, holdForwardOffset));

        var localAnchor = bodyRoot.InverseTransformPoint(anchorWorld);
        var forwardRange = Mathf.Max(0.01f, holdForwardOffset - behindBackThreshold);
        var behindAmount = Mathf.Clamp01((behindBackThreshold - localAnchor.z) / forwardRange);
        var effectiveBlend = Mathf.Lerp(anchorBlend, 0.2f, behindAmount);

        localAnchor.x = Mathf.Lerp(side * holdSideOffset, localAnchor.x, effectiveBlend);
        localAnchor.x = Mathf.Clamp(
            localAnchor.x,
            side * holdSideOffset - holdLateralClamp,
            side * holdSideOffset + holdLateralClamp);
        localAnchor.y = Mathf.Clamp(localAnchor.y, holdHeightOffset - holdVerticalClamp, holdHeightOffset + holdVerticalClamp);
        localAnchor.z = Mathf.Max(localAnchor.z, holdForwardOffset * 0.35f);

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
        if (_hipsBodyRb == null || _hipsBodyRb.isKinematic || reactionForce.sqrMagnitude <= 0f)
            return;

        _hipsBodyRb.AddForce(reactionForce, ForceMode.Acceleration);
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
        if (_hipsBodyRb == null || _hipsBodyRb.isKinematic || bodyRoot == null || depth <= 0f)
            return;

        var planarHand = Vector3.ProjectOnPlane(handWorld - bodyRoot.position, bodyRoot.up);
        if (planarHand.sqrMagnitude <= 0.0001f)
            return;

        var signedAngle = Vector3.SignedAngle(bodyRoot.forward, planarHand.normalized, bodyRoot.up);
        var turnAssist = Mathf.Clamp(signedAngle / 90f, -1f, 1f) * behindBackTurnTorque * Mathf.Clamp01(depth * 4f);
        _hipsBodyRb.AddTorque(bodyRoot.up * turnAssist, ForceMode.Acceleration);
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
