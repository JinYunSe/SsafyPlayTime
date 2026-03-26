using RootMotion.Dynamics;
using SSAFYPlayTime.Character;
using UnityEngine;

[DefaultExecutionOrder(10000)]
public class ProceduralHeadbutt : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PuppetMaster puppetMaster;

    [Header("Timing")]
    [SerializeField] private float windUpDuration = 0.04f;
    [SerializeField] private float impactDuration = 0.07f;
    [SerializeField] private float recoveryDuration = 0.12f;

    [Header("Reach")]
    [SerializeField] private float headbuttReach = 0.46f;
    [SerializeField] private float impactOvershoot = 0.10f;
    [SerializeField] private float windUpPullBack = 0.03f;
    [SerializeField] private float windUpForwardBias = 0.05f;
    [SerializeField] private float windUpDrop = 0.07f;
    [SerializeField] private float impactDownBias = 0.05f;
    [SerializeField] private float fallbackHeadHeight = 1.02f;

    [Header("Forces")]
    [SerializeField] private float headDriveForce = 320f;
    [SerializeField] private float headReturnForce = 310f;
    [SerializeField] private float headDamping = 20f;
    [SerializeField] private float upperChestAssistForce = 104f;
    [SerializeField] private float hipsCounterForce = 72f;

    [Header("Visual")]
    [SerializeField] private float headWindUpAngle = -24f;
    [SerializeField] private float headImpactAngle = -48f;
    [SerializeField] private float neckImpactAngle = -22f;
    [SerializeField] private float chestWindUpAngle = -8f;
    [SerializeField] private float chestImpactAngle = -22f;
    [SerializeField] private float hipsWindUpAngle = -5f;
    [SerializeField] private float hipsImpactAngle = -12f;

    [Header("Presentation")]
    [SerializeField, Range(0f, 1f)] private float headPresentationBlend = 0.92f;
    [SerializeField, Range(0f, 1f)] private float chestPresentationBlend = 0.74f;
    [SerializeField, Range(0f, 1f)] private float hipsPresentationBlend = 0.56f;

    private enum HeadbuttPhase
    {
        None,
        WindUp,
        Impact,
        Recovery
    }

    private HeadbuttPhase _phase;
    private float _phaseStartTime;
    private Vector3 _impactTargetWorld;

    private NetworkPlayer _networkPlayer;
    private Rigidbody _headRb;
    private Rigidbody _upperChestRb;
    private Rigidbody _hipsRb;
    private Animator _visualAnimator;
    private Transform _headVisual;
    private Transform _neckVisual;
    private Transform _chestVisual;
    private Transform _hipsVisual;
    private Vector3 _headRestLocalOffset;
    private Vector3 _upperChestRestLocalOffset;
    private bool _hasHeadRestOffset;
    private bool _hasUpperChestRestOffset;
    private Quaternion _headRestRotation;
    private Quaternion _neckRestRotation;
    private Quaternion _chestRestRotation;
    private Quaternion _hipsRestRotation;
    private bool _hasVisualRestPose;
    private bool _usingNetworkedBoneBlend;

    public float TotalHeadbuttDuration => windUpDuration + impactDuration + recoveryDuration;
    public bool IsHeadbutting => _phase != HeadbuttPhase.None;

    private void Awake()
    {
        _networkPlayer = GetComponent<NetworkPlayer>();
        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);

        FindReferences();
    }

    public void TriggerHeadbutt(Vector3 forward)
    {
        TryTriggerHeadbutt(forward);
    }

    public bool TryTriggerHeadbutt(Vector3 forward)
    {
        if (_headRb == null || !_hasVisualRestPose)
            FindReferences();

        if (IsHeadbuttSuppressed() || _phase != HeadbuttPhase.None || !HasDriveTarget())
            return false;

        _phase = HeadbuttPhase.WindUp;
        _phaseStartTime = Time.time;
        _impactTargetWorld = ComputeImpactTarget(forward);
        return true;
    }

    public void CancelHeadbutt(bool restoreVisualPoseImmediately = true)
    {
        _phase = HeadbuttPhase.None;
        ClearPresentationOverride();
        if (restoreVisualPoseImmediately)
            ResetVisualPoseImmediate();
    }

    private void OnDisable()
    {
        ClearPresentationOverride();
    }

    private void OnDestroy()
    {
        ClearPresentationOverride();
    }

    private void FixedUpdate()
    {
        if (_phase == HeadbuttPhase.None)
            return;

        if (IsHeadbuttSuppressed())
        {
            CancelHeadbutt();
            return;
        }

        if (ShouldDrivePhysics())
        {
            TickPhysicsHeadbutt();
            return;
        }

        TickTimelineOnly();
    }

    private void LateUpdate()
    {
        if (_phase != HeadbuttPhase.None && IsHeadbuttSuppressed())
        {
            CancelHeadbutt();
            return;
        }

        if (UsesPhysicsBindingPresentation())
        {
            TickPhysicsBindingPresentation();
            // Bone-blend weights alone produce only a subtle flinch.
            // Fall through to TickVisualHeadbutt so remote proxies also
            // see the full head/neck/chest rotation.
            if (_phase != HeadbuttPhase.None && _hasVisualRestPose)
            {
                TickVisualHeadbutt();
                return;
            }
            return;
        }

        if (!_hasVisualRestPose)
            return;

        if (_phase == HeadbuttPhase.None)
        {
            RestoreVisualPose();
            return;
        }

        TickVisualHeadbutt();
    }

    private void FindReferences()
    {
        FindVisualReferences();

        if (puppetMaster == null || puppetMaster.muscles == null)
            return;

        if (puppetMaster.muscles.Length > 0)
        {
            var hipsMuscle = puppetMaster.muscles[0];
            _hipsRb = hipsMuscle.rigidbody ?? hipsMuscle.joint?.GetComponent<Rigidbody>();
        }

        for (var i = 0; i < puppetMaster.muscles.Length; i++)
        {
            var muscleTransform = puppetMaster.muscles[i].transform;
            if (muscleTransform == null)
                continue;

            if (_headRb == null && muscleTransform.name == "Head")
                _headRb = muscleTransform.GetComponent<Rigidbody>();

            if (_upperChestRb == null &&
                (muscleTransform.name == "UpperChest" ||
                 muscleTransform.name == "Chest" ||
                 muscleTransform.name == "Spine"))
            {
                _upperChestRb = muscleTransform.GetComponent<Rigidbody>();
            }
        }

        var bodyRoot = ResolveBodyRoot();
        if (_headRb != null)
        {
            _headRestLocalOffset = bodyRoot.InverseTransformPoint(_headRb.position);
            _hasHeadRestOffset = true;
        }

        if (_upperChestRb != null)
        {
            _upperChestRestLocalOffset = bodyRoot.InverseTransformPoint(_upperChestRb.position);
            _hasUpperChestRestOffset = true;
        }
    }

    private void TickTimelineOnly()
    {
        var elapsed = Time.time - _phaseStartTime;
        switch (_phase)
        {
            case HeadbuttPhase.WindUp:
                if (elapsed >= windUpDuration)
                {
                    _phase = HeadbuttPhase.Impact;
                    _phaseStartTime = Time.time;
                }
                break;
            case HeadbuttPhase.Impact:
                if (elapsed >= impactDuration)
                {
                    _phase = HeadbuttPhase.Recovery;
                    _phaseStartTime = Time.time;
                }
                break;
            case HeadbuttPhase.Recovery:
                if (elapsed >= recoveryDuration)
                    _phase = HeadbuttPhase.None;
                break;
        }
    }

    private void FindVisualReferences()
    {
        _visualAnimator = ResolvePreferredVisualAnimator();
        if (_visualAnimator == null || !_visualAnimator.isHuman)
            return;

        _headVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.Head);
        _neckVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.Neck);
        _chestVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.UpperChest)
            ?? _visualAnimator.GetBoneTransform(HumanBodyBones.Chest)
            ?? _visualAnimator.GetBoneTransform(HumanBodyBones.Spine);
        _hipsVisual = _visualAnimator.GetBoneTransform(HumanBodyBones.Hips);

        if (_headVisual == null || _chestVisual == null)
            return;

        _headRestRotation = _headVisual.localRotation;
        _neckRestRotation = _neckVisual != null ? _neckVisual.localRotation : Quaternion.identity;
        _chestRestRotation = _chestVisual.localRotation;
        _hipsRestRotation = _hipsVisual != null ? _hipsVisual.localRotation : Quaternion.identity;
        _hasVisualRestPose = true;
    }

    private Animator ResolvePreferredVisualAnimator()
    {
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
            if (candidate != null && candidate.isHuman)
                return candidate;
        }

        return null;
    }

    private void TickPhysicsHeadbutt()
    {
        var elapsed = Time.time - _phaseStartTime;
        var bodyRoot = ResolveBodyRoot();
        var forward = ResolvePlanarForward(bodyRoot.forward);

        switch (_phase)
        {
            case HeadbuttPhase.WindUp:
                if (elapsed >= windUpDuration)
                {
                    _phase = HeadbuttPhase.Impact;
                    _phaseStartTime = Time.time;
                    ApplyHipsCounterForce(forward, 0.35f);
                }
                else
                {
                    ApplyHeadSteeringForce(ResolveWindUpTarget(forward), headDriveForce * 0.55f);
                    ApplyChestAssistForce(ResolveChestWindUpTarget(forward), upperChestAssistForce * 0.45f);
                }
                break;

            case HeadbuttPhase.Impact:
                if (elapsed >= impactDuration)
                {
                    _phase = HeadbuttPhase.Recovery;
                    _phaseStartTime = Time.time;
                }
                else
                {
                    var impactTarget = _impactTargetWorld + forward * impactOvershoot + Vector3.down * impactDownBias;
                    ApplyHeadSteeringForce(impactTarget, headDriveForce);
                    ApplyChestAssistForce(ResolveChestImpactTarget(forward), upperChestAssistForce);
                    ApplyHipsCounterForce(forward, 1f);
                }
                break;

            case HeadbuttPhase.Recovery:
                if (elapsed >= recoveryDuration)
                {
                    _phase = HeadbuttPhase.None;
                    return;
                }

                ApplyHeadSteeringForce(ResolveHeadRestWorldPosition(), headReturnForce);
                ApplyChestAssistForce(ResolveChestRestWorldPosition(), upperChestAssistForce * 0.65f);
                if (_headRb != null)
                    _headRb.AddForce(-_headRb.velocity * headDamping * 1.25f, ForceMode.Acceleration);
                ApplyHipsCounterForce(-forward, 0.42f);
                break;
        }
    }

    private void TickVisualHeadbutt()
    {
        if (_headVisual == null || _chestVisual == null)
            return;

        Quaternion headTarget = _headRestRotation;
        Quaternion neckTarget = _neckVisual != null ? _neckRestRotation : Quaternion.identity;
        Quaternion chestTarget = _chestRestRotation;
        Quaternion hipsTarget = _hipsVisual != null ? _hipsRestRotation : Quaternion.identity;
        var elapsed = Time.time - _phaseStartTime;

        switch (_phase)
        {
            case HeadbuttPhase.WindUp:
            {
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, windUpDuration));
                var headAngle = Mathf.Lerp(0f, headWindUpAngle, t);
                headTarget = _headRestRotation * Quaternion.Euler(headAngle, 0f, 0f);
                if (_neckVisual != null)
                    neckTarget = _neckRestRotation * Quaternion.Euler(headAngle * 0.45f, 0f, 0f);
                chestTarget = _chestRestRotation * Quaternion.Euler(Mathf.Lerp(0f, chestWindUpAngle, t), 0f, 0f);
                if (_hipsVisual != null)
                    hipsTarget = _hipsRestRotation * Quaternion.Euler(Mathf.Lerp(0f, hipsWindUpAngle, t), 0f, 0f);
                break;
            }

            case HeadbuttPhase.Impact:
            {
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, impactDuration));
                headTarget = _headRestRotation * Quaternion.Euler(Mathf.Lerp(headWindUpAngle, headImpactAngle, t), 0f, 0f);
                if (_neckVisual != null)
                    neckTarget = _neckRestRotation * Quaternion.Euler(Mathf.Lerp(headWindUpAngle * 0.45f, neckImpactAngle, t), 0f, 0f);
                chestTarget = _chestRestRotation * Quaternion.Euler(Mathf.Lerp(chestWindUpAngle, chestImpactAngle, t), 0f, 0f);
                if (_hipsVisual != null)
                    hipsTarget = _hipsRestRotation * Quaternion.Euler(Mathf.Lerp(hipsWindUpAngle, hipsImpactAngle, t), 0f, 0f);
                break;
            }

            case HeadbuttPhase.Recovery:
            {
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, recoveryDuration));
                var spring = Mathf.Sin(t * Mathf.PI * 1.2f) * (1f - t);
                headTarget = _headRestRotation * Quaternion.Euler(Mathf.Lerp(headImpactAngle, 0f, t) + spring * 10f, 0f, 0f);
                if (_neckVisual != null)
                    neckTarget = _neckRestRotation * Quaternion.Euler(Mathf.Lerp(neckImpactAngle, 0f, t) + spring * 4f, 0f, 0f);
                chestTarget = _chestRestRotation * Quaternion.Euler(Mathf.Lerp(chestImpactAngle, 0f, t) + spring * 2f, 0f, 0f);
                if (_hipsVisual != null)
                    hipsTarget = _hipsRestRotation * Quaternion.Euler(Mathf.Lerp(hipsImpactAngle, 0f, t) + spring * 1.4f, 0f, 0f);
                break;
            }
        }

        _headVisual.localRotation = headTarget;
        if (_neckVisual != null)
            _neckVisual.localRotation = neckTarget;
        _chestVisual.localRotation = chestTarget;
        if (_hipsVisual != null)
            _hipsVisual.localRotation = hipsTarget;
    }

    private void RestoreVisualPose()
    {
        if (_headVisual != null)
            _headVisual.localRotation = Quaternion.Slerp(_headVisual.localRotation, _headRestRotation, 0.55f);
        if (_neckVisual != null)
            _neckVisual.localRotation = Quaternion.Slerp(_neckVisual.localRotation, _neckRestRotation, 0.50f);
        if (_chestVisual != null)
            _chestVisual.localRotation = Quaternion.Slerp(_chestVisual.localRotation, _chestRestRotation, 0.45f);
        if (_hipsVisual != null)
            _hipsVisual.localRotation = Quaternion.Slerp(_hipsVisual.localRotation, _hipsRestRotation, 0.40f);
    }

    private void ResetVisualPoseImmediate()
    {
        if (_headVisual != null)
            _headVisual.localRotation = _headRestRotation;
        if (_neckVisual != null)
            _neckVisual.localRotation = _neckRestRotation;
        if (_chestVisual != null)
            _chestVisual.localRotation = _chestRestRotation;
        if (_hipsVisual != null)
            _hipsVisual.localRotation = _hipsRestRotation;
    }

    private void TickPhysicsBindingPresentation()
    {
        if (_networkPlayer == null)
            return;

        if (_phase == HeadbuttPhase.None)
        {
            ClearPresentationOverride();
            return;
        }

        var elapsed = Time.time - _phaseStartTime;
        float headWeight;
        float chestWeight;
        float hipsWeight;
        switch (_phase)
        {
            case HeadbuttPhase.WindUp:
            {
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, windUpDuration));
                headWeight = Mathf.Lerp(headPresentationBlend * 0.45f, headPresentationBlend * 0.72f, t);
                chestWeight = Mathf.Lerp(chestPresentationBlend * 0.35f, chestPresentationBlend * 0.6f, t);
                hipsWeight = Mathf.Lerp(hipsPresentationBlend * 0.25f, hipsPresentationBlend * 0.48f, t);
                break;
            }
            case HeadbuttPhase.Impact:
            {
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, impactDuration));
                headWeight = Mathf.Lerp(headPresentationBlend * 0.72f, headPresentationBlend, t);
                chestWeight = Mathf.Lerp(chestPresentationBlend * 0.6f, chestPresentationBlend, t);
                hipsWeight = Mathf.Lerp(hipsPresentationBlend * 0.48f, hipsPresentationBlend, t);
                break;
            }
            case HeadbuttPhase.Recovery:
            {
                var t = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, recoveryDuration));
                headWeight = Mathf.Lerp(headPresentationBlend * 0.82f, 0f, t);
                chestWeight = Mathf.Lerp(chestPresentationBlend * 0.7f, 0f, t);
                hipsWeight = Mathf.Lerp(hipsPresentationBlend * 0.65f, 0f, t);
                break;
            }
            default:
                headWeight = 0f;
                chestWeight = 0f;
                hipsWeight = 0f;
                break;
        }

        _networkPlayer.SetAnchorGrabBoneBlend(GrabAnchorPoint.AnchorId.Head, headWeight);
        _networkPlayer.SetAnchorGrabBoneBlend(GrabAnchorPoint.AnchorId.Chest, chestWeight);
        _networkPlayer.SetAnchorGrabBoneBlend(GrabAnchorPoint.AnchorId.Hips, hipsWeight);
        _usingNetworkedBoneBlend = true;
    }

    private Vector3 ComputeImpactTarget(Vector3 forward)
    {
        var planarForward = ResolvePlanarForward(forward);
        return ResolveHeadRestWorldPosition() + planarForward * headbuttReach;
    }

    private Vector3 ResolveWindUpTarget(Vector3 forward)
    {
        return ResolveHeadRestWorldPosition() + forward * windUpForwardBias - forward * windUpPullBack - Vector3.up * windUpDrop;
    }

    private Vector3 ResolveChestWindUpTarget(Vector3 forward)
    {
        return ResolveChestRestWorldPosition()
             + forward * (windUpForwardBias * 0.62f)
             - forward * (windUpPullBack * 0.28f)
             - Vector3.up * (windUpDrop * 0.32f);
    }

    private Vector3 ResolveChestImpactTarget(Vector3 forward)
    {
        return ResolveChestRestWorldPosition() + forward * (headbuttReach * 0.44f);
    }

    private void ApplyHeadSteeringForce(Vector3 targetWorld, float driveForce)
    {
        if (_headRb == null || _headRb.isKinematic)
            return;

        ApplySteeringForce(_headRb, targetWorld, driveForce);
    }

    private void ApplyChestAssistForce(Vector3 targetWorld, float driveForce)
    {
        if (_upperChestRb == null || _upperChestRb.isKinematic)
            return;

        ApplySteeringForce(_upperChestRb, targetWorld, driveForce);
    }

    private void ApplySteeringForce(Rigidbody body, Vector3 targetWorld, float driveForce)
    {
        var toTarget = targetWorld - body.position;
        var distance = toTarget.magnitude;
        if (distance <= 0.001f)
            return;

        var force = toTarget / distance * driveForce * Mathf.Clamp(distance * 4f, 0.45f, 1.85f);
        body.AddForce(force, ForceMode.Acceleration);
        body.AddForce(-body.velocity * headDamping, ForceMode.Acceleration);
    }

    private void ApplyHipsCounterForce(Vector3 forward, float scale)
    {
        if (_hipsRb == null || _hipsRb.isKinematic)
            return;

        var planarForward = ResolvePlanarForward(forward);
        _hipsRb.AddForce(-planarForward * hipsCounterForce * Mathf.Max(0f, scale), ForceMode.Acceleration);
    }

    private Vector3 ResolveHeadRestWorldPosition()
    {
        if (_hasHeadRestOffset)
            return ResolveBodyRoot().TransformPoint(_headRestLocalOffset);

        return ResolveBodyRoot().position + Vector3.up * fallbackHeadHeight;
    }

    private Vector3 ResolveChestRestWorldPosition()
    {
        if (_hasUpperChestRestOffset)
            return ResolveBodyRoot().TransformPoint(_upperChestRestLocalOffset);

        return ResolveBodyRoot().position + Vector3.up * (fallbackHeadHeight * 0.72f);
    }

    private Transform ResolveBodyRoot()
    {
        if (_visualAnimator != null && _visualAnimator.isHuman)
        {
            var hips = _visualAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
                return hips;
        }

        if (puppetMaster != null && puppetMaster.targetRoot != null)
            return puppetMaster.targetRoot;

        return transform;
    }

    private static Vector3 ResolvePlanarForward(Vector3 forward)
    {
        var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            planarForward = Vector3.forward;

        return planarForward.normalized;
    }

    private bool ShouldDrivePhysics()
    {
        return _networkPlayer == null ||
               !_networkPlayer.IsNetworkReady ||
               _networkPlayer.HasStateAuthority;
    }

    private bool UsesPhysicsBindingPresentation()
    {
        return HasAnimatedPresentationFallback();
    }

    private bool HasDriveTarget()
    {
        return _headRb != null || _hasVisualRestPose || UsesPhysicsBindingPresentation();
    }

    private bool HasAnimatedPresentationFallback()
    {
        if (_networkPlayer == null)
            return false;

        if (_networkPlayer.UsesAnimatedVisualPresentationRig())
            return true;

        return !_networkPlayer.HasStateAuthority && _networkPlayer.GetPresentationRootTransform() != null;
    }

    private void ClearPresentationOverride()
    {
        if (!_usingNetworkedBoneBlend || _networkPlayer == null)
            return;

        _networkPlayer.ClearAnchorGrabBoneBlend(GrabAnchorPoint.AnchorId.Head);
        _networkPlayer.ClearAnchorGrabBoneBlend(GrabAnchorPoint.AnchorId.Chest);
        _networkPlayer.ClearAnchorGrabBoneBlend(GrabAnchorPoint.AnchorId.Hips);
        _usingNetworkedBoneBlend = false;
    }

    private bool IsHeadbuttSuppressed()
    {
        if (_networkPlayer == null)
            return false;

        var usesPhysicsBindingPresentation = HasAnimatedPresentationFallback();
        if (_networkPlayer.ShouldUseHardPhysicsVisualMode())
        {
            return true;
        }

        // Proxy/animated-visual rigs still need to play replicated headbutt presentation
        // even when the local ragdoll-active flag is not yet established.
        if (!usesPhysicsBindingPresentation &&
            (!_networkPlayer.IsActiveRagdoll || _networkPlayer.ShouldUsePhysicalPhasePresentation()))
        {
            return true;
        }

        var phase = _networkPlayer.GetPhysicalPhase();
        return phase == NetworkPlayer.PhysicalPhase.GrabIntent
            || phase == NetworkPlayer.PhysicalPhase.Holding
            || phase == NetworkPlayer.PhysicalPhase.CarryingStunned
            || phase == NetworkPlayer.PhysicalPhase.WeaponEquipped
            || NetworkPlayer.UsesPhysicsPosePresentation(phase);
    }
}
