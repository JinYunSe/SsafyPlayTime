using System.Text;
using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private const float MoveDiagnosticsForceSampleInterval = 0.20f;
    private const float MoveDiagnosticsStateSampleInterval = 0.35f;
    private const float MoveDiagnosticsPublishSampleInterval = 0.35f;
    private const float MoveDiagnosticsProxySampleInterval = 0.25f;
    private const float MoveDiagnosticsFacingSampleInterval = 0.30f;

    private float _nextMoveForceDiagnosticsSampleTime = float.NegativeInfinity;
    private float _nextMoveStateDiagnosticsSampleTime = float.NegativeInfinity;
    private float _nextMovePublishDiagnosticsSampleTime = float.NegativeInfinity;
    private float _nextMoveProxyDiagnosticsSampleTime = float.NegativeInfinity;
    private float _nextMoveFacingDiagnosticsSampleTime = float.NegativeInfinity;
    private string _lastMoveForceSignature = string.Empty;
    private string _lastMoveStateSignature = string.Empty;
    private string _lastMovePublishSignature = string.Empty;
    private string _lastMoveProxySignature = string.Empty;
    private string _lastMoveFacingSignature = string.Empty;
    private bool _moveDiagnosticsOverlayEnsured;
    private bool _moveDiagnosticsStartupLogged;

    private bool ShouldLogMoveDiagnostics()
    {
        return SSAFYPlayTime.RuntimeLoggingSettings.AreRuntimeLogsEnabled;
    }

    private void EnsureMoveDiagnosticsOverlay()
    {
        if (_moveDiagnosticsOverlayEnsured || !ShouldLogMoveDiagnostics())
            return;

        SSAFYPlayTime.RuntimeLogOverlay.EnsureInstance();
        _moveDiagnosticsOverlayEnsured = true;

        if (_moveDiagnosticsStartupLogged)
            return;

        _moveDiagnosticsStartupLogged = true;
        Debug.Log(
            $"[MoveDiag] Diagnostics armed for {name}. editor={Application.isEditor} debugBuild={Debug.isDebugBuild}",
            this);
    }

    private static bool ShouldEmitMoveDiagnostic(
        string signature,
        float interval,
        ref string lastSignature,
        ref float nextSampleTime,
        bool force = false)
    {
        if (force || !string.Equals(signature, lastSignature, System.StringComparison.Ordinal))
        {
            lastSignature = signature;
            nextSampleTime = Time.time + interval;
            return true;
        }

        if (Time.time < nextSampleTime)
            return false;

        nextSampleTime = Time.time + interval;
        return true;
    }

    private static string FormatMoveVector3(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private bool GetObservedGroundedState()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return NetworkedIsGrounded;

        return _isGrounded;
    }

    private string BuildMoveReasonSummary(
        float buffMoveMultiplier,
        float carryMultiplier,
        float sprintMultiplier,
        float hostCompensation,
        float hitPenaltyMultiplier,
        bool recoilActive,
        bool unstableHitPenalty)
    {
        var reasons = new StringBuilder(128);

        if (!CanDriveLocomotion)
            reasons.Append("locomotion_off,");
        if (_localPhysicalPhase == PhysicalPhase.CarryingStunned)
            reasons.Append("carrying_stunned,");
        if (_beingGrabbedRefCount > 0 || NetworkedIsBeingGrabbed)
            reasons.Append("being_grabbed,");
        if (_localIsDragged || (IsNetworkReady && !HasStateAuthority && NetworkedIsDragged))
            reasons.Append("dragged,");
        if (IsAnyHandHoldingStunnedPlayer)
            reasons.Append("holding_stunned,");
        if (ShouldFreezeFacingDuringSingleHandStunnedHold())
            reasons.Append("facing_locked,");
        if (recoilActive)
            reasons.Append("hit_recoil,");
        if (unstableHitPenalty)
            reasons.Append("hit_penalty,");
        if (Mathf.Abs(buffMoveMultiplier - 1f) > 0.01f)
            reasons.Append("buff=").Append(buffMoveMultiplier.ToString("F2")).Append(',');
        if (Mathf.Abs(carryMultiplier - 1f) > 0.01f)
            reasons.Append("carryMul=").Append(carryMultiplier.ToString("F2")).Append(',');
        if (Mathf.Abs(sprintMultiplier - 1f) > 0.01f)
            reasons.Append("sprintMul=").Append(sprintMultiplier.ToString("F2")).Append(',');
        if (Mathf.Abs(hostCompensation - 1f) > 0.01f)
            reasons.Append("hostComp=").Append(hostCompensation.ToString("F2")).Append(',');
        if (Mathf.Abs(hitPenaltyMultiplier - 1f) > 0.01f)
            reasons.Append("hitMul=").Append(hitPenaltyMultiplier.ToString("F2")).Append(',');
        if (!_isGrounded)
            reasons.Append("airborne,");

        if (reasons.Length == 0)
            return "normal";

        reasons.Length -= 1;
        return reasons.ToString();
    }

    internal void UpdateMoveSyncDiagnosticsHotkey()
    {
        if (!ShouldLogMoveDiagnostics())
            return;

        EnsureMoveDiagnosticsOverlay();

        if (!Input.GetKeyDown(KeyCode.F7))
            return;

        _nextMoveForceDiagnosticsSampleTime = Time.time;
        _nextMoveStateDiagnosticsSampleTime = Time.time;
        _nextMovePublishDiagnosticsSampleTime = Time.time;
        _nextMoveProxyDiagnosticsSampleTime = Time.time;
        _nextMoveFacingDiagnosticsSampleTime = Time.time;
        _lastMoveForceSignature = string.Empty;
        _lastMoveStateSignature = string.Empty;
        _lastMovePublishSignature = string.Empty;
        _lastMoveProxySignature = string.Empty;
        _lastMoveFacingSignature = string.Empty;

        Debug.Log($"[MoveDiag] Manual snapshot requested for {name}.", this);
        TraceMoveAuthorityState("F7");
        TraceMovePublish("F7");
        TraceMoveProxyState("F7");
    }

    internal void TraceMoveAuthorityInput(string source, in PlayerNetworkInput input)
    {
        _ = source;
        _ = input;
    }

    internal void TraceMoveAuthorityState(string source)
    {
        if (!ShouldLogMoveDiagnostics() || !HasStateAuthority)
            return;

        var signature =
            $"{source}|{GetPhysicalPhase()}|{(CanDriveLocomotion ? 1 : 0)}|{(_isGrounded ? 1 : 0)}|{_beingGrabbedRefCount}|{(_localIsDragged ? 1 : 0)}|{_localMoveSpeed:F2}";
        if (!ShouldEmitMoveDiagnostic(
                signature,
                MoveDiagnosticsStateSampleInterval,
                ref _lastMoveStateSignature,
                ref _nextMoveStateDiagnosticsSampleTime))
        {
            return;
        }

        EnsureMoveDiagnosticsOverlay();
        Debug.Log(
            $"[MoveState] {name} {source} t={Time.time:F2} phase={GetPhysicalPhase()} canDrive={(CanDriveLocomotion ? 1 : 0)} grounded={(_isGrounded ? 1 : 0)} " +
            $"grabbedRef={_beingGrabbedRefCount} grabbedNet={(NetworkedIsBeingGrabbed ? 1 : 0)} dragged={(_localIsDragged ? 1 : 0)} " +
            $"holdStunned={(IsAnyHandHoldingStunnedPlayer ? 1 : 0)} dualHold={(IsDualGrabbingStunnedPlayer ? 1 : 0)} " +
            $"move={_localMoveSpeed:F2} netMove={NetworkedMoveSpeed:F2} reason={(CanDriveLocomotion ? "active" : "blocked")}",
            this);
    }

    internal void TraceMovePublish(string source)
    {
        if (!ShouldLogMoveDiagnostics() || !HasStateAuthority || Runner == null || Object == null || !Object.IsValid)
            return;

        var signature =
            $"{source}|{NetworkedMoveSpeed:F2}|{(byte)NetworkedPhysicalPhase}|{(NetworkedIsGrounded ? 1 : 0)}|{(NetworkedIsSprinting ? 1 : 0)}|{(NetworkedIsDragged ? 1 : 0)}";
        if (!ShouldEmitMoveDiagnostic(
                signature,
                MoveDiagnosticsPublishSampleInterval,
                ref _lastMovePublishSignature,
                ref _nextMovePublishDiagnosticsSampleTime))
        {
            return;
        }

        EnsureMoveDiagnosticsOverlay();
        Debug.Log(
            $"[MovePublish] {name} {source} t={Time.time:F2} netMove={NetworkedMoveSpeed:F2} localMove={_localMoveSpeed:F2} " +
            $"netPhase={(PhysicalPhase)NetworkedPhysicalPhase} localPhase={_localPhysicalPhase} netGround={(NetworkedIsGrounded ? 1 : 0)} " +
            $"netSprint={(NetworkedIsSprinting ? 1 : 0)} netDragged={(NetworkedIsDragged ? 1 : 0)}",
            this);
    }

    internal void TraceMoveProxyState(string source)
    {
        if (!ShouldLogMoveDiagnostics() || Runner == null || Object == null || !Object.IsValid || HasStateAuthority)
            return;

        var networkedMove = GetNetworkedMoveSpeed();
        var planarSpeed = rigidbody3D != null ? new Vector2(rigidbody3D.velocity.x, rigidbody3D.velocity.z).magnitude : 0f;
        var observedGrounded = GetObservedGroundedState();
        var signature =
            $"{source}|{GetPhysicalPhase()}|{networkedMove:F2}|{planarSpeed:F2}|{(observedGrounded ? 1 : 0)}|{(GetNetworkedIsSprinting() ? 1 : 0)}";
        if (!ShouldEmitMoveDiagnostic(
                signature,
                MoveDiagnosticsProxySampleInterval,
                ref _lastMoveProxySignature,
                ref _nextMoveProxyDiagnosticsSampleTime))
        {
            return;
        }

        EnsureMoveDiagnosticsOverlay();
        Debug.Log(
            $"[MoveProxy] {name} {source} t={Time.time:F2} auth={(HasStateAuthority ? 1 : 0)} input={(HasInputAuthority ? 1 : 0)} " +
            $"phase={GetPhysicalPhase()} grounded={(observedGrounded ? 1 : 0)} netGround={(NetworkedIsGrounded ? 1 : 0)} " +
            $"netMove={networkedMove:F2} localMove={_localMoveSpeed:F2} planar={planarSpeed:F2} sprintNet={(GetNetworkedIsSprinting() ? 1 : 0)} " +
            $"draggedNet={(NetworkedIsDragged ? 1 : 0)} pos={FormatMoveVector3(transform.position)}",
            this);
    }

    internal void TraceMoveHoldForce(
        string source,
        Vector3 moveDirection,
        float inputMagnitude,
        float moveSpeedMultiplier,
        Vector3 planarVelocity,
        Vector3 targetVelocity,
        Vector3 velocityDelta,
        float acceleration,
        bool sprintPressed,
        bool recoilActive,
        bool unstableHitPenalty)
    {
        if (!ShouldLogMoveDiagnostics())
            return;

        var buffApplier = ResolveItemBuffApplier();
        var buffMoveMultiplier = buffApplier != null ? buffApplier.CurrentMoveSpeedMultiplier : 1f;
        var carryMultiplier = _localPhysicalPhase == PhysicalPhase.CarryingStunned ? 0.7f : 1f;
        var sprintMultiplier = sprintPressed ? (config != null ? config.sprintSpeedMultiplier : 1.8f) : 1f;
        var hostCompensation = ResolveHostRemoteClientMoveSpeedCompensation();
        var hitPenaltyMultiplier = unstableHitPenalty ? HitReactionMoveSpeedScale : 1f;
        var totalMoveMultiplier = buffMoveMultiplier * carryMultiplier * sprintMultiplier * hostCompensation * hitPenaltyMultiplier;
        var targetSpeed = targetVelocity.magnitude;
        var planarSpeed = planarVelocity.magnitude;
        var reasons = BuildMoveReasonSummary(
            buffMoveMultiplier,
            carryMultiplier,
            sprintMultiplier,
            hostCompensation,
            hitPenaltyMultiplier,
            recoilActive,
            unstableHitPenalty);

        if (inputMagnitude <= 0.01f &&
            planarSpeed <= 0.15f &&
            reasons == "normal")
        {
            return;
        }

        var runnerLocalOwns =
            Runner != null &&
            Runner.LocalPlayer.IsRealPlayer &&
            Object != null &&
            Object.IsValid &&
            Object.InputAuthority == Runner.LocalPlayer;
        var preHitMoveMultiplier = moveSpeedMultiplier;

        var signature =
            $"{source}|{GetPhysicalPhase()}|{inputMagnitude:F2}|{totalMoveMultiplier:F2}|{targetSpeed:F2}|{planarSpeed:F2}|{recoilActive}|{unstableHitPenalty}|{reasons}";
        if (!ShouldEmitMoveDiagnostic(
                signature,
                MoveDiagnosticsForceSampleInterval,
                ref _lastMoveForceSignature,
                ref _nextMoveForceDiagnosticsSampleTime))
        {
            return;
        }

        EnsureMoveDiagnosticsOverlay();
        Debug.Log(
            $"[MoveDiag] {name} {source} t={Time.time:F2} auth={(HasStateAuthority ? 1 : 0)} input={(HasInputAuthority ? 1 : 0)} server={(Runner != null && Runner.IsServer ? 1 : 0)} ownerLocal={(runnerLocalOwns ? 1 : 0)} " +
            $"phase={GetPhysicalPhase()} grounded={(_isGrounded ? 1 : 0)} inputMag={inputMagnitude:F2} preHit={preHitMoveMultiplier:F2} buff={buffMoveMultiplier:F2} carry={carryMultiplier:F2} sprint={sprintMultiplier:F2} " +
            $"hostComp={hostCompensation:F2} hitMul={hitPenaltyMultiplier:F2} total={totalMoveMultiplier:F2} targetSpeed={targetSpeed:F2} planar={planarSpeed:F2} accel={acceleration:F2} " +
            $"recoil={(recoilActive ? 1 : 0)} unstable={(unstableHitPenalty ? 1 : 0)} moveDir={FormatMoveVector3(moveDirection)} planarVel={FormatMoveVector3(planarVelocity)} " +
            $"targetVel={FormatMoveVector3(targetVelocity)} delta={FormatMoveVector3(velocityDelta)} reason={reasons}",
            this);
    }

    internal void TraceMoveHoldFacing(
        string source,
        float currentYaw,
        float anchorYaw,
        float desiredYaw,
        float currentDelta,
        float desiredDelta,
        float clampedDelta,
        float softLimit,
        float hardLimit,
        float rotateSpeed,
        bool hasMoveInput)
    {
        if (!ShouldLogMoveDiagnostics())
            return;

        var signature =
            $"{source}|{GetPhysicalPhase()}|{Mathf.RoundToInt(currentDelta)}|{Mathf.RoundToInt(desiredDelta)}|{Mathf.RoundToInt(clampedDelta)}|{(hasMoveInput ? 1 : 0)}";
        if (!ShouldEmitMoveDiagnostic(
                signature,
                MoveDiagnosticsFacingSampleInterval,
                ref _lastMoveFacingSignature,
                ref _nextMoveFacingDiagnosticsSampleTime))
        {
            return;
        }

        EnsureMoveDiagnosticsOverlay();
        Debug.Log(
            $"[MoveFacing] {name} {source} t={Time.time:F2} phase={GetPhysicalPhase()} holdStunned={(IsAnyHandHoldingStunnedPlayer ? 1 : 0)} dualHold={(IsDualGrabbingStunnedPlayer ? 1 : 0)} " +
            $"moveInput={(hasMoveInput ? 1 : 0)} yaw(current={currentYaw:F1},anchor={anchorYaw:F1},desired={desiredYaw:F1}) " +
            $"delta(current={currentDelta:F1},desired={desiredDelta:F1},clamped={clampedDelta:F1}) limit(soft={softLimit:F1},hard={hardLimit:F1}) rot={rotateSpeed:F1}",
            this);
    }
}
