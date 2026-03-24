using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using RootMotion.Dynamics;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class PartyMonsterAnimationDriver : MonoBehaviour
{
    const int LocoLayer = 0;
    const int UpperBodyLayer = 1;

    const string IdleState = "Idle01";
    const string WalkState = "WalkFWD";
    const string SprintState = "SprintFWD";
    const string PunchLeftState = "PunchLeft";
    const string PunchRightState = "PunchRight";
    const string GrabState = "GrabIdle";
    const string CarryState = "Carry";
    const string ThrowState = "Throw";
    const string DefaultAerialKickClipName = "Attack02";
    const string AerialKickCombatStatId = "JET_KICK";
    const string UpperBodyIdleState = "UpperBodyIdle";
    const string MovementSpeedParameter = "movementSpeed";
    const string IsSprintingParameter = "isSprinting";
    const float AerialKickEndPoseHoldMaxDuration = 4.00f;
    const float AerialKickEndPoseHoldExtensionDuration = 0.75f;

    [SerializeField]
    Animator animator;

    [SerializeField]
    Rigidbody rigidbody3D;

    [SerializeField]
    float locomotionThreshold = 0.1f;

    [SerializeField]
    float grabHoldThreshold = 0.15f;

    [SerializeField]
    float attackLockDuration = 0.08f;

    [SerializeField]
    float attackVisualDuration = 0.3f;

    [SerializeField]
    float throwLockDuration = 0.85f;

    [SerializeField]
    AnimationClip aerialKickClip;

    [SerializeField]
    float aerialKickLockDuration = 0.72f;

    [SerializeField]
    AnimationClip recoverySupineClip;

    [SerializeField]
    AnimationClip recoveryProneClip;

    PuppetMaster puppetMaster;
    BehaviourPuppet behaviourPuppet;
    NetworkPlayer networkPlayer;
    CharacterGrabController characterGrabController;
    float attackButtonPressedAt = -1f;
    float nextOwnerProxyAerialKickPredictionAt = float.NegativeInfinity;
    float actionLockedUntil;
    float upperBodyStateVisibleUntil;
    bool isGrabPoseActive;
    bool hasMovementSpeedParameter;
    bool hasIsSprintingParameter;
    int movementSpeedParameterHash;
    int isSprintingParameterHash;
    bool nextAttackLeft;
    bool isSprinting;
    string currentStateName;
    string currentUpperBodyStateName;

    // 콤보 입력 버퍼: 잠금 중 입력된 펀치를 잠금 해제 직후 실행
    bool _punchBuffered;
    float _punchBufferExpiry;
    bool _wasActionLocked;
    const float PunchBufferWindow = 0.25f;

    // 네트워크: 원격 프록시 모드 (로컬 입력 대신 네트워크 데이터로 애니메이션 구동)
    bool isRemoteProxy;
    // 피호스트 로컬 플레이어: 입력은 읽지만 로코모션은 네트워크 기반
    bool isLocalWithoutAuthority;
    bool recoveryQueued;
    bool isAerialKickAnimationActive;
    bool isRecoveryAnimationActive;
    AnimationClip currentAerialKickClip;
    NetworkPlayer.RecoveryAnimationVariant queuedRecoveryVariant;
    AnimationClip currentRecoveryClip;
    Vector3 aerialKickAnimatorLocalPosition;
    Vector3 aerialKickAnimatorLocalScale;
    bool hasAerialKickAnimatorRootPose;
    PlayableGraph aerialKickGraph;
    AnimationClipPlayable aerialKickPlayable;
    AnimationPlayableOutput aerialKickOutput;
    bool isAerialKickEndPoseHeld;
    float aerialKickEndPoseForceReleaseTime = float.NegativeInfinity;
    PlayableGraph recoveryGraph;
    AnimationClipPlayable recoveryPlayable;
    AnimationPlayableOutput recoveryOutput;

    void Reset()
    {
        CacheReferences();
    }

    void OnValidate()
    {
        if (!Application.isPlaying)
            CacheReferences();
    }

    void Awake()
    {
        CacheReferences();
        ConfigureAnimator();
    }

    void OnEnable()
    {
        SetMovementSpeedParameter(0f);
        currentStateName = null;
        currentUpperBodyStateName = null;
        PlayState(IdleState);
        if (animator != null)
            animator.SetLayerWeight(UpperBodyLayer, 0f);
    }

    void OnDisable()
    {
        CancelAerialKickAnimation();
        CancelRecoveryAnimation();
    }

    void OnDestroy()
    {
        CancelAerialKickAnimation();
        CancelRecoveryAnimation();
    }

    void Update()
    {
        if (animator == null)
            return;

        var canDriveAnimation = CanDriveAnimation();
        if (!canDriveAnimation)
        {
            if (isAerialKickAnimationActive)
                CancelAerialKickAnimation();
            if (isRecoveryAnimationActive)
                CancelRecoveryAnimation();

            ResetActionState();
            animator.enabled = false;
            return;
        }

        if (!animator.enabled)
            animator.enabled = true;

        bool isRecoveryPhase = networkPlayer != null &&
                               (networkPlayer.GetPhysicalPhase() == NetworkPlayer.PhysicalPhase.Recovering ||
                                networkPlayer.GetStunPresentationPhase() == NetworkPlayer.StunPresentationPhase.RecoverStabilizing);
        bool preserveQueuedRecoveryDuringResetWindow = networkPlayer != null &&
                                                       networkPlayer.IsRemoteRecoveryPresentationResetWindowActive();

        if (!isRecoveryPhase && recoveryQueued && !preserveQueuedRecoveryDuringResetWindow)
        {
            recoveryQueued = false;
            queuedRecoveryVariant = NetworkPlayer.RecoveryAnimationVariant.None;
        }

        if (!isRecoveryPhase && isRecoveryAnimationActive)
        {
            StopRecoveryAnimation();
        }

        if (isRecoveryPhase && isAerialKickAnimationActive)
        {
            LogAerialKickAnimationEvent("Stop", "recovery-phase-entered");
            StopAerialKickAnimation();
        }

        // Handoff transition can occasionally miss the visual restore latch in player builds.
        // Keep the fallback strictly inside the actual recovery phase so it cannot affect normal combat.
        if (recoveryQueued)
        {
            RestoreAnimatorAfterPhysicsPresentation();
            if (isRecoveryAnimationActive)
                return;
        }

        if (isRecoveryAnimationActive)
        {
            TickRecoveryAnimation();
            return;
        }

        if (isAerialKickAnimationActive)
        {
            TickAerialKickAnimation();
            return;
        }

        if (isRemoteProxy)
        {
            // 그랩 동기화는 로컬/원격 프록시 공통
            SyncGrabAnimation();

            if (isLocalWithoutAuthority)
            {
                // 피호스트 로컬 플레이어: 입력은 읽어서 즉시 예측 연출,
                // 로코모션은 네트워크 기반 (자기 rigidbody는 시뮬 안 하므로)
                HandleInput();
                UpdateLocomotionForOwnerProxy();
            }
            else
            {
                // 순수 원격 프록시: 모든 것이 네트워크 데이터 기반
                UpdateLocomotionFromNetwork();
            }

            TryFlushPunchBuffer();
            UpdateUpperBodyLayerState();
            return;
        }

        HandleInput();
        SyncGrabAnimation();
        TryFlushPunchBuffer();

        // 그랩 중에도 locomotion 애니메이션 유지 (전투는 Upper Body Layer에서 처리)
        UpdateLocomotion();

        UpdateUpperBodyLayerState();
    }

    void LateUpdate()
    {
        if (!isAerialKickAnimationActive)
            return;

        MaintainAerialKickAnimatorRootPose();
    }

    /// <summary>
    /// 원격 프록시 모드 설정. true이면 로컬 입력 대신 네트워크 데이터로 애니메이션을 구동한다.
    /// </summary>
    public void SetRemoteProxy(bool value)
    {
        isRemoteProxy = value;
    }

    /// <summary>
    /// 피호스트 로컬 플레이어 모드 설정.
    /// 로코모션은 네트워크 기반이지만, 입력(펀치/잡기)은 로컬에서 읽어 즉시 예측 연출한다.
    /// </summary>
    public void SetLocalWithoutAuthority(bool value)
    {
        isLocalWithoutAuthority = value;
    }

    /// <summary>
    /// 외부에서 Animator를 명시적으로 지정한다.
    /// NetworkPlayer가 _animatedVisualRoot에서 선택한 Animator와 일치시키기 위해 사용.
    /// </summary>
    public void SetAnimator(Animator newAnimator)
    {
        if (newAnimator == null || newAnimator == animator)
            return;

        animator = newAnimator;
        ConfigureAnimator();
    }

    public void RestoreAnimatorAfterPhysicsPresentation()
    {
        if (animator == null)
            return;

        ResetActionState();
        currentStateName = null;
        currentUpperBodyStateName = null;
        upperBodyStateVisibleUntil = 0f;
        animator.enabled = true;
        animator.Update(0f);
        animator.SetLayerWeight(UpperBodyLayer, 0f);

        if (TryPlayQueuedRecoveryAnimation())
            return;

        if (!animator.isInitialized)
            animator.Rebind();
        animator.Update(0f);
        UpdateCurrentLocomotion();
    }

    internal void QueueRecoveryAnimation(NetworkPlayer.RecoveryAnimationVariant variant)
    {
        queuedRecoveryVariant = variant == NetworkPlayer.RecoveryAnimationVariant.None
            ? NetworkPlayer.RecoveryAnimationVariant.Supine
            : variant;
        recoveryQueued = true;
    }

    internal void CancelRecoveryAnimation()
    {
        recoveryQueued = false;
        queuedRecoveryVariant = NetworkPlayer.RecoveryAnimationVariant.None;
        isRecoveryAnimationActive = false;
        currentRecoveryClip = null;
        DestroyRecoveryGraph();
    }

    internal void CancelAerialKickAnimation()
    {
        isAerialKickAnimationActive = false;
        isAerialKickEndPoseHeld = false;
        aerialKickEndPoseForceReleaseTime = float.NegativeInfinity;
        currentAerialKickClip = null;
        RestoreAerialKickAnimatorRootPose();
        hasAerialKickAnimatorRootPose = false;
        DestroyAerialKickGraph();
    }

    public void PlayAttack()
    {
        if (!CanDriveAnimation())
            return;

        bool isLeft = nextAttackLeft;
        nextAttackLeft = !nextAttackLeft;

        string punchState = isLeft ? PunchLeftState : PunchRightState;
        PlayLockedAction(punchState, attackLockDuration, attackVisualDuration);

        // OwnerProxy: NetworkPlayer에 예측 방향을 알려서 reconcile 시 비교 가능하게
        if (networkPlayer != null)
            networkPlayer.NotifyLocalPunchPrediction(isLeft);
    }

    /// <summary>
    /// 네트워크 이벤트에서 호출. 호스트가 결정한 왼손 펀치를 재생한다.
    /// </summary>
    public void PlayAttackLeft()
    {
        if (!CanDriveAnimation())
            return;

        PlayLockedAction(PunchLeftState, attackLockDuration, attackVisualDuration);
    }

    /// <summary>
    /// 네트워크 이벤트에서 호출. 호스트가 결정한 오른손 펀치를 재생한다.
    /// </summary>
    public void PlayAttackRight()
    {
        if (!CanDriveAnimation())
            return;

        PlayLockedAction(PunchRightState, attackLockDuration, attackVisualDuration);
    }

    /// <summary>
    /// 네트워크 이벤트에서 호출. isGrabPoseActive 체크 없이 Throw 애니메이션을 재생한다.
    /// 원격 클라이언트에서는 grab 상태가 정확히 동기화되지 않을 수 있기 때문.
    /// </summary>
    public void PlayThrowFromNetwork()
    {
        if (!CanDriveAnimation())
            return;

        isGrabPoseActive = false;
        PlayLockedAction(ThrowState, throwLockDuration);
    }

    public void PlayKickLeft()
    {
        if (!CanDriveAnimation())
            return;

        PlayKick(true);
    }

    public void PlayKickRight()
    {
        if (!CanDriveAnimation())
            return;

        PlayKick(false);
    }

    public void PlayAerialKick()
    {
        if (!CanDriveAnimation() || isAerialKickAnimationActive)
            return;

        isGrabPoseActive = false;
        ClearUpperBodyState();
        currentUpperBodyStateName = null;
        upperBodyStateVisibleUntil = 0f;

        var clip = ResolveAerialKickClip();
        if (clip != null)
        {
            StartAerialKickAnimation(clip);
            return;
        }

        actionLockedUntil = Time.time + aerialKickLockDuration;
        if (networkPlayer != null)
        {
            networkPlayer.PlayProceduralKickPresentation(false);
            return;
        }

        var kickLeg = GetComponent<ProceduralKickLeg>();
        if (kickLeg == null)
            kickLeg = gameObject.AddComponent<ProceduralKickLeg>();
        kickLeg.TriggerRightKick(transform.forward);
    }

    public void BeginGrab()
    {
        if (!CanDriveAnimation())
            return;

        if (IsActionLocked())
            return;

        isGrabPoseActive = true;
    }

    public void EndGrab()
    {
        if (IsActionLocked())
            return;

        isGrabPoseActive = false;
        ClearUpperBodyState();
        UpdateCurrentLocomotion();
    }

    public void ThrowHeld()
    {
        if (!CanDriveAnimation())
            return;

        if (!isGrabPoseActive)
            return;

        isGrabPoseActive = false;
        if (networkPlayer != null)
            networkPlayer.NotifyLocalThrowPrediction();
        PlayLockedAction(ThrowState, throwLockDuration);
    }

    void PlayKick(bool isLeft)
    {
        isGrabPoseActive = false;
        ClearUpperBodyState();
        currentUpperBodyStateName = null;
        upperBodyStateVisibleUntil = 0f;
        actionLockedUntil = Time.time + ResolveKickLockDuration();

        if (networkPlayer != null)
        {
            networkPlayer.PlayProceduralKickPresentation(isLeft);
            return;
        }

        var kickLeg = GetComponent<ProceduralKickLeg>();
        if (kickLeg == null)
            kickLeg = gameObject.AddComponent<ProceduralKickLeg>();

        var forward = transform.forward;
        if (isLeft)
            kickLeg.TriggerLeftKick(forward);
        else
            kickLeg.TriggerRightKick(forward);
    }

    void CacheReferences()
    {
        puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        behaviourPuppet = GetComponentInChildren<BehaviourPuppet>(true);
        networkPlayer = GetComponent<NetworkPlayer>();
        characterGrabController = GetComponent<CharacterGrabController>();

        if (rigidbody3D == null)
            rigidbody3D = FindMainRigidbody();

        if (animator == null)
            animator = (puppetMaster != null && puppetMaster.targetRoot != null ? puppetMaster.targetRoot.GetComponentInChildren<Animator>(true) : null) ?? GetComponentInChildren<Animator>(true);
    }

    float ResolveKickLockDuration()
    {
        if (networkPlayer != null)
            return networkPlayer.ResolveKickPresentationLockDuration();

        var kickLeg = GetComponent<ProceduralKickLeg>();
        return kickLeg != null ? kickLeg.TotalKickDuration : 0.45f;
    }

    bool TryPlayQueuedRecoveryAnimation()
    {
        if (!recoveryQueued)
            return false;

        recoveryQueued = false;
        var clip = ResolveRecoveryClip(queuedRecoveryVariant);
        queuedRecoveryVariant = NetworkPlayer.RecoveryAnimationVariant.None;
        if (clip == null)
            return false;

        StartRecoveryAnimation(clip);
        return true;
    }

    AnimationClip ResolveRecoveryClip(NetworkPlayer.RecoveryAnimationVariant variant)
    {
        return variant switch
        {
            NetworkPlayer.RecoveryAnimationVariant.Prone => recoveryProneClip != null ? recoveryProneClip : recoverySupineClip,
            _ => recoverySupineClip != null ? recoverySupineClip : recoveryProneClip
        };
    }

    string ResolveConfiguredAerialKickClipName()
    {
        var stat = CombatSettings.Instance?.GetAttackStat(AerialKickCombatStatId);
        if (stat.HasValue && !string.IsNullOrWhiteSpace(stat.Value.AnimationClip))
            return stat.Value.AnimationClip;

        return DefaultAerialKickClipName;
    }

    AnimationClip ResolveAerialKickClip()
    {
        var configuredClipName = ResolveConfiguredAerialKickClipName();
        if (aerialKickClip != null && aerialKickClip.name == configuredClipName)
            return aerialKickClip;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            var clips = animator.runtimeAnimatorController.animationClips;
            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip != null && clip.name == configuredClipName)
                {
                    aerialKickClip = clip;
                    return aerialKickClip;
                }
            }
        }

