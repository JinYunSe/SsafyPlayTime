using UnityEngine;

public sealed partial class NetworkPlayer
{
    [Header("Diagnostics")]
    [SerializeField] private bool enableProxyAnimationDiagnostics = true;

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
}
