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
    float grabHoldThreshold = 0.2f;

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
            SyncGrabAnimation();
            if (!IsActionLocked())
                UpdateLocomotionFromNetwork();
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

    public void PlayAttack()
    {
        if (!CanDriveAnimation())
            return;

        // 좌우 펀치 번갈아 재생
        string punchState = nextAttackLeft ? PunchLeftState : PunchRightState;
        nextAttackLeft = !nextAttackLeft;

        PlayLockedAction(punchState, attackLockDuration);
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
        UpdateLocomotion();
    }

    public void ThrowHeld()
    {
        if (!CanDriveAnimation())
            return;

        if (!isGrabPoseActive)
            return;

        isGrabPoseActive = false;
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
            if (isRemoteProxy)
                UpdateLocomotionFromNetwork();
            else
                UpdateLocomotion();
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

        string locomotionState;
        if (speed <= locomotionThreshold)
            locomotionState = IdleState;
        else
            locomotionState = isSprinting ? SprintState : WalkState;

        if (hasIsSprintingParameter)
            animator.SetBool(isSprintingParameterHash, isSprinting);

        if (!hasMovementSpeedParameter)
        {
            PlayState(locomotionState);
            return;
        }

        SetMovementSpeedParameter(speed);

        if (currentStateName != IdleState && currentStateName != WalkState && currentStateName != SprintState)
        {
            animator.CrossFadeInFixedTime(locomotionState, 0.1f, 0, 0f);
            currentStateName = locomotionState;
        }
        else
            PlayState(locomotionState);
    }

    /// <summary>
    /// 원격 프록시용 로코모션 업데이트. 네트워크 동기화된 데이터로 애니메이션 구동.
    /// </summary>
    void UpdateLocomotionFromNetwork()
    {
        if (networkPlayer == null)
            return;

        float speed = networkPlayer.GetNetworkedMoveSpeed();
        bool sprinting = networkPlayer.GetNetworkedIsSprinting();

        string locomotionState;
        if (speed <= locomotionThreshold)
            locomotionState = IdleState;
        else
            locomotionState = sprinting ? SprintState : WalkState;

        if (hasIsSprintingParameter)
            animator.SetBool(isSprintingParameterHash, sprinting);

        SetMovementSpeedParameter(speed);
        PlayState(locomotionState);
    }

    bool IsActionLocked()
    {
        return Time.time < actionLockedUntil;
    }

    bool CanDriveAnimation()
    {
        return behaviourPuppet == null || behaviourPuppet.state == BehaviourPuppet.State.Puppet;
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
