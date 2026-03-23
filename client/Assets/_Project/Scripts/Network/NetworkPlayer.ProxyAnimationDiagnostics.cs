using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    internal void UpdateStunForceDiagnosticsHotkey()
    {
    }

    internal void ArmStunForceDiagnostics(string source, string details = null)
    {
    }

    internal void TraceCarryDebugSample(string source, string details = null, bool forceSample = false)
    {
        if (!enableProxyAnimationDiagnostics || !Application.isPlaying)
            return;
        if (!(Debug.isDebugBuild || Application.isEditor))
            return;

        var phase = GetPhysicalPhase();
        var carryMode = GetLocalCarryMode();
        var carryRelevant = phase == PhysicalPhase.BeingCarriedStunned ||
                            phase == PhysicalPhase.CarryingStunned ||
                            carryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None ||
                            _beingGrabbedRefCount > 0 ||
                            _grabbedByCount > 0;
        if (!forceSample && !carryRelevant)
            return;

        var now = Time.unscaledTime;
        if (!forceSample && now - _carryDiagnosticsLastSampleTime < CarryDiagnosticsSampleInterval)
            return;

        _carryDiagnosticsLastSampleTime = now;

        var tick = Runner != null ? Runner.Tick.Raw : -1;
        var role = HasStateAuthority
            ? "StateAuth"
            : (HasInputAuthority ? "InputProxy" : "RemoteProxy");
        var detailSuffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" {details}";

        Debug.Log(
            $"[CarryDiag] tick={tick} frame={Time.frameCount} role={role} name={name} source={source} " +
            $"phase={phase} carry={carryMode} victimAnchorValid={(bool)NetworkedVictimAnchorValid} " +
            $"victimRootOffsetValid={(bool)NetworkedVictimRootOffsetValid} victimCarryRootValid={(bool)NetworkedVictimCarryRootValid} " +
            $"carrierAnchorValid={(bool)NetworkedCarrierAnchorValid} " +
            $"hipsNet={FormatCarryDebugVector(NetworkedHipsPosition)} root={FormatCarryDebugVector(transform.position)}" +
            detailSuffix,
            this);
    }

    internal void TraceStunCollisionImpact(string source, float impactMagnitude, Vector3 impulse, Vector3 normal)
    {
    }

    internal void TraceStunForceEvent(
        string source,
        Rigidbody targetRigidbody,
        Vector3 vector,
        ForceMode mode,
        Vector3 velocityBefore,
        Vector3 velocityAfter,
        bool applied,
        string note = null)
    {
    }

    internal void TraceStunImpulseSummary(
        string source,
        float rootForce,
        float focusedScale,
        float spreadScale,
        float twistTorqueScale,
        string targetMuscleName,
        string note = null)
    {
    }

    internal void TraceStunVelocityClamp(
        string source,
        Vector3 rootVelocityBefore,
        Vector3 rootVelocityAfter,
        Vector3 rootAngularBefore,
        Vector3 rootAngularAfter,
        float maxMusclePlanarBefore,
        float maxMusclePlanarAfter)
    {
    }

    internal void TraceStunCollapsePose(string source, bool forceSample = false)
    {
    }

    internal void TraceStunnedMotionSample(string source)
    {
    }

    internal void TraceProxyStunPresentation(string source, Vector3 hipsCurrent, Vector3 hipsTarget)
    {
    }

    private static string FormatStunForceDiagnosticsVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
