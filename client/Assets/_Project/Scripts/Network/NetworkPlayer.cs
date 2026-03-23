using Fusion;
using RootMotion.Dynamics;
using SSAFYPlayTime.Character;
using SSAFYPlayTime.Gameplay.Items;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// CarrySolveFrame: carry 중 손/victim root/proxy hips/presentation root가
// 모두 같은 기준점을 보게 만드는 통합 구조.

// Fusion NetworkBehaviour 기반의 캐릭터 컨트롤러.
// StateAuthority(서버/호스트)에서만 물리 시뮬레이션을 실행한다.
//
// [전투 시스템]
// - stunDamage 누적 -> 임계값(30) 초과 -> 기절 -> 가중치 기반 자동 회복
// - 좌클릭 짧게 = 아이템 사용(보유 시)/펀치, 좌클릭 길게 = 그랩(아이템이면 즉시 습득), 우클릭 = 던지기
// - 부위별/상태별 배율, 그로기 시스템
public sealed partial class NetworkPlayer : NetworkBehaviour
{
    private static readonly HashSet<NetworkPlayer> RegisteredPlayers = new();

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
    [SerializeField] private CharacterGrabController characterGrabController;

    [Header("Carry Solve Frame")]
    [SerializeField] private CarryRig carryRig;
    [SerializeField] private CarryPhysicsProfile carryPhysicsProfile;

    [Header("Camera Follow")]
    [SerializeField] private Vector3 cameraAnchorPresentationLocalOffset = new Vector3(0f, 1.35f, 0f);
    [SerializeField] private Vector3 cameraAnchorFallbackRootLocalOffset = new Vector3(0f, 1.25f, 0f);
    [SerializeField] private float cameraAnchorStableFollowSpeed = 25f;
    [SerializeField] private float cameraAnchorStableAirborneFollowSpeed = 18f;
    [SerializeField] private float cameraAnchorHoldingFollowSpeed = 18f;
    [SerializeField] private float cameraAnchorRecoveringFollowSpeed = 12f;
    [SerializeField] private float cameraAnchorHardPhysicsFollowSpeed = 6f;
    [SerializeField] private float cameraAnchorVerticalFollowMultiplier = 0.75f;
    [SerializeField] private float cameraAnchorStableAirborneVerticalFollowMultiplier = 0.9f;
    [SerializeField] private float cameraAnchorHoldingVerticalFollowMultiplier = 0.82f;
    [SerializeField] private float cameraAnchorHardPhysicsVerticalFollowMultiplier = 0.58f;
    [SerializeField] private float cameraAnchorSnapDistance = 2f;
    [SerializeField] private float cameraAnchorStableAirborneSnapMultiplier = 0.9f;
    [SerializeField] private float cameraAnchorHoldingSnapMultiplier = 0.85f;
    [SerializeField] private float cameraAnchorRecoveringSnapMultiplier = 1.1f;
    [SerializeField] private float cameraAnchorOwnerProxyLeadDistance = 0.18f;
    [SerializeField] private float cameraAnchorOwnerProxyPresentationCorrectionScale = 0.35f;
    [SerializeField] private float cameraAnchorOwnerProxyMaxCorrectionDistance = 0.12f;
    [SerializeField, Range(0f, 1f)] private float cameraAnchorHoldingOwnerProxyLeadScale = 0.32f;
    [SerializeField, Range(0f, 1f)] private float cameraAnchorHoldingOwnerProxyCorrectionMultiplier = 0.18f;
    [SerializeField, Range(0f, 1f)] private float cameraAnchorHoldingOwnerProxyMaxCorrectionMultiplier = 0.22f;

    // 네트워크 동기화 변수
    [Networked] private float NetworkedMoveSpeed { get; set; }
    [Networked] private int NetworkedMotorState { get; set; }
    [Networked] private int NetworkedAnimationEventSequence { get; set; }
    [Networked] private int NetworkedAnimationEventType { get; set; }
    [Networked] private int NetworkedKnockoutConfirmSequence { get; set; }

    // 스폰 시 확정된 캐릭터 종류(0=Ssaty, 1=AlG, 2=Fit, 3=Wise).
    // 랜덤 선택도 스폰 전 onBeforeSpawned에서 실제 배정값으로 기록된다.
    // 호스트 마이그레이션 캡처 시 roster 수신 여부와 무관하게 실제 외형을 보존하기 위해 사용한다.
    [Networked] public int CharacterTypeIndex { get; set; } = -1;

    // 스프린트 상태 동기화 (원격 클라이언트 애니메이션용)
    [Networked] private NetworkBool NetworkedIsSprinting { get; set; }
    [Networked] private float NetworkedVisualYaw { get; set; }
    [Networked] private byte NetworkedLocomotionState { get; set; }

    // 좌/우 펀치 동기화 - 호스트가 결정, 모든 클라이언트가 동일한 클립 재생
    [Networked] private NetworkBool NetworkedPunchIsLeft { get; set; }

    // 관절 회전값 네트워크 배열
    [Networked, Capacity(15)]
    public NetworkArray<Quaternion> BoneRotations { get; }

    // Hips(muscles[0]) 절대 위치 - 잡기로 끌려갈 때 원격에서 위치 추적용
    // Human Fall Flat 방식: 루트 뼈 절대 위치 + 나머지 뼈 상대 회전
    [Networked] public Vector3 NetworkedHipsPosition { get; set; }

    // 액티브 래그돌 상태
    [Networked] public NetworkBool NetworkedIsActiveRagdoll { get; set; }
    [Networked] private byte NetworkedStunPresentationPhase { get; set; }

    // 그랩 상태 동기화
    [Networked] public NetworkBool NetworkedLeftGrabHolding { get; set; }
    [Networked] public NetworkBool NetworkedRightGrabHolding { get; set; }

    // 그랩 관계 동기화 (OwnerProxy 예측 + 원격 표시용)
    // Left/Right grab target과 anchor를 네트워크로 동기화한다.
    [Networked] public NetworkId LeftGrabTargetId { get; set; }
    [Networked] public NetworkId RightGrabTargetId { get; set; }
    [Networked] public Vector3 LeftGrabAnchorLocal { get; set; }
    [Networked] public Vector3 RightGrabAnchorLocal { get; set; }
    [Networked] public NetworkBool LeftGrabConfirmed { get; set; }
    [Networked] public NetworkBool RightGrabConfirmed { get; set; }
    // 잡힌 앵커 부위 동기화 (GrabAnchorPoint.AnchorId byte)
    [Networked] public byte LeftGrabAnchorId { get; set; }
    [Networked] public byte RightGrabAnchorId { get; set; }

    // ─── CarrySolveFrame 네트워크 동기화 ───
    // victim anchor: BeingCarriedStunned인 플레이어가 자신의 hips-chest 가중 평균을 전송
    [Networked] private Vector3 NetworkedVictimAnchorPosition { get; set; }
    [Networked] private Vector3 NetworkedVictimAnchorForward { get; set; }
    [Networked] private NetworkBool NetworkedVictimAnchorValid { get; set; }
    [Networked] private Vector3 NetworkedVictimRootOffset { get; set; }
    [Networked] private NetworkBool NetworkedVictimRootOffsetValid { get; set; }
    // carrier anchor: CarryingStunned인 플레이어가 자신의 carry rig anchor를 전송
    [Networked] private Vector3 NetworkedCarrierAnchorPosition { get; set; }
    [Networked] private Vector3 NetworkedCarrierAnchorForward { get; set; }
    [Networked] private NetworkBool NetworkedCarrierAnchorValid { get; set; }
    [Networked] private byte NetworkedCarryMode { get; set; }
    [Networked] private byte NetworkedGrabActionState { get; set; }
    [Networked] private byte NetworkedGrabHoldVariant { get; set; }
    [Networked] private byte NetworkedLeftHandHoldMode { get; set; }
    [Networked] private byte NetworkedRightHandHoldMode { get; set; }

    // OwnerProxy에서 다른 플레이어에게 잡힌 상태
    [Networked] public NetworkBool NetworkedIsBeingGrabbed { get; set; }
    [Networked] private byte NetworkedPhysicalPhase { get; set; }
    [Networked] private float NetworkedInstability { get; set; }
    [Networked] public NetworkBool NetworkedIsDragged { get; set; }
    [Networked] private byte NetworkedPhysicsPresentationResetVersion { get; set; }
    [Networked] private byte NetworkedRecoveryAnimationVariant { get; set; }

    // PuppetMaster 상태 동기화
    [Networked] private float NetworkedPuppetPinWeight { get; set; }
    [Networked] private float NetworkedPuppetMuscleWeight { get; set; }
    [Networked] private int NetworkedPuppetState { get; set; }  // PuppetMaster.State (Alive=0, Dead=1)
    [Networked] private int NetworkedPuppetMode { get; set; }   // PuppetMaster.Mode (Active=0, Kinematic=1, Disabled=2)

    // 기절 시스템
    // stunDamage 누적치 (임계값 초과 시 기절)
    [Networked] private float AccumulatedStunDamage { get; set; }
    // 현재 기절 남은 시간
    [Networked] private float StunTimeRemaining { get; set; }

    [Header("Grab Debug")]
    [SerializeField] bool debugGrabLog = true;
    [SerializeField] bool enableProxyAnimationDiagnostics = true;

    [Header("Startup Launch Diagnostics")]
    [SerializeField] private bool enableStartupLaunchDiagnostics = true;
    [SerializeField, Range(0.05f, 0.5f)] private float startupLaunchDiagnosticsSampleInterval = 0.15f;
    [SerializeField, Range(1f, 8f)] private float startupLaunchDiagnosticsWindow = 4f;

    // ─── 로컬 변수 ───
    private float _localMoveSpeed;
    private int _localMotorState;
    private float _localVisualYaw;
    private PresentationLocomotionState _localPresentationLocomotionState;
    private PhysicalPhase _localPhysicalPhase = PhysicalPhase.Stable;
    private float _localInstability;
    private bool _localIsDragged;
    private byte _lastObservedPhysicsPresentationResetVersion;
    private int _remotePhysicsPresentationHoldUntilFrame = -1;
    private int _lastConsumedAnimationEventSequence = -1;
    private int _lastConsumedKnockoutConfirmSequence = -1;
    private Vector2 _sandboxInput;
    private bool _sandboxJump;

