using RootMotion.Dynamics;
using UnityEngine;

/// <summary>
/// Procedural close-range kick that pushes a foot rigidbody forward without a dedicated animation clip.
/// Mirrors ProceduralPunchArm's feel so both attacks fit the same active-ragdoll presentation style.
/// </summary>
[DefaultExecutionOrder(10000)]
public class ProceduralKickLeg : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PuppetMaster puppetMaster;

    [Header("Kick Timing")]
    [SerializeField] private float windUpDuration = 0.09f;
    [SerializeField] private float impactDuration = 0.08f;
    [SerializeField] private float recoveryDuration = 0.24f;

    [Header("Kick Reach")]
    [SerializeField] private float kickReach = 1.12f;
    [SerializeField] private float kickSideOffset = 0.08f;
    [SerializeField] private float kickHeight = 0.34f;
    [SerializeField] private float windUpPullBack = 0.12f;
    [SerializeField] private float windUpLift = 0.14f;
    [SerializeField] private float impactOvershoot = 0.28f;

    [Header("Forces")]
    [SerializeField] private float footKickForce = 260f;
    [SerializeField] private float footReturnForce = 140f;
    [SerializeField] private float footDamping = 10f;
    [SerializeField] private float hipsBackwardReaction = 0.16f;
    [SerializeField] private float hipsCounterReaction = 0.12f;

    [Header("Visual Kick")]
    [SerializeField] private float lowerLegWindUpAngle = -12f;
    [SerializeField] private float lowerLegImpactAngle = 54f;
    [SerializeField] private float footImpactAngle = 28f;
    [SerializeField] private float kneeOutwardAngle = 9f;

    private enum KickPhase
    {
        None,
        WindUp,
        Impact,
        Recovery
    }

    private KickPhase _leftPhase;
    private float _leftPhaseStartTime;
    private Vector3 _leftTargetWorld;

    private KickPhase _rightPhase;
    private float _rightPhaseStartTime;
    private Vector3 _rightTargetWorld;

    private NetworkPlayer _networkPlayer;
    private Rigidbody _leftFootRb;
    private Rigidbody _rightFootRb;
    private Rigidbody _hipsRb;
    private Transform _hipsRef;
    private Animator _visualAnimator;
    private Transform _leftLowerLegVisual;
    private Transform _rightLowerLegVisual;
    private Transform _leftFootVisual;
    private Transform _rightFootVisual;
    private Vector3 _leftFootRestLocalOffset;
    private Vector3 _rightFootRestLocalOffset;
    private bool _hasLeftFootRestOffset;
    private bool _hasRightFootRestOffset;
    private Quaternion _leftLowerLegRestRotation;
    private Quaternion _rightLowerLegRestRotation;
    private Quaternion _leftFootRestRotation;
    private Quaternion _rightFootRestRotation;
    private bool _hasVisualRestPose;

    public float TotalKickDuration => windUpDuration + impactDuration + recoveryDuration;
    public bool IsLeftKicking => _leftPhase != KickPhase.None;
    public bool IsRightKicking => _rightPhase != KickPhase.None;
    public bool IsAnyKicking => _leftPhase != KickPhase.None || _rightPhase != KickPhase.None;

    private void Awake()
    {
        _networkPlayer = GetComponent<NetworkPlayer>();
        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);

        FindPhysicsReferences();
    }

    public void TriggerLeftKick(Vector3 forward)
    {
        TryTriggerLeftKick(forward);
    }

    public void TriggerRightKick(Vector3 forward)
    {
        TryTriggerRightKick(forward);
    }

    public bool TryTriggerLeftKick(Vector3 forward)
    {
        return TryStartKick(true, forward);
    }

    public bool TryTriggerRightKick(Vector3 forward)
    {
        return TryStartKick(false, forward);
    }

    private void FixedUpdate()
    {
        if (puppetMaster == null)
            return;

        TickKickLeg(ref _leftPhase, ref _leftPhaseStartTime, _leftFootRb, _leftTargetWorld, true);
        TickKickLeg(ref _rightPhase, ref _rightPhaseStartTime, _rightFootRb, _rightTargetWorld, false);
    }

    private void LateUpdate()
    {
        TickVisualKick(_leftPhase, _leftPhaseStartTime, true);
        TickVisualKick(_rightPhase, _rightPhaseStartTime, false);
    }

    private void FindPhysicsReferences()
    {
        if (puppetMaster == null || puppetMaster.muscles == null)
            return;

        if (puppetMaster.muscles.Length > 0)
        {
            var hipsMuscle = puppetMaster.muscles[0];
            _hipsRb = hipsMuscle.rigidbody ?? hipsMuscle.joint?.GetComponent<Rigidbody>();
        }

        var animator = ResolvePreferredVisualAnimator();
        if (animator != null && animator.isHuman)
            _hipsRef = animator.GetBoneTransform(HumanBodyBones.Hips);

        FindVisualReferences(animator);

        foreach (var muscle in puppetMaster.muscles)
        {
            if (muscle.transform == null)
                continue;

            if (muscle.transform.name == "LeftFoot" || (_leftFootRb == null && muscle.transform.name == "LeftLowerLeg"))
            {
                _leftFootRb = muscle.transform.GetComponent<Rigidbody>();
                if (_leftFootRb != null)
                {
                    _leftFootRestLocalOffset = ResolveBodyRoot().InverseTransformPoint(muscle.transform.position);
                    _hasLeftFootRestOffset = true;
                }
            }
            else if (muscle.transform.name == "RightFoot" || (_rightFootRb == null && muscle.transform.name == "RightLowerLeg"))
            {
                _rightFootRb = muscle.transform.GetComponent<Rigidbody>();
                if (_rightFootRb != null)
                {
                    _rightFootRestLocalOffset = ResolveBodyRoot().InverseTransformPoint(muscle.transform.position);
                    _hasRightFootRestOffset = true;
                }
            }
        }
    }

    private void FindVisualReferences(Animator animator = null)
    {
        _visualAnimator = ResolvePreferredVisualAnimator(animator);
        if (_visualAnimator == null || !_visualAnimator.isHuman)
            return;

        _leftLowerLegVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        _rightLowerLegVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        _leftFootVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
        _rightFootVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

        if (_leftLowerLegVisual == null || _rightLowerLegVisual == null || _leftFootVisual == null || _rightFootVisual == null)
            return;

        _leftLowerLegRestRotation = _leftLowerLegVisual.localRotation;
        _rightLowerLegRestRotation = _rightLowerLegVisual.localRotation;
        _leftFootRestRotation = _leftFootVisual.localRotation;
        _rightFootRestRotation = _rightFootVisual.localRotation;
        _hasVisualRestPose = true;
    }

    private Animator ResolvePreferredVisualAnimator(Animator preferred = null)
    {
        if (preferred != null && preferred.isHuman)
            return preferred;

        if (_visualAnimator != null && _visualAnimator.isHuman)
            return _visualAnimator;

        if (puppetMaster != null && puppetMaster.targetRoot != null)
        {
            var targetRootAnimator = puppetMaster.targetRoot.GetComponent<Animator>()
                ?? puppetMaster.targetRoot.GetComponentInChildren<Animator>(true);
            if (targetRootAnimator != null && targetRootAnimator.isHuman)
                return targetRootAnimator;
        }

        var animationDriver = transform.Find("_AnimationDriver");
        if (animationDriver != null)
        {
            var driverAnimator = animationDriver.GetComponent<Animator>()
                ?? animationDriver.GetComponentInChildren<Animator>(true);
            if (driverAnimator != null && driverAnimator.isHuman)
                return driverAnimator;
        }

        var animators = GetComponentsInChildren<Animator>(true);
        for (var i = 0; i < animators.Length; i++)
        {
            var candidate = animators[i];
            if (candidate == null || !candidate.isHuman)
                continue;

            if (candidate.transform.name == "_AnimationDriver" || candidate.transform.name.Contains("Animated"))
                return candidate;
        }

        for (var i = 0; i < animators.Length; i++)
        {
            var candidate = animators[i];
            if (candidate != null && candidate.isHuman)
                return candidate;
        }

        return null;
    }

    private bool TryStartKick(bool isLeft, Vector3 forward)
    {
        if (_leftFootRb == null || _rightFootRb == null)
            FindPhysicsReferences();
        if (!_hasVisualRestPose)
            FindVisualReferences();

        if (IsKickSuppressed())
            return false;

        if (isLeft)
        {
            if (_leftPhase != KickPhase.None)
                return false;

            _leftPhase = KickPhase.WindUp;
            _leftPhaseStartTime = Time.time;
            _leftTargetWorld = ComputeKickTarget(true, forward);
            return true;
        }

        if (_rightPhase != KickPhase.None)
            return false;

        _rightPhase = KickPhase.WindUp;
        _rightPhaseStartTime = Time.time;
        _rightTargetWorld = ComputeKickTarget(false, forward);
        return true;
    }

    private void TickKickLeg(ref KickPhase phase, ref float phaseStart, Rigidbody footRb, Vector3 targetWorld, bool isLeft)
    {
        if (phase == KickPhase.None || footRb == null)
            return;

        var elapsed = Time.time - phaseStart;
        var bodyRoot = ResolveBodyRoot();

        switch (phase)
        {
            case KickPhase.WindUp:
                if (elapsed >= windUpDuration)
                {
                    phase = KickPhase.Impact;
                    phaseStart = Time.time;
                    ApplyHipsKickReaction(isLeft);
                }
                else
                {
                    var sideDirection = bodyRoot.right * (isLeft ? -1f : 1f);
                    var pullTarget = ResolveFootRestWorldPosition(isLeft)
                        - bodyRoot.forward * windUpPullBack
                        + Vector3.up * windUpLift
                        - sideDirection * (windUpPullBack * 0.2f);
                    ApplyFootSteeringForce(footRb, pullTarget, footKickForce * 0.42f);
                }
                break;

            case KickPhase.Impact:
                if (elapsed >= impactDuration)
                {
                    phase = KickPhase.Recovery;
                    phaseStart = Time.time;
                }
                else
                {
                    var impactTarget = targetWorld + bodyRoot.forward * impactOvershoot;
                    ApplyFootSteeringForce(footRb, impactTarget, footKickForce);
                }
                break;

            case KickPhase.Recovery:
                if (elapsed >= recoveryDuration)
                {
                    phase = KickPhase.None;
                    ApplyHipsCounterReaction(isLeft);
                }
                else
                {
                    ApplyFootSteeringForce(footRb, ResolveFootRestWorldPosition(isLeft), footReturnForce);
                    footRb.AddForce(-footRb.velocity * footDamping * 1.2f, ForceMode.Acceleration);
                }
                break;
        }
    }

    private void TickVisualKick(KickPhase phase, float phaseStart, bool isLeft)
    {
        if (!_hasVisualRestPose)
            return;

        var lowerLeg = isLeft ? _leftLowerLegVisual : _rightLowerLegVisual;
        var foot = isLeft ? _leftFootVisual : _rightFootVisual;
        if (lowerLeg == null || foot == null)
            return;

        if (phase == KickPhase.None)
        {
            RestoreVisualPose(isLeft);
            return;
        }

        var elapsed = Time.time - phaseStart;
        float kneePitch;
        float footPitch;
        switch (phase)
        {
            case KickPhase.WindUp:
            {
                var t = Mathf.Clamp01(windUpDuration > 0f ? elapsed / windUpDuration : 1f);
                kneePitch = Mathf.Lerp(0f, lowerLegWindUpAngle, t);
                footPitch = Mathf.Lerp(0f, -footImpactAngle * 0.35f, t);
                break;
            }
            case KickPhase.Impact:
            {
                var t = Mathf.Clamp01(impactDuration > 0f ? elapsed / impactDuration : 1f);
                kneePitch = Mathf.Lerp(lowerLegWindUpAngle, lowerLegImpactAngle, t);
                footPitch = Mathf.Lerp(-footImpactAngle * 0.35f, footImpactAngle, t);
                break;
            }
            default:
            {
                var t = Mathf.Clamp01(recoveryDuration > 0f ? elapsed / recoveryDuration : 1f);
                kneePitch = Mathf.Lerp(lowerLegImpactAngle, 0f, t);
                footPitch = Mathf.Lerp(footImpactAngle, 0f, t);
                break;
            }
        }

        var side = isLeft ? -1f : 1f;
        var kneeRotation = Quaternion.Euler(kneePitch, 0f, side * kneeOutwardAngle);
        var footRotation = Quaternion.Euler(footPitch, 0f, -side * kneeOutwardAngle * 0.5f);

        lowerLeg.localRotation = (isLeft ? _leftLowerLegRestRotation : _rightLowerLegRestRotation) * kneeRotation;
        foot.localRotation = (isLeft ? _leftFootRestRotation : _rightFootRestRotation) * footRotation;
    }

    private void ApplyFootSteeringForce(Rigidbody footRb, Vector3 targetWorld, float forceScale)
    {
        var toTarget = targetWorld - footRb.position;
        var distance = toTarget.magnitude;
        if (distance > 0.01f)
        {
            var force = toTarget / distance * forceScale * Mathf.Clamp(distance * 3f, 0.45f, 2.1f);
            footRb.AddForce(force, ForceMode.Acceleration);
        }

        footRb.AddForce(-footRb.velocity * footDamping, ForceMode.Acceleration);
    }

    private Vector3 ComputeKickTarget(bool isLeft, Vector3 forward)
    {
        var bodyRoot = ResolveBodyRoot();
        var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = bodyRoot.forward;
        planarForward.Normalize();

        var side = isLeft ? -1f : 1f;
        var sideDirection = Vector3.Cross(Vector3.up, planarForward).normalized * (side * kickSideOffset);
        return bodyRoot.position
            + planarForward * (kickReach + impactOvershoot)
            + sideDirection
            + Vector3.up * kickHeight;
    }

    private void ApplyHipsKickReaction(bool isLeft)
    {
        if (_hipsRb == null || _hipsRb.isKinematic)
            return;

        var bodyRoot = ResolveBodyRoot();
        var side = isLeft ? -1f : 1f;
        _hipsRb.AddForce(-bodyRoot.forward * footKickForce * hipsBackwardReaction, ForceMode.Acceleration);
        _hipsRb.AddTorque(bodyRoot.up * side * footKickForce * hipsCounterReaction * 0.2f, ForceMode.Acceleration);
    }

    private void ApplyHipsCounterReaction(bool isLeft)
    {
        if (_hipsRb == null || _hipsRb.isKinematic)
            return;

        var bodyRoot = ResolveBodyRoot();
        var side = isLeft ? 1f : -1f;
        _hipsRb.AddTorque(bodyRoot.up * side * footKickForce * hipsCounterReaction * 0.32f, ForceMode.Acceleration);
    }

    private Transform ResolveBodyRoot()
    {
        if (_hipsRef != null)
            return _hipsRef;
        if (puppetMaster != null && puppetMaster.targetRoot != null)
            return puppetMaster.targetRoot;
        return transform;
    }

    private Vector3 ResolveFootRestWorldPosition(bool isLeft)
    {
        var bodyRoot = ResolveBodyRoot();
        if (isLeft && _hasLeftFootRestOffset)
            return bodyRoot.TransformPoint(_leftFootRestLocalOffset);
        if (!isLeft && _hasRightFootRestOffset)
            return bodyRoot.TransformPoint(_rightFootRestLocalOffset);

        var sideOffset = bodyRoot.right * (isLeft ? -kickSideOffset : kickSideOffset);
        return bodyRoot.position + sideOffset + Vector3.up * 0.05f;
    }

    private void RestoreVisualPose(bool isLeft)
    {
        if (!_hasVisualRestPose)
            return;

        var lowerLeg = isLeft ? _leftLowerLegVisual : _rightLowerLegVisual;
        var foot = isLeft ? _leftFootVisual : _rightFootVisual;
        if (lowerLeg != null)
            lowerLeg.localRotation = isLeft ? _leftLowerLegRestRotation : _rightLowerLegRestRotation;
        if (foot != null)
            foot.localRotation = isLeft ? _leftFootRestRotation : _rightFootRestRotation;
    }

    private bool IsKickSuppressed()
    {
        if (_networkPlayer == null)
            return false;

        var phase = _networkPlayer.GetPhysicalPhase();
        return phase == NetworkPlayer.PhysicalPhase.GrabIntent
            || phase == NetworkPlayer.PhysicalPhase.Holding
            || phase == NetworkPlayer.PhysicalPhase.CarryingStunned
            || phase == NetworkPlayer.PhysicalPhase.WeaponEquipped
            || NetworkPlayer.UsesPhysicsPosePresentation(phase);
    }
}
