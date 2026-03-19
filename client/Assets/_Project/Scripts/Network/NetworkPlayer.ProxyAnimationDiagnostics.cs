using System;
using System.IO;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    [Header("Diagnostics")]
    [SerializeField] private bool enableProxyAnimationDiagnostics = true;
    [Header("Stun Force Diagnostics")]
    [SerializeField] private bool enableStunForceDiagnostics = false;
    [SerializeField] private bool stunForceDiagnosticsIncludeProxies = true;
    [SerializeField, Range(0.25f, 5f)] private float stunForceDiagnosticsWindow = 1.5f;
    [SerializeField, Range(0.02f, 0.5f)] private float stunForceDiagnosticsSampleInterval = 0.12f;

    private static bool s_runtimeStunForceDiagnosticsEnabled;
    private static int s_runtimeStunForceDiagnosticsLastToggleFrame = -1;
    private static string s_runtimeStunForceDiagnosticsPath;
    private static StreamWriter s_runtimeStunForceDiagnosticsWriter;

    private bool _proxyAnimationDiagInitialized;
    private PhysicalPhase _proxyAnimationDiagLastPhase;
    private bool _proxyAnimationDiagLastPhysicalPresentation;
    private bool _proxyAnimationDiagLastHardPresentation;
    private bool _proxyAnimationDiagLastAnimatorEnabled;
    private bool _proxyAnimationDiagLastNetworkedRagdoll;
    private PresentationLocomotionState _proxyAnimationDiagLastLocomotion;
    private float _proxyAnimationDiagLastMoveSpeed = float.NaN;
    private float _proxyAnimationDiagLastVisualYaw = float.NaN;
    private byte _proxyAnimationDiagLastResetVersion;
    private float _stunForceDiagnosticsUntilTime;
    private float _stunForceDiagnosticsLastAuthoritySampleTime = float.NegativeInfinity;
    private float _stunForceDiagnosticsLastProxySampleTime = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStunForceDiagnosticsRuntimeState()
    {
        s_runtimeStunForceDiagnosticsEnabled = false;
        s_runtimeStunForceDiagnosticsLastToggleFrame = -1;
        s_runtimeStunForceDiagnosticsPath = null;
        s_runtimeStunForceDiagnosticsWriter?.Dispose();
        s_runtimeStunForceDiagnosticsWriter = null;
    }

    private bool ShouldEmitProxyAnimationDiagnostics()
    {
        if (!enableProxyAnimationDiagnostics || !Application.isPlaying)
            return false;

        if (!(Debug.isDebugBuild || Application.isEditor))
            return false;

        return Runner != null && Object != null && Object.IsValid && !HasStateAuthority;
    }

    private void TraceProxyAnimationDiagnostics(string source)
    {
        if (!ShouldEmitProxyAnimationDiagnostics())
            return;

        var phase = GetPhysicalPhase();
        var usesPhysicalPresentation = ShouldUsePhysicalPhasePresentation();
        var usesHardPresentation = ShouldUseHardPhysicsPresentation();
        var animatorEnabled = animator != null && animator.enabled;
        var networkedRagdoll = NetworkedIsActiveRagdoll;
        var locomotionState = GetNetworkedLocomotionState();
        var moveSpeed = GetNetworkedMoveSpeed();
        var visualYaw = GetNetworkedVisualYaw();
        var resetVersion = NetworkedPhysicsPresentationResetVersion;

        var changed = !_proxyAnimationDiagInitialized
                      || _proxyAnimationDiagLastPhase != phase
                      || _proxyAnimationDiagLastPhysicalPresentation != usesPhysicalPresentation
                      || _proxyAnimationDiagLastHardPresentation != usesHardPresentation
                      || _proxyAnimationDiagLastAnimatorEnabled != animatorEnabled
                      || _proxyAnimationDiagLastNetworkedRagdoll != networkedRagdoll
                      || _proxyAnimationDiagLastLocomotion != locomotionState
                      || Mathf.Abs(_proxyAnimationDiagLastMoveSpeed - moveSpeed) > 0.2f
                      || Mathf.Abs(Mathf.DeltaAngle(_proxyAnimationDiagLastVisualYaw, visualYaw)) > 10f
                      || _proxyAnimationDiagLastResetVersion != resetVersion;

        if (!changed)
            return;

        _proxyAnimationDiagInitialized = true;
        _proxyAnimationDiagLastPhase = phase;
        _proxyAnimationDiagLastPhysicalPresentation = usesPhysicalPresentation;
        _proxyAnimationDiagLastHardPresentation = usesHardPresentation;
        _proxyAnimationDiagLastAnimatorEnabled = animatorEnabled;
        _proxyAnimationDiagLastNetworkedRagdoll = networkedRagdoll;
        _proxyAnimationDiagLastLocomotion = locomotionState;
        _proxyAnimationDiagLastMoveSpeed = moveSpeed;
        _proxyAnimationDiagLastVisualYaw = visualYaw;
        _proxyAnimationDiagLastResetVersion = resetVersion;

        var presentationYaw = ResolvePresentationYawFromTransform();
        Debug.Log(
            $"[ProxyAnimDiag:{source}] name={name} inputAuth={HasInputAuthority} stateAuth={HasStateAuthority} " +
            $"phase={phase} phys={usesPhysicalPresentation} hard={usesHardPresentation} netRagdoll={networkedRagdoll} " +
            $"animEnabled={animatorEnabled} loco={locomotionState} moveSpeed={moveSpeed:F2} " +
            $"visualYaw={visualYaw:F1} presentationYaw={presentationYaw:F1} resetVer={resetVersion} " +
            $"eventSeq={NetworkedAnimationEventSequence}",
            this);
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
        string targetMuscleName)
    {
        if (!ShouldEmitStunForceDiagnostics(false))
            return;

        if (!IsStunForceDiagnosticsInteresting() && rootForce <= 0.0001f)
            return;

        ExtendStunForceDiagnosticsWindow();
        EmitStunForceDiagnostics(
            $"[StunDiag:ImpulseSummary] role={ResolveStunForceDiagnosticsRole()} name={name} source={source} " +
            $"phase={GetPhysicalPhase()} rootForce={rootForce:F2} focusedScale={focusedScale:F3} " +
            $"spreadScale={spreadScale:F3} twistScale={twistTorqueScale:F3} targetMuscle={targetMuscleName}");
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