    private readonly GroundProbe _groundProbe = new();
    private readonly PlayerMotorStateMachine _stateMachine = new();

    private bool _isGrounded;
    private float _startupLaunchDiagnosticsUntilTime = float.NegativeInfinity;
    private float _startupLaunchDiagnosticsLastSampleTime = float.NegativeInfinity;
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

    // 카메라 Follow 앵커 - transform.position이 SyncRootToPhysicsBody로 급격히 변해도
    // 카메라는 이 앵커를 따라가므로 부드럽게 추적한다.
    private const string ExplicitCameraAnchorName = "CameraFollowAnchor";
    private const string ExplicitPresentationAnchorName = "PresentationAnchor";

    private Transform _cameraFollowAnchor;
    private Transform _cameraAnchorSourceCache;
    private Transform _cameraAnchorSourceRootCache;
    private CameraAnchorSourceMode _cameraAnchorSourceMode;
    private RecoveryAnimationVariant _localRecoveryAnimationVariant;
    private Transform _recoveryPoseHips;
    private Transform _recoveryPoseHead;
    private Transform _recoveryPoseLeftArm;
    private Transform _recoveryPoseRightArm;
    private float _recoveryPoseForwardSign = 1f;
    private bool _recoveryPoseForwardSignResolved;
    private static readonly string[] LegacyCameraAnchorTargetNames =
    {
        "FollowTarget",
        "CameraPivot",
        "ShoulderPivot",
        "HeadTarget"
    };

    // ─── CarrySolveFrame 로컬 상태 ───
    private CarryPhysicsProfile.CarryMode _localCarryMode = CarryPhysicsProfile.CarryMode.None;
    private CarryPhysicsProfile.CarryMode _lastObservedCarryMode = CarryPhysicsProfile.CarryMode.None;
    private float _carryReleaseSettleRemaining;
    private Vector3 _lastCarryAnchorPosition;
    private Vector3 _lastCarryAnchorForward;
    private Vector3 _carriedVictimRootOffset;
    private bool _hasCarriedVictimRootOffset;

    // PuppetMaster 통합
    private PuppetMaster _puppetMaster;
    private BehaviourPuppet _behaviourPuppet;
    private Transform _targetRoot;
    private SSAFYPlayTime.Character.BodyPartPhysicsManager _bodyPartPhysicsManager;
    private SSAFYPlayTime.Character.GrabAntiStretchController _grabAntiStretchController;

    private bool _isActiveRagdoll = true;
    public bool IsActiveRagdoll => _isActiveRagdoll;

    // 공개 상태 API
    /// <summary>기절 중 여부 (activeRagdoll이 아닌 상태)</summary>
    public bool IsStunned => !_isActiveRagdoll;
    /// <summary>기절 회복 직후 취약 상태 여부</summary>
    public bool IsRecovering => _isRecovering;
    /// <summary>전투 행동(펀치/그랩/던지기) 가능 여부. 기절/회복 중이거나 사망 상태면 false.</summary>
    public bool CanPerformCombatActions => _isActiveRagdoll && !_isRecovering && !GetIsDeadState();
    /// <summary>이동 가능 여부. 기절 중이거나 사망 상태면 false.</summary>
    public bool CanDriveLocomotion => _isActiveRagdoll && !GetIsDeadState();

    /// <summary>카메라가 따라가야 할 타겟. Follow 앵커가 있으면 앵커, 없으면 transform.</summary>
    public Transform GetCameraFollowTarget() => _cameraFollowAnchor != null ? _cameraFollowAnchor : transform;

    /// <summary>CarryRig 참조 (carrier/victim 양쪽에서 사용)</summary>
    internal CarryRig GetCarryRig() => carryRig;
    /// <summary>CarryPhysicsProfile 참조</summary>
    internal CarryPhysicsProfile GetCarryPhysicsProfile() => carryPhysicsProfile;
    /// <summary>Grab/carry 상태 허브 참조</summary>
    internal CharacterGrabController GetCharacterGrabController() => characterGrabController;
    /// <summary>
    /// 현재 carry 모드. StateAuthority는 로컬 값, proxy는 네트워크 값을 반환.
    /// </summary>
    internal CarryPhysicsProfile.CarryMode GetLocalCarryMode()
    {
        if (IsNetworkReady && !HasStateAuthority)
            return (CarryPhysicsProfile.CarryMode)NetworkedCarryMode;
        return _localCarryMode;
    }

    internal bool TryGetReplicatedGrabControllerState(
        out CharacterGrabController.GrabActionState actionState,
        out CharacterGrabController.HoldVariant holdVariant,
        out CharacterGrabController.HandHoldMode leftMode,
        out CharacterGrabController.HandHoldMode rightMode)
    {
        actionState = CharacterGrabController.GrabActionState.Idle;
        holdVariant = CharacterGrabController.HoldVariant.None;
        leftMode = CharacterGrabController.HandHoldMode.None;
        rightMode = CharacterGrabController.HandHoldMode.None;

        // Any proxy should read the authority-published grab controller state.
        // OwnerProxy keeps short-lived local prediction elsewhere, but confirmed presentation
        // must come from the same replicated source as RemoteProxy.
        if (!IsNetworkReady || HasStateAuthority)
            return false;

        actionState = (CharacterGrabController.GrabActionState)NetworkedGrabActionState;
        holdVariant = (CharacterGrabController.HoldVariant)NetworkedGrabHoldVariant;
        leftMode = (CharacterGrabController.HandHoldMode)NetworkedLeftHandHoldMode;
        rightMode = (CharacterGrabController.HandHoldMode)NetworkedRightHandHoldMode;
        return true;
    }
    /// <summary>carry release settle 중인지 여부</summary>
    internal bool IsCarryReleaseSettling => _carryReleaseSettleRemaining > 0f;

    // OwnerProxy 잡힘 상태 레퍼런스 카운트
    private int _beingGrabbedRefCount;

    private bool _isGrabActive;
    private bool _isLeftGrabActive;
    private bool _isRightGrabActive;
    public bool IsGrabActive => _isGrabActive;

    // 다른 플레이어에 의해 잡힌 횟수 (여러 손에 동시에 잡힐 수 있으므로 카운터)
    private int _grabbedByCount;
    public bool IsGrabbedByOther => _grabbedByCount > 0;
    private Vector3 _cachedIncomingHeldAnchorWorld;
    private Vector3 _cachedIncomingHeldHolderWorld;
    private GrabAnchorPoint.AnchorId _cachedIncomingHeldAnchorId = GrabAnchorPoint.AnchorId.None;
    private float _cachedIncomingHeldUntilTime = float.NegativeInfinity;
    private bool _hasCachedIncomingHeldPresentation;

    public bool IsHandGrabActive(HandGrabHandler.HandSide side)
    {
        return side == HandGrabHandler.HandSide.Left ? _isLeftGrabActive : _isRightGrabActive;
    }

    internal PhysicalPhase GetPhysicalPhase()
    {
        if (IsNetworkReady && !HasStateAuthority)
            return (PhysicalPhase)NetworkedPhysicalPhase;

        return _localPhysicalPhase;
    }

    internal float GetPhysicalInstability()
    {
        if (IsNetworkReady && !HasStateAuthority)
            return NetworkedInstability;

        return _localInstability;
    }

    internal bool IsDraggedByPhysics()
    {
        if (IsNetworkReady && !HasStateAuthority)
            return NetworkedIsDragged;

        return _localIsDragged;
    }

    /// <summary>Spawned 이후에만 Networked 속성 접근 가능 여부.</summary>
    internal bool IsNetworkReady => Runner != null && Object != null && Object.IsValid;

    private void ArmStartupLaunchDiagnostics(string source, string note = null)
    {
        if (!ShouldEmitStartupLaunchDiagnostics(allowOutsideWindow: true))
            return;

        _startupLaunchDiagnosticsUntilTime = Mathf.Max(
            _startupLaunchDiagnosticsUntilTime,
            Time.unscaledTime + startupLaunchDiagnosticsWindow);
        _startupLaunchDiagnosticsLastSampleTime = float.NegativeInfinity;
        TraceStartupLaunchDiagnostics(source, force: true, note: note);
    }

    private bool ShouldEmitStartupLaunchDiagnostics(bool allowOutsideWindow = false)
    {
        if (!enableStartupLaunchDiagnostics || !Application.isPlaying)
            return false;

        if (!HasStateAuthority || !HasInputAuthority)
            return false;

        return allowOutsideWindow || Time.unscaledTime <= _startupLaunchDiagnosticsUntilTime;
    }