#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(configuredClipName))
        {
            var guids = AssetDatabase.FindAssets($"{configuredClipName} t:AnimationClip");
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null || clip.name != configuredClipName)
                    continue;

                aerialKickClip = clip;
                return aerialKickClip;
            }
        }

        if (configuredClipName == DefaultAerialKickClipName)
            aerialKickClip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/PartyMonsterRumblePBR/Animation/Attack02.fbx");
#endif

        return aerialKickClip != null && aerialKickClip.name == configuredClipName
            ? aerialKickClip
            : null;
    }

    void StartAerialKickAnimation(AnimationClip clip)
    {
        DestroyAerialKickGraph();

        currentAerialKickClip = clip;
        isAerialKickAnimationActive = true;
        isAerialKickEndPoseHeld = false;
        aerialKickEndPoseForceReleaseTime = float.NegativeInfinity;
        CacheAerialKickAnimatorRootPose();
        actionLockedUntil = Time.time + Mathf.Max(aerialKickLockDuration, clip.length);
        upperBodyStateVisibleUntil = actionLockedUntil;
        currentStateName = null;
        currentUpperBodyStateName = null;
        animator.SetLayerWeight(UpperBodyLayer, 0f);

        aerialKickGraph = PlayableGraph.Create($"{name}_AerialKick");
        aerialKickGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        aerialKickOutput = AnimationPlayableOutput.Create(aerialKickGraph, "AerialKick", animator);
        aerialKickPlayable = AnimationClipPlayable.Create(aerialKickGraph, clip);
        aerialKickPlayable.SetApplyFootIK(false);
        aerialKickPlayable.SetApplyPlayableIK(false);
        aerialKickPlayable.SetTime(0d);
        aerialKickPlayable.SetDuration(clip.length);
        aerialKickOutput.SetSourcePlayable(aerialKickPlayable);
        aerialKickOutput.SetWeight(1f);
        aerialKickGraph.Play();
        LogAerialKickAnimationEvent("Start", $"clip={clip.name} length={clip.length:F2}");
    }

    void HoldAerialKickEndPose()
    {
        if (!aerialKickGraph.IsValid() || currentAerialKickClip == null)
            return;

        var poseTime = Mathf.Max(0f, currentAerialKickClip.length - (1f / 60f));
        aerialKickPlayable.SetTime(poseTime);
        aerialKickPlayable.SetSpeed(0d);
    }

    bool ShouldKeepHoldingAerialKickPose()
    {
        if (!isAerialKickEndPoseHeld)
            return false;

        if (Time.time >= aerialKickEndPoseForceReleaseTime)
        {
            if (networkPlayer != null && !networkPlayer.IsGroundedForPresentation())
                return true;

            return false;
        }

        return networkPlayer == null || !networkPlayer.ShouldEndAerialKickPresentation();
    }

    void TickAerialKickAnimation()
    {
        if (!isAerialKickAnimationActive)
            return;

        if (!aerialKickGraph.IsValid() || currentAerialKickClip == null)
        {
            StopAerialKickAnimation();
            return;
        }

        if (aerialKickPlayable.IsDone() && !isAerialKickEndPoseHeld)
        {
            isAerialKickEndPoseHeld = true;
            aerialKickEndPoseForceReleaseTime = Time.time + AerialKickEndPoseHoldMaxDuration;
            actionLockedUntil = Mathf.Max(actionLockedUntil, aerialKickEndPoseForceReleaseTime);
            upperBodyStateVisibleUntil = Mathf.Max(upperBodyStateVisibleUntil, actionLockedUntil);
            HoldAerialKickEndPose();
            LogAerialKickAnimationEvent("HoldEndPose", $"forceReleaseAt={aerialKickEndPoseForceReleaseTime:F2}");
        }

        if (!isAerialKickEndPoseHeld)
            return;

        if (Time.time >= aerialKickEndPoseForceReleaseTime &&
            networkPlayer != null &&
            !networkPlayer.IsGroundedForPresentation())
        {
            aerialKickEndPoseForceReleaseTime = Time.time + AerialKickEndPoseHoldExtensionDuration;
            actionLockedUntil = Mathf.Max(actionLockedUntil, aerialKickEndPoseForceReleaseTime);
            upperBodyStateVisibleUntil = Mathf.Max(upperBodyStateVisibleUntil, actionLockedUntil);
            LogAerialKickAnimationEvent("ExtendHold", $"extendedUntil={aerialKickEndPoseForceReleaseTime:F2}");
        }

        if (!ShouldKeepHoldingAerialKickPose())
        {
            var reason = Time.time >= aerialKickEndPoseForceReleaseTime
                ? "timeout"
                : networkPlayer == null
                    ? "network-player-missing"
                    : networkPlayer.IsGroundedForPresentation()
                        ? "presentation-grounded"
                        : $"presentation-ended phase={networkPlayer.GetPhysicalPhase()}";
            LogAerialKickAnimationEvent("Stop", reason);
            StopAerialKickAnimation();
            return;
        }

        HoldAerialKickEndPose();
    }

    void StopAerialKickAnimation()
    {
        isAerialKickAnimationActive = false;
        isAerialKickEndPoseHeld = false;
        aerialKickEndPoseForceReleaseTime = float.NegativeInfinity;
        currentAerialKickClip = null;
        RestoreAerialKickAnimatorRootPose();
        hasAerialKickAnimatorRootPose = false;
        DestroyAerialKickGraph();

        if (animator == null)
            return;

        animator.enabled = true;
        animator.SetLayerWeight(UpperBodyLayer, 0f);
        UpdateCurrentLocomotion();
    }

    void DestroyAerialKickGraph()
    {
        if (aerialKickGraph.IsValid())
            aerialKickGraph.Destroy();
    }

    void CacheAerialKickAnimatorRootPose()
    {
        if (animator == null)
        {
            hasAerialKickAnimatorRootPose = false;
            return;
        }

        aerialKickAnimatorLocalPosition = animator.transform.localPosition;
        aerialKickAnimatorLocalScale = animator.transform.localScale;
        hasAerialKickAnimatorRootPose = true;
    }

    void MaintainAerialKickAnimatorRootPose()
    {
        if (animator == null || !hasAerialKickAnimatorRootPose)
            return;

        // 애니메이션 클립의 루트모션이 localPosition/Scale을 변형하는 것만 방지
        // 위치 이동은 물리 루트(rigidbody3D) → 부모 오브젝트를 통해 자연스럽게 전달됨
        animator.transform.localPosition = aerialKickAnimatorLocalPosition;
        animator.transform.localScale = aerialKickAnimatorLocalScale;
    }

    void RestoreAerialKickAnimatorRootPose()
    {
        if (animator == null || !hasAerialKickAnimatorRootPose)
            return;

        animator.transform.localPosition = aerialKickAnimatorLocalPosition;
        animator.transform.localScale = aerialKickAnimatorLocalScale;
    }

    bool ShouldLogAerialKickDiagnostics()
    {
        return networkPlayer != null
            ? networkPlayer.ShouldLogAerialKickDiagnostics()
            : (Application.isEditor || Debug.isDebugBuild);
    }

    void LogAerialKickAnimationEvent(string source, string note)
    {
        if (!ShouldLogAerialKickDiagnostics())
            return;

        var grounded = networkPlayer != null && networkPlayer.IsGroundedForPresentation();
        var phase = networkPlayer != null ? networkPlayer.GetPhysicalPhase().ToString() : "none";
        Debug.Log(
            $"[AerialKickAnim] {name} {source} t={Time.time:F2} active={isAerialKickAnimationActive} hold={isAerialKickEndPoseHeld} grounded={grounded} phase={phase} note={note}",
            this);
    }

    void StartRecoveryAnimation(AnimationClip clip)
    {
        DestroyRecoveryGraph();

        currentRecoveryClip = clip;
        isRecoveryAnimationActive = true;
        actionLockedUntil = Time.time + clip.length;
        upperBodyStateVisibleUntil = actionLockedUntil;
        currentStateName = null;
        currentUpperBodyStateName = null;

        recoveryGraph = PlayableGraph.Create($"{name}_Recovery");
        recoveryGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        recoveryOutput = AnimationPlayableOutput.Create(recoveryGraph, "Recovery", animator);
        recoveryPlayable = AnimationClipPlayable.Create(recoveryGraph, clip);
        recoveryPlayable.SetApplyFootIK(false);
        recoveryPlayable.SetApplyPlayableIK(false);
        recoveryPlayable.SetTime(0d);
        recoveryPlayable.SetDuration(clip.length);
        recoveryOutput.SetSourcePlayable(recoveryPlayable);
        recoveryOutput.SetWeight(1f);
        recoveryGraph.Play();
    }

    void TickRecoveryAnimation()
    {
        if (!isRecoveryAnimationActive)
            return;

        if (!recoveryGraph.IsValid() || currentRecoveryClip == null)
        {
            StopRecoveryAnimation();
            return;
        }

        if (recoveryPlayable.IsDone())
            StopRecoveryAnimation();
    }

    void StopRecoveryAnimation()
    {
        isRecoveryAnimationActive = false;
        currentRecoveryClip = null;
        DestroyRecoveryGraph();

        if (animator == null)
            return;

        animator.enabled = true;
        animator.SetLayerWeight(UpperBodyLayer, 0f);
        UpdateCurrentLocomotion();
    }

    void DestroyRecoveryGraph()
    {
        if (recoveryGraph.IsValid())
            recoveryGraph.Destroy();
    }

    Rigidbody FindMainRigidbody()
    {
        if (puppetMaster == null)
            return GetComponent<Rigidbody>() ?? GetComponentInChildren<Rigidbody>(true);

        Rigidbody[] rigidbodies = puppetMaster.GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody body in rigidbodies)
        {
            ConfigurableJoint joint = body.GetComponent<ConfigurableJoint>();
            if (joint != null && joint.connectedBody == null)
                return body;
        }

        return rigidbodies.Length > 0 ? rigidbodies[0] : null;
    }

    void ConfigureAnimator()
    {
        if (animator == null)
            return;

        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        movementSpeedParameterHash = Animator.StringToHash(MovementSpeedParameter);
        isSprintingParameterHash = Animator.StringToHash(IsSprintingParameter);
        hasMovementSpeedParameter = false;
        hasIsSprintingParameter = false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Float && parameter.name == MovementSpeedParameter)
                hasMovementSpeedParameter = true;
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == IsSprintingParameter)
                hasIsSprintingParameter = true;
        }

        // Upper Body Layer는 전투 액션 시에만 활성화 (기본 비활성)
        animator.SetLayerWeight(UpperBodyLayer, 0f);
    }

    void HandleInput()
    {
        // 기절 중이거나 회복 안정화 중이면 전투 입력 무시
        if (networkPlayer != null && !networkPlayer.CanPerformCombatActions)
            return;

        if (isLocalWithoutAuthority &&
            networkPlayer != null &&
            Input.GetMouseButtonDown(1) &&
            Time.time >= nextOwnerProxyAerialKickPredictionAt &&
            networkPlayer.ShouldPredictOwnerProxyAerialKickPresentation())
        {
            nextOwnerProxyAerialKickPredictionAt = Time.time + networkPlayer.GetConfiguredAerialKickCooldown();
            PlayAerialKick();
        }

        if (Input.GetMouseButtonDown(0))
            attackButtonPressedAt = Time.time;

        if (Input.GetMouseButtonUp(0))
        {
            bool isQuickClick = !isGrabPoseActive &&
                                attackButtonPressedAt >= 0f &&
                                Time.time - attackButtonPressedAt < grabHoldThreshold;

            if (isQuickClick)
            {
                if (!IsActionLocked())
                    PlayAttack();
                else
                {
                    // 잠금 중 입력 → 버퍼에 저장해서 잠금 해제 직후 실행 (콤보 윈도우)
                    _punchBuffered = true;
                    _punchBufferExpiry = Time.time + PunchBufferWindow;
                }
            }

            attackButtonPressedAt = -1f;
        }

        // HandGrabHandler 물리 그랩 상태와 애니메이션 동기화
    }

    void SyncGrabAnimation()
    {
        if (animator == null)
            return;

        var shouldPreserve = ShouldPreserveGrabPose();
        var holdStateActive = IsHoldUpperBodyState(currentUpperBodyStateName);
        isGrabPoseActive = shouldPreserve;

        if (!shouldPreserve)
        {
            if (holdStateActive && !IsActionLocked())
            {
                ClearUpperBodyState();
                UpdateCurrentLocomotion();
            }

            return;
        }

        if (IsActionLocked() && !holdStateActive)
            return;

        var holdStateName = ResolveHoldUpperBodyStateName();
        if (string.IsNullOrEmpty(holdStateName))
            return;

        PlayUpperBodyState(holdStateName);
    }

    void UpdateLocomotion()
    {
        float speed = 0f;

        if (rigidbody3D != null)
        {
            Vector3 planarVelocity = rigidbody3D.velocity;
            planarVelocity.y = 0f;
            speed = planarVelocity.magnitude;
        }

        isSprinting = Input.GetKey(KeyCode.LeftShift) && speed > locomotionThreshold;
        var locomotionState = speed <= locomotionThreshold
            ? NetworkPlayer.PresentationLocomotionState.Idle
            : (isSprinting ? NetworkPlayer.PresentationLocomotionState.Sprint : NetworkPlayer.PresentationLocomotionState.Walk);

        ApplyLocomotionState(locomotionState, speed);
    }

    /// <summary>
    /// 원격 프록시용 로코모션 업데이트. 네트워크 동기화된 데이터로 애니메이션 구동.
    /// </summary>
    void UpdateLocomotionFromNetwork()
    {
        if (networkPlayer == null)
            return;

        float speed = networkPlayer.GetNetworkedMoveSpeed();
        var locomotionState = networkPlayer.GetNetworkedLocomotionState();
        ApplyLocomotionState(locomotionState, speed);
    }

    void UpdateLocomotionForOwnerProxy()
    {
        if (networkPlayer == null)
            return;

        var localMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        var predictedMagnitude = Mathf.Clamp01(localMove.magnitude);
        var networkSpeed = networkPlayer.GetNetworkedMoveSpeed();
        var isHolding = networkPlayer.GetPhysicalPhase() == NetworkPlayer.PhysicalPhase.Holding;
        var predictedSpeed = predictedMagnitude;
        if (isHolding)
            predictedSpeed = Mathf.Lerp(networkSpeed, predictedMagnitude, 0.35f);

        var speed = Mathf.Max(networkSpeed, predictedSpeed);
        var movementThreshold = isHolding ? locomotionThreshold * 0.85f : locomotionThreshold;
        var wantsLocomotion = isHolding
            ? speed > movementThreshold
            : predictedMagnitude > movementThreshold;
        var allowSprintPresentation = Input.GetKey(KeyCode.LeftShift) &&
                                      (!isHolding || speed > locomotionThreshold * 1.2f);
        var locomotionState = wantsLocomotion
            ? (allowSprintPresentation
                ? NetworkPlayer.PresentationLocomotionState.Sprint
                : NetworkPlayer.PresentationLocomotionState.Walk)
            : networkPlayer.GetNetworkedLocomotionState();

        ApplyLocomotionState(locomotionState, speed);
    }

    void UpdateCurrentLocomotion()
    {
        if (isRemoteProxy)
        {
            if (isLocalWithoutAuthority)
                UpdateLocomotionForOwnerProxy();
            else
                UpdateLocomotionFromNetwork();
            return;
        }

        UpdateLocomotion();
    }

    /// <summary>
    /// 잠금 해제 직후 버퍼된 펀치를 실행한다 (콤보 윈도우).
    /// 로컬/프록시 양쪽 Update 경로에서 공통으로 호출된다.
    /// </summary>
    void TryFlushPunchBuffer()
    {
        bool actionLocked = IsActionLocked();
        if (_wasActionLocked && !actionLocked && _punchBuffered && Time.time <= _punchBufferExpiry)
        {
            _punchBuffered = false;
            PlayAttack();
        }
        _wasActionLocked = actionLocked;
    }

    /// <summary>
    /// 전투 상태가 아닐 때 Upper Body Layer를 비활성화한다.
    /// 로컬/프록시 양쪽 Update 경로에서 공통으로 호출된다.
    /// </summary>
    void UpdateUpperBodyLayerState()
    {
        if (ShouldPreserveGrabPose())
            return;

        if (Time.time < upperBodyStateVisibleUntil)
            return;

        if (!IsActionLocked())
            ClearUpperBodyState();
    }

    bool IsActionLocked()
    {
        return Time.time < actionLockedUntil;
    }

    bool CanDriveAnimation()
    {
        // 기절 중이면 애니메이션 구동 불가 (래그돌이 대신 보임)
        if (networkPlayer != null && networkPlayer.ShouldUseHardPhysicsVisualMode())
            return false;

        // 원격 프록시는 로컬 BehaviourPuppet 상태와 무관하게 항상 애니메이션 구동
        // (래그돌 상태는 네트워크 데이터로 제어)
        if (isRemoteProxy)
            return true;

        return true;
    }

    bool ShouldPreserveGrabPose()
    {
        if (networkPlayer == null)
            return isGrabPoseActive;

        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            if (characterGrabController.ShouldPreserveGrabPose)
                return true;

            // OwnerProxy keeps a brief local grab prediction until host confirmation arrives.
            return isLocalWithoutAuthority && networkPlayer.IsGrabActive;
        }

        var phase = networkPlayer.GetPhysicalPhase();
        if (phase == NetworkPlayer.PhysicalPhase.GrabIntent ||
            phase == NetworkPlayer.PhysicalPhase.Holding ||
            phase == NetworkPlayer.PhysicalPhase.CarryingStunned)
            return true;

        return networkPlayer.IsGrabActive ||
               networkPlayer.IsAnyHandHolding ||
               (!isRemoteProxy && isGrabPoseActive);
    }

    string ResolveHoldUpperBodyStateName()
    {
        if (ShouldUseCarryUpperBodyState())
        {
            if (ShouldUseGrabUpperBodyForCarry() && HasUpperBodyState(GrabState))
                return GrabState;

            if (HasUpperBodyState(CarryState))
                return CarryState;
        }

        return HasUpperBodyState(GrabState) ? GrabState : string.Empty;
    }

    bool ShouldUseCarryUpperBodyState()
    {
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            return characterGrabController.ShouldUseCarryPresentation;
        }

        return networkPlayer != null &&
               networkPlayer.GetPhysicalPhase() == NetworkPlayer.PhysicalPhase.CarryingStunned;
    }

    bool ShouldUseGrabUpperBodyForCarry()
    {
        if (characterGrabController == null)
            return false;

        var holdVariant = characterGrabController.CurrentHoldVariant;
        return holdVariant == CharacterGrabController.HoldVariant.FrontCarry ||
               holdVariant == CharacterGrabController.HoldVariant.OverheadCarry ||
               holdVariant == CharacterGrabController.HoldVariant.DualCarry;
    }

    bool HasUpperBodyState(string stateName)
    {
        return animator != null &&
               !string.IsNullOrEmpty(stateName) &&
               animator.HasState(UpperBodyLayer, Animator.StringToHash(stateName));
    }

    static bool IsHoldUpperBodyState(string stateName)
    {
        return stateName == GrabState || stateName == CarryState;
    }

    void ResetActionState()
    {
        attackButtonPressedAt = -1f;
        actionLockedUntil = 0f;
        upperBodyStateVisibleUntil = 0f;
        isGrabPoseActive = false;
        CancelAerialKickAnimation();
        _punchBuffered = false;
        _wasActionLocked = false;
        SetMovementSpeedParameter(0f);
        ClearUpperBodyState();
    }

    void SetMovementSpeedParameter(float speed)
    {
        if (animator == null || !hasMovementSpeedParameter)
            return;

        animator.SetFloat(movementSpeedParameterHash, speed);
    }

    void ApplyLocomotionState(NetworkPlayer.PresentationLocomotionState locomotionState, float speed)
    {
        isSprinting = locomotionState == NetworkPlayer.PresentationLocomotionState.Sprint;

        if (hasIsSprintingParameter)
            animator.SetBool(isSprintingParameterHash, isSprinting);

        SetMovementSpeedParameter(speed);

        // PlayState()가 CrossFadeInFixedTime을 사용하므로 모든 전환이 부드럽게 처리됨
        PlayState(ResolveLocomotionStateName(locomotionState));
    }

    string ResolveLocomotionStateName(NetworkPlayer.PresentationLocomotionState locomotionState)
    {
        return locomotionState switch
        {
            NetworkPlayer.PresentationLocomotionState.Sprint => SprintState,
            NetworkPlayer.PresentationLocomotionState.Walk => WalkState,
            _ => IdleState
        };
    }

    /// <summary>
    /// 전투 애니메이션(펀치/그랩/던지기)을 Upper Body Layer(Layer 1)에서 재생하고 잠근다.
    /// Base Layer의 로코모션은 계속 실행된다.
    /// </summary>
    void PlayLockedAction(string stateName, float duration)
    {
        PlayLockedAction(stateName, duration, duration);
    }

    void PlayLockedAction(string stateName, float lockDuration, float visibleDuration)
    {
        actionLockedUntil = Time.time + lockDuration;
        upperBodyStateVisibleUntil = Time.time + Mathf.Max(lockDuration, visibleDuration);
        PlayUpperBodyState(stateName);
    }

    /// <summary>
    /// Base Layer(Layer 0)에서 로코모션 상태를 CrossFade로 재생한다.
    /// </summary>
    void PlayState(string stateName)
    {
        if (animator == null)
            return;

        if (currentStateName == stateName)
            return;

        animator.CrossFadeInFixedTime(stateName, 0.15f, LocoLayer, 0f);
        currentStateName = stateName;
    }

    /// <summary>
    /// Upper Body Layer(Layer 1)에서 전투 애니메이션을 CrossFade로 재생한다.
    /// 레이어 웨이트를 1로 설정해 상체 마스크가 활성화된다.
    /// 콤보(PunchLeft→PunchRight)도 0.08s 블렌드로 자연스럽게 전환된다.
    /// </summary>
    void PlayUpperBodyState(string stateName)
    {
        if (animator == null)
            return;

        animator.SetLayerWeight(UpperBodyLayer, 1f);

        if (currentUpperBodyStateName == stateName)
            return;

        // 펀치 콤보는 짧은 블렌드, 그랩·던지기는 약간 긴 블렌드
        float blendTime = (stateName == PunchLeftState || stateName == PunchRightState) ? 0.08f : 0.12f;
        animator.CrossFadeInFixedTime(stateName, blendTime, UpperBodyLayer, 0f);
        currentUpperBodyStateName = stateName;
    }

    /// <summary>
    /// Upper Body Layer(Layer 1) 웨이트를 0으로 설정해 비활성화한다.
    /// Base Layer의 로코모션이 전신을 다시 제어한다.
    /// </summary>
    void ClearUpperBodyState()
    {
        if (animator == null)
            return;

        if (animator.GetLayerWeight(UpperBodyLayer) == 0f)
            return;

        animator.SetLayerWeight(UpperBodyLayer, 0f);
        upperBodyStateVisibleUntil = 0f;
        currentUpperBodyStateName = null;
    }
}
