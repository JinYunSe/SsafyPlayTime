using Fusion;
using RootMotion.Dynamics;
using SSAFYPlayTime.Character;
using SSAFYPlayTime.Gameplay.Items;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Visuals")]
    [SerializeField] private bool useAnimatedVisualOnly = true;
    [SerializeField] private bool disablePhysicsAnimationSync = true;

    [Header("Grab")]
    [SerializeField] private Transform holdPoint;

    // ─── 네트워크 동기화 변수 ───
    [Networked] private float NetworkedMoveSpeed { get; set; }
    [Networked] private int NetworkedMotorState { get; set; }
    [Networked] private int NetworkedAnimationEventSequence { get; set; }
    [Networked] private int NetworkedAnimationEventType { get; set; }

    // 스폰 시 확정된 캐릭터 종류(0=Ssaty, 1=AlG, 2=Fit, 3=Wise).
    // ? 선택도 스폰 전 onBeforeSpawned에서 실제 배정값으로 기록된다.
    // 호스트 마이그레이션 캡처 시 roster 수신 여부와 무관하게 실제 외형을 보존하기 위해 사용한다.
    [Networked] public int CharacterTypeIndex { get; set; } = -1;

    // 스프린트 상태 동기화 (원격 클라이언트 애니메이션용)
    [Networked] private NetworkBool NetworkedIsSprinting { get; set; }

    // 관절 회전값 네트워크 배열
    [Networked, Capacity(15)]
    public NetworkArray<Quaternion> BoneRotations { get; }

    // Hips(muscles[0]) 절대 위치 — 잡기로 끌려갈 때 원격에서 위치 추적용
    // Human Fall Flat 방식: 루트 뼈 절대 위치 + 나머지 뼈 상대 회전
    [Networked] public Vector3 NetworkedHipsPosition { get; set; }

    // 액티브 래그돌 상태
    [Networked] public NetworkBool NetworkedIsActiveRagdoll { get; set; }

    // ─── 그랩 상태 동기화 ───
    [Networked] public NetworkBool NetworkedLeftGrabHolding { get; set; }
    [Networked] public NetworkBool NetworkedRightGrabHolding { get; set; }

    // ─── 그랩 관계 동기화 (OwnerProxy 예측 + 원격 표시용) ───
    [Networked] public NetworkId LeftGrabTargetId { get; set; }
    [Networked] public NetworkId RightGrabTargetId { get; set; }
    [Networked] public Vector3 LeftGrabAnchorLocal { get; set; }
    [Networked] public Vector3 RightGrabAnchorLocal { get; set; }
    [Networked] public NetworkBool LeftGrabConfirmed { get; set; }
    [Networked] public NetworkBool RightGrabConfirmed { get; set; }

    // ─── OwnerProxy용 — 내가 다른 플레이어에게 잡힌 상태 ───
    [Networked] public NetworkBool NetworkedIsBeingGrabbed { get; set; }

    // ─── PuppetMaster 상태 동기화 ───
    [Networked] private float NetworkedPuppetPinWeight { get; set; }
    [Networked] private float NetworkedPuppetMuscleWeight { get; set; }
    [Networked] private int NetworkedPuppetState { get; set; }  // PuppetMaster.State (Alive=0, Dead=1)
    [Networked] private int NetworkedPuppetMode { get; set; }   // PuppetMaster.Mode (Active=0, Kinematic=1, Disabled=2)

    // ─── 기절 시스템 ───
    // stunDamage 누적치 (임계값 초과 시 기절)
    [Networked] private float AccumulatedStunDamage { get; set; }
    // 현재 기절 남은 시간
    [Networked] private float StunTimeRemaining { get; set; }

    // ─── 로컬 변수 ───
    private float _localMoveSpeed;
    private int _localMotorState;
    private int _lastConsumedAnimationEventSequence = -1;
    private Vector2 _sandboxInput;
    private bool _sandboxJump;

    private readonly GroundProbe _groundProbe = new();
    private readonly PlayerMotorStateMachine _stateMachine = new();

    private bool _isGrounded;
    private HandGrabHandler[] _handGrabHandlers;
    private ItemRuntimeHost _itemRuntimeHost;
    private ItemFieldInteractionService _itemFieldInteractionService;
    private ItemCharacterHeldItemPresenter _heldItemPresenter;
    private Transform _animatedVisualRoot;
    private Camera[] _ownedCamerasCache;
    private AudioListener[] _ownedListenersCache;
    private CameraRig[] _ownedCameraRigsCache;
    private CameraModeController[] _ownedCameraModeControllersCache;
    private readonly List<Transform> _detachedCameraRoots = new();
    private bool _cameraHierarchyDetached;

    // PuppetMaster 통합
    private PuppetMaster _puppetMaster;
    private BehaviourPuppet _behaviourPuppet;
    private Transform _targetRoot;
    private SSAFYPlayTime.Character.BodyPartPhysicsManager _bodyPartPhysicsManager;

    private bool _isActiveRagdoll = true;
    public bool IsActiveRagdoll => _isActiveRagdoll;

    // OwnerProxy 잡힘 상태 레퍼런스 카운트
    private int _beingGrabbedRefCount;

    private bool _isGrabActive;
    private bool _isLeftGrabActive;
    private bool _isRightGrabActive;
    public bool IsGrabActive => _isGrabActive;

    public bool IsHandGrabActive(HandGrabHandler.HandSide side)
    {
        return side == HandGrabHandler.HandSide.Left ? _isLeftGrabActive : _isRightGrabActive;
    }

    /// <summary>Spawned 이후에만 Networked 속성 접근 가능 여부.</summary>
    private bool IsNetworkReady => Runner != null && Object != null && Object.IsValid;

    // ─── OwnerProxy 플레이어 타입 판별 ───
    /// <summary>AuthorityOwner: 호스트 로컬 캐릭터</summary>
    private bool IsAuthorityOwner => HasStateAuthority && HasInputAuthority;
    /// <summary>OwnerProxy: 피호스트 로컬 캐릭터 (입력은 있으나 물리 시뮬 권한 없음)</summary>
    private bool IsOwnerProxy => HasInputAuthority && !HasStateAuthority;
    /// <summary>RemoteProxy: 순수 원격 캐릭터 (타인의 캐릭터)</summary>
    private bool IsRemoteProxy => !HasInputAuthority && !HasStateAuthority;

    /// <summary>
    /// 다른 플레이어의 HandGrabHandler가 이 캐릭터를 잡았/놓았을 때 호출.
    /// 호스트(StateAuthority)에서만 NetworkedIsBeingGrabbed를 갱신한다.
    /// </summary>
    public void SetGrabbedByOther(bool grabbed)
    {
        _beingGrabbedRefCount += grabbed ? 1 : -1;
        _beingGrabbedRefCount = Mathf.Max(0, _beingGrabbedRefCount);
        if (IsNetworkReady && HasStateAuthority)
            NetworkedIsBeingGrabbed = _beingGrabbedRefCount > 0;
    }

    /// <summary>
    /// HandGrabHandler에서 조인트 생성 시 호출 — grab 관계를 네트워크에 기록.
    /// </summary>
    public void ReportGrabAttached(HandGrabHandler.HandSide side, NetworkId targetId, Vector3 anchorLocal)
    {
        if (!HasStateAuthority) return;
        if (side == HandGrabHandler.HandSide.Left)
        {
            LeftGrabTargetId = targetId;
            LeftGrabAnchorLocal = anchorLocal;
            LeftGrabConfirmed = true;
        }
        else
        {
            RightGrabTargetId = targetId;
            RightGrabAnchorLocal = anchorLocal;
            RightGrabConfirmed = true;
        }
    }

    /// <summary>
    /// HandGrabHandler에서 조인트 해제 시 호출 — grab 관계를 네트워크에서 제거.
    /// </summary>
    public void ReportGrabDetached(HandGrabHandler.HandSide side)
    {
        if (!HasStateAuthority) return;
        if (side == HandGrabHandler.HandSide.Left)
        {
            LeftGrabTargetId = default;
            LeftGrabAnchorLocal = Vector3.zero;
            LeftGrabConfirmed = false;
        }
        else
        {
            RightGrabTargetId = default;
            RightGrabAnchorLocal = Vector3.zero;
            RightGrabConfirmed = false;
        }
    }

    /// <summary>
    /// PuppetStateSync(StateAuthority)에서 호출 — Networked 속성에 기록.
    /// </summary>
    public void WritePuppetSyncState(float pinWeight, float muscleWeight, int puppetState, int puppetMode)
    {
        if (!IsNetworkReady || !HasStateAuthority) return;
        NetworkedPuppetPinWeight = pinWeight;
        NetworkedPuppetMuscleWeight = muscleWeight;
        NetworkedPuppetState = puppetState;
        NetworkedPuppetMode = puppetMode;
    }

    /// <summary>
    /// PuppetStateSync(원격 클라이언트)에서 호출 — Networked 속성에서 읽기.
    /// Spawned 전이면 기본값(1,1, Alive, Active)을 반환한다.
    /// </summary>
    public void ReadPuppetSyncState(out float pinWeight, out float muscleWeight, out int puppetState, out int puppetMode)
    {
        if (!IsNetworkReady)
        {
            pinWeight = 1f;
            muscleWeight = 1f;
            puppetState = 0; // Alive
            puppetMode = 0;  // Active
            return;
        }
        pinWeight = NetworkedPuppetPinWeight;
        muscleWeight = NetworkedPuppetMuscleWeight;
        puppetState = NetworkedPuppetState;
        puppetMode = NetworkedPuppetMode;
    }

    // 원격 클라이언트 애니메이션 드라이버용 접근자
    public float GetNetworkedMoveSpeed() => Runner != null && Object != null && Object.IsValid ? NetworkedMoveSpeed : _localMoveSpeed;
    public int GetNetworkedMotorState() => Runner != null && Object != null && Object.IsValid ? NetworkedMotorState : _localMotorState;
    public bool GetNetworkedIsSprinting() => Runner != null && Object != null && Object.IsValid ? (bool)NetworkedIsSprinting : false;

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

    // 우클릭 꾹 vs 연타 판별 (우클릭 꾹 = 오른손 그랩, 짧게 = 던지기)
    private bool _rightMouseDown;
    private float _rightMouseDownTime;
    private bool _rightMouseConsumedAsGrab;

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

    private enum AnimationEventType
    {
        None = 0,
        Punch = 1,
        Throw = 2,
        GetHit = 3,
        StunFall = 4,
        StunRecover = 5
    }

    private void Awake()
    {
        InitializeInternal();
    }

    private void Start()
    {
        if (Runner == null)
            ConfigureLocalOwnershipPresentation();
    }

    private void OnDestroy()
    {
        CleanupDetachedCameraRoots();
    }

    public override void Spawned()
    {
        InitializeInternal();
        MarkItemBuffNetworkReady();
        MarkItemWorldEffectNetworkReady();

        if (HasStateAuthority)
        {
            NetworkedIsActiveRagdoll = true;
            AccumulatedStunDamage = 0f;
            StunTimeRemaining = 0f;
            NetworkedAnimationEventSequence = 0;
            NetworkedAnimationEventType = (int)AnimationEventType.None;
        }

        if (!HasStateAuthority)
        {
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
                rb.isKinematic = true;
        }

        ConfigureLocalOwnershipPresentation();
        InitializeAnimationEventState();
        InitializeAnimationDriverNetworkMode();

        // Issue 8: 호스트 마이그레이션 시 새 호스트의 자신 캐릭터 Spawned에서 드롭 아이템 위치를 재동기화한다.
        if (HasStateAuthority && HasInputAuthority && Runner != null && Runner.IsServer)
            StartCoroutine(CoResyncAllFieldDropsOnHostMigration());
    }

    private void InitializeInternal()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>(true);

        _puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        _behaviourPuppet = GetComponentInChildren<BehaviourPuppet>(true);

        // PuppetMaster가 있는데 syncPhysicsObjects가 비어 있으면
        // muscle의 물리 뼈에서 SyncPhysicsObject를 자동 생성하여 BoneRotations 동기화를 활성화한다.
        if (_puppetMaster != null && (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            && _puppetMaster.muscles != null && _puppetMaster.muscles.Length > 0)
        {
            var muscleCount = Mathf.Min(_puppetMaster.muscles.Length, 15); // BoneRotations Capacity
            syncPhysicsObjects = new SyncPhysicsObject[muscleCount];
            for (int i = 0; i < muscleCount; i++)
            {
                var muscle = _puppetMaster.muscles[i];
                if (muscle.joint == null) continue;
                var boneGo = muscle.joint.gameObject;
                var sync = boneGo.GetComponent<SyncPhysicsObject>();
                if (sync == null)
                    sync = boneGo.AddComponent<SyncPhysicsObject>();
                syncPhysicsObjects[i] = sync;
            }
        }
        _targetRoot = _puppetMaster != null ? _puppetMaster.targetRoot : null;
        _bodyPartPhysicsManager = GetComponentInChildren<SSAFYPlayTime.Character.BodyPartPhysicsManager>(true);

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

        ConfigureAnimatedVisualMode();
        EnsureAnimatorBinding();
        CacheOwnedPresentationComponents();
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
        EnsureItemWorldEffectBindings();

        if (_handGrabHandlers != null)
        {
            foreach (var handler in _handGrabHandlers)
            {
                if (handler != null)
                    handler.SetItemRuntimeHost(_itemRuntimeHost);
            }
        }

        _itemFieldInteractionService = GetComponent<ItemFieldInteractionService>();
        if (_itemFieldInteractionService == null)
        {
            _itemFieldInteractionService = gameObject.AddComponent<ItemFieldInteractionService>();
        }

        _itemFieldInteractionService.SetRuntimeHost(runtimeHost);
        _itemFieldInteractionService.SetOwnerTransform(transform);

        _heldItemPresenter = GetComponent<ItemCharacterHeldItemPresenter>();
        if (_heldItemPresenter == null)
        {
            // 손에 든 아이템 시각화가 누락되지 않도록 프리젠터를 보장한다.
            _heldItemPresenter = gameObject.AddComponent<ItemCharacterHeldItemPresenter>();
        }
        _heldItemPresenter.SetRuntimeHost(runtimeHost);
        _heldItemPresenter.SetCharacterRoot(transform);

        var buffApplier = GetComponent<ItemCharacterBuffApplier>();
        if (buffApplier == null)
        {
            // 소비형 버프는 캐릭터 루트에 직접 적용되므로 네트워크 플레이어 경로에서도 항상 보장한다.
            buffApplier = gameObject.AddComponent<ItemCharacterBuffApplier>();
        }

        buffApplier.SetRuntimeHost(runtimeHost);
        buffApplier.SetCharacterRoot(transform);

    }

    private ItemRuntimeHost ResolveItemRuntimeHostForCharacter()
    {
        var localHost = GetComponent<ItemRuntimeHost>();
        if (localHost != null)
            return localHost;

        var root = transform.root;
        if (root != null && root != transform)
        {
            var rootHost = root.GetComponent<ItemRuntimeHost>();
            if (rootHost != null)
                return rootHost;
        }

        // 플레이어 아이템 상태는 캐릭터별로 분리되어야 하므로 씬 공용 호스트를 fallback으로 재사용하지 않는다.
        return null;
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

    private void InitializeAnimationDriverNetworkMode()
    {
        var driver = GetComponent<PartyMonsterAnimationDriver>();
        if (driver == null)
            return;

        // StateAuthority가 없는 모든 캐릭터는 네트워크 데이터 기반 애니메이션을 사용.
        // 피호스트 로컬 플레이어(HasInputAuthority=true, HasStateAuthority=false)도
        // 자기 캐릭터를 직접 시뮬하지 않으므로, 네트워크 속도/이벤트 기반으로 구동해야 한다.
        // 이전: !HasInputAuthority → 피호스트 로컬 플레이어가 로컬 rigidbody 기반으로 잘못 분기됨
        var isRemote = Runner != null && !HasStateAuthority;
        driver.SetRemoteProxy(isRemote);

        // 피호스트 로컬 플레이어: 입력은 즉시 예측 연출, 로코모션만 네트워크 기반
        var isLocalWithoutAuth = Runner != null && HasInputAuthority && !HasStateAuthority;
        driver.SetLocalWithoutAuthority(isLocalWithoutAuth);
    }

    private void ConfigureLocalOwnershipPresentation()
    {
        var isLocalOwner = Runner == null || HasInputAuthority;
        CacheOwnedPresentationComponents();

        if (isLocalOwner)
            DetachOwnedCameraHierarchyIfNeeded();

        foreach (var camera in _ownedCamerasCache)
        {
            if (camera == null)
                continue;

            camera.enabled = isLocalOwner;

            if (isLocalOwner)
                camera.tag = "MainCamera";
            else if (camera.CompareTag("MainCamera"))
                camera.tag = "Untagged";
        }

        foreach (var listener in _ownedListenersCache)
        {
            if (listener == null)
                continue;

            listener.enabled = isLocalOwner;
        }

        foreach (var cameraRig in _ownedCameraRigsCache)
        {
            if (cameraRig == null)
                continue;

            cameraRig.enabled = isLocalOwner;
            if (isLocalOwner)
                cameraRig.SetTarget(transform);
        }

        foreach (var cameraModeController in _ownedCameraModeControllersCache)
        {
            if (cameraModeController == null)
                continue;

            cameraModeController.enabled = isLocalOwner;
            if (isLocalOwner)
                cameraModeController.BindLocalPlayer(gameObject);
        }

        if (!isLocalOwner)
            return;

        // 로컬 오너 메인 Rigidbody에 보간 활성화.
        // 물리 이동(AddForce)과 LateUpdate 카메라 추적 사이의 프레임 불일치로 인한
        // 카메라 떨림(jitter)을 해소한다. 원격 프록시는 kinematic이므로 불필요.
        if (rigidbody3D != null)
            rigidbody3D.interpolation = RigidbodyInterpolation.Interpolate;

        foreach (var camera in FindObjectsOfType<Camera>(true))
        {
            if (camera == null)
                continue;

            var isOwnedCamera = false;
            for (var i = 0; i < _ownedCamerasCache.Length; i++)
            {
                if (_ownedCamerasCache[i] == camera)
                {
                    isOwnedCamera = true;
                    break;
                }
            }

            if (isOwnedCamera)
                continue;

            if (camera.CompareTag("MainCamera"))
                camera.tag = "Untagged";

            var listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;
        }
    }

    private void CacheOwnedPresentationComponents()
    {
        _ownedCamerasCache ??= GetComponentsInChildren<Camera>(true);
        _ownedListenersCache ??= GetComponentsInChildren<AudioListener>(true);
        _ownedCameraRigsCache ??= GetComponentsInChildren<CameraRig>(true);
        _ownedCameraModeControllersCache ??= GetComponentsInChildren<CameraModeController>(true);
    }

    private void DetachOwnedCameraHierarchyIfNeeded()
    {
        if (_cameraHierarchyDetached)
            return;

        var detachedAny = false;
        var detachedRoots = new HashSet<Transform>();

        foreach (var cameraRig in _ownedCameraRigsCache)
        {
            if (cameraRig == null)
                continue;

            var rigTransform = cameraRig.transform;
            if (rigTransform == null || rigTransform == transform || !rigTransform.IsChildOf(transform))
                continue;

            if (!detachedRoots.Add(rigTransform))
                continue;

            rigTransform.SetParent(null, true);
            _detachedCameraRoots.Add(rigTransform);
            detachedAny = true;
        }

        _cameraHierarchyDetached = detachedAny || _cameraHierarchyDetached;
    }

    private void CleanupDetachedCameraRoots()
    {
        for (var i = 0; i < _detachedCameraRoots.Count; i++)
        {
            var root = _detachedCameraRoots[i];
            if (root == null)
                continue;

            Destroy(root.gameObject);
        }

        _detachedCameraRoots.Clear();
        _cameraHierarchyDetached = false;
    }

}
