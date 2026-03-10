using Fusion;
using SSAFYPlayTime.Character;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

// Fusion NetworkBehaviour 기반의 캐릭터 컨트롤러.
// StateAuthority(서버/호스트)에서만 물리 시뮬레이션을 실행한다.
//
// [전투 시스템]
// - stunDamage 누적 → 임계값(30) 초과 → 기절 → 가중치 기반 자동 회복
// - 좌클릭 짧게=아이템 사용(보유 시)/펀치, 좌클릭 꾹=그랩(아이템이면 즉시 습득), 우클릭=던지기
// - 부위별/상태별 배율, 그로기 시스템
public sealed partial class NetworkPlayer : NetworkBehaviour
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

    // 스폰 시 확정된 캐릭터 종류(0=Statty, 1=AlG, 2=Fit, 3=Wise).
    // ? 선택도 스폰 전 onBeforeSpawned에서 실제 배정값으로 기록된다.
    // 호스트 마이그레이션 캡처 시 roster 수신 여부와 무관하게 실제 외형을 보존하기 위해 사용한다.
    [Networked] public int CharacterTypeIndex { get; set; } = -1;

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
    private ItemRuntimeHost _itemRuntimeHost;
    private ItemCharacterUseInteractor _itemUseInteractor;
    private ItemCharacterHeldItemPresenter _heldItemPresenter;

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
    private bool _leftClickUseTriggered;
    private const float GRAB_HOLD_THRESHOLD = 0.15f;

    // 로컬 트리거
    private bool _dropTriggered;
    private bool _throwTriggered;
    private float _nextRuntimeIntegrationRefreshTime;

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
        EnsureItemRuntimeIntegration();

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

    private void EnsureItemRuntimeIntegration()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (runtimeHost == null)
        {
            // 씬에 공유 호스트가 전혀 없을 때만 로컬 호스트를 생성한다.
            runtimeHost = gameObject.AddComponent<ItemRuntimeHost>();
        }

        // 로컬 입력 권한 캐릭터(또는 샌드박스)만 소유자 트랜스폼을 갱신한다.
        if (Runner == null || HasInputAuthority)
            runtimeHost.SetOwnerTransform(transform);

        _itemRuntimeHost = runtimeHost;

        if (_handGrabHandlers != null)
        {
            foreach (var handler in _handGrabHandlers)
            {
                if (handler != null)
                    handler.SetItemRuntimeHost(_itemRuntimeHost);
            }
        }

        _itemUseInteractor = GetComponent<ItemCharacterUseInteractor>();
        if (_itemUseInteractor == null)
        {
            // 좌클릭 사용 입력이 항상 런타임 호스트로 연결되도록 인터랙터를 보장한다.
            _itemUseInteractor = gameObject.AddComponent<ItemCharacterUseInteractor>();
        }

        _itemUseInteractor.SetRuntimeHost(runtimeHost);
        _itemUseInteractor.SetOwnerRoot(transform);
        _itemUseInteractor.SetUseItemKey(KeyCode.Mouse0);
        _itemUseInteractor.SetUseLegacyInput(false);

        _heldItemPresenter = GetComponent<ItemCharacterHeldItemPresenter>();
        if (_heldItemPresenter == null)
        {
            // 손에 든 아이템 시각화가 누락되지 않도록 프리젠터를 보장한다.
            _heldItemPresenter = gameObject.AddComponent<ItemCharacterHeldItemPresenter>();
        }
        _heldItemPresenter.SetRuntimeHost(runtimeHost);
        _heldItemPresenter.SetCharacterRoot(transform);

        // 사용/드롭 이벤트를 처리하는 씬 시스템이 같은 런타임 호스트를 바라보도록 맞춘다.
        if (Runner == null || HasInputAuthority)
            SynchronizeRuntimeHostForSceneSystems(runtimeHost);
    }

    private ItemRuntimeHost ResolveItemRuntimeHostForCharacter()
    {
        var root = transform.root;
        var hosts = FindObjectsOfType<ItemRuntimeHost>(true);
        var hostCount = 0;
        ItemRuntimeHost localHost = null;
        ItemRuntimeHost gameplayRunnerHost = null;
        ItemRuntimeHost fieldDropSpawnerHost = null;
        ItemRuntimeHost ownerMatchedHost = null;
        var ownerMatchedScore = int.MinValue;
        ItemRuntimeHost singleFallback = null;
        for (var i = 0; i < hosts.Length; i++)
        {
            var host = hosts[i];
            if (host == null)
                continue;

            hostCount++;
            if (singleFallback == null)
                singleFallback = host;
            if (host.gameObject == gameObject)
                localHost = host;
            if (gameplayRunnerHost == null && host.GetComponent<ItemGameplayRunner>() != null)
                gameplayRunnerHost = host;
            if (fieldDropSpawnerHost == null && host.GetComponent<ItemFieldDropSpawner>() != null)
                fieldDropSpawnerHost = host;

            var owner = host.OwnerTransform;
            if (owner == root || (owner != null && owner.root == root))
            {
                var score = 0;
                if (host.GetComponent<ItemGameplayRunner>() != null) score += 100;
                if (host.GetComponent<ItemFieldDropSpawner>() != null) score += 80;
                if (host.GetComponent<ItemFieldInteractionService>() != null) score += 50;
                if (host.gameObject == gameObject) score += 10;

                if (score > ownerMatchedScore)
                {
                    ownerMatchedScore = score;
                    ownerMatchedHost = host;
                }
            }
        }

        if (ownerMatchedHost != null)
            return ownerMatchedHost;
        if (gameplayRunnerHost != null)
            return gameplayRunnerHost;
        if (fieldDropSpawnerHost != null)
            return fieldDropSpawnerHost;
        if (localHost != null)
            return localHost;

        return hostCount == 1 ? singleFallback : null;
    }

    private void RefreshRuntimeIntegrationIfNeeded()
    {
        var now = Time.unscaledTime;
        if (now < _nextRuntimeIntegrationRefreshTime)
            return;

        _nextRuntimeIntegrationRefreshTime = now + 1f;
        var resolved = ResolveItemRuntimeHostForCharacter();
        if (resolved == null)
            return;

        if (_itemRuntimeHost != resolved)
            EnsureItemRuntimeIntegration();
    }

    private void SynchronizeRuntimeHostForSceneSystems(ItemRuntimeHost runtimeHost)
    {
        if (runtimeHost == null)
            return;

        var spawners = FindObjectsOfType<ItemFieldDropSpawner>(true);
        for (var i = 0; i < spawners.Length; i++)
        {
            var spawner = spawners[i];
            if (spawner == null)
                continue;

            // 이미 연결된 스포너는 유지하고, 비연결 상태만 현재 호스트로 보강한다.
            if (spawner.RuntimeHost == null)
                spawner.SetRuntimeHost(runtimeHost);
        }

        // ItemScene 테스트 러너가 존재하면 동일 호스트로 이벤트를 받게 맞춘다.
        var runners = FindObjectsOfType<ItemGameplayRunner>(true);
        for (var i = 0; i < runners.Length; i++)
        {
            var runner = runners[i];
            if (runner == null)
                continue;

            runner.SetRuntimeHost(runtimeHost);
        }
    }

}
