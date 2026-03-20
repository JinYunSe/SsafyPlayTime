using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    [Header("Move Sync Diagnostics")]
    [SerializeField] private bool enableMoveSyncDiagnostics = false;
    [SerializeField] private bool moveSyncDiagnosticsLogToConsole = false;
    [SerializeField] private bool moveSyncDiagnosticsIncludeAuthority = true;
    [SerializeField] private bool moveSyncDiagnosticsIncludeOwnerProxy = true;
    [SerializeField] private bool moveSyncDiagnosticsIncludeRemoteProxy = true;
    [SerializeField, Range(0.05f, 0.5f)] private float moveSyncDiagnosticsInputSampleInterval = 0.12f;
    [SerializeField, Range(0.05f, 0.5f)] private float moveSyncDiagnosticsStateSampleInterval = 0.12f;

    private float _moveSyncDiagLastAuthorityInputTime = float.NegativeInfinity;
    private float _moveSyncDiagLastAuthorityStateTime = float.NegativeInfinity;
    private float _moveSyncDiagLastPublishTime = float.NegativeInfinity;
    private float _moveSyncDiagLastProxyStateTime = float.NegativeInfinity;
    private Vector3 _moveSyncDiagPrevAuthorityRoot;
    private Vector3 _moveSyncDiagPrevProxyRoot;
    private Vector3 _moveSyncDiagPrevProxyAnchor;
    private bool _moveSyncDiagAuthorityInitialized;
    private bool _moveSyncDiagProxyInitialized;

    private enum MoveSyncDiagRole
    {
        Authority,
        OwnerProxy,
        RemoteProxy
    }

    internal void UpdateMoveSyncDiagnosticsHotkey()
    {
        MoveSyncDiagnostics.UpdateHotkey();
    }

    internal void TraceMoveAuthorityInput(string source, in PlayerNetworkInput input)
    {
        if (!HasStateAuthority || !ShouldEmitMoveSyncDiagnostics(MoveSyncDiagRole.Authority))
            return;

        var now = Time.unscaledTime;
        if (!HasInterestingMoveInput(input) &&
            now - _moveSyncDiagLastAuthorityInputTime < moveSyncDiagnosticsInputSampleInterval)
        {
            return;
        }

        _moveSyncDiagLastAuthorityInputTime = now;

        EmitMoveSyncDiagnostics(
            "Input",
            source,
            $"move={MoveSyncDiagnostics.FormatVector2(input.Move)} camYaw={input.CameraYaw:F1} " +
            $"jump={FormatMoveSyncBool((bool)input.Jump)} sprint={FormatMoveSyncBool((bool)input.Sprint)} " +
            $"punch={FormatMoveSyncBool((bool)input.Punch)} throw={FormatMoveSyncBool((bool)input.Throw)} " +
            $"drop={FormatMoveSyncBool((bool)input.Drop)} headbutt={FormatMoveSyncBool((bool)input.Headbutt)} " +
            $"leftGrab={FormatMoveSyncBool((bool)input.LeftGrabHold)} rightGrab={FormatMoveSyncBool((bool)input.RightGrabHold)}");
    }

    internal void TraceMoveAuthorityState(string source)
    {
        if (!HasStateAuthority || !ShouldEmitMoveSyncDiagnostics(MoveSyncDiagRole.Authority))
            return;

        var now = Time.unscaledTime;
        if (now - _moveSyncDiagLastAuthorityStateTime < moveSyncDiagnosticsStateSampleInterval)
            return;

        _moveSyncDiagLastAuthorityStateTime = now;

        var rootPosition = transform.position;
        var rigidbodyPosition = rigidbody3D != null ? rigidbody3D.position : rootPosition;
        var pelvisPosition = ResolveMoveSyncPelvisPosition();
        var presentationRoot = GetPresentationRootTransform();
        var presentationPosition = presentationRoot != null ? presentationRoot.position : rootPosition;
        var rootDelta = _moveSyncDiagAuthorityInitialized
            ? (rootPosition - _moveSyncDiagPrevAuthorityRoot).magnitude
            : 0f;

        _moveSyncDiagPrevAuthorityRoot = rootPosition;
        _moveSyncDiagAuthorityInitialized = true;

        EmitMoveSyncDiagnostics(
            "Authority",
            source,
            $"root={FormatMoveSyncVector3(rootPosition)} " +
            $"body={FormatMoveSyncVector3(rigidbodyPosition)} " +
            $"pelvis={FormatMoveSyncVector3(pelvisPosition)} " +
            $"presentation={FormatMoveSyncVector3(presentationPosition)} " +
            $"presentationRoot={FormatMoveSyncTransformName(presentationRoot)} " +
            $"rootDelta={rootDelta:F3} rootBodyGap={(rootPosition - rigidbodyPosition).magnitude:F3} " +
            $"rootPelvisGap={(rootPosition - pelvisPosition).magnitude:F3} " +
            $"presentationGap={(presentationPosition - rootPosition).magnitude:F3} " +
            $"moveSpeed={_localMoveSpeed:F3} grounded={FormatMoveSyncBool(_isGrounded)} " +
            $"phase={GetPhysicalPhase()} active={FormatMoveSyncBool(_isActiveRagdoll)}");
    }

    internal void TraceMovePublish(string source)
    {
        if (!HasStateAuthority || !ShouldEmitMoveSyncDiagnostics(MoveSyncDiagRole.Authority))
            return;

        var now = Time.unscaledTime;
        if (now - _moveSyncDiagLastPublishTime < moveSyncDiagnosticsStateSampleInterval)
            return;

        _moveSyncDiagLastPublishTime = now;

        EmitMoveSyncDiagnostics(
            "Publish",
            source,
            $"netMoveSpeed={NetworkedMoveSpeed:F3} netMotorState={NetworkedMotorState} " +
            $"netLoco={((PresentationLocomotionState)NetworkedLocomotionState)} netYaw={NetworkedVisualYaw:F1} " +
            $"netPhase={((PhysicalPhase)NetworkedPhysicalPhase)} netActive={FormatMoveSyncBool((bool)NetworkedIsActiveRagdoll)} " +
            $"netDragged={FormatMoveSyncBool((bool)NetworkedIsDragged)} netGrabbed={FormatMoveSyncBool((bool)NetworkedIsBeingGrabbed)} " +
            $"netLeftGrab={FormatMoveSyncBool((bool)NetworkedLeftGrabHolding)} netRightGrab={FormatMoveSyncBool((bool)NetworkedRightGrabHolding)} " +
            $"netHips={FormatMoveSyncVector3(NetworkedHipsPosition)}");
    }

    internal void TraceMoveProxyState(string source)
    {
        if (HasStateAuthority)
            return;

        var role = HasInputAuthority ? MoveSyncDiagRole.OwnerProxy : MoveSyncDiagRole.RemoteProxy;
        if (!ShouldEmitMoveSyncDiagnostics(role))
            return;

        var now = Time.unscaledTime;
        if (now - _moveSyncDiagLastProxyStateTime < moveSyncDiagnosticsStateSampleInterval)
            return;

        _moveSyncDiagLastProxyStateTime = now;

        var rootPosition = transform.position;
        var anchorPosition = _cameraFollowAnchor != null ? _cameraFollowAnchor.position : rootPosition;
        var pelvisPosition = ResolveMoveSyncPelvisPosition();
        var presentationRoot = GetPresentationRootTransform();
        var presentationPosition = presentationRoot != null ? presentationRoot.position : rootPosition;

        var rootDelta = _moveSyncDiagProxyInitialized
            ? (rootPosition - _moveSyncDiagPrevProxyRoot).magnitude
            : 0f;
        var anchorDelta = _moveSyncDiagProxyInitialized
            ? (anchorPosition - _moveSyncDiagPrevProxyAnchor).magnitude
            : 0f;

        _moveSyncDiagPrevProxyRoot = rootPosition;
        _moveSyncDiagPrevProxyAnchor = anchorPosition;
        _moveSyncDiagProxyInitialized = true;

        EmitMoveSyncDiagnostics(
            "Proxy",
            source,
            $"root={FormatMoveSyncVector3(rootPosition)} " +
            $"presentation={FormatMoveSyncVector3(presentationPosition)} " +
            $"presentationRoot={FormatMoveSyncTransformName(presentationRoot)} " +
            $"anchor={FormatMoveSyncVector3(anchorPosition)} " +
            $"pelvis={FormatMoveSyncVector3(pelvisPosition)} " +
            $"netHips={FormatMoveSyncVector3(NetworkedHipsPosition)} " +
            $"rootDelta={rootDelta:F3} anchorDelta={anchorDelta:F3} " +
            $"rootPresentationGap={(presentationPosition - rootPosition).magnitude:F3} " +
            $"rootAnchorGap={(anchorPosition - rootPosition).magnitude:F3} " +
            $"rootPelvisGap={(pelvisPosition - rootPosition).magnitude:F3} " +
            $"hipsNetGap={(NetworkedHipsPosition - pelvisPosition).magnitude:F3} " +
            $"netMoveSpeed={NetworkedMoveSpeed:F3} netYaw={NetworkedVisualYaw:F1} " +
            $"netPhase={((PhysicalPhase)NetworkedPhysicalPhase)} " +
            $"physPose={FormatMoveSyncBool(ShouldUsePhysicalPhasePresentation())} " +
            $"rootSmooth={FormatMoveSyncBool(ShouldSmoothProxyPresentationRoot(presentationRoot))} " +
            $"hardPose={FormatMoveSyncBool(ShouldUseHardPhysicsPresentation())} " +
            $"netActive={FormatMoveSyncBool((bool)NetworkedIsActiveRagdoll)}");
    }

    private bool ShouldEmitMoveSyncDiagnostics(MoveSyncDiagRole role)
    {
        if (!enableMoveSyncDiagnostics || !Application.isPlaying || !MoveSyncDiagnostics.Enabled)
            return false;

        return role switch
        {
            MoveSyncDiagRole.Authority => moveSyncDiagnosticsIncludeAuthority,
            MoveSyncDiagRole.OwnerProxy => moveSyncDiagnosticsIncludeOwnerProxy,
            MoveSyncDiagRole.RemoteProxy => moveSyncDiagnosticsIncludeRemoteProxy,
            _ => false
        };
    }

    private void EmitMoveSyncDiagnostics(string category, string source, string payload)
    {
        var tick = Runner != null ? Runner.Tick.Raw : -1;
        MoveSyncDiagnostics.Emit(
            $"[MoveDiag:{category}] role={ResolveMoveSyncRole()} name={name} tick={tick} source={source} {payload}",
            this,
            moveSyncDiagnosticsLogToConsole);
    }

    private Vector3 ResolveMoveSyncPelvisPosition()
    {
        if (_puppetMaster != null &&
            _puppetMaster.muscles != null &&
            _puppetMaster.muscles.Length > 0 &&
            _puppetMaster.muscles[0].joint != null)
        {
            return _puppetMaster.muscles[0].joint.transform.position;
        }

        return rigidbody3D != null ? rigidbody3D.position : transform.position;
    }

    private string ResolveMoveSyncRole()
    {
        if (HasStateAuthority)
            return HasInputAuthority ? "HostOwner" : "HostAuthority";

        return HasInputAuthority ? "OwnerProxy" : "RemoteProxy";
    }

    private static bool HasInterestingMoveInput(in PlayerNetworkInput input)
    {
        return (bool)input.Jump ||
               (bool)input.Punch ||
               (bool)input.Throw ||
               (bool)input.Drop ||
               (bool)input.Headbutt ||
               (bool)input.LeftGrabHold ||
               (bool)input.RightGrabHold ||
               (bool)input.Sprint;
    }

    private static string FormatMoveSyncBool(bool value)
    {
        return value ? "1" : "0";
    }

    private static string FormatMoveSyncVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private static string FormatMoveSyncTransformName(Transform target)
    {
        if (target == null)
            return "null";

        return target.name;
    }
}