    private void TraceStartupLaunchDiagnostics(
        string source,
        Vector3? targetPosition = null,
        bool force = false,
        string note = null)
    {
        if (!ShouldEmitStartupLaunchDiagnostics(force))
            return;

        var now = Time.unscaledTime;
        if (!force && now - _startupLaunchDiagnosticsLastSampleTime < startupLaunchDiagnosticsSampleInterval)
            return;

        _startupLaunchDiagnosticsLastSampleTime = now;

        var rootPosition = transform.position;
        var bodyPosition = rigidbody3D != null ? rigidbody3D.position : rootPosition;
        var bodyVelocity = rigidbody3D != null && !rigidbody3D.isKinematic
            ? rigidbody3D.velocity
            : Vector3.zero;
        var pelvisPosition = ResolveStartupLaunchPelvisPosition(out var pelvisVelocity);
        var phase = GetPhysicalPhase();
        var carryMode = GetLocalCarryMode();
        var rootBodyGap = (rootPosition - bodyPosition).magnitude;
        var rootPelvisGap = (rootPosition - pelvisPosition).magnitude;
        var hasTarget = targetPosition.HasValue;
        var targetGap = hasTarget ? Vector3.Distance(rootPosition, targetPosition.Value) : 0f;

        if (!force &&
            phase == PhysicalPhase.Stable &&
            carryMode == CarryPhysicsProfile.CarryMode.None &&
            _beingGrabbedRefCount == 0 &&
            _grabbedByCount == 0 &&
            !IsAnyHandHoldingObject() &&
            !IsAnyHandHoldingStunnedPlayer &&
            rootBodyGap < 0.12f &&
            rootPelvisGap < 0.85f &&
            Mathf.Abs(bodyVelocity.y) < 2f &&
            Mathf.Abs(pelvisVelocity.y) < 2f &&
            (!hasTarget || targetGap < 0.2f))
        {
            return;
        }

        var tick = Runner != null ? Runner.Tick.Raw : -1;
        var noteSuffix = string.IsNullOrWhiteSpace(note) ? string.Empty : $" note={note}";
        var targetSuffix = hasTarget
            ? $" target={FormatStartupLaunchVector(targetPosition.Value)} targetGap={targetGap:F3}"
            : string.Empty;

        Debug.Log(
            $"[StartupLaunchDiag] tick={tick} name={name} source={source} " +
            $"phase={phase} carry={carryMode} active={_isActiveRagdoll} grounded={_isGrounded} " +
            $"grabbedRef={_beingGrabbedRefCount} grabbedBy={_grabbedByCount} " +
            $"holding={IsAnyHandHoldingObject()} anyStunned={IsAnyHandHoldingStunnedPlayer} dual={IsDualGrabbingStunnedPlayer} " +
            $"root={FormatStartupLaunchVector(rootPosition)} body={FormatStartupLaunchVector(bodyPosition)} " +
            $"pelvis={FormatStartupLaunchVector(pelvisPosition)} bodyVel={FormatStartupLaunchVector(bodyVelocity)} " +
            $"pelvisVel={FormatStartupLaunchVector(pelvisVelocity)} rootBodyGap={rootBodyGap:F3} " +
            $"rootPelvisGap={rootPelvisGap:F3}{targetSuffix}{noteSuffix}",
            this);
    }

    private Vector3 ResolveStartupLaunchPelvisPosition(out Vector3 pelvisVelocity)
    {
        pelvisVelocity = Vector3.zero;

        if (_puppetMaster != null &&
            _puppetMaster.muscles != null &&
            _puppetMaster.muscles.Length > 0 &&
            _puppetMaster.muscles[0].joint != null)
        {
            var pelvisJoint = _puppetMaster.muscles[0].joint;
            var pelvisBody = pelvisJoint.GetComponent<Rigidbody>();
            if (pelvisBody != null)
                pelvisVelocity = pelvisBody.velocity;

            return pelvisJoint.transform.position;
        }

        return rigidbody3D != null ? rigidbody3D.position : transform.position;
    }

    private static string FormatStartupLaunchVector(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    internal enum PresentationLocomotionState : byte
    {
        Idle = 0,
        Walk = 1,
        Sprint = 2
    }

    internal enum StunPresentationPhase : byte
    {
        Active = 0,
        Stunned = 1,
        RecoverStabilizing = 2
    }

    internal enum RecoveryAnimationVariant : byte
    {
        None = 0,
        Supine = 1,
        Prone = 2
    }

    internal enum PhysicalPhase : byte
    {
        Stable = 0,
        GrabIntent = 1,
        Holding = 2,
        BeingGrabbed = 3,
        Dragged = 4,
        Unstable = 5,
        Stunned = 6,
        Recovering = 7,
        CarryingStunned = 8,
        WeaponEquipped = 9,
        BeingCarriedStunned = 10,
        StunnedCollapse = 11
    }

    private enum CameraAnchorSourceMode : byte
    {
        None = 0,
        ExplicitPresentationAnchor = 1,
        ExplicitCameraTarget = 2,
        LegacyNamedTarget = 3,
        PresentationRoot = 4,
        RootTransform = 5
    }

    private readonly struct CameraAnchorFollowSettings
    {
        public readonly float planarFollowSpeed;
        public readonly float verticalFollowMultiplier;
        public readonly float snapDistance;
        public readonly float ownerProxyLeadScale;
        public readonly float ownerProxyCorrectionScale;
        public readonly float maxOwnerProxyCorrectionDistance;

        public CameraAnchorFollowSettings(
            float planarFollowSpeed,
            float verticalFollowMultiplier,
            float snapDistance,
            float ownerProxyLeadScale,
            float ownerProxyCorrectionScale,
            float maxOwnerProxyCorrectionDistance)
        {
            this.planarFollowSpeed = planarFollowSpeed;
            this.verticalFollowMultiplier = verticalFollowMultiplier;
            this.snapDistance = snapDistance;
            this.ownerProxyLeadScale = ownerProxyLeadScale;
            this.ownerProxyCorrectionScale = ownerProxyCorrectionScale;
            this.maxOwnerProxyCorrectionDistance = maxOwnerProxyCorrectionDistance;
        }
    }

    private const int RemotePhysicsPresentationHoldFrameCount = 4;

    // OwnerProxy 플레이어 판별 관계
    /// <summary>AuthorityOwner: 호스트 로컬 플레이어</summary>
    private bool IsAuthorityOwner => HasStateAuthority && HasInputAuthority;
    /// <summary>OwnerProxy: 입력 권한은 있지만 물리 권한은 없는 로컬 플레이어</summary>
    private bool IsOwnerProxy => HasInputAuthority && !HasStateAuthority;
    /// <summary>RemoteProxy: 입력 권한과 물리 권한이 모두 없는 원격 플레이어</summary>
    private bool IsRemoteProxy => !HasInputAuthority && !HasStateAuthority;

    /// <summary>
    /// 다른 플레이어의 HandGrabHandler가 이 캐릭터를 잡거나 놓을 때 호출한다.
    /// 호스트(StateAuthority)에서만 NetworkedIsBeingGrabbed를 갱신한다.
    /// </summary>
    public void SetGrabbedByOther(bool grabbed)
    {
        var previousRefCount = _beingGrabbedRefCount;
        _beingGrabbedRefCount += grabbed ? 1 : -1;
        _beingGrabbedRefCount = Mathf.Max(0, _beingGrabbedRefCount);
        if (IsNetworkReady && HasStateAuthority)
            NetworkedIsBeingGrabbed = _beingGrabbedRefCount > 0;

        if (debugGrabLog && previousRefCount != _beingGrabbedRefCount)
        {
            Debug.Log($"[GrabState] {name} SetGrabbedByOther({grabbed}) ref={previousRefCount}->{_beingGrabbedRefCount} " +
                $"netGrabbed={(IsNetworkReady ? NetworkedIsBeingGrabbed.ToString() : "N/A")}", this);
        }

        TraceCarryDebugSample(
            "SetGrabbedByOther",
            $"grabbed={grabbed} ref={previousRefCount}->{_beingGrabbedRefCount}",
            true);
    }

    /// <summary>
    /// HandGrabHandler에서 조인트 생성 후 호출해 grab 관계를 네트워크에 기록한다.
    /// </summary>
    public void ReportGrabAttached(HandGrabHandler.HandSide side, NetworkId targetId, Vector3 anchorLocal,
        byte anchorId = 0)
    {
        if (!HasStateAuthority) return;
        if (side == HandGrabHandler.HandSide.Left)
        {
            LeftGrabTargetId = targetId;
            LeftGrabAnchorLocal = anchorLocal;
            LeftGrabConfirmed = true;
            LeftGrabAnchorId = anchorId;
        }
        else
        {
            RightGrabTargetId = targetId;
            RightGrabAnchorLocal = anchorLocal;
            RightGrabConfirmed = true;
            RightGrabAnchorId = anchorId;
        }

        if (debugGrabLog)
        {
            Debug.Log($"[GrabState] {name} ReportGrabAttached side={side} targetId={targetId} anchorLocal={anchorLocal} " +
                $"anchorId={anchorId} L_confirmed={LeftGrabConfirmed} R_confirmed={RightGrabConfirmed}", this);
        }

        TraceCarryDebugSample(
            "ReportGrabAttached",
            $"side={side} targetId={targetId} anchorLocal={FormatStunForceDiagnosticsVector(anchorLocal)} anchorId={anchorId}",
            true);
    }

    /// <summary>
    /// HandGrabHandler에서 조인트 해제 후 호출해 grab 관계를 네트워크에서 제거한다.
    /// </summary>
    public void ReportGrabDetached(HandGrabHandler.HandSide side)
    {
        if (!HasStateAuthority) return;
        if (side == HandGrabHandler.HandSide.Left)
        {
            LeftGrabTargetId = default;
            LeftGrabAnchorLocal = Vector3.zero;
            LeftGrabConfirmed = false;
            LeftGrabAnchorId = 0;
        }
        else
        {
            RightGrabTargetId = default;
            RightGrabAnchorLocal = Vector3.zero;
            RightGrabConfirmed = false;
            RightGrabAnchorId = 0;
        }

        if (debugGrabLog)
        {
            Debug.Log($"[GrabState] {name} ReportGrabDetached side={side} " +
                $"L_confirmed={LeftGrabConfirmed} R_confirmed={RightGrabConfirmed}", this);
        }

        TraceCarryDebugSample(
            "ReportGrabDetached",
            $"side={side} leftTarget={LeftGrabTargetId} rightTarget={RightGrabTargetId}",
            true);
    }

    private bool TryGetNetworkHeldAnchorData(
        HandGrabHandler.HandSide side,
        out NetworkId targetId,
        out Vector3 anchorLocal,
        out byte anchorId)
    {
        targetId = default;
        anchorLocal = Vector3.zero;
        anchorId = 0;

        var isConfirmed = side == HandGrabHandler.HandSide.Left
            ? (bool)LeftGrabConfirmed
            : (bool)RightGrabConfirmed;
        if (!isConfirmed)
            return false;

        targetId = side == HandGrabHandler.HandSide.Left ? LeftGrabTargetId : RightGrabTargetId;
        anchorLocal = side == HandGrabHandler.HandSide.Left ? LeftGrabAnchorLocal : RightGrabAnchorLocal;
        anchorId = side == HandGrabHandler.HandSide.Left ? LeftGrabAnchorId : RightGrabAnchorId;
        return targetId.IsValid;
    }

    internal bool TryGetReplicatedGrabAnchorWorld(byte anchorIdByte, Vector3 anchorLocal, out Vector3 anchorWorld)
    {
        anchorWorld = Vector3.zero;

        GrabRigRuntimeBootstrap.EnsureCharacterRig(transform, null, null);

        var desiredAnchorId = (GrabAnchorPoint.AnchorId)anchorIdByte;
        var anchors = GetComponentsInChildren<GrabAnchorPoint>(true);
        GrabAnchorPoint bestLocalMatch = null;
        var bestLocalMatchDistance = float.PositiveInfinity;

        for (var i = 0; i < anchors.Length; i++)
        {
            var anchor = anchors[i];
            if (anchor == null)
                continue;

            if (desiredAnchorId != GrabAnchorPoint.AnchorId.None && anchor.Id == desiredAnchorId)
            {
                anchorWorld = anchor.GetGripWorldPosition();
                return true;
            }

            if (anchorLocal == Vector3.zero)
                continue;

            var localDistance = (anchor.GetGripLocalInBone() - anchorLocal).sqrMagnitude;
            if (localDistance >= bestLocalMatchDistance)
                continue;

            bestLocalMatchDistance = localDistance;
            bestLocalMatch = anchor;
        }

        if (bestLocalMatch != null)
        {
            anchorWorld = bestLocalMatch.GetGripWorldPosition();
            return true;
        }

        return false;
    }

    private bool TryResolveNetworkHeldAnchorWorldPosition(HandGrabHandler.HandSide side, out Vector3 anchorWorld)
    {
        anchorWorld = Vector3.zero;

        if (Runner == null || !Object.IsValid)
            return false;

        if (!TryGetNetworkHeldAnchorData(side, out var targetId, out var anchorLocal, out var anchorId))
            return false;

        var targetObject = Runner.FindObject(targetId);
        if (targetObject == null)
            return false;

        var targetPlayer = targetObject.GetComponent<NetworkPlayer>();
        if (targetPlayer != null && targetPlayer.TryGetReplicatedGrabAnchorWorld(anchorId, anchorLocal, out anchorWorld))
            return true;

        if (anchorLocal != Vector3.zero)
        {
            anchorWorld = targetObject.transform.TransformPoint(anchorLocal);
            return true;
        }

        return false;
    }

    internal bool TryGetHeldAnchorWorldPosition(HandGrabHandler.HandSide side, out Vector3 anchorWorld)
    {
        anchorWorld = Vector3.zero;
        if (_handGrabHandlers != null)
        {
            for (var i = 0; i < _handGrabHandlers.Length; i++)
            {
                var handler = _handGrabHandlers[i];
                if (handler == null || handler.Side != side || !handler.IsHolding)
                    continue;

                anchorWorld = handler.GetGrabAnchorWorldPosition();
                return true;
            }
        }

        return !HasStateAuthority && TryResolveNetworkHeldAnchorWorldPosition(side, out anchorWorld);
    }

    internal bool TryGetAverageHeldAnchorWorldPosition(out Vector3 anchorWorld)
    {
        anchorWorld = Vector3.zero;
        var count = 0;

        if (TryGetHeldAnchorWorldPosition(HandGrabHandler.HandSide.Left, out var leftAnchor))
        {
            anchorWorld += leftAnchor;
            count++;
        }

        if (TryGetHeldAnchorWorldPosition(HandGrabHandler.HandSide.Right, out var rightAnchor))
        {
            anchorWorld += rightAnchor;
            count++;
        }

        if (count <= 0)
            return false;

        anchorWorld /= count;
        return true;
    }

    private CharacterGrabController.HoldVariant ResolveCurrentOrReplicatedHoldVariant()
    {
        if (IsNetworkReady && !HasStateAuthority)
            return (CharacterGrabController.HoldVariant)NetworkedGrabHoldVariant;

        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            return characterGrabController.CurrentHoldVariant;
        }

        return CharacterGrabController.HoldVariant.None;
    }

