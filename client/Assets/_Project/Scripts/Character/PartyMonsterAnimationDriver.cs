using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using RootMotion.Dynamics;

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
    const string ThrowState = "Throw";
    const string UpperBodyIdleState = "UpperBodyIdle";
    const string MovementSpeedParameter = "movementSpeed";
    const string IsSprintingParameter = "isSprinting";

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
    AnimationClip recoverySupineClip;

    [SerializeField]
    AnimationClip recoveryProneClip;

    PuppetMaster puppetMaster;
    BehaviourPuppet behaviourPuppet;
    NetworkPlayer networkPlayer;
    float attackButtonPressedAt = -1f;
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
    bool isRecoveryAnimationActive;
    NetworkPlayer.RecoveryAnimationVariant queuedRecoveryVariant;
    AnimationClip currentRecoveryClip;
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
        CancelRecoveryAnimation();
    }

    void OnDestroy()
    {
        CancelRecoveryAnimation();
    }

    void Update()
    {
        if (animator == null)
            return;

        var canDriveAnimation = CanDriveAnimation();
        if (!canDriveAnimation)
        {
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

        if (!isRecoveryPhase && recoveryQueued)
        {
            recoveryQueued = false;
            queuedRecoveryVariant = NetworkPlayer.RecoveryAnimationVariant.None;
        }

        if (!isRecoveryPhase && isRecoveryAnimationActive)
        {
            StopRecoveryAnimation();
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
        TryFlushPunchBuffer();

        // 그랩 중에도 locomotion 애니메이션 유지 (전투는 Upper Body Layer에서 처리)
        UpdateLocomotion();

        UpdateUpperBodyLayerState();
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

    void CacheReferences()
    {
        puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        behaviourPuppet = GetComponentInChildren<BehaviourPuppet>(true);
        networkPlayer = GetComponent<NetworkPlayer>();

        if (rigidbody3D == null)
            rigidbody3D = FindMainRigidbody();

        if (animator == null)
            animator = (puppetMaster != null && puppetMaster.targetRoot != null ? puppetMaster.targetRoot.GetComponentInChildren<Animator>(true) : null) ?? GetComponentInChildren<Animator>(true);
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
        SyncGrabAnimation();
    }

    void SyncGrabAnimation()
    {
        if (networkPlayer == null) return;

        // Grab presentation stays procedural through ProceduralGrabArm.
        // The legacy GrabIdle layer forces both arms into the same forward pose, so keep it disabled.
        if (isGrabPoseActive)
        {
            isGrabPoseActive = false;
            ClearUpperBodyState();
            UpdateCurrentLocomotion();
        }
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
        var speed = Mathf.Max(networkSpeed, predictedMagnitude);
        var locomotionState = predictedMagnitude > locomotionThreshold
            ? (Input.GetKey(KeyCode.LeftShift)
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
        if (isGrabPoseActive)
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

        var phase = networkPlayer.GetPhysicalPhase();
        if (phase == NetworkPlayer.PhysicalPhase.GrabIntent || phase == NetworkPlayer.PhysicalPhase.Holding)
            return true;

        return networkPlayer.IsGrabActive || (!isRemoteProxy && isGrabPoseActive);
    }

    void ResetActionState()
    {
        attackButtonPressedAt = -1f;
        actionLockedUntil = 0f;
        upperBodyStateVisibleUntil = 0f;
        isGrabPoseActive = false;
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
