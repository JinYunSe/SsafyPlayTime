using System;
using System.IO;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    [Header("Stun Force Diagnostics")]
    [SerializeField] private bool enableStunForceDiagnostics = false;
    [SerializeField] private bool stunForceDiagnosticsIncludeProxies = true;
    [SerializeField, Range(0.25f, 5f)] private float stunForceDiagnosticsWindow = 1.5f;
    [SerializeField, Range(0.02f, 0.5f)] private float stunForceDiagnosticsSampleInterval = 0.12f;

    private static bool s_runtimeStunForceDiagnosticsEnabled;
    private static int s_runtimeStunForceDiagnosticsLastToggleFrame = -1;
    private static string s_runtimeStunForceDiagnosticsPath;
    private static StreamWriter s_runtimeStunForceDiagnosticsWriter;

    private float _stunForceDiagnosticsUntilTime;
    private float _stunForceDiagnosticsLastAuthoritySampleTime = float.NegativeInfinity;
    private float _stunForceDiagnosticsLastProxySampleTime = float.NegativeInfinity;
    private float _stunForceDiagnosticsLastCollapseSampleTime = float.NegativeInfinity;
    private float _stunForceDiagnosticsLastCarrySampleTime = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStunForceDiagnosticsRuntimeState()
    {
        s_runtimeStunForceDiagnosticsEnabled = false;
        s_runtimeStunForceDiagnosticsLastToggleFrame = -1;
        s_runtimeStunForceDiagnosticsPath = null;
        s_runtimeStunForceDiagnosticsWriter?.Dispose();
        s_runtimeStunForceDiagnosticsWriter = null;
    }

    internal void UpdateStunForceDiagnosticsHotkey()
    {
        if (!Application.isPlaying)
            return;

        if (!(Runner == null || HasInputAuthority))
            return;

        if (s_runtimeStunForceDiagnosticsLastToggleFrame == Time.frameCount)
            return;

        if (!Input.GetKeyDown(KeyCode.F6))
            return;

        s_runtimeStunForceDiagnosticsLastToggleFrame = Time.frameCount;
        ToggleRuntimeStunForceDiagnostics();
    }

    private bool ShouldEmitStunForceDiagnostics(bool allowProxy)
    {
        if (!IsStunForceDiagnosticsEnabled() || !Application.isPlaying)
            return false;

        if (!s_runtimeStunForceDiagnosticsEnabled && !(Debug.isDebugBuild || Application.isEditor))
            return false;

        if (!allowProxy && Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return false;

        if (!stunForceDiagnosticsIncludeProxies &&
            Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
        {
            return false;
        }

        return true;
    }

    private bool IsStunForceDiagnosticsEnabled()
    {
        return enableStunForceDiagnostics || s_runtimeStunForceDiagnosticsEnabled;
    }

    private bool IsStunForceDiagnosticsWindowActive()
    {
        return Time.time <= _stunForceDiagnosticsUntilTime;
    }

    private bool IsStunForceDiagnosticsInteresting()
    {
        return !_isActiveRagdoll || _isRecovering || IsStunForceDiagnosticsWindowActive();
    }

    private void ExtendStunForceDiagnosticsWindow()
    {
        _stunForceDiagnosticsUntilTime = Mathf.Max(_stunForceDiagnosticsUntilTime, Time.time + stunForceDiagnosticsWindow);
    }

    private string ResolveStunForceDiagnosticsRole()
    {
        if (Runner == null || Object == null || !Object.IsValid)
            return "Offline";

        if (HasStateAuthority && HasInputAuthority)
            return "HostOwner";

        if (HasStateAuthority)
            return "HostReplica";

        if (HasInputAuthority)
            return "OwnerProxy";

        return "RemoteProxy";
    }

    private static string FormatStunForceDiagnosticsVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    private void EmitStunForceDiagnostics(string line)
    {
        if (enableStunForceDiagnostics)
            Debug.Log(line, this);

        if (s_runtimeStunForceDiagnosticsEnabled)
            WriteRuntimeStunForceDiagnostics(line);
    }

    private static void ToggleRuntimeStunForceDiagnostics()
    {
        if (s_runtimeStunForceDiagnosticsEnabled)
        {
            WriteRuntimeStunForceDiagnostics("[StunDiag] capture stopped");
            s_runtimeStunForceDiagnosticsEnabled = false;
            s_runtimeStunForceDiagnosticsWriter?.Dispose();
            s_runtimeStunForceDiagnosticsWriter = null;
            Debug.Log($"[StunDiag] F6 capture OFF path={s_runtimeStunForceDiagnosticsPath}");
            return;
        }

        s_runtimeStunForceDiagnosticsEnabled = true;
        EnsureRuntimeStunForceDiagnosticsWriter();
        WriteRuntimeStunForceDiagnostics("[StunDiag] capture started");
        Debug.Log($"[StunDiag] F6 capture ON path={s_runtimeStunForceDiagnosticsPath}");
    }

    private static void EnsureRuntimeStunForceDiagnosticsWriter()
    {
        if (s_runtimeStunForceDiagnosticsWriter != null)
            return;

        var directory = ResolveRuntimeStunForceDiagnosticsDirectory();
        Directory.CreateDirectory(directory);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        s_runtimeStunForceDiagnosticsPath = Path.Combine(directory, $"stun-force-{timestamp}.log");
        s_runtimeStunForceDiagnosticsWriter = new StreamWriter(
            new FileStream(s_runtimeStunForceDiagnosticsPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        s_runtimeStunForceDiagnosticsWriter.WriteLine($"# stun-force diagnostics started {DateTime.Now:O}");
    }

    private static string ResolveRuntimeStunForceDiagnosticsDirectory()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
                return Path.Combine(desktop, "StunForceLogs");
        }
        catch
        {
        }

        return Path.Combine(Application.persistentDataPath, "StunForceLogs");
    }

    private static void WriteRuntimeStunForceDiagnostics(string line)
    {
        try
        {
            EnsureRuntimeStunForceDiagnosticsWriter();
            s_runtimeStunForceDiagnosticsWriter?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[StunDiag] failed to write runtime log: {ex.Message}");
        }
    }

    internal void ArmStunForceDiagnostics(string source, string details = null)
    {
        if (!ShouldEmitStunForceDiagnostics(true))
            return;

        ExtendStunForceDiagnosticsWindow();

        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" details={details}";
        EmitStunForceDiagnostics(
            $"[StunDiag:Arm] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} activeRagdoll={_isActiveRagdoll} recovering={_isRecovering} " +
            $"stunRemaining={GetStunTimeRemaining():F2}{suffix}");
    }

    internal void TraceCarryDebugSample(string source, string details = null, bool forceSample = false)
    {
        if (!ShouldEmitStunForceDiagnostics(true))
            return;

        if (!forceSample && !IsStunForceDiagnosticsInteresting())
            return;

        if (!forceSample && Time.time - _stunForceDiagnosticsLastCarrySampleTime < stunForceDiagnosticsSampleInterval)
            return;

        _stunForceDiagnosticsLastCarrySampleTime = Time.time;
        ExtendStunForceDiagnosticsWindow();

        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" details={details}";
        var networkGrabbed = IsNetworkReady ? NetworkedIsBeingGrabbed.ToString() : "N/A";
        EmitStunForceDiagnostics(
            $"[StunDiag:Carry] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} activeRagdoll={_isActiveRagdoll} recovering={_isRecovering} " +
            $"stunRemaining={GetStunTimeRemaining():F2} beingGrabbed={_beingGrabbedRefCount} grabbedBy={_grabbedByCount} " +
            $"netGrabbed={networkGrabbed} localHolding={IsAnyHandHolding} anyStunned={IsAnyHandHoldingStunnedPlayer} " +
            $"dual={IsDualGrabbingStunnedPlayer} leftConfirmed={LeftGrabConfirmed} rightConfirmed={RightGrabConfirmed}{suffix}");
    }

    internal void TraceStunCollisionImpact(string source, float impactMagnitude, Vector3 impulse, Vector3 normal)
    {
        if (!ShouldEmitStunForceDiagnostics(false))
            return;

        ExtendStunForceDiagnosticsWindow();
        EmitStunForceDiagnostics(
            $"[StunDiag:Collision] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} impact={impactMagnitude:F2} impulse={FormatStunForceDiagnosticsVector(impulse)} " +
            $"normal={FormatStunForceDiagnosticsVector(normal)}");
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
        if (!ShouldEmitStunForceDiagnostics(true))
            return;

        if (!IsStunForceDiagnosticsInteresting() && vector.sqrMagnitude <= 0.0001f && applied)
            return;

        ExtendStunForceDiagnosticsWindow();

        var targetName = targetRigidbody != null ? targetRigidbody.name : "null";
        var suffix = string.IsNullOrWhiteSpace(note) ? string.Empty : $" note={note}";
        EmitStunForceDiagnostics(
            $"[StunDiag:Force] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} target={targetName} applied={applied} mode={mode} " +
            $"vec={FormatStunForceDiagnosticsVector(vector)} mag={vector.magnitude:F2} " +
            $"velBefore={FormatStunForceDiagnosticsVector(velocityBefore)} " +
            $"velAfter={FormatStunForceDiagnosticsVector(velocityAfter)}{suffix}");
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
        if (!ShouldEmitStunForceDiagnostics(false))
            return;

        if (!IsStunForceDiagnosticsInteresting() && rootForce <= 0.0001f)
            return;

        ExtendStunForceDiagnosticsWindow();
        var suffix = string.IsNullOrWhiteSpace(note) ? string.Empty : $" note={note}";
        EmitStunForceDiagnostics(
            $"[StunDiag:ImpulseSummary] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} rootForce={rootForce:F2} focusedScale={focusedScale:F3} " +
            $"spreadScale={spreadScale:F3} twistScale={twistTorqueScale:F3} targetMuscle={targetMuscleName}{suffix}");
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
        if (!ShouldEmitStunForceDiagnostics(false))
            return;

        if (!IsStunForceDiagnosticsInteresting())
            return;

        ExtendStunForceDiagnosticsWindow();
        EmitStunForceDiagnostics(
            $"[StunDiag:Clamp] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} rootVelBefore={FormatStunForceDiagnosticsVector(rootVelocityBefore)} " +
            $"rootVelAfter={FormatStunForceDiagnosticsVector(rootVelocityAfter)} " +
            $"rootAngBefore={FormatStunForceDiagnosticsVector(rootAngularBefore)} " +
            $"rootAngAfter={FormatStunForceDiagnosticsVector(rootAngularAfter)} " +
            $"maxMusclePlanarBefore={maxMusclePlanarBefore:F2} maxMusclePlanarAfter={maxMusclePlanarAfter:F2}");
    }

    internal void TraceStunCollapsePose(string source, bool forceSample = false)
    {
        if (!ShouldEmitStunForceDiagnostics(false))
            return;

        if (!forceSample && GetPhysicalPhase() != PhysicalPhase.StunnedCollapse)
            return;

        if (!forceSample && !IsStunForceDiagnosticsInteresting())
            return;

        if (!forceSample && Time.time - _stunForceDiagnosticsLastCollapseSampleTime < stunForceDiagnosticsSampleInterval)
            return;

        _stunForceDiagnosticsLastCollapseSampleTime = Time.time;

        var rootPosition = transform.position;
        var rootVelocity = rigidbody3D != null ? rigidbody3D.velocity : Vector3.zero;
        var rootAngular = rigidbody3D != null ? rigidbody3D.angularVelocity : Vector3.zero;
        var pelvisPosition = rootPosition;
        var pelvisVelocity = Vector3.zero;
        var headPosition = rootPosition + Vector3.up * 1.25f;

        if (_puppetMaster != null &&
            _puppetMaster.muscles != null &&
            _puppetMaster.muscles.Length > 0 &&
            _puppetMaster.muscles[0].joint != null)
        {
            var pelvisJoint = _puppetMaster.muscles[0].joint;
            pelvisPosition = pelvisJoint.transform.position;
            var pelvisBody = pelvisJoint.GetComponent<Rigidbody>();
            if (pelvisBody != null)
                pelvisVelocity = pelvisBody.velocity;
        }

        EnsureRecoveryPoseReferences();
        if (_recoveryPoseHead != null)
        {
            headPosition = _recoveryPoseHead.position;
        }
        else if (animator != null && animator.isHuman)
        {
            var headBone = animator.GetBoneTransform(HumanBodyBones.Head)
                           ?? animator.GetBoneTransform(HumanBodyBones.UpperChest)
                           ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            if (headBone != null)
                headPosition = headBone.position;
        }

        var anchorPosition = _hasRecoverAnchorPose ? _recoverAnchorPosition : rootPosition;
        var anchorDelta = rootPosition - anchorPosition;
        var rootToPelvis = pelvisPosition - rootPosition;
        var pelvisToHead = headPosition - pelvisPosition;
        var spineLength = pelvisToHead.magnitude;
        var mainSpring = mainJoint != null ? mainJoint.slerpDrive.positionSpring : 0f;
        var bodyUpDot = spineLength > 0.0001f
            ? Vector3.Dot(pelvisToHead / spineLength, Vector3.up)
            : 0f;

        EmitStunForceDiagnostics(
            $"[StunDiag:CollapsePose] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} collapseTimer={_stunCollapseTimer:F2} stunRemaining={GetStunTimeRemaining():F2} " +
            $"grabbed={_beingGrabbedRefCount} collapseEarly={IsEarlyCollapsePhaseActive()} mainSpring={mainSpring:F1} " +
            $"anchorYaw={_recoverAnchorRotation.eulerAngles.y:F1} " +
            $"rootYaw={transform.eulerAngles.y:F1} anchor={FormatStunForceDiagnosticsVector(anchorPosition)} " +
            $"anchorDelta={FormatStunForceDiagnosticsVector(anchorDelta)} root={FormatStunForceDiagnosticsVector(rootPosition)} " +
            $"pelvis={FormatStunForceDiagnosticsVector(pelvisPosition)} head={FormatStunForceDiagnosticsVector(headPosition)} " +
            $"rootToPelvis={FormatStunForceDiagnosticsVector(rootToPelvis)} " +
            $"pelvisToHead={FormatStunForceDiagnosticsVector(pelvisToHead)} spineLen={spineLength:F2} " +
            $"bodyUpDot={bodyUpDot:F3} rootVel={FormatStunForceDiagnosticsVector(rootVelocity)} " +
            $"pelvisVel={FormatStunForceDiagnosticsVector(pelvisVelocity)} " +
            $"rootAng={FormatStunForceDiagnosticsVector(rootAngular)}");
    }

    internal void TraceStunnedMotionSample(string source)
    {
        if (!ShouldEmitStunForceDiagnostics(false))
            return;

        if (!IsStunForceDiagnosticsInteresting())
            return;

        if (Time.time - _stunForceDiagnosticsLastAuthoritySampleTime < stunForceDiagnosticsSampleInterval)
            return;

        _stunForceDiagnosticsLastAuthoritySampleTime = Time.time;

        var rootVelocity = rigidbody3D != null ? rigidbody3D.velocity : Vector3.zero;
        var rootAngular = rigidbody3D != null ? rigidbody3D.angularVelocity : Vector3.zero;
        var pelvisPosition = Vector3.zero;
        if (_puppetMaster != null &&
            _puppetMaster.muscles != null &&
            _puppetMaster.muscles.Length > 0 &&
            _puppetMaster.muscles[0].joint != null)
        {
            pelvisPosition = _puppetMaster.muscles[0].joint.transform.position;
        }

        EmitStunForceDiagnostics(
            $"[StunDiag:Sample] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} activeRagdoll={_isActiveRagdoll} recovering={_isRecovering} " +
            $"stunRemaining={GetStunTimeRemaining():F2} rootVel={FormatStunForceDiagnosticsVector(rootVelocity)} " +
            $"rootAng={FormatStunForceDiagnosticsVector(rootAngular)} pelvis={FormatStunForceDiagnosticsVector(pelvisPosition)}");
    }

    internal void TraceProxyStunPresentation(string source, Vector3 hipsCurrent, Vector3 hipsTarget)
    {
        if (!ShouldEmitStunForceDiagnostics(true))
            return;

        if (Runner != null && Object != null && Object.IsValid && HasStateAuthority)
            return;

        if (!IsStunForceDiagnosticsInteresting())
            return;

        if (Time.time - _stunForceDiagnosticsLastProxySampleTime < stunForceDiagnosticsSampleInterval)
            return;

        _stunForceDiagnosticsLastProxySampleTime = Time.time;

        EmitStunForceDiagnostics(
            $"[StunDiag:Proxy] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} netRagdoll={NetworkedIsActiveRagdoll} " +
            $"hipsCurrent={FormatStunForceDiagnosticsVector(hipsCurrent)} " +
            $"hipsTarget={FormatStunForceDiagnosticsVector(hipsTarget)} " +
            $"hipsDelta={(hipsTarget - hipsCurrent).magnitude:F3} root={FormatStunForceDiagnosticsVector(transform.position)}");
    }
}