    private bool IsEligibleForIncomingHeldPresentation()
    {
        return GetPhysicalPhase() switch
        {
            PhysicalPhase.Stable => true,
            PhysicalPhase.Holding => true,
            PhysicalPhase.Recovering => true,
            PhysicalPhase.Unstable => true,
            _ => false
        };
    }

    private static float ResolveIncomingHeldAnchorPriority(GrabAnchorPoint.AnchorId anchorId)
    {
        return anchorId switch
        {
            GrabAnchorPoint.AnchorId.Chest => 4f,
            GrabAnchorPoint.AnchorId.Hips => 3f,
            GrabAnchorPoint.AnchorId.LeftUpperArm => 2f,
            GrabAnchorPoint.AnchorId.RightUpperArm => 2f,
            GrabAnchorPoint.AnchorId.LeftForearm => 1f,
            GrabAnchorPoint.AnchorId.RightForearm => 1f,
            GrabAnchorPoint.AnchorId.Head => 0.5f,
            _ => 0f
        };
    }

    private bool TryAccumulateIncomingHeldSide(
        NetworkPlayer holder,
        HandGrabHandler.HandSide side,
        NetworkId selfId,
        ref int count,
        ref Vector3 anchorWorldSum,
        ref Vector3 holderWorldSum,
        ref GrabAnchorPoint.AnchorId resolvedAnchorId,
        ref float resolvedAnchorPriority)
    {
        if (holder == null ||
            !holder.TryGetNetworkHeldAnchorData(side, out var targetId, out _, out var anchorIdByte) ||
            targetId != selfId)
        {
            return false;
        }

        if (!holder.TryGetHeldAnchorWorldPosition(side, out var sideAnchorWorld))
            return false;

        anchorWorldSum += sideAnchorWorld;
        holderWorldSum += holder.transform.position;
        count++;

        var anchorId = (GrabAnchorPoint.AnchorId)anchorIdByte;
        var anchorPriority = ResolveIncomingHeldAnchorPriority(anchorId);
        if (anchorPriority > resolvedAnchorPriority)
        {
            resolvedAnchorPriority = anchorPriority;
            resolvedAnchorId = anchorId;
        }

        return true;
    }

    private bool TryResolveIncomingHeldPresentationData(
        out Vector3 anchorWorld,
        out Vector3 holderWorld,
        out GrabAnchorPoint.AnchorId anchorId)
    {
        anchorWorld = Vector3.zero;
        holderWorld = transform.position;
        anchorId = GrabAnchorPoint.AnchorId.None;

        if (!IsNetworkReady || !Object.IsValid)
            return false;

        var selfId = Object.Id;
        var resolvedCount = 0;
        var anchorWorldSum = Vector3.zero;
        var holderWorldSum = Vector3.zero;
        var resolvedAnchorPriority = float.NegativeInfinity;

        foreach (var candidate in RegisteredPlayers)
        {
            if (candidate == null || candidate == this || !candidate.IsNetworkReady || !candidate.Object.IsValid)
                continue;

            if (candidate.ResolveCurrentOrReplicatedHoldVariant() != CharacterGrabController.HoldVariant.ConsciousPlayer)
                continue;

            TryAccumulateIncomingHeldSide(
                candidate,
                HandGrabHandler.HandSide.Left,
                selfId,
                ref resolvedCount,
                ref anchorWorldSum,
                ref holderWorldSum,
                ref anchorId,
                ref resolvedAnchorPriority);
            TryAccumulateIncomingHeldSide(
                candidate,
                HandGrabHandler.HandSide.Right,
                selfId,
                ref resolvedCount,
                ref anchorWorldSum,
                ref holderWorldSum,
                ref anchorId,
                ref resolvedAnchorPriority);
        }

        if (resolvedCount <= 0)
            return false;

        anchorWorld = anchorWorldSum / resolvedCount;
        holderWorld = holderWorldSum / resolvedCount;
        return true;
    }

    internal bool TryGetIncomingHeldPresentationData(
        out Vector3 anchorWorld,
        out Vector3 holderWorld,
        out GrabAnchorPoint.AnchorId anchorId,
        out bool isRecoveringTarget)
    {
        anchorWorld = Vector3.zero;
        holderWorld = transform.position;
        anchorId = GrabAnchorPoint.AnchorId.None;
        isRecoveringTarget = GetPhysicalPhase() == PhysicalPhase.Recovering;

        if (!IsEligibleForIncomingHeldPresentation())
        {
            _hasCachedIncomingHeldPresentation = false;
            _cachedIncomingHeldAnchorId = GrabAnchorPoint.AnchorId.None;
            return false;
        }

        if (TryResolveIncomingHeldPresentationData(out anchorWorld, out holderWorld, out anchorId))
        {
            _cachedIncomingHeldAnchorWorld = anchorWorld;
            _cachedIncomingHeldHolderWorld = holderWorld;
            _cachedIncomingHeldAnchorId = anchorId;
            _cachedIncomingHeldUntilTime = Time.time + 0.14f;
            _hasCachedIncomingHeldPresentation = true;
            return true;
        }

        if (_hasCachedIncomingHeldPresentation && Time.time <= _cachedIncomingHeldUntilTime)
        {
            anchorWorld = _cachedIncomingHeldAnchorWorld;
            holderWorld = _cachedIncomingHeldHolderWorld;
            anchorId = _cachedIncomingHeldAnchorId;
            return true;
        }

        _hasCachedIncomingHeldPresentation = false;
        _cachedIncomingHeldAnchorId = GrabAnchorPoint.AnchorId.None;
        return false;
    }

