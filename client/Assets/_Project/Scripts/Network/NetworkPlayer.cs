using Fusion;
using SSAFYPlayTime.Character;
using UnityEngine;

// Fusion NetworkBehaviour 기반의 캐릭터 컨트롤러.
// StateAuthority(서버/호스트)에서만 물리 시뮬레이션을 실행한다.
//
// [전투 시스템]
// - stunDamage 누적 → 임계값(30) 초과 → 기절 → 가중치 기반 자동 회복
// - 좌클릭 짧게=펀치, 좌클릭 꾹=그랩, 우클릭=던지기/어퍼컷
// - 부위별/상태별 배율, 그로기 시스템
public sealed class NetworkPlayer : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rigidbody3D;
    [SerializeField] private ConfigurableJoint mainJoint;
    [SerializeField] private Animator animator;
    [SerializeField] private SyncPhysicsObject[] syncPhysicsObjects;

    [Header("Config")]
    [SerializeField] private PlayerMotorConfig config;
    [SerializeField] private RuntimeAnimatorController fallbackAnimatorController;

    [Header("Grab")]
    [SerializeField] private Transform holdPoint;

    // ─── 네트워크 동기화 변수 ───
    [Networked] private float NetworkedMoveSpeed { get; set; }
    [Networked] private int NetworkedMotorState { get; set; }

    // 관절 회전값 네트워크 배열
    [Networked, Capacity(15)]
    public NetworkArray<Quaternion> BoneRotations { get; }

    // 액티브 래그돌 상태
    [Networked] public NetworkBool NetworkedIsActiveRagdoll { get; set; }

    // ─── 기절 시스템 ───
    // stunDamage 누적치 (임계값 초과 시 기절)
    [Networked] private float AccumulatedStunDamage { get; set; }
    // 현재 기절 남은 시간
    [Networked] private float StunTimeRemaining { get; set; }

    // ─── 로컬 변수 ───
    private float _localMoveSpeed;
    private int _localMotorState;
    private Vector2 _sandboxInput;
    private bool _sandboxJump;

    private readonly GroundProbe _groundProbe = new();
    private readonly PlayerMotorStateMachine _stateMachine = new();

    private bool _isGrounded;
    private HandGrabHandler[] _handGrabHandlers;

    private bool _isActiveRagdoll = true;
    public bool IsActiveRagdoll => _isActiveRagdoll;

    private bool _isGrabActive;
    public bool IsGrabActive => _isGrabActive;

    // 기절 관련
    private float _startSlerpPositionSpring;
    private bool _isRecovering; // 회복 직전 취약 상태
    private float _recoveringTimer;
    private const float RECOVERING_DURATION = 2.0f; // 일어난 후 2초간 취약

    // 좌클릭 꾹 vs 연타 판별
    private bool _leftMouseDown;
    private float _leftMouseDownTime;
    private bool _leftMouseConsumedAsGrab;
    private const float GRAB_HOLD_THRESHOLD = 0.15f;

    // 로컬 트리거
    private bool _dropTriggered;
    private bool _throwTriggered;

    // 기절 애니메이션 해시
    private static readonly int H_MovementSpeed = Animator.StringToHash("movementSpeed");
    private static readonly int H_MotorState = Animator.StringToHash("MotorState");
    private static readonly int H_IsGrabbing = Animator.StringToHash("isGrabbing");
    private static readonly int H_Punch = Animator.StringToHash("Punch");
    private static readonly int H_Throw = Animator.StringToHash("Throw");
    private static readonly int H_GetHit = Animator.StringToHash("GetHit");
    private static readonly int H_StunFall = Animator.StringToHash("StunFall");
    private static readonly int H_StunRecover = Animator.StringToHash("StunRecover");

    private void Awake()
    {
        InitializeInternal();
    }

    public override void Spawned()
    {
        InitializeInternal();

        if (HasStateAuthority)
        {
            NetworkedIsActiveRagdoll = true;
            AccumulatedStunDamage = 0f;
            StunTimeRemaining = 0f;
        }

        if (!HasStateAuthority)
        {
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
                rb.isKinematic = true;
        }
    }

    private void InitializeInternal()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>(true);

        _handGrabHandlers = GetComponentsInChildren<HandGrabHandler>(true);

        if (holdPoint == null)
        {
            var go = new GameObject("HoldPoint");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0f, 0.4f, 0.35f);
            holdPoint = go.transform;
        }

        foreach (var handler in _handGrabHandlers)
            handler.SetHoldPoint(holdPoint);

        if (mainJoint != null)
            _startSlerpPositionSpring = mainJoint.slerpDrive.positionSpring;

        EnsureAnimatorBinding();
    }

    private void Update()
    {
        if (Object != null && Object.IsValid && !HasInputAuthority) return;

        if (Runner == null)
        {
            _sandboxInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            if (Input.GetKeyDown(KeyCode.Space)) _sandboxJump = true;
        }

        // 좌클릭 꾹 vs 연타 판별
        if (Input.GetMouseButtonDown(0))
        {
            _leftMouseDown = true;
            _leftMouseDownTime = Time.time;
            _leftMouseConsumedAsGrab = false;
        }

        if (Input.GetMouseButton(0) && _leftMouseDown)
        {
            if (Time.time - _leftMouseDownTime >= GRAB_HOLD_THRESHOLD && !_leftMouseConsumedAsGrab)
                _leftMouseConsumedAsGrab = true;
        }

        if (Input.GetMouseButtonUp(0))
            _leftMouseDown = false;

        if (Input.GetKeyDown(KeyCode.F)) _dropTriggered = true;
        if (Input.GetMouseButtonDown(1)) _throwTriggered = true;
    }

    private void FixedUpdate()
    {
        if (Runner != null) return;

        bool punchTrigger = false;
        if (Input.GetMouseButtonUp(0) && !_leftMouseConsumedAsGrab
            && Time.time - _leftMouseDownTime < GRAB_HOLD_THRESHOLD)
        {
            punchTrigger = true;
        }

        PlayerNetworkInput input = new PlayerNetworkInput
        {
            Move = _sandboxInput,
            Jump = _sandboxJump,
            Punch = punchTrigger,
            Drop = _dropTriggered,
            Throw = _throwTriggered,
            GrabHold = _leftMouseDown && _leftMouseConsumedAsGrab,
            Headbutt = Input.GetMouseButtonDown(2),
            Sprint = Input.GetKey(KeyCode.LeftShift)
        };

        DoPhysicsStep(input, Time.fixedDeltaTime);
        _sandboxJump = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        if (GetInput(out PlayerNetworkInput input))
        {
            DoPhysicsStep(input, Runner.DeltaTime);
        }

        // 관절 회전값 동기화
        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            BoneRotations.Set(i, syncPhysicsObjects[i].transform.localRotation);

        NetworkedIsActiveRagdoll = _isActiveRagdoll;

        // 그랩 핸들러 업데이트
        foreach (HandGrabHandler handler in _handGrabHandlers)
            handler.UpdateState();

        // 낙사 방지
        if (transform.position.y < -10)
        {
            rigidbody3D.position = Vector3.zero;
            rigidbody3D.velocity = Vector3.zero;
            rigidbody3D.angularVelocity = Vector3.zero;
            ForceRecover();
        }
    }

    private void DoPhysicsStep(PlayerNetworkInput input, float dt)
    {
        if (config == null || rigidbody3D == null || mainJoint == null) return;

        _isGrabActive = input.GrabHold;

        // ─── stunDamage 감쇠 (초당 5씩) ───
        float decayRate = CombatSettings.Instance != null ? 5f : 5f; // CSV에서 읽기 가능
        float accumulated = Runner != null ? AccumulatedStunDamage : _localAccumulatedStun;
        accumulated = Mathf.Max(0f, accumulated - decayRate * dt);
        if (Runner != null && Object != null && Object.IsValid)
            AccumulatedStunDamage = accumulated;
        else
            _localAccumulatedStun = accumulated;

        // ─── 회복 취약 타이머 ───
        if (_isRecovering)
        {
            _recoveringTimer -= dt;
            if (_recoveringTimer <= 0f)
                _isRecovering = false;
        }

        // ─── 기절 상태: 자동 회복 카운트다운 ───
        if (!_isActiveRagdoll)
        {
            float remaining = Runner != null ? StunTimeRemaining : _localStunTimeRemaining;
            remaining -= dt;

            if (Runner != null && Object != null && Object.IsValid)
                StunTimeRemaining = remaining;
            else
                _localStunTimeRemaining = remaining;

            if (remaining <= 0f)
            {
                // 자동 회복!
                ForceRecover();
            }
            return; // 기절 중에는 이동/공격 불가
        }

        // ─── 정상 상태 로직 ───
        _isGrounded = _groundProbe.IsGrounded(
            rigidbody3D.position, transform,
            config.groundProbeRadius, config.groundProbeDistance);

        if (!_isGrounded)
            rigidbody3D.AddForce(Vector3.down * config.extraGravity, ForceMode.Acceleration);

        float inputMagnitude = input.Move.magnitude;
        float forwardVelocity = Vector3.Dot(transform.forward, rigidbody3D.velocity);

        if (inputMagnitude > 0.001f)
        {
            var desired = Quaternion.LookRotation(new Vector3(input.Move.x, 0f, -input.Move.y), transform.up);
            mainJoint.targetRotation = Quaternion.RotateTowards(
                mainJoint.targetRotation, desired, dt * config.rotateSpeedDeg);

            if (Mathf.Abs(forwardVelocity) < config.maxSpeed)
                rigidbody3D.AddForce(transform.forward * inputMagnitude * config.acceleration, ForceMode.Acceleration);
        }

        if (_isGrounded && input.Jump)
        {
            rigidbody3D.AddForce(Vector3.up * config.jumpImpulse, ForceMode.Impulse);
            _stateMachine.SetJump();
        }

        _stateMachine.Tick(_isGrounded, inputMagnitude, dt, config);

        if (Runner != null && Object != null && Object.IsValid)
        {
            NetworkedMoveSpeed = Mathf.Abs(forwardVelocity) * 0.4f;
            NetworkedMotorState = (int)_stateMachine.CurrentState;
        }
        else
        {
            _localMoveSpeed = Mathf.Abs(forwardVelocity) * 0.4f;
            _localMotorState = (int)_stateMachine.CurrentState;
        }

        if (_isActiveRagdoll)
        {
            for (var i = 0; i < syncPhysicsObjects.Length; i++)
                syncPhysicsObjects[i].UpdateJointFromAnimation();
        }

        ProcessInteractions(input);
    }

    // ─── 로컬 전용 기절 변수 (샌드박스) ───
    private float _localAccumulatedStun;
    private float _localStunTimeRemaining;

    // ─── 외부에서 호출: stunDamage 적용 ───
    /// <summary>
    /// 피격 시 호출. stunDamage를 누적하고, 임계값 초과 시 기절.
    /// bodyPartMultiplier: 부위 배율 (Head=1.5, Body=1.0, Limb=0.7)
    /// attackerVelocity: 공격자 속도 (속도 보너스 계산용)
    /// impulseMagnitude: 충격량 크기 (무게 보너스 계산용)
    /// </summary>
    public void ApplyStunDamage(float stunDamage, float bodyPartMultiplier, float attackerVelocity, float impulseMagnitude)
    {
        if (!_isActiveRagdoll) return; // 이미 기절 중
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority) return;

        // 상태 배율 적용
        float stateMultiplier = 1.0f;
        if (_isRecovering)
            stateMultiplier = CombatSettings.Instance != null ? 2.0f : 2.0f;
        else if (!_isGrounded) // 공중
            stateMultiplier = CombatSettings.Instance != null ? 1.5f : 1.5f;

        float finalStunDamage = stunDamage * bodyPartMultiplier * stateMultiplier;

        // 누적
        float accumulated;
        if (Runner != null && Object != null && Object.IsValid)
        {
            AccumulatedStunDamage += finalStunDamage;
            accumulated = AccumulatedStunDamage;
        }
        else
        {
            _localAccumulatedStun += finalStunDamage;
            accumulated = _localAccumulatedStun;
        }

        // 피격 애니메이션
        if (animator != null)
            animator.SetTrigger(H_GetHit);

        // 기절 판정
        float threshold = CombatSettings.Instance != null
            ? CombatSettings.Instance.knockoutThreshold : 30f;

        if (accumulated >= threshold)
        {
            // 기절 시간 계산
            float stunDuration = CalculateStunDuration(attackerVelocity, impulseMagnitude);
            TriggerStun(stunDuration);
        }
    }

    /// <summary>
    /// 기존 충돌 기반 기절 (DetectCollision에서 호출).
    /// impulse 기반으로 stunDamage를 자동 계산.
    /// </summary>
    public void OnPlayerBodyPartHit()
    {
        // 기존 호환 - 고정 stunDamage 사용
        ApplyStunDamage(15f, 1.0f, 0f, 0f);
    }

    private float CalculateStunDuration(float attackerVelocity, float impulseMagnitude)
    {
        float baseMin = 1.5f;
        float baseMax = 4.0f;
        float velocityBonus = 0.15f;
        float weightBonus = 0.02f;
        float stunMin = 1.5f;
        float stunMax = 8.0f;

        if (CombatSettings.Instance != null)
        {
            // CombatSettings에서 파라미터 읽기 (추후 CSV 확장)
            stunMin = 1.5f;
            stunMax = 8.0f;
        }

        float duration = Random.Range(baseMin, baseMax)
                       + attackerVelocity * velocityBonus
                       + impulseMagnitude * weightBonus;

        return Mathf.Clamp(duration, stunMin, stunMax);
    }

    private void TriggerStun(float duration)
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority) return;

        // mainJoint 스프링 제거
        if (mainJoint != null)
        {
            JointDrive jd = mainJoint.slerpDrive;
            jd.positionSpring = 0;
            mainJoint.slerpDrive = jd;
        }

        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].MakeRagdoll();

        _isActiveRagdoll = false;
        _isGrabActive = false;

        if (Runner != null && Object != null && Object.IsValid)
        {
            StunTimeRemaining = duration;
            AccumulatedStunDamage = 0f;
        }
        else
        {
            _localStunTimeRemaining = duration;
            _localAccumulatedStun = 0f;
        }

        // 기절 애니메이션
        if (animator != null)
            animator.SetTrigger(H_StunFall);

        Debug.Log($"[Combat] 기절! 시간: {duration:F1}초");
    }

    private void ForceRecover()
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority) return;

        // mainJoint 스프링 복원
        if (mainJoint != null)
        {
            JointDrive jd = mainJoint.slerpDrive;
            jd.positionSpring = _startSlerpPositionSpring;
            mainJoint.slerpDrive = jd;
        }

        for (int i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].MakeActiveRagdoll();

        _isActiveRagdoll = true;
        _isGrabActive = false;
        _isRecovering = true;
        _recoveringTimer = RECOVERING_DURATION;

        if (Runner != null && Object != null && Object.IsValid)
        {
            StunTimeRemaining = 0f;
            AccumulatedStunDamage = 0f;
        }
        else
        {
            _localStunTimeRemaining = 0f;
            _localAccumulatedStun = 0f;
        }

        // 회복 애니메이션
        if (animator != null)
            animator.SetTrigger(H_StunRecover);

        Debug.Log("[Combat] 회복! (2초간 취약 상태)");
    }

    private void ProcessInteractions(PlayerNetworkInput input)
    {
        if (_handGrabHandlers == null) return;
        if (!_isActiveRagdoll) return;

        bool punch = input.Punch;
        bool drop = input.Drop || _dropTriggered;
        bool @throw = input.Throw || _throwTriggered;

        bool anyHolding = false;
        foreach (var handler in _handGrabHandlers)
            if (handler.IsHolding) anyHolding = true;

        // 펀치: 좌클릭 짧게 + 잡고 있지 않음 + 그랩 모드 아님
        if (punch && !anyHolding && !_isGrabActive)
        {
            if (animator != null)
                animator.SetTrigger(H_Punch);
        }

        // 좌클릭 꾹 = 그랩 시도
        if (_isGrabActive && !anyHolding)
        {
            foreach (var handler in _handGrabHandlers)
            {
                if (!handler.IsHolding)
                {
                    handler.TryGrab();
                    if (handler.IsHolding) break;
                }
            }
        }

        if (drop)
        {
            foreach (var handler in _handGrabHandlers) handler.Drop();
        }

        if (@throw)
        {
            bool didThrow = false;
            foreach (var handler in _handGrabHandlers)
            {
                if (handler.IsHolding) { handler.Throw(); didThrow = true; }
            }
            if (didThrow && animator != null) animator.SetTrigger(H_Throw);
        }

        _dropTriggered = false;
        _throwTriggered = false;

        anyHolding = false;
        foreach (var handler in _handGrabHandlers)
            if (handler.IsHolding) anyHolding = true;

        if (animator != null)
            animator.SetBool(H_IsGrabbing, anyHolding);
    }

    public override void Render()
    {
        UpdateAnimationParameters();

        if (!HasStateAuthority && Object != null && Object.IsValid)
        {
            var interpolator = new NetworkBehaviourBufferInterpolator(this);
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
            {
                syncPhysicsObjects[i].transform.localRotation = Quaternion.Slerp(
                    syncPhysicsObjects[i].transform.localRotation,
                    BoneRotations.Get(i),
                    interpolator.Alpha);
            }
        }
    }

    private void LateUpdate()
    {
        if (Runner == null)
            UpdateAnimationParameters();
    }

    private void UpdateAnimationParameters()
    {
        if (animator == null) return;
        float speed;
        int state;

        if (Runner != null && Object != null && Object.IsValid)
        {
            speed = NetworkedMoveSpeed;
            state = NetworkedMotorState;
        }
        else
        {
            speed = _localMoveSpeed;
            state = _localMotorState;
        }

        animator.SetFloat(H_MovementSpeed, speed);
        animator.SetInteger(H_MotorState, state);
    }

    private void EnsureAnimatorBinding()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
        if (animator == null)
            animator = gameObject.AddComponent<Animator>();
        if (animator.runtimeAnimatorController == null && fallbackAnimatorController != null)
            animator.runtimeAnimatorController = fallbackAnimatorController;

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.enabled = true;
        animator.Rebind();
        animator.Update(0f);
    }
}
