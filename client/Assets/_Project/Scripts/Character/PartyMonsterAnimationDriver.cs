using UnityEngine;
using RootMotion.Dynamics;

[DisallowMultipleComponent]
public class PartyMonsterAnimationDriver : MonoBehaviour
{
    const string IdleState = "Idle01";
    const string WalkState = "WalkFWD";
    const string SprintState = "SprintFWD";
    const string PunchLeftState = "PunchLeft";
    const string PunchRightState = "PunchRight";
    const string GrabState = "GrabIdle";
    const string ThrowState = "Throw";
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
    float attackLockDuration = 0.7f;

    [SerializeField]
    float throwLockDuration = 0.85f;

    PuppetMaster puppetMaster;
    BehaviourPuppet behaviourPuppet;
    NetworkPlayer networkPlayer;
    float attackButtonPressedAt = -1f;
    float actionLockedUntil;
    bool isGrabPoseActive;
    bool hasMovementSpeedParameter;
    bool hasIsSprintingParameter;
    int movementSpeedParameterHash;
    int isSprintingParameterHash;
    bool nextAttackLeft;
    bool isSprinting;
    string currentStateName;

    // 네트워크: 원격 프록시 모드 (로컬 입력 대신 네트워크 데이터로 애니메이션 구동)
    bool isRemoteProxy;
    // 피호스트 로컬 플레이어: 입력은 읽지만 로코모션은 네트워크 기반
    bool isLocalWithoutAuthority;

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
        PlayState(IdleState);
    }

    void Update()
    {
        if (animator == null)
            return;

        if (!CanDriveAnimation())
        {
            ResetActionState();
            animator.enabled = false;
            return;
        }

        if (!animator.enabled)
            animator.enabled = true;

        if (isRemoteProxy)
        {
            if (isLocalWithoutAuthority)
            {
                // 피호스트 로컬 플레이어: 입력은 읽어서 즉시 예측 연출,
                // 로코모션은 네트워크 기반 (자기 rigidbody는 시뮬 안 하므로)
                SyncGrabAnimation();
                HandleInput();
                if (!IsActionLocked())
                    UpdateLocomotionForOwnerProxy();
            }
            else
            {
                // 순수 원격 프록시: 모든 것이 네트워크 데이터 기반
                SyncGrabAnimation();
                if (!IsActionLocked())
                    UpdateLocomotionFromNetwork();
            }
            return;
        }

        HandleInput();

        if (IsActionLocked())
            return;

        // 그랩 중에도 locomotion 애니메이션 유지 (팔은 ProceduralGrabArm이 절차적으로 제어)
        UpdateLocomotion();
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

    public void PlayAttack()
    {
        if (!CanDriveAnimation())
            return;

        bool isLeft = nextAttackLeft;
        nextAttackLeft = !nextAttackLeft;

        string punchState = isLeft ? PunchLeftState : PunchRightState;
        PlayLockedAction(punchState, attackLockDuration);

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

        PlayLockedAction(PunchLeftState, attackLockDuration);
    }

    /// <summary>
    /// 네트워크 이벤트에서 호출. 호스트가 결정한 오른손 펀치를 재생한다.
    /// </summary>
    public void PlayAttackRight()
    {
        if (!CanDriveAnimation())
            return;

        PlayLockedAction(PunchRightState, attackLockDuration);
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
    }

    void HandleInput()
    {
        // 기절 중이거나 회복 안정화 중이면 전투 입력 무시
        if (networkPlayer != null && !networkPlayer.CanPerformCombatActions)
            return;

        if (Input.GetMouseButtonDown(0))
            attackButtonPressedAt = Time.time;

        if (Input.GetMouseButton(0) && !isGrabPoseActive && attackButtonPressedAt >= 0f)
        {
            if (Time.time - attackButtonPressedAt >= grabHoldThreshold)
                BeginGrab();
        }

        if (Input.GetMouseButtonUp(0))
        {
            bool isQuickClick = !isGrabPoseActive &&
                                attackButtonPressedAt >= 0f &&
                                Time.time - attackButtonPressedAt < grabHoldThreshold;

            if (isQuickClick)
                PlayAttack();
            else if (isGrabPoseActive)
                EndGrab();

            attackButtonPressedAt = -1f;
        }

        if (Input.GetMouseButtonDown(1) && isGrabPoseActive)
            ThrowHeld();

        // HandGrabHandler 물리 그랩 상태와 애니메이션 동기화
        SyncGrabAnimation();
    }

    void SyncGrabAnimation()
    {
        if (networkPlayer == null) return;

        bool anyHandHolding = networkPlayer.IsAnyHandHolding;

        // 물리적으로 잡고 있는데 그랩 포즈가 아니면 → 그랩 애니메이션 시작
        if (anyHandHolding && !isGrabPoseActive && !IsActionLocked())
        {
            isGrabPoseActive = true;
            PlayState(GrabState);
        }
        // 물리적으로 아무것도 안 잡고 있는데 그랩 포즈가 활성이고 그랩 입력도 없으면 → 해제
        else if (!anyHandHolding && isGrabPoseActive && !networkPlayer.IsGrabActive)
        {
            isGrabPoseActive = false;
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

    bool IsActionLocked()
    {
        return Time.time < actionLockedUntil;
    }

    bool CanDriveAnimation()
    {
        // 기절 중이면 애니메이션 구동 불가 (래그돌이 대신 보임)
        if (networkPlayer != null && networkPlayer.ShouldUsePhysicsPosePresentation())
            return false;

        // 원격 프록시는 로컬 BehaviourPuppet 상태와 무관하게 항상 애니메이션 구동
        // (래그돌 상태는 네트워크 데이터로 제어)
        if (isRemoteProxy)
            return true;

        return true;
    }

    void ResetActionState()
    {
        attackButtonPressedAt = -1f;
        actionLockedUntil = 0f;
        isGrabPoseActive = false;
        SetMovementSpeedParameter(0f);
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

        var locomotionStateName = ResolveLocomotionStateName(locomotionState);
        if (!hasMovementSpeedParameter)
        {
            PlayState(locomotionStateName);
            return;
        }

        if (currentStateName != IdleState && currentStateName != WalkState && currentStateName != SprintState)
        {
            animator.CrossFadeInFixedTime(locomotionStateName, 0.1f, 0, 0f);
            currentStateName = locomotionStateName;
            return;
        }

        PlayState(locomotionStateName);
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

    void PlayLockedAction(string stateName, float duration)
    {
        actionLockedUntil = Time.time + duration;
        PlayState(stateName);
    }

    void PlayState(string stateName)
    {
        if (animator == null)
            return;

        if (currentStateName == stateName)
            return;

        animator.Play(stateName, 0, 0f);
        currentStateName = stateName;
    }
}