    internal bool TryGetCarrierSupportFrameWorld(out Vector3 supportWorld, out Vector3 supportForward)
    {
        supportWorld = Vector3.zero;
        supportForward = transform.forward;

        var phase = GetPhysicalPhase();
        var carryMode = GetLocalCarryMode();
        var holdVariant = CharacterGrabController.HoldVariant.None;
        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            holdVariant = characterGrabController.CurrentHoldVariant;
        }
        var isCarrierMode = phase == PhysicalPhase.CarryingStunned ||
                            carryMode == CarryPhysicsProfile.CarryMode.StunnedSingleCarry ||
                            carryMode == CarryPhysicsProfile.CarryMode.StunnedDualCarry;
        if (!isCarrierMode)
            return false;

        if (!HasStateAuthority && (bool)NetworkedCarrierAnchorValid)
        {
            supportWorld = NetworkedCarrierAnchorPosition;
            supportForward = NetworkedCarrierAnchorForward.sqrMagnitude > 0.0001f
                ? NetworkedCarrierAnchorForward.normalized
                : transform.forward;
            return true;
        }

        if (carryRig != null &&
            carryMode != CarryPhysicsProfile.CarryMode.None &&
            carryMode != CarryPhysicsProfile.CarryMode.CarriedVictim &&
            carryRig.TryGetCarrierSupportFrameWorld(carryMode, holdVariant, out supportWorld, out supportForward))
        {
            return true;
        }

        return false;
    }

    internal bool TryGetCarryOrHeldReferenceWorldPosition(out Vector3 referenceWorld)
    {
        referenceWorld = Vector3.zero;

        if (TryGetCarrierSupportFrameWorld(out referenceWorld, out _))
            return true;

        return TryGetAverageHeldAnchorWorldPosition(out referenceWorld);
    }

    internal bool IsGrabFacingLocked()
    {
        var phase = GetPhysicalPhase();
        if (UsesPhysicsPosePresentation(phase))
            return false;

        if (characterGrabController != null)
        {
            characterGrabController.RefreshNow();
            return characterGrabController.ShouldLockFacingToHoldTarget;
        }

        if (HasStateAuthority)
            return IsAnyHandHoldingObject();

        return (phase == PhysicalPhase.Holding || phase == PhysicalPhase.CarryingStunned) &&
               (NetworkedLeftGrabHolding || NetworkedRightGrabHolding);
    }

    internal bool TryGetCameraYawClamp(out float centerYaw, out float halfAngle)
    {
        centerYaw = ResolveGrabCameraReferenceYaw();
        halfAngle = 0f;

        var phase = GetPhysicalPhase();
        switch (phase)
        {
            case PhysicalPhase.Holding:
                if (!IsAnyHandHolding)
                    return false;

                halfAngle = 62f;
                return true;

            case PhysicalPhase.CarryingStunned:
                halfAngle = IsDualGrabbingStunnedPlayer ? 52f : 58f;
                return true;

            case PhysicalPhase.BeingGrabbed:
            case PhysicalPhase.Dragged:
                halfAngle = 42f;
                return true;

            case PhysicalPhase.BeingCarriedStunned:
                halfAngle = 36f;
                return true;

            default:
                return false;
        }
    }

    private float ResolveGrabCameraReferenceYaw()
    {
        var phase = GetPhysicalPhase();
        if ((phase == PhysicalPhase.Holding || phase == PhysicalPhase.CarryingStunned) &&
            TryGetCarryOrHeldReferenceWorldPosition(out var grabAnchorWorld))
        {
            var pivotPosition = ResolveGrabFacingPivotPosition();
            var anchorPlanar = grabAnchorWorld - pivotPosition;
            anchorPlanar.y = 0f;
            if (anchorPlanar.sqrMagnitude > 0.0001f)
                return Quaternion.LookRotation(anchorPlanar.normalized, Vector3.up).eulerAngles.y;
        }

        return ResolvePresentationYawFromTransform();
    }

    /// <summary>
    /// PuppetStateSync(StateAuthority)에서 호출해 Networked 속성에 기록한다.
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
    /// PuppetStateSync(원격 클라이언트)에서 호출해 Networked 속성에서 읽는다.
    /// 아직 Spawned 전이면 기본값(1,1, Alive, Active)을 반환한다.
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

    // 원격 클라이언트 애니메이션/프레젠테이션 접근용
    public float GetNetworkedMoveSpeed() => Runner != null && Object != null && Object.IsValid ? NetworkedMoveSpeed : _localMoveSpeed;
    public int GetNetworkedMotorState() => Runner != null && Object != null && Object.IsValid ? NetworkedMotorState : _localMotorState;
    public bool GetNetworkedIsSprinting() => Runner != null && Object != null && Object.IsValid ? (bool)NetworkedIsSprinting : false;
    internal float GetNetworkedVisualYaw() => Runner != null && Object != null && Object.IsValid ? NetworkedVisualYaw : _localVisualYaw;
    internal PresentationLocomotionState GetNetworkedLocomotionState() =>
        Runner != null && Object != null && Object.IsValid
            ? (PresentationLocomotionState)NetworkedLocomotionState
            : _localPresentationLocomotionState;

    internal StunPresentationPhase GetStunPresentationPhase()
    {
        if (!IsNetworkReady || HasStateAuthority)
            return ResolveLocalStunPresentationPhase();

        return (StunPresentationPhase)NetworkedStunPresentationPhase;
    }

    internal RecoveryAnimationVariant GetRecoveryAnimationVariant()
    {
        if (!IsNetworkReady || HasStateAuthority)
            return _localRecoveryAnimationVariant;

        return (RecoveryAnimationVariant)NetworkedRecoveryAnimationVariant;
    }

    private void SetRecoveryAnimationVariant(RecoveryAnimationVariant variant)
    {
        _localRecoveryAnimationVariant = variant;

        if (IsNetworkReady && HasStateAuthority)
            NetworkedRecoveryAnimationVariant = (byte)variant;
    }

    internal PresentationLocomotionState ResolveLocomotionState(float speed, bool sprinting)
    {
        if (speed <= PM_LocomotionThreshold)
            return PresentationLocomotionState.Idle;

        return sprinting ? PresentationLocomotionState.Sprint : PresentationLocomotionState.Walk;
    }

    internal Transform GetPresentationRootTransform()
    {
        if (_targetRoot != null)
            return _targetRoot;

        if (_animatedVisualRoot != null)
            return _animatedVisualRoot;

        if (animator != null && animator.transform != transform)
            return animator.transform;

        return null;
    }

    private Transform ResolveCameraAnchorSource()
    {
        var presentationRoot = GetPresentationRootTransform();
        var searchRoot = presentationRoot != null ? presentationRoot : transform;

        if (_cameraAnchorSourceCache != null && _cameraAnchorSourceRootCache == searchRoot)
            return _cameraAnchorSourceCache;

        _cameraAnchorSourceRootCache = searchRoot;
        _cameraAnchorSourceCache = null;
        _cameraAnchorSourceMode = CameraAnchorSourceMode.None;

        if (searchRoot == null)
            return null;

        var explicitAnchor = FindNamedTransformRecursive(searchRoot, ExplicitCameraAnchorName)
            ?? FindNamedTransformRecursive(searchRoot, ExplicitPresentationAnchorName);
        if (explicitAnchor != null)
        {
            _cameraAnchorSourceCache = explicitAnchor;
            _cameraAnchorSourceMode = CameraAnchorSourceMode.ExplicitPresentationAnchor;
            return _cameraAnchorSourceCache;
        }

        var directCameraTarget = FindNamedTransformRecursive(searchRoot, "CameraTarget");
        if (directCameraTarget != null)
        {
            _cameraAnchorSourceCache = directCameraTarget;
            _cameraAnchorSourceMode = CameraAnchorSourceMode.ExplicitCameraTarget;
            return _cameraAnchorSourceCache;
        }

        if (presentationRoot != null)
        {
            _cameraAnchorSourceCache = presentationRoot;
            _cameraAnchorSourceMode = CameraAnchorSourceMode.PresentationRoot;
            return _cameraAnchorSourceCache;
        }

        for (var i = 0; i < LegacyCameraAnchorTargetNames.Length; i++)
        {
            var legacy = FindNamedTransformRecursive(searchRoot, LegacyCameraAnchorTargetNames[i]);
            if (legacy == null)
                continue;

            _cameraAnchorSourceCache = legacy;
            _cameraAnchorSourceMode = CameraAnchorSourceMode.LegacyNamedTarget;
            return _cameraAnchorSourceCache;
        }

        _cameraAnchorSourceCache = transform;
        _cameraAnchorSourceMode = CameraAnchorSourceMode.RootTransform;
        return _cameraAnchorSourceCache;
    }

    private static Transform FindNamedTransformRecursive(Transform root, string exactName)
    {
        if (root == null || string.IsNullOrEmpty(exactName))
            return null;

        if (root.name == exactName)
            return root;

        var children = root.GetComponentsInChildren<Transform>(true);
        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];
            if (child != null && child.name == exactName)
                return child;
        }

        return null;
    }

    private Vector3 ResolveCameraAnchorBasePosition()
    {
        var anchorSource = ResolveCameraAnchorSource();
        if (anchorSource != null)
        {
            var presentationRoot = GetPresentationRootTransform();
            if (_cameraAnchorSourceMode == CameraAnchorSourceMode.PresentationRoot && presentationRoot != null)
                return ResolveYawOnlyAnchorOffsetPosition(presentationRoot, cameraAnchorPresentationLocalOffset);

            if (_cameraAnchorSourceMode == CameraAnchorSourceMode.RootTransform)
                return ResolveYawOnlyAnchorOffsetPosition(transform, cameraAnchorFallbackRootLocalOffset);

            return anchorSource.position;
        }

        var presentationFallback = GetPresentationRootTransform();
        if (presentationFallback != null)
            return ResolveYawOnlyAnchorOffsetPosition(presentationFallback, cameraAnchorPresentationLocalOffset);

        return ResolveYawOnlyAnchorOffsetPosition(transform, cameraAnchorFallbackRootLocalOffset);
    }

    private Vector3 ResolveCameraAnchorDesiredPosition(bool includeOwnerProxyBias)
    {
        var settings = ResolveCameraAnchorFollowSettings();
        var basePosition = ResolveCameraAnchorBasePosition();
        if (!includeOwnerProxyBias)
            return basePosition;

        return basePosition
             + ResolveOwnerProxyCameraAnchorLead(settings.ownerProxyLeadScale)
             + ResolveOwnerProxyCameraAnchorCorrection(
                 settings.ownerProxyCorrectionScale,
                 settings.maxOwnerProxyCorrectionDistance);
    }

    private static Vector3 ResolveYawOnlyAnchorOffsetPosition(Transform basis, Vector3 localOffset)
    {
        if (basis == null)
            return localOffset;

        var yawOnlyRotation = Quaternion.Euler(0f, basis.eulerAngles.y, 0f);
        return basis.position + yawOnlyRotation * localOffset;
    }

    private Vector3 ResolveOwnerProxyCameraAnchorLead(float leadScale)
    {
        if (!IsOwnerProxy || ShouldUseHardPhysicsPresentation())
            return Vector3.zero;
        if (NetworkedIsBeingGrabbed || IsDraggedByPhysics() || IsGrabFacingLocked())
            return Vector3.zero;
        if (leadScale <= 0.0001f)
            return Vector3.zero;

        var localMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (localMove.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        var moveDirection = ResolvePresentationMoveDirection(localMove);
        moveDirection.y = 0f;
        if (moveDirection.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        var moveMagnitude = Mathf.Clamp01(localMove.magnitude);
        return moveDirection.normalized * (cameraAnchorOwnerProxyLeadDistance * leadScale * moveMagnitude);
    }

    private Vector3 ResolveOwnerProxyCameraAnchorCorrection(float correctionScale, float maxCorrectionDistance)
    {
        if (!IsOwnerProxy || ShouldUseHardPhysicsPresentation())
            return Vector3.zero;
        if (NetworkedIsBeingGrabbed || IsDraggedByPhysics() || IsGrabFacingLocked())
            return Vector3.zero;
        if (correctionScale <= 0.0001f || maxCorrectionDistance <= 0.0001f)
            return Vector3.zero;
        if (_cameraAnchorSourceMode == CameraAnchorSourceMode.ExplicitPresentationAnchor ||
            _cameraAnchorSourceMode == CameraAnchorSourceMode.ExplicitCameraTarget ||
            _cameraAnchorSourceMode == CameraAnchorSourceMode.LegacyNamedTarget)
        {
            return Vector3.zero;
        }

        var presentationRoot = GetPresentationRootTransform();
        if (presentationRoot == null)
            return Vector3.zero;

        var planarGap = presentationRoot.position - transform.position;
        planarGap.y = 0f;
        if (planarGap.sqrMagnitude <= 0.0001f)
            return Vector3.zero;

        return Vector3.ClampMagnitude(planarGap * correctionScale, maxCorrectionDistance);
    }

    private CameraAnchorFollowSettings ResolveCameraAnchorFollowSettings()
    {
        if (ShouldUseHardPhysicsPresentation())
        {
            return new CameraAnchorFollowSettings(
                cameraAnchorHardPhysicsFollowSpeed,
                cameraAnchorHardPhysicsVerticalFollowMultiplier,
                cameraAnchorSnapDistance * 1.35f,
                0f,
                0f,
                0f);
        }

        var phase = GetPhysicalPhase();
        if (phase == PhysicalPhase.Holding)
        {
            return new CameraAnchorFollowSettings(
                cameraAnchorHoldingFollowSpeed,
                cameraAnchorHoldingVerticalFollowMultiplier,
                cameraAnchorSnapDistance * cameraAnchorHoldingSnapMultiplier,
                cameraAnchorHoldingOwnerProxyLeadScale,
                cameraAnchorOwnerProxyPresentationCorrectionScale * cameraAnchorHoldingOwnerProxyCorrectionMultiplier,
                cameraAnchorOwnerProxyMaxCorrectionDistance * cameraAnchorHoldingOwnerProxyMaxCorrectionMultiplier);
        }

        if (phase == PhysicalPhase.CarryingStunned ||
            phase == PhysicalPhase.GrabIntent)
        {
            return new CameraAnchorFollowSettings(
                cameraAnchorHoldingFollowSpeed,
                cameraAnchorHoldingVerticalFollowMultiplier,
                cameraAnchorSnapDistance * cameraAnchorHoldingSnapMultiplier,
                1f,
                cameraAnchorOwnerProxyPresentationCorrectionScale * 0.5f,
                cameraAnchorOwnerProxyMaxCorrectionDistance * 0.5f);
        }

        if (phase == PhysicalPhase.Unstable || phase == PhysicalPhase.Recovering)
        {
            return new CameraAnchorFollowSettings(
                cameraAnchorRecoveringFollowSpeed,
                cameraAnchorVerticalFollowMultiplier,
                cameraAnchorSnapDistance * cameraAnchorRecoveringSnapMultiplier,
                0.45f,
                cameraAnchorOwnerProxyPresentationCorrectionScale * 0.25f,
                cameraAnchorOwnerProxyMaxCorrectionDistance * 0.35f);
        }

        if (phase == PhysicalPhase.BeingGrabbed ||
            phase == PhysicalPhase.Dragged ||
            phase == PhysicalPhase.StunnedCollapse ||
            phase == PhysicalPhase.Stunned ||
            phase == PhysicalPhase.BeingCarriedStunned)
        {
            return new CameraAnchorFollowSettings(
                cameraAnchorHardPhysicsFollowSpeed,
                cameraAnchorHardPhysicsVerticalFollowMultiplier,
                cameraAnchorSnapDistance * 1.35f,
                0f,
                0f,
                0f);
        }

        if (IsCameraAnchorAirborneState())
        {
            return new CameraAnchorFollowSettings(
                cameraAnchorStableAirborneFollowSpeed,
                cameraAnchorStableAirborneVerticalFollowMultiplier,
                cameraAnchorSnapDistance * cameraAnchorStableAirborneSnapMultiplier,
                0.85f,
                cameraAnchorOwnerProxyPresentationCorrectionScale * 0.2f,
                cameraAnchorOwnerProxyMaxCorrectionDistance * 0.25f);
        }

        return new CameraAnchorFollowSettings(
            cameraAnchorStableFollowSpeed,
            cameraAnchorVerticalFollowMultiplier,
            cameraAnchorSnapDistance,
            1f,
            cameraAnchorOwnerProxyPresentationCorrectionScale,
            cameraAnchorOwnerProxyMaxCorrectionDistance);
    }

    private bool IsCameraAnchorAirborneState()
    {
        if (_isGrounded)
            return false;

        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return false;

        var phase = GetPhysicalPhase();
        return phase == PhysicalPhase.Stable ||
               phase == PhysicalPhase.GrabIntent ||
               phase == PhysicalPhase.WeaponEquipped;
    }

    private float ResolvePresentationYawFromTransform()
    {
        var presentationRoot = GetPresentationRootTransform();
        return presentationRoot != null ? presentationRoot.eulerAngles.y : transform.eulerAngles.y;
    }

    private void SetPresentationVisualYaw(float yaw)
    {
        _localVisualYaw = Mathf.Repeat(yaw, 360f);

        if (Runner != null && Object != null && Object.IsValid && HasStateAuthority)
            NetworkedVisualYaw = _localVisualYaw;
    }

    private StunPresentationPhase ResolveLocalStunPresentationPhase()
    {
        if (!_isActiveRagdoll)
            return StunPresentationPhase.Stunned;

        return _isRecoverStabilizing
            ? StunPresentationPhase.RecoverStabilizing
            : StunPresentationPhase.Active;
    }

    private void SynchronizeStunPresentationPhase()
    {
        if (!IsNetworkReady || !HasStateAuthority)
            return;

        NetworkedStunPresentationPhase = (byte)ResolveLocalStunPresentationPhase();
    }

    internal bool ShouldUsePhysicalPhasePresentation()
    {
        return GetStunPresentationPhase() == StunPresentationPhase.RecoverStabilizing ||
               UsesPhysicsPosePresentation(GetPhysicalPhase()) ||
               IsRemotePhysicsPresentationResetLocked();
    }

    internal bool ShouldUseHardPhysicsPresentation()
    {
        if (!ShouldUsePhysicalPhasePresentation())
            return false;

        if (IsRemotePhysicsPresentationResetLocked())
            return true;

        if (GetStunPresentationPhase() == StunPresentationPhase.RecoverStabilizing)
            return true;

        return GetPhysicalPhase() switch
        {
            PhysicalPhase.BeingGrabbed => true,
            PhysicalPhase.Dragged => true,
            PhysicalPhase.StunnedCollapse => true,
            PhysicalPhase.Stunned => true,
            PhysicalPhase.BeingCarriedStunned => true,
            _ => false
        };
    }

    internal static bool UsesPhysicsPosePresentation(PhysicalPhase phase)
    {
        return phase == PhysicalPhase.BeingGrabbed ||
               phase == PhysicalPhase.Dragged ||
               phase == PhysicalPhase.Unstable ||
               phase == PhysicalPhase.StunnedCollapse ||
               phase == PhysicalPhase.Stunned ||
               phase == PhysicalPhase.BeingCarriedStunned;
    }

    internal bool ShouldUsePhysicsPosePresentation()
    {
        return ShouldUsePhysicalPhasePresentation();
    }

    internal void UpdateRemotePhysicsPresentationResetWindow()
    {
        if (!IsNetworkReady || HasStateAuthority)
            return;

        if (_lastObservedPhysicsPresentationResetVersion == NetworkedPhysicsPresentationResetVersion)
            return;

        _lastObservedPhysicsPresentationResetVersion = NetworkedPhysicsPresentationResetVersion;
        _remotePhysicsPresentationHoldUntilFrame = Time.frameCount + RemotePhysicsPresentationHoldFrameCount;
    }

    private bool IsRemotePhysicsPresentationResetLocked()
    {
        return !HasStateAuthority &&
               _remotePhysicsPresentationHoldUntilFrame >= Time.frameCount;
    }

    private void FlagPhysicsPresentationReset()
    {
        if (!IsNetworkReady || !HasStateAuthority)
            return;

        NetworkedPhysicsPresentationResetVersion++;
    }

    // 기절 관련
    private float _startSlerpPositionSpring;
    private bool _isRecovering; // 회복 직후 취약 상태
    private float _recoveringTimer;
    private const float RECOVERING_DURATION = 2.0f; // 회복 후 2초간 취약

    // 좌클릭 짧게 vs 길게 구분
    private bool _leftMouseDown;
    private float _leftMouseDownTime;
    private bool _leftMouseConsumedAsGrab;
    private bool _leftClickUseTriggered;
    private const float GRAB_HOLD_THRESHOLD = 0.15f;

    // 우클릭 짧게 vs 길게 구분 (짧게 = 빠른 그랩, 길게 = 던지기)
    private bool _rightMouseDown;
    private float _rightMouseDownTime;
    private bool _rightMouseConsumedAsGrab;

    // 호스트 측 좌우 근접 공격 토글
    private bool _hostNextPunchLeft;
    private bool _hostNextKickLeft;

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
        StunRecover = 5,
        PunchLeft = 6,
        PunchRight = 7,
        KickLeft = 8,
        KickRight = 9,
        AerialKick = 10
    }

    private void Awake()
    {
        RegisteredPlayers.Add(this);
        debugGrabLog = true; // 디버그 진단용 강제 활성화
        InitializeInternal();
    }

    private void Start()
    {
        if (Runner == null)
            ConfigureLocalOwnershipPresentation();
    }

    private void OnDestroy()
    {
        RegisteredPlayers.Remove(this);
        DestroyLocalGhostMode();
        CleanupDetachedCameraRoots();
        if (_cameraFollowAnchor != null)
            Destroy(_cameraFollowAnchor.gameObject);
    }

    public override void Spawned()
    {
        InitializeInternal();
        MarkItemBuffNetworkReady();
        MarkItemWorldEffectNetworkReady();
        EnsureVitalStateInitialized();

        if (HasStateAuthority)
        {
            NetworkedIsActiveRagdoll = true;
            NetworkedStunPresentationPhase = (byte)StunPresentationPhase.Active;
            NetworkedPhysicalPhase = (byte)PhysicalPhase.Stable;
            NetworkedInstability = 0f;
            NetworkedIsDragged = false;
            NetworkedPhysicsPresentationResetVersion = 0;
            NetworkedRecoveryAnimationVariant = (byte)RecoveryAnimationVariant.None;
            AccumulatedStunDamage = 0f;
            StunTimeRemaining = 0f;
            NetworkedAnimationEventSequence = 0;
            NetworkedAnimationEventType = (int)AnimationEventType.None;
            NetworkedKnockoutConfirmSequence = 0;
            NetworkedVisualYaw = _localVisualYaw;
            NetworkedLocomotionState = (byte)_localPresentationLocomotionState;
            NetworkedVictimAnchorValid = false;
            NetworkedVictimRootOffsetValid = false;
            NetworkedCarrierAnchorValid = false;
            NetworkedCarryMode = (byte)CarryPhysicsProfile.CarryMode.None;
            NetworkedGrabActionState = (byte)CharacterGrabController.GrabActionState.Idle;
            NetworkedGrabHoldVariant = (byte)CharacterGrabController.HoldVariant.None;
            NetworkedLeftHandHoldMode = (byte)CharacterGrabController.HandHoldMode.None;
            NetworkedRightHandHoldMode = (byte)CharacterGrabController.HandHoldMode.None;
        }

        _lastObservedPhysicsPresentationResetVersion = NetworkedPhysicsPresentationResetVersion;
        _lastConsumedKnockoutConfirmSequence = NetworkedKnockoutConfirmSequence;

        if (HasStateAuthority)
        {
            if (_behaviourPuppet != null)
                _behaviourPuppet.enabled = false;
        }
        else
        {
            foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
                rb.isKinematic = true;

            // 비호스트에서는 PuppetMaster muscle의 target 매핑(Map())을 비활성화한다.
            // kinematic muscle이 frozen 상태일 때 Map()만 돌면 target skeleton 출력이 꼬일 수 있다.
            // 스폰 직후 한 프레임 동안 Animator 출력이 무효화되는 문제를 방지한다.
            // 기절/그랩 등 physics presentation 진입 전 Active로 복원한다.
            if (_puppetMaster != null)
                _puppetMaster.mode = RootMotion.Dynamics.PuppetMaster.Mode.Disabled;
        }

        ConfigureLocalOwnershipPresentation();
        InitializeAnimationEventState();
        InitializeAnimationDriverNetworkMode();
        ArmStartupLaunchDiagnostics("Spawned");

        // 호스트 마이그레이션 이후 로컬 플레이어의 필드 아이템 위치를 다시 동기화한다.
        if (HasStateAuthority && HasInputAuthority && Runner != null && Runner.IsServer)
            StartCoroutine(CoResyncAllFieldDropsOnHostMigration());
    }

    private void InitializeInternal()
    {
        EnsureVitalStateInitialized();

        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>(true);

        _puppetMaster = GetComponentInChildren<PuppetMaster>(true);
        _behaviourPuppet = GetComponentInChildren<BehaviourPuppet>(true);

        // PuppetMaster는 있지만 syncPhysicsObjects가 비어 있으면
        // muscle 물리 뼈마다 SyncPhysicsObject를 자동 생성해 BoneRotations 동기화를 구성한다.
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
        MarkPhysicsPoseBindingsDirty();
        _bodyPartPhysicsManager = GetComponentInChildren<SSAFYPlayTime.Character.BodyPartPhysicsManager>(true);

        // CarryRig 초기화 — 프리팹에 없으면 런타임 생성
        if (carryRig == null)
            carryRig = GetComponentInChildren<CarryRig>(true);
        if (carryRig == null && _targetRoot != null)
        {
            var rigGo = new GameObject("CarryRig");
            rigGo.transform.SetParent(_targetRoot);
            rigGo.transform.localPosition = Vector3.zero;
            rigGo.transform.localRotation = Quaternion.identity;
            carryRig = rigGo.AddComponent<CarryRig>();
            carryRig.EnsureAnchorsCreated();
        }

        _grabAntiStretchController = GetComponent<SSAFYPlayTime.Character.GrabAntiStretchController>();
        if (_grabAntiStretchController == null)
            _grabAntiStretchController = gameObject.AddComponent<SSAFYPlayTime.Character.GrabAntiStretchController>();

        _handGrabHandlers = GetComponentsInChildren<HandGrabHandler>(true);
        EnsureItemRuntimeIntegration();

        if (characterGrabController == null)
            characterGrabController = GetComponent<CharacterGrabController>();
        if (characterGrabController == null)
            characterGrabController = gameObject.AddComponent<CharacterGrabController>();

        if (holdPoint == null)
        {
            var go = new GameObject("HoldPoint");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0f, 0.4f, 0.35f);
            holdPoint = go.transform;
        }

        foreach (var handler in _handGrabHandlers)
            handler.SetHoldPoint(holdPoint);

        characterGrabController.RefreshNow();

        if (mainJoint != null)
            _startSlerpPositionSpring = mainJoint.slerpDrive.positionSpring;

        ConfigureAnimatedVisualMode();
        EnsureAnimatorBinding();
        SetPresentationVisualYaw(ResolvePresentationYawFromTransform());
        CacheOwnedPresentationComponents();
        EnsureAntiStretchConstraint();

        if (GetComponent<PlayerStats>() == null)
            gameObject.AddComponent<PlayerStats>();
    }

    private void EnsureAntiStretchConstraint()
    {
        if (GetComponent<AntiStretchConstraint>() != null)
            return;

        gameObject.AddComponent<AntiStretchConstraint>();
    }

    private void EnsureItemRuntimeIntegration()
    {
        var runtimeHost = ResolveItemRuntimeHostForCharacter();
        if (runtimeHost == null)
        {
            // 같은 루트에 공유 인스턴스가 없을 때만 로컬 호스트를 생성한다.
            runtimeHost = gameObject.AddComponent<ItemRuntimeHost>();
        }

        // 입력 권한이 있는 캐릭터만 런타임 호스트의 owner transform을 갱신한다.
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
            // held visual presenter가 없으면 런타임에 보장 생성한다.
            _heldItemPresenter = gameObject.AddComponent<ItemCharacterHeldItemPresenter>();
        }
        _heldItemPresenter.SetRuntimeHost(runtimeHost);
        _heldItemPresenter.SetCharacterRoot(transform);

        var buffApplier = GetComponent<ItemCharacterBuffApplier>();
        if (buffApplier == null)
        {
            // 버프 적용기도 캐릭터 루트에 직접 붙여 원격 경로에서도 항상 동일하게 보이게 한다.
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

        // 플레이어 아이템 상태는 캐릭터별로 분리되어야 하므로 공용 루트 host를 fallback으로 쓰지 않는다.
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

        // PartyMonsterAnimationDriver가 NetworkPlayer와 동일한 Animator를 사용하도록 보장한다.
        // ConfigureAnimatedVisualMode()에서 선택한 _animatedVisualRoot의 Animator를 기준으로 맞춘다.
        // 별도 serialized 참조가 다른 비활성 Animator를 가리키는 경우를 막는다.
        if (animator != null)
            driver.SetAnimator(animator);

        // StateAuthority가 없는 캐릭터는 네트워크 데이터 기반 프레젠테이션만 사용한다.
        // 호스트 로컬 플레이어(HasInputAuthority=true, HasStateAuthority=false)도
        // 직접 물리를 돌리지 않으므로 네트워크 속도/이벤트 기반으로 구동해야 한다.
        // 이전에는 !HasInputAuthority 조건 때문에 호스트 로컬 플레이어가 잘못 분기됐다.
        var isRemote = Runner != null && (!HasInputAuthority || !HasStateAuthority);
        driver.SetRemoteProxy(isRemote);

        // 호스트 로컬 플레이어는 입력을 즉시 반영하되, locomotion만 네트워크 기반으로 처리한다.
        var isLocalWithoutAuth = Runner != null && HasInputAuthority && !HasStateAuthority;
        driver.SetLocalWithoutAuthority(isLocalWithoutAuth);
    }

    /// <summary>
    /// 다른 플레이어의 HandGrabHandler가 이 캐릭터를 잡았을 때 호출.
    /// 코어 joint spring을 높여 형태를 유지하고, 팔다리 spring을 낮춰 느슨하게 만든다.
    /// </summary>
    public void OnGrabbed()
    {
        _grabbedByCount++;
        // 기절 중(래그돌)이면 spring 조정 불필요 — 이미 느슨한 상태
        if (_grabbedByCount == 1 && _isActiveRagdoll)
            ApplyGrabbedJointState(true);

        TraceCarryDebugSample("OnGrabbed", $"grabbedByCount={_grabbedByCount}", true);
    }

    /// <summary>
    /// 다른 플레이어의 HandGrabHandler가 이 캐릭터를 놓았을 때 호출.
    /// </summary>
    public void OnReleased()
    {
        _grabbedByCount = Mathf.Max(0, _grabbedByCount - 1);
        if (_grabbedByCount == 0)
            ApplyGrabbedJointState(false);

        TraceCarryDebugSample("OnReleased", $"grabbedByCount={_grabbedByCount}", true);
    }

    // 잡힌 상태에서 원래 spring 복원을 위해 캐싱
    private (ConfigurableJoint joint, float originalSpring)[] _grabbedJointCache;

    private void ApplyGrabbedJointState(bool grabbed)
    {
        // SyncPhysicsObject 경로 (비-Puppet 모드)
        if (!ShouldDisablePhysicsAnimationSync && syncPhysicsObjects != null)
        {
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
            {
                if (syncPhysicsObjects[i] == null) continue;

                if (grabbed)
                    syncPhysicsObjects[i].MakeGrabbed();
                else
                    syncPhysicsObjects[i].MakeUnGrabbed();
            }
            return;
        }

        // PuppetMaster 경로: PuppetMaster 하위 ConfigurableJoint를 직접 조정
        if (grabbed)
        {
            var joints = GetComponentsInChildren<ConfigurableJoint>(true);
            var cache = new System.Collections.Generic.List<(ConfigurableJoint, float)>();

            foreach (var cj in joints)
            {
                // 루트의 mainJoint과 그랩용으로 동적 생성된 joint는 제외
                if (cj == mainJoint) continue;
                if (cj.gameObject == gameObject) continue;

                float originalSpring = cj.slerpDrive.positionSpring;
                float multiplier = ClassifyBodyPart(cj.gameObject.name);

                var drive = cj.slerpDrive;
                drive.positionSpring = originalSpring * multiplier;
                cj.slerpDrive = drive;

                cache.Add((cj, originalSpring));
            }
            _grabbedJointCache = cache.ToArray();
        }
        else if (_grabbedJointCache != null)
        {
            foreach (var (cj, originalSpring) in _grabbedJointCache)
            {
                if (cj == null) continue;
                var drive = cj.slerpDrive;
                drive.positionSpring = originalSpring;
                cj.slerpDrive = drive;
            }
            _grabbedJointCache = null;
        }
    }

    private static float ClassifyBodyPart(string name)
    {
        var cs = CombatSettings.Instance;
        float coreM = cs != null ? cs.grabbedCoreSpringMultiplier : 2.5f;
        float headM = cs != null ? cs.grabbedHeadSpringMultiplier : 1.5f;
        float limbM = cs != null ? cs.grabbedLimbSpringMultiplier : 0.3f;

        var lower = name.ToLowerInvariant();
        // Core: 형태 유지
        if (lower.Contains("hip") || lower.Contains("spine") || lower.Contains("waist") || lower.Contains("chest"))
            return coreM;
        // Head: 약간 강화
        if (lower.Contains("head"))
            return headM;
        // Limb: 느슨
        return limbM;
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

        // 카메라 Follow 앵커를 만들고, transform이 급변해도 카메라가 부드럽게 따라가게 한다.
        // 카메라는 루트 대신 앵커를 추적한다.
        if (isLocalOwner)
            EnsureCameraFollowAnchor();

        foreach (var cameraRig in _ownedCameraRigsCache)
        {
            if (cameraRig == null)
                continue;

            cameraRig.enabled = isLocalOwner;
            if (isLocalOwner)
                cameraRig.SetTarget(_cameraFollowAnchor != null ? _cameraFollowAnchor : transform);
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

        // 로컬 오너의 메인 Rigidbody에는 보간(interpolation)을 활성화한다.
        // AddForce 기반 이동과 LateUpdate 카메라 추적 사이의 프레임 차이로 인한 떨림을 줄인다.
        // 원격 프록시는 kinematic 기반이라 별도 보간을 강제하지 않는다.
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

    // 카메라 Follow 앵커

    private Vector3 _diagPrevRootPos;
    private Vector3 _diagPrevAnchorPos;
    private Vector3 _diagPrevPresentationPos;
    private bool _diagCameraDeltaInitialized;

    /// <summary>
    /// 카메라 Follow용 앵커를 월드 공간에 생성한다.
    /// 캐릭터 계층 바깥에 두어 SyncRootToPhysicsBody의 급격한 위치 변화가
    /// 카메라에 직접 전달되지 않도록 한다.
    /// </summary>
    private void EnsureCameraFollowAnchor()
    {
        if (_cameraFollowAnchor != null) return;
        var go = new GameObject("_CameraFollowAnchor");
        var initialPosition = ResolveCameraAnchorDesiredPosition(includeOwnerProxyBias: false);
        go.transform.position = initialPosition;
        _cameraFollowAnchor = go.transform;

        var context = go.AddComponent<SSAFYPlayTime.Character.CameraFollowAnchorContext>();
        context.SetOwner(this);
    }

    /// <summary>
    /// 카메라 앵커를 캐릭터 위치에 맞춰 부드럽게 추적한다.
    /// 정상 상태에서는 빠르게 따라가고, 큰 이동에도 카메라가 튀지 않게 한다.
    /// 기절/물리 상태에서는 더 완만하게 따라가 SyncRootToPhysicsBody의 급변을 흡수한다.
    /// </summary>
    internal void UpdateCameraFollowAnchor()
    {
        if (_cameraFollowAnchor == null) return;

        // 1f - Exp(-speed * dt): 프레임레이트 독립적인 지수 감쇠 보간
        // Time.deltaTime * speed 방식보다 FPS 차이에 따른 반응속도 변화를 줄인다.
        var desiredPosition = ResolveCameraAnchorDesiredPosition(includeOwnerProxyBias: true);
        var settings = ResolveCameraAnchorFollowSettings();

        var snapDistance = settings.snapDistance;
        if ((_cameraFollowAnchor.position - desiredPosition).sqrMagnitude > snapDistance * snapDistance)
        {
            _cameraFollowAnchor.position = desiredPosition;
            return;
        }

        var followSpeed = Mathf.Max(0.01f, settings.planarFollowSpeed);
        var planarAlpha = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        var verticalAlpha = 1f - Mathf.Exp(-(followSpeed * Mathf.Max(0.01f, settings.verticalFollowMultiplier)) * Time.deltaTime);

        var nextPosition = _cameraFollowAnchor.position;
        nextPosition.x = Mathf.Lerp(nextPosition.x, desiredPosition.x, planarAlpha);
        nextPosition.z = Mathf.Lerp(nextPosition.z, desiredPosition.z, planarAlpha);
        nextPosition.y = Mathf.Lerp(nextPosition.y, desiredPosition.y, verticalAlpha);

        _cameraFollowAnchor.position = nextPosition;
    }

    internal void TraceCameraDeltaDiagnostics()
    {
        if (!enableProxyAnimationDiagnostics || !Application.isPlaying)
            return;
        if (!(Debug.isDebugBuild || Application.isEditor))
            return;
        if (Runner == null || Object == null || !Object.IsValid || !HasInputAuthority)
            return;

        var rootPos = transform.position;
        var anchorPos = _cameraFollowAnchor != null ? _cameraFollowAnchor.position : rootPos;
        var presentationRoot = GetPresentationRootTransform();
        var presentationPos = presentationRoot != null ? presentationRoot.position : rootPos;

        if (!_diagCameraDeltaInitialized)
        {
            _diagPrevRootPos = rootPos;
            _diagPrevAnchorPos = anchorPos;
            _diagPrevPresentationPos = presentationPos;
            _diagCameraDeltaInitialized = true;
            return;
        }

        var rootDelta = (rootPos - _diagPrevRootPos).magnitude;
        var anchorDelta = (anchorPos - _diagPrevAnchorPos).magnitude;
        var presentationDelta = (presentationPos - _diagPrevPresentationPos).magnitude;

        _diagPrevRootPos = rootPos;
        _diagPrevAnchorPos = anchorPos;
        _diagPrevPresentationPos = presentationPos;

        if (rootDelta < 0.01f && anchorDelta < 0.01f && presentationDelta < 0.01f)
            return;

        var rootAnchorGap = (rootPos - anchorPos).magnitude;
        var presentationAnchorGap = (presentationPos - anchorPos).magnitude;
        var anchorSource = ResolveCameraAnchorSource();
        var anchorSourceName = anchorSource != null ? $"{anchorSource.name}/{_cameraAnchorSourceMode}" : "PresentationOffset";
        Debug.Log(
            $"[CamDeltaDiag] name={name} auth={HasStateAuthority} " +
            $"rootDelta={rootDelta:F4} anchorDelta={anchorDelta:F4} presentationDelta={presentationDelta:F4} " +
            $"rootToAnchor={rootAnchorGap:F4} presentationToAnchor={presentationAnchorGap:F4} " +
            $"anchorSource={anchorSourceName} phase={GetPhysicalPhase()} dt={Time.deltaTime:F4}",
            this);
    }

}
