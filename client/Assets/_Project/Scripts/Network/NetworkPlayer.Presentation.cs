using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    // EP2 PuppetMaster 캐릭터용 애니메이션 상태 이름
    private const string PM_IdleState = "Idle01";
    private const string PM_WalkState = "WalkFWD";
    private const string PM_SprintState = "SprintFWD";
    private const string PM_PunchState = "Punch";
    private const string PM_PunchLeftState = "PunchLeft";
    private const string PM_PunchRightState = "PunchRight";
    private const string PM_ThrowState = "Throw";
    private const float PM_LocomotionThreshold = 0.1f;
    private const float PM_DefaultPunchPredictionWindow = 0.35f;
    private const float PM_ThrowLockDuration = 0.85f;
    private const float OwnerRecoveringHipsLerpScale = 0.35f;
    private const float OwnerRecoveringHipsDeadzone = 0.12f;
    private const float OwnerUnstableHipsLerpScale = 0.55f;
    private const float OwnerUnstableHipsDeadzone = 0.08f;
    private const float OwnerCarryHipsLerpScale = 1.25f;
    private const float OwnerCarryHipsDeadzone = 0.02f;
    private const float OwnerRecoveringBoneRotationLerpScale = 0.3f;
    private const float OwnerUnstableBoneRotationLerpScale = 0.55f;
    private const float OwnerCarryBoneRotationLerpScale = 1.1f;
    private const float CarryHipsImmediateSnapDistance = 0.85f;
    private const float CarryPresentationTraceGapThreshold = 0.3f;
    private const float CarryProxyRootFollowSpeed = 20f;
    private const float CarryProxyRootSnapDistance = 1.10f;
    private const float CarryResidualRootGapThreshold = 0.90f;
    private const float CarryRootDebugGapThreshold = 1.35f;
    private const float RemoteStablePresentationRootFollowSpeed = 10f;
    private const float RemoteBufferedPresentationRootFollowSpeed = 7f;
    private const float OwnerBufferedPresentationRootFollowSpeed = 9f;
    private const float ProxyPresentationRootSnapDistance = 2.75f;
    private bool _pmNextAttackLeft;

    // OwnerProxy 로컬 예측 reconcile
    private float _localPunchPredictionTime = -1f;
    private float _localThrowPredictionTime = -1f;
    private bool _localPredictedPunchIsLeft; // 로컬 예측 시 어느 손을 재생했는지

    /// <summary>
    /// PartyMonsterAnimationDriver가 로컬 예측 펀치 시 호출.
    /// 예측 타임스탬프와 방향을 기록하여 네트워크 reconcile 시 비교한다.
    /// </summary>
    internal void NotifyLocalPunchPrediction(bool isLeft)
    {
        _localPunchPredictionTime = Time.time;
        _localPredictedPunchIsLeft = isLeft;
    }

    internal void NotifyLocalThrowPrediction()
    {
        _localThrowPredictionTime = Time.time;
    }

    // ─── 스냅샷 보간 버퍼 ───
    // 이전(from) / 현재(to) 두 틱의 뼈 회전·힙 위치를 보관하고,
    // 렌더 프레임에서 Alpha로 보간한다 (latest 추종이 아닌 정식 snapshot interpolation).
    private Quaternion[] _boneSnapshotFrom;
    private Quaternion[] _boneSnapshotTo;
    private Vector3 _hipsSnapshotFrom;
    private Vector3 _hipsSnapshotTo;
    private bool _snapshotBufferInitialized;
    private bool _proxyPresentationRootSmoothingActive;

    // CarrySolveFrame: carry 진입/종료 시 snapshot 재시드용
    private bool _wasCarryPhaseLastFrame;
    private Vector3 _carryExitSnapshotAnchor;

    // PuppetMaster 애니메이션 모드 런타임 상태
    private bool _usePuppetMasterAnimation;
    private bool _hasExternalAnimationDriver; // PartyMonsterAnimationDriver가 존재하면 true
    private PartyMonsterAnimationDriver _externalAnimationDriver; // 캐시된 드라이버 참조
    private bool _pmHasMovementSpeedParam;
    private string _pmCurrentStateName;
    private float _pmActionLockedUntil;

    public override void Render()
    {
        UpdateRemotePhysicsPresentationResetWindow();
        UpdateAnimationParameters();
        ApplyReplicatedAnimationEvent();
        ApplyReplicatedKnockoutConfirm();

        if (Object == null || !Object.IsValid)
            return;

        // ── 플레이어 타입별 3분기 ──
        if (HasStateAuthority)
        {
            // AuthorityOwner: 물리 시뮬레이션이 직접 뼈를 구동 → 보간 불필요
            UpdateCharacterPresentationEffects();
            return;
        }

        if (HasInputAuthority)
        {
            // OwnerProxy: 상태 동기화는 항상 받되, 뼈 보간은 confirmed ragdoll일 때만
            SyncConfirmedOwnerState();
            // grab/carry 애니메이터 파라미터를 호스트 확정 상태에서 동기화
            // (UpdateGrabbingAnimatorFlag는 StateAuthority에서만 실행되므로)
            SyncGrabbingAnimatorFromNetwork();
        }
        else
        {
            // RemoteProxy: 순수 원격 — 항상 뼈 보간 + 상태 동기화
            SyncRemoteActiveRagdollState();
            if (ShouldUseBufferedProxyPoseInterpolation())
                InterpolateRemoteBoneRotations();
            // grab/carry 애니메이터 파라미터 동기화
            SyncGrabbingAnimatorFromNetwork();
        }

        UpdateProxyPresentationRoot();
        UpdatePhysicsDrivenVisualPose();
        ApplyProxyPresentationRotation();
        UpdateCharacterPresentationEffects();
    }

    /// <summary>
    /// OwnerProxy 전용 — 호스트에서 확정된 상태를 받되, 뼈 보간은 조건부.
    /// 평소: 로컬 애니메이터/PuppetMaster가 비주얼 구동 (뼈 보간 OFF)
    /// 기절/잡힘: 호스트 물리 결과를 따라야 하므로 뼈 보간 ON
    /// </summary>
    private void SyncConfirmedOwnerState()
    {
        // 1) 액티브 래그돌 상태 전환은 항상 수신 (기절/회복)
        SyncRemoteActiveRagdollState();

        // 2) 내가 기절(ragdoll) 또는 잡힌 상태일 때만 뼈 보간 적용
        //    → 호스트가 물리로 끌고 있는 결과를 따라가야 하므로
        bool isInConfirmedRagdoll = ShouldUseBufferedProxyPoseInterpolation();
        if (isInConfirmedRagdoll)
            InterpolateRemoteBoneRotations();
    }

    /// <summary>
    /// 원격 클라이언트에서 호스트의 IsActiveRagdoll 상태를 로컬에 반영.
    /// 기절(false→래그돌) / 회복(true→액티브 래그돌) 전환 시
    /// SyncPhysicsObject의 관절 스프링도 실제로 전환한다.
    /// </summary>
    private void SyncRemoteActiveRagdollState()
    {
        bool networkedActive = NetworkedIsActiveRagdoll;
        if (_isActiveRagdoll == networkedActive)
            return;

        bool wasStunned = !_isActiveRagdoll;   // 이전 상태
        bool isRecovering = networkedActive;    // 새 상태

        _isActiveRagdoll = networkedActive;

        // SyncPhysicsObject 관절 스프링 전환 (원격 프록시도 관절 상태를 맞춰야 뼈 회전 보간이 자연스럽다)
        if (syncPhysicsObjects != null)
        {
            for (int i = 0; i < syncPhysicsObjects.Length; i++)
            {
                if (syncPhysicsObjects[i] == null) continue;
                if (isRecovering)
                    syncPhysicsObjects[i].MakeActiveRagdoll();
                else
                    syncPhysicsObjects[i].MakeRagdoll();
            }
        }

        // BodyPartPhysicsManager 상태 전환

        // 비호스트 비주얼 모드 동기화
        if (!isRecovering)
        {
            // 기절 진입: 래그돌(물리) 메시 표시
            SetStunVisualMode(true);
        }
        else if (GetStunPresentationPhase() != StunPresentationPhase.RecoverStabilizing)
        {
            // 안정화 단계가 아닌 회복(완전 회복): 애니메이션 메시 복원
            // RecoverStabilizing 중에는 SynchronizePhysicsPresentationState()가
            // 단계 종료를 감지하여 자동으로 복원하므로 여기서 호출하지 않는다.
            // (조기 복원 시 안정화 0.4초 동안 캐릭터가 Idle 포즈로 보이는 버그 발생)
            SetStunVisualMode(false);
        }

        // 로컬 플레이어(OwnerProxy)가 기절 진입 시 슬로우모션 연출
        if (!isRecovering && HasInputAuthority)
            TriggerStunSlowMotion();

        ArmStunForceDiagnostics(
            "SyncRemoteActiveRagdollState",
            $"isRecovering={isRecovering} netRagdoll={networkedActive}");

    }

    private void LateUpdate()
    {
        // 비주얼 상태를 먼저 갱신한 뒤 카메라 앵커를 갱신해야
        // 앵커가 최종 표시 비주얼 위치를 기준으로 추적한다.
        UpdateRemotePhysicsPresentationResetWindow();
        UpdatePhysicsDrivenVisualPose();
        UpdateProxyPresentationRoot();

        if (Runner == null)
            UpdateAnimationParameters();

        UpdateCharacterPresentationEffects();

        // 비주얼 갱신 완료 후 카메라 앵커 갱신 — CameraRig.LateUpdate에서 읽는다.
        UpdateCameraFollowAnchor();

        TraceCameraDeltaDiagnostics();
        TraceMoveProxyState("LateUpdate");

        // 기절 슬로우모션 timeScale 복원 틱 (로컬 플레이어만)
        TickKnockoutConfirmSlowMotion();
        TickStunSlowMotion();
        UpdateMoveSyncDiagnosticsHotkey();
        UpdateStunForceDiagnosticsHotkey();
    }

    private static bool IsCarryPhysicalPhase(PhysicalPhase phase)
    {
        return phase == PhysicalPhase.BeingCarriedStunned ||
               phase == PhysicalPhase.CarryingStunned;
    }

    private static bool UsesBufferedProxyPosePhase(PhysicalPhase phase)
    {
        return phase == PhysicalPhase.Holding ||
               phase == PhysicalPhase.GrabIntent ||
               phase == PhysicalPhase.Recovering ||
               phase == PhysicalPhase.CarryingStunned ||
               phase == PhysicalPhase.BeingCarriedStunned ||  // 운반 당하는 쪽도 포함
               UsesPhysicsPosePresentation(phase);
    }

    private bool ShouldUseBufferedProxyPoseInterpolation()
    {
        return GetStunPresentationPhase() == StunPresentationPhase.RecoverStabilizing ||
               UsesBufferedProxyPosePhase(GetPhysicalPhase()) ||
               IsRemotePhysicsPresentationResetLocked();
    }

    private bool ShouldSmoothProxyPresentationRoot(Transform presentationRoot)
    {
        if (presentationRoot == null || presentationRoot == transform)
            return false;

        if (HasStateAuthority || ShouldUseHardPhysicsVisualMode())
            return false;

        if (!HasInputAuthority)
            return true;

        return UsesBufferedProxyPosePhase(GetPhysicalPhase());
    }

    private float ResolveProxyPresentationRootFollowSpeed()
    {
        if (!HasInputAuthority)
        {
            return ShouldUseBufferedProxyPoseInterpolation()
                ? RemoteBufferedPresentationRootFollowSpeed
                : RemoteStablePresentationRootFollowSpeed;
        }

        return OwnerBufferedPresentationRootFollowSpeed;
    }

    private void UpdateProxyPresentationRoot()
    {
        var presentationRoot = GetPresentationRootTransform();
        if (presentationRoot == null || presentationRoot == transform)
            return;

        var targetPosition = transform.position;

        // BeingCarriedStunned: 운반 중 presentation root를 transform.position에 즉시 동기화.
        // 클라이언트의 transform.position은 Fusion 네트워크 동기화(호스트 기준) carry 위치.
        // SyncCarriedRootToPhysicsBody()는 StateAuthority(호스트)에서만 실행되므로
        // 클라이언트에서는 Fusion이 전달한 carry 위치가 그대로 유지된다.
        if (!HasStateAuthority && GetPhysicalPhase() == PhysicalPhase.BeingCarriedStunned)
        {
            presentationRoot.position = targetPosition;
            _proxyPresentationRootSmoothingActive = false;
            return;
        }

        if (!ShouldSmoothProxyPresentationRoot(presentationRoot))
        {
            // 물리 포즈 페이즈(BeingGrabbed/Dragged 등)에서는 ShouldUseHardPhysicsVisualMode()=true로
            // smoothing이 꺼지지만, presentationRoot는 여전히 갱신되어야 한다.
            // 그렇지 않으면 _proxyPresentationRootSmoothingActive=false인 상태에서
            // 아무것도 하지 않아 비주얼 메시 루트가 그랩 이전 위치에 고정된다.
            if (UsesPhysicsPosePresentation(GetPhysicalPhase()) && !HasStateAuthority)
            {
                presentationRoot.position = targetPosition;
                _proxyPresentationRootSmoothingActive = false;
                return;
            }

            if (_proxyPresentationRootSmoothingActive &&
                (presentationRoot.position - targetPosition).sqrMagnitude > 0.0001f)
            {
                presentationRoot.position = targetPosition;
            }

            _proxyPresentationRootSmoothingActive = false;
            return;
        }

        if (!_proxyPresentationRootSmoothingActive ||
            (presentationRoot.position - targetPosition).sqrMagnitude >
            ProxyPresentationRootSnapDistance * ProxyPresentationRootSnapDistance)
        {
            presentationRoot.position = targetPosition;
            _proxyPresentationRootSmoothingActive = true;
            return;
        }

        var alpha = 1f - Mathf.Exp(-ResolveProxyPresentationRootFollowSpeed() * Time.deltaTime);
        presentationRoot.position = Vector3.Lerp(presentationRoot.position, targetPosition, alpha);
        _proxyPresentationRootSmoothingActive = true;
    }

    private bool TryApplyCarryProxyRootCorrection(
        Vector3 carryRootTarget,
        SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode carryMode,
        float slowMoAlphaScale,
        out Vector3 rootBefore,
        out Vector3 rootAfter,
        out float gapBefore,
        out float gapAfter,
        out bool didSnap,
        bool isSettling = false)
    {
        rootBefore = transform.position;
        rootAfter = rootBefore;
        gapBefore = Vector3.Distance(rootBefore, carryRootTarget);
        gapAfter = gapBefore;
        didSnap = false;

        if (HasStateAuthority || gapBefore <= 0.0001f)
            return false;

        // CarrySolveFrame: CarryPhysicsProfile에서 proxy 설정값 가져오기
        var proxyFollowSpeed = CarryProxyRootFollowSpeed;
        var proxySnapDistance = CarryProxyRootSnapDistance;
        if (carryPhysicsProfile != null)
        {
            var settings = carryPhysicsProfile.GetSettings(carryMode);
            proxyFollowSpeed = settings.proxyRootFollowSpeed;
            proxySnapDistance = settings.proxyRootSnapDistance;
        }

        if (gapBefore >= proxySnapDistance)
        {
            rootAfter = carryRootTarget;
            didSnap = true;
        }
        else
        {
            var step = Mathf.Max(0.10f, proxyFollowSpeed * Time.deltaTime * Mathf.Max(slowMoAlphaScale, 0.35f));
            rootAfter = Vector3.MoveTowards(rootBefore, carryRootTarget, step);
        }

        ApplyProxyCarryRootPosition(rootAfter, isSettling);
        gapAfter = Vector3.Distance(rootAfter, carryRootTarget);
        return (rootAfter - rootBefore).sqrMagnitude > 0.000001f;
    }

    private void ApplyProxyCarryRootPosition(Vector3 nextRootPosition, bool isSettling = false)
    {
        // settle 중에는 rigidbody position을 건드리지 않음 — 물리 velocity 기반 이동 보존
        if (!isSettling && !HasStateAuthority && rigidbody3D != null && !rigidbody3D.isKinematic)
            rigidbody3D.position = nextRootPosition;

        transform.position = nextRootPosition;
    }

    private bool TryResolveProxyCarryTargets(
        PhysicalPhase phase,
        Vector3 desiredHipsPosition,
        out Vector3 carryAnchorTarget,
        out Vector3 carryRootTarget)
    {
        carryAnchorTarget = desiredHipsPosition;
        carryRootTarget = desiredHipsPosition;

        if (phase == PhysicalPhase.BeingCarriedStunned)
        {
            if (!(bool)NetworkedVictimAnchorValid)
                return false;

            carryAnchorTarget = NetworkedVictimAnchorPosition;
            carryRootTarget = carryAnchorTarget;
            if ((bool)NetworkedVictimRootOffsetValid)
                carryRootTarget += NetworkedVictimRootOffset;

            return true;
        }

        if (phase == PhysicalPhase.CarryingStunned)
            return true;

        return false;
    }

    private void InterpolateRemoteBoneRotations()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            return;

        var interpolator = new NetworkBehaviourBufferInterpolator(this);
        int boneCount = syncPhysicsObjects.Length;
        var phase = GetPhysicalPhase();
        var isCarryPhase = IsCarryPhysicalPhase(phase);

        // ── 스냅샷 버퍼 초기화 ──
        if (!_snapshotBufferInitialized || _boneSnapshotFrom == null || _boneSnapshotFrom.Length != boneCount)
        {
            _boneSnapshotFrom = new Quaternion[boneCount];
            _boneSnapshotTo = new Quaternion[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                var rot = BoneRotations.Get(i);
                _boneSnapshotFrom[i] = rot;
                _boneSnapshotTo[i] = rot;
            }
            _hipsSnapshotFrom = NetworkedHipsPosition;
            _hipsSnapshotTo = NetworkedHipsPosition;
            _snapshotBufferInitialized = true;
        }

        // ── 네트워크 상태가 바뀌었으면 스냅샷 시프트: to → from, latest → to ──
        bool changed = false;
        var latestHips = NetworkedHipsPosition;
        if (latestHips != _hipsSnapshotTo)
            changed = true;

        if (!changed)
        {
            for (int i = 0; i < boneCount; i++)
            {
                if (BoneRotations.Get(i) != _boneSnapshotTo[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            _hipsSnapshotFrom = _hipsSnapshotTo;
            _hipsSnapshotTo = latestHips;
            System.Array.Copy(_boneSnapshotTo, _boneSnapshotFrom, boneCount);
            for (int i = 0; i < boneCount; i++)
                _boneSnapshotTo[i] = BoneRotations.Get(i);
        }

        // ── CarrySolveFrame: carry 진입/종료 시 snapshot 재시드 ──
        {
            var isCarryNow = isCarryPhase;
            var currentCarryMode = GetLocalCarryMode();
            if (isCarryNow && currentCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
                _lastObservedCarryMode = currentCarryMode;
            if (isCarryNow && !_wasCarryPhaseLastFrame)
            {
                // BeingCarriedStunned 클라이언트: 로컬 muscle 위치(기절 진입 위치)를 hipsFrom으로 쓰면
                // Fusion Alpha가 0→1을 순환할 때마다 hips가 기절위치↔carry위치를 진동해
                // 캐릭터·카메라가 원래 위치로 돌아가는 버그 발생.
                // 피운반자(victim) 클라이언트에서는 네트워크 carry 위치를 즉시 기준으로 삼는다.
                bool victimCarryOnClient = !HasStateAuthority && phase == PhysicalPhase.BeingCarriedStunned;
                _hipsSnapshotFrom = victimCarryOnClient
                    ? latestHips
                    : (syncPhysicsObjects[0] != null ? syncPhysicsObjects[0].transform.position : latestHips);
                _hipsSnapshotTo = latestHips;

                if (!HasStateAuthority &&
                    TryResolveProxyCarryTargets(phase, latestHips, out _, out var carryRootTarget))
                {
                    _lastCarryAnchorPosition = carryRootTarget;
                }
            }
            else if (isCarryNow)
            {
                if (!HasStateAuthority)
                {
                    if (TryResolveProxyCarryTargets(phase, latestHips, out _, out var carryRootTarget))
                        _lastCarryAnchorPosition = carryRootTarget;
                    else
                        _lastCarryAnchorPosition = latestHips;
                }
            }
            else if (!isCarryNow && _wasCarryPhaseLastFrame)
            {
                _carryExitSnapshotAnchor = _lastCarryAnchorPosition != Vector3.zero
                    ? _lastCarryAnchorPosition
                    : transform.position;
                // carry 종료 직후: 로컬 물리(stun entry) 대신 마지막 carry 위치를 from으로.
                // 로컬 물리 위치를 쓰면 carry 종료 후에도 진동 버그가 동일하게 재발.
                _hipsSnapshotFrom = _lastCarryAnchorPosition != Vector3.zero
                    ? _lastCarryAnchorPosition
                    : latestHips;
                _hipsSnapshotTo = latestHips;

                if (!HasStateAuthority)
                {
                    var settleMode = _lastObservedCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None
                        ? _lastObservedCarryMode
                        : SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry;
                    var settleProfile = carryPhysicsProfile != null
                        ? carryPhysicsProfile.GetSettings(settleMode)
                        : new SSAFYPlayTime.Character.CarryPhysicsProfile.CarryModeSettings { carryReleaseSettleDuration = 0.15f };
                    _carryReleaseSettleRemaining = settleProfile.carryReleaseSettleDuration;
                }
            }
            _wasCarryPhaseLastFrame = isCarryNow;
        }

        // ── 슬로우모션 보간 스케일 ──
        // 비호스트에서 Time.timeScale은 Fusion 보간 alpha에 영향을 주지 않으므로
        // 슬로우모션 중에는 alpha를 직접 스케일해서 뼈 움직임도 느리게 보이게 한다.
        var slowMoAlphaScale = _stunSlowMotionActive ? Mathf.Max(Time.timeScale, 0.05f) : 1f;

        // ── Hips(muscles[0]) 절대 위치 — from→to 스냅샷 보간 ──
        if (boneCount > 0 && syncPhysicsObjects[0] != null)
        {
            var hipsFrom = _hipsSnapshotFrom;
            var hipsTo = _hipsSnapshotTo;
            var hipsCurrent = syncPhysicsObjects[0].transform.position;
            var deadzone = ResolveOwnerProxyHipsDeadzone();
            var snapSqrDistance = isCarryPhase
                ? CarryHipsImmediateSnapDistance * CarryHipsImmediateSnapDistance
                : 15f;
            var hipsAlpha = 1f;
            var didHipsSnap = false;
            var desiredHipsPosition = hipsCurrent;

            // 텔레포트 방지: 거리가 너무 크면 즉시 스냅 (HFF 방식, sqrMag > 15)
            if ((hipsTo - hipsCurrent).sqrMagnitude > snapSqrDistance)
            {
                desiredHipsPosition = hipsTo;
                // 스냅 시 버퍼도 리셋
                _hipsSnapshotFrom = hipsTo;
                didHipsSnap = true;
            }
            else
            {
                hipsAlpha = ResolveHipsInterpolationAlpha(interpolator.Alpha) * slowMoAlphaScale;
                var interpolatedHips = Vector3.Lerp(hipsFrom, hipsTo, hipsAlpha);

                if (deadzone > 0f && (interpolatedHips - hipsCurrent).sqrMagnitude <= deadzone * deadzone)
                    desiredHipsPosition = hipsCurrent;
                else
                    desiredHipsPosition = interpolatedHips;
            }

            var rootBeforeCorrection = transform.position;
            var rootAfterCorrection = rootBeforeCorrection;
            var rootGapBeforeCorrection = Vector3.Distance(rootBeforeCorrection, desiredHipsPosition);
            var rootGapAfterCorrection = rootGapBeforeCorrection;
            var didRootSnap = false;
            var didApplyRootCorrection = false;
            var proxyCarryAnchor = desiredHipsPosition;
            var proxyCarryRootTarget = desiredHipsPosition;
            if (isCarryPhase)
            {
                var activeCarryMode = GetLocalCarryMode();
                var carryModeForProxy = activeCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None
                    ? activeCarryMode
                    : _lastObservedCarryMode;
                if (!TryResolveProxyCarryTargets(phase, desiredHipsPosition, out proxyCarryAnchor, out proxyCarryRootTarget))
                {
                    proxyCarryAnchor = desiredHipsPosition;
                    proxyCarryRootTarget = desiredHipsPosition;
                }

                didApplyRootCorrection = TryApplyCarryProxyRootCorrection(
                    proxyCarryRootTarget,
                    carryModeForProxy,
                    slowMoAlphaScale,
                    out rootBeforeCorrection,
                    out rootAfterCorrection,
                    out rootGapBeforeCorrection,
                    out rootGapAfterCorrection,
                    out didRootSnap);

                if (!HasStateAuthority)
                    _lastCarryAnchorPosition = proxyCarryRootTarget;
            }
            else if (!HasStateAuthority && _carryReleaseSettleRemaining > 0f)
            {
                _carryReleaseSettleRemaining = Mathf.Max(0f, _carryReleaseSettleRemaining - Time.deltaTime);
                var settleMode = _lastObservedCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None
                    ? _lastObservedCarryMode
                    : SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.StunnedSingleCarry;

                didApplyRootCorrection = TryApplyCarryProxyRootCorrection(
                    desiredHipsPosition,
                    settleMode,
                    slowMoAlphaScale,
                    out rootBeforeCorrection,
                    out rootAfterCorrection,
                    out rootGapBeforeCorrection,
                    out rootGapAfterCorrection,
                    out didRootSnap,
                    isSettling: true);

                if (_carryReleaseSettleRemaining <= 0f)
                    _carryExitSnapshotAnchor = Vector3.zero;
            }

            syncPhysicsObjects[0].transform.position = desiredHipsPosition;

            // carry anchor 정보 미수신 등으로 TryApplyCarryProxyRootCorrection()이
            // 실패했을 때 hips 위치를 root로 사용하는 최소 fallback.
            if (isCarryPhase && !HasStateAuthority && !didApplyRootCorrection)
            {
                ApplyProxyCarryRootPosition(desiredHipsPosition, isSettling: true);
            }

            var appliedHips = syncPhysicsObjects[0].transform.position;
            var rootGap = Vector3.Distance(appliedHips, transform.position);
            if (isCarryPhase)
            {
                if (rootGap > CarryResidualRootGapThreshold || rootGapBeforeCorrection > CarryResidualRootGapThreshold)
                {
                    TraceCarryDebugSample(
                        "CarryProxyRootCorrection",
                        $"phase={phase} carryAnchor={FormatCarryDebugVector(proxyCarryAnchor)} rootTarget={FormatCarryDebugVector(proxyCarryRootTarget)} hipsCurrent={FormatCarryDebugVector(hipsCurrent)} " +
                        $"hipsTarget={FormatCarryDebugVector(hipsTo)} hipsApplied={FormatCarryDebugVector(appliedHips)} " +
                        $"rootBefore={FormatCarryDebugVector(rootBeforeCorrection)} rootAfter={FormatCarryDebugVector(rootAfterCorrection)} " +
                        $"gapBefore={rootGapBeforeCorrection:F2} gapAfter={rootGapAfterCorrection:F2} residualGap={rootGap:F2} " +
                        $"hipsAlpha={hipsAlpha:F2} deadzone={deadzone:F2} hipsSnap={didHipsSnap} rootSnap={didRootSnap} rootMoved={didApplyRootCorrection}",
                        rootGap > CarryRootDebugGapThreshold);
                }
            }
            else if (!HasStateAuthority && _carryReleaseSettleRemaining > 0f && rootGap > CarryPresentationTraceGapThreshold)
            {
                TraceCarryDebugSample(
                    "CarryProxyReleaseSettle",
                    $"phase={phase} carryExitAnchor={FormatCarryDebugVector(_carryExitSnapshotAnchor)} hipsCurrent={FormatCarryDebugVector(hipsCurrent)} " +
                    $"hipsTarget={FormatCarryDebugVector(hipsTo)} hipsApplied={FormatCarryDebugVector(appliedHips)} " +
                    $"rootBefore={FormatCarryDebugVector(rootBeforeCorrection)} rootAfter={FormatCarryDebugVector(rootAfterCorrection)} " +
                    $"gapBefore={rootGapBeforeCorrection:F2} gapAfter={rootGapAfterCorrection:F2} residualGap={rootGap:F2} " +
                    $"remaining={_carryReleaseSettleRemaining:F2} hipsAlpha={hipsAlpha:F2} rootSnap={didRootSnap} rootMoved={didApplyRootCorrection}");
            }
            else if (rootGap > CarryPresentationTraceGapThreshold)
            {
                TraceCarryDebugSample(
                    "ProxyHipsInterpolation",
                    $"phase={phase} hipsCurrent={FormatCarryDebugVector(hipsCurrent)} hipsTarget={FormatCarryDebugVector(hipsTo)} " +
                    $"hipsApplied={FormatCarryDebugVector(appliedHips)} root={FormatCarryDebugVector(transform.position)} " +
                    $"rootGap={rootGap:F2} alpha={hipsAlpha:F2} deadzone={deadzone:F2} snap={didHipsSnap}");
            }

            TraceProxyStunPresentation("InterpolateRemoteBoneRotations", hipsCurrent, hipsTo);
        }

        // ── 뼈 회전 — from→to 스냅샷 보간 ──
        var rotationAlpha = ResolveBoneRotationInterpolationAlpha(interpolator.Alpha) * slowMoAlphaScale;
        for (int i = 0; i < boneCount; i++)
        {
            if (syncPhysicsObjects[i] == null) continue;
            syncPhysicsObjects[i].transform.localRotation =
                Quaternion.Slerp(_boneSnapshotFrom[i], _boneSnapshotTo[i], rotationAlpha);
        }
    }

    private float ResolveHipsInterpolationAlpha(float baseAlpha)
    {
        if (!IsOwnerProxy)
            return baseAlpha;

        return GetPhysicalPhase() switch
        {
            PhysicalPhase.BeingCarriedStunned => Mathf.Clamp01(baseAlpha * OwnerCarryHipsLerpScale),
            PhysicalPhase.CarryingStunned => Mathf.Clamp01(baseAlpha * OwnerCarryHipsLerpScale),
            PhysicalPhase.Recovering => Mathf.Clamp01(baseAlpha * OwnerRecoveringHipsLerpScale),
            PhysicalPhase.Unstable => Mathf.Clamp01(baseAlpha * OwnerUnstableHipsLerpScale),
            _ => baseAlpha
        };
    }

    private float ResolveOwnerProxyHipsDeadzone()
    {
        if (!IsOwnerProxy)
            return 0f;

        return GetPhysicalPhase() switch
        {
            PhysicalPhase.BeingCarriedStunned => OwnerCarryHipsDeadzone,
            PhysicalPhase.CarryingStunned => OwnerCarryHipsDeadzone,
            PhysicalPhase.Recovering => OwnerRecoveringHipsDeadzone,
            PhysicalPhase.Unstable => OwnerUnstableHipsDeadzone,
            _ => 0f
        };
    }

    private float ResolveBoneRotationInterpolationAlpha(float baseAlpha)
    {
        if (!IsOwnerProxy)
            return baseAlpha;

        return GetPhysicalPhase() switch
        {
            PhysicalPhase.BeingCarriedStunned => Mathf.Clamp01(baseAlpha * OwnerCarryBoneRotationLerpScale),
            PhysicalPhase.CarryingStunned => Mathf.Clamp01(baseAlpha * OwnerCarryBoneRotationLerpScale),
            PhysicalPhase.Recovering => Mathf.Clamp01(baseAlpha * OwnerRecoveringBoneRotationLerpScale),
            PhysicalPhase.Unstable => Mathf.Clamp01(baseAlpha * OwnerUnstableBoneRotationLerpScale),
            _ => baseAlpha
        };
    }

    private void UpdateAnimationParameters()
    {
        // PartyMonsterAnimationDriver가 로코모션/전투 애니메이션을 모두 제어하므로 스킵
        if (_hasExternalAnimationDriver)
            return;

        if (animator == null)
            return;

        if (ShouldUseHardPhysicsPresentation())
            return;

        var (speed, state) = ResolveAnimationParameters();

        if (_usePuppetMasterAnimation)
        {
            UpdatePuppetMasterLocomotion(speed);
            return;
        }

        animator.SetFloat(H_MovementSpeed, speed);
        animator.SetInteger(H_MotorState, state);
    }

    private void UpdatePuppetMasterLocomotion(float speed)
    {
        // 액션 잠금 중(펀치/던지기 애니메이션 재생 중)에는 로코모션 전환하지 않음
        if (Time.time < _pmActionLockedUntil)
            return;

        RestorePMPunchSpeed();

        if (_pmHasMovementSpeedParam)
            animator.SetFloat(H_MovementSpeed, speed);

        PlayPMState(ResolvePuppetMasterLocomotionStateName(speed));
    }

    private (float speed, int state) ResolveAnimationParameters()
    {
        if (Runner != null && Object != null && Object.IsValid)
            return (NetworkedMoveSpeed, NetworkedMotorState);

        return (_localMoveSpeed, _localMotorState);
    }

    private string ResolvePuppetMasterLocomotionStateName(float speed)
    {
        PresentationLocomotionState locomotionState;
        if (Runner != null && Object != null && Object.IsValid)
            locomotionState = GetNetworkedLocomotionState();
        else
            locomotionState = ResolveLocomotionState(speed, Input.GetKey(KeyCode.LeftShift));

        return locomotionState switch
        {
            PresentationLocomotionState.Sprint => PM_SprintState,
            PresentationLocomotionState.Walk => PM_WalkState,
            _ => PM_IdleState
        };
    }

    private void ApplyProxyPresentationRotation()
    {
        if (HasStateAuthority || ShouldUseHardPhysicsPresentation())
            return;

        var presentationRoot = GetPresentationRootTransform();
        if (presentationRoot == null)
            return;

        var targetYaw = GetNetworkedVisualYaw();
        if (IsOwnerProxy && TryResolveOwnerProxyPredictedYaw(out var predictedYaw))
            targetYaw = Mathf.LerpAngle(targetYaw, predictedYaw, 0.85f);

        var rotateSpeed = config != null ? config.rotateSpeedDeg : 360f;
        var targetRotation = Quaternion.Euler(0f, targetYaw, 0f);
        presentationRoot.rotation = Quaternion.RotateTowards(
            presentationRoot.rotation,
            targetRotation,
            rotateSpeed * Time.deltaTime);

        SetPresentationVisualYaw(presentationRoot.rotation.eulerAngles.y);
    }

    private bool TryResolveOwnerProxyPredictedYaw(out float yaw)
    {
        yaw = 0f;

        if (!IsOwnerProxy)
            return false;
        if (IsGrabFacingLocked())
            return false;

        var localMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (localMove.sqrMagnitude <= 0.0001f)
            return false;

        var moveDirection = ResolvePresentationMoveDirection(localMove);
        if (moveDirection.sqrMagnitude <= 0.0001f)
            return false;

        var visualDirection = _targetRoot != null
            ? moveDirection
            : new Vector3(moveDirection.x, 0f, moveDirection.z);

        if (visualDirection.sqrMagnitude <= 0.0001f)
            return false;

        yaw = Quaternion.LookRotation(visualDirection.normalized, Vector3.up).eulerAngles.y;
        return true;
    }

    private static Vector3 ResolvePresentationMoveDirection(Vector2 localMove)
    {
        var moveInput = new Vector3(localMove.x, 0f, localMove.y);
        var mainCamera = Camera.main;
        if (mainCamera == null)
            return moveInput;

        var cameraForward = mainCamera.transform.forward;
        var cameraRight = mainCamera.transform.right;
        cameraForward.y = 0f;
        cameraRight.y = 0f;

        if (cameraForward.sqrMagnitude <= 0.0001f || cameraRight.sqrMagnitude <= 0.0001f)
            return moveInput;

        return cameraForward.normalized * moveInput.z + cameraRight.normalized * moveInput.x;
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

        DetectPuppetMasterAnimationMode();
        MarkPresentationEffectsDirty();
    }

    /// <summary>
    /// PuppetMaster 캐릭터의 Animator Controller인지 판별.
    /// EP2 컨트롤러는 "Punch" 트리거가 없고 "Attack01" 상태를 직접 재생하는 방식.
    /// </summary>
    private void DetectPuppetMasterAnimationMode()
    {
        _usePuppetMasterAnimation = false;
        _hasExternalAnimationDriver = false;
        _externalAnimationDriver = null;
        _pmHasMovementSpeedParam = false;

        // PartyMonsterAnimationDriver가 있으면 모든 애니메이션을 해당 드라이버에 위임
        var externalDriver = GetComponent<PartyMonsterAnimationDriver>();
        if (externalDriver != null)
        {
            _hasExternalAnimationDriver = true;
            _externalAnimationDriver = externalDriver;
            return;
        }

        if (_puppetMaster == null || animator == null)
            return;

        // EP2 Animator Controller 감지: "Punch" 트리거가 없으면 PM 모드
        var hasPunchTrigger = false;
        foreach (var param in animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger && param.nameHash == H_Punch)
                hasPunchTrigger = true;
            if (param.type == AnimatorControllerParameterType.Float && param.nameHash == H_MovementSpeed)
                _pmHasMovementSpeedParam = true;
        }

        _usePuppetMasterAnimation = !hasPunchTrigger;
    }

    private void InitializeAnimationEventState()
    {
        if (Runner == null || Object == null || !Object.IsValid)
        {
            _lastConsumedAnimationEventSequence = 0;
            return;
        }

        _lastConsumedAnimationEventSequence = NetworkedAnimationEventSequence;
    }

    private void ApplyReplicatedAnimationEvent()
    {
        if (Runner == null || Object == null || !Object.IsValid)
            return;

        if (!_hasExternalAnimationDriver && animator == null)
            return;

        if (_lastConsumedAnimationEventSequence < 0)
        {
            _lastConsumedAnimationEventSequence = NetworkedAnimationEventSequence;
            return;
        }

        if (NetworkedAnimationEventSequence == _lastConsumedAnimationEventSequence)
            return;

        _lastConsumedAnimationEventSequence = NetworkedAnimationEventSequence;

        var eventType = (AnimationEventType)NetworkedAnimationEventType;

        // 피호스트 로컬 플레이어: 로컬 예측 reconcile.
        // 예측이 최근이고 손이 일치하면 스킵. 손이 다르면 교정 재생.
        // 예측 없거나 만료 → 호스트 확정 이벤트로 강제 재생.
        if (HasInputAuthority && !HasStateAuthority)
        {
            if (eventType == AnimationEventType.Punch || eventType == AnimationEventType.PunchLeft || eventType == AnimationEventType.PunchRight)
            {
                bool withinWindow = Time.time - _localPunchPredictionTime < ResolvePMPunchPredictionWindow();
                if (withinWindow)
                {
                    // 호스트가 결정한 손과 로컬 예측이 같으면 스킵 (이미 맞는 애니메이션 재생 중)
                    bool hostIsLeft = (eventType == AnimationEventType.PunchLeft);
                    if (hostIsLeft == _localPredictedPunchIsLeft)
                        return;
                    // 손이 다르면 → 아래로 진행하여 교정 재생
                }
                // 예측 없음/만료 → 아래로 진행하여 호스트 확정 이벤트 재생
            }
            else if (eventType == AnimationEventType.Throw)
            {
                if (Time.time - _localThrowPredictionTime < PM_ThrowLockDuration)
                    return;
            }
        }

        // 원격 클라이언트에서 GetHit 수신 시 카메라 킥 + 히트 VFX 연출
        if (eventType == AnimationEventType.GetHit && !HasStateAuthority)
        {
            // 비호스트: 정확한 hitPoint가 없으므로 캐릭터 중심 + 전방 오프셋으로 근사
            var approxHitPoint = transform.position + Vector3.up * 0.8f + ResolveCombatForward() * 0.2f;
            var approxDir = -ResolveCombatForward();
            SpawnHitImpactVFX(approxHitPoint, approxDir, FallbackPunchKnockbackForce);
            if (HasInputAuthority)
                TriggerVictimCameraKick(approxDir, FallbackPunchKnockbackForce);
        }

        // 비호스트 로컬 플레이어: StunFall 수신 시 즉시 슬로우모션 발동
        // SyncRemoteActiveRagdollState보다 먼저 실행되므로 가장 빠른 타이밍
        if (eventType == AnimationEventType.StunFall && !HasStateAuthority && HasInputAuthority)
        {
            TriggerStunSlowMotion();
        }

        // PartyMonsterAnimationDriver가 있으면 드라이버를 통해 애니메이션 이벤트 적용
        if (_hasExternalAnimationDriver && _externalAnimationDriver != null)
        {
            ApplyExternalDriverAnimationEvent(eventType);
            return;
        }

        if (_usePuppetMasterAnimation)
        {
            ApplyPuppetMasterAnimationEvent(eventType);
            return;
        }

        switch (eventType)
        {
            case AnimationEventType.Punch:
            case AnimationEventType.PunchLeft:
            case AnimationEventType.PunchRight:
                animator.SetTrigger(H_Punch);
                break;
            case AnimationEventType.Throw:
                animator.SetTrigger(H_Throw);
                break;
            case AnimationEventType.GetHit:
                animator.SetTrigger(H_GetHit);
                break;
            case AnimationEventType.StunFall:
                animator.SetTrigger(H_StunFall);
                break;
            case AnimationEventType.StunRecover:
                animator.SetTrigger(H_StunRecover);
                break;
        }
    }

    private void RaiseAnimationEvent(AnimationEventType eventType, int triggerHash)
    {
        // OwnerProxy 로컬 예측 타임스탬프 + 손 방향 기록 (reconcile용)
        if (HasInputAuthority && !HasStateAuthority)
        {
            if (eventType == AnimationEventType.Punch || eventType == AnimationEventType.PunchLeft || eventType == AnimationEventType.PunchRight)
            {
                _localPunchPredictionTime = Time.time;
                _localPredictedPunchIsLeft = (eventType == AnimationEventType.PunchLeft);
            }
            else if (eventType == AnimationEventType.Throw)
                _localThrowPredictionTime = Time.time;
        }

        // PartyMonsterAnimationDriver가 있으면 애니메이션은 거기서 직접 제어
        if (_hasExternalAnimationDriver && _externalAnimationDriver != null)
        {
            // StateAuthority 플레이어는 Render()에서 replicated event를 재생하지 않으므로 즉시 적용.
            // HasInputAuthority(호스트 본인)도 포함 — 기절 이벤트(StunFall/StunRecover)는
            // HandleInput() 경로를 거치지 않으므로 RaiseAnimationEvent에서 직접 드라이버에 전달해야 한다.
            if (HasStateAuthority)
                ApplyExternalDriverAnimationEvent(eventType);
        }
        else if (animator != null)
        {
            if (_usePuppetMasterAnimation)
                ApplyPuppetMasterAnimationEvent(eventType);
            else
                animator.SetTrigger(triggerHash);
        }

        if (Runner == null || Object == null || !Object.IsValid)
            return;

        NetworkedAnimationEventType = (int)eventType;
        NetworkedAnimationEventSequence = unchecked(NetworkedAnimationEventSequence + 1);
        _lastConsumedAnimationEventSequence = NetworkedAnimationEventSequence;
    }

    private void ApplyExternalDriverAnimationEvent(AnimationEventType eventType)
    {
        switch (eventType)
        {
            case AnimationEventType.Punch:
                _externalAnimationDriver.PlayAttack();
                break;
            case AnimationEventType.PunchLeft:
                _externalAnimationDriver.PlayAttackLeft();
                break;
            case AnimationEventType.PunchRight:
                _externalAnimationDriver.PlayAttackRight();
                break;
            case AnimationEventType.Throw:
                _externalAnimationDriver.PlayThrowFromNetwork();
                break;
            case AnimationEventType.GetHit:
                ApplyPuppetMasterAnimationEvent(eventType);
                break;
            case AnimationEventType.StunFall:
                _externalAnimationDriver.CancelRecoveryAnimation();
                ApplyPuppetMasterAnimationEvent(eventType);
                break;
            case AnimationEventType.StunRecover:
                QueueRecoveryAnimationForVisuals();
                break;
        }
    }

    private void QueueRecoveryAnimationForVisuals()
    {
        if (!_hasExternalAnimationDriver || _externalAnimationDriver == null)
            return;

        var variant = GetRecoveryAnimationVariant();
        if (variant == RecoveryAnimationVariant.None)
            variant = RecoveryAnimationVariant.Supine;

        _externalAnimationDriver.QueueRecoveryAnimation(variant);
    }

    private const float PM_PunchAnimSpeed = 1.6f;
    private const float PM_PunchAnimStartOffset = 0.08f;
    private bool _pmPunchSpeedActive;

    private void ApplyPuppetMasterAnimationEvent(AnimationEventType eventType)
    {
        switch (eventType)
        {
            case AnimationEventType.Punch:
            {
                // 레거시 호환: 구분 없는 Punch 이벤트 → 로컬 토글
                var punchIsLeft = _pmNextAttackLeft;
                _pmNextAttackLeft = !_pmNextAttackLeft;
                var punchState = ResolvePMPunchStateName(punchIsLeft);
                PlayPMFastPunch(punchState);
                TriggerProceduralPunchFromPM(punchIsLeft);
                break;
            }
            case AnimationEventType.PunchLeft:
                PlayPMFastPunch(ResolvePMPunchStateName(true));
                TriggerProceduralPunchFromPM(true);
                break;
            case AnimationEventType.PunchRight:
                PlayPMFastPunch(ResolvePMPunchStateName(false));
                TriggerProceduralPunchFromPM(false);
                break;
            case AnimationEventType.Throw:
                PlayPMLockedAction(PM_ThrowState, PM_ThrowLockDuration);
                break;
            case AnimationEventType.GetHit:
                if (animator != null) animator.SetTrigger(H_GetHit);
                break;
            case AnimationEventType.StunFall:
                if (animator != null) animator.SetTrigger(H_StunFall);
                break;
            case AnimationEventType.StunRecover:
                if (animator != null) animator.SetTrigger(H_StunRecover);
                break;
        }
    }

    private void TriggerProceduralPunchFromPM(bool isLeft)
    {
        var punchArm = GetComponent<ProceduralPunchArm>();
        if (punchArm == null) return;

        var forward = _targetRoot != null ? _targetRoot.forward : transform.forward;
        if (isLeft)
            punchArm.TriggerLeftPunch(forward);
        else
            punchArm.TriggerRightPunch(forward);
    }

    private void PlayPMFastPunch(string stateName)
    {
        _pmActionLockedUntil = Time.time + ResolvePMPunchLockDuration();
        if (animator != null)
        {
            animator.speed = PM_PunchAnimSpeed;
            _pmPunchSpeedActive = true;
            animator.Play(stateName, 0, PM_PunchAnimStartOffset);
            _pmCurrentStateName = stateName;
        }
    }

    private void RestorePMPunchSpeed()
    {
        if (!_pmPunchSpeedActive) return;
        _pmPunchSpeedActive = false;
        if (animator != null)
            animator.speed = 1f;
    }

    private void PlayPMLockedAction(string stateName, float duration)
    {
        _pmActionLockedUntil = Time.time + duration;
        PlayPMState(stateName);
    }

    private float ResolvePMPunchPredictionWindow()
    {
        return Mathf.Max(PM_DefaultPunchPredictionWindow, GetConfiguredPunchCooldown());
    }

    private float ResolvePMPunchLockDuration()
    {
        var punchArm = GetComponent<ProceduralPunchArm>();
        var proceduralDuration = punchArm != null ? punchArm.TotalPunchDuration : 0f;
        return Mathf.Max(GetConfiguredPunchCooldown(), proceduralDuration);
    }

    private string ResolvePMPunchStateName(bool isLeft)
    {
        var requestedState = isLeft ? PM_PunchLeftState : PM_PunchRightState;
        if (animator == null)
            return requestedState;

        if (HasPMPunchState(requestedState))
            return requestedState;

        if (HasPMPunchState(PM_PunchState))
            return PM_PunchState;

        return requestedState;
    }

    private bool HasPMPunchState(string stateName)
    {
        if (animator == null)
            return false;

        return animator.HasState(0, Animator.StringToHash(stateName))
            || animator.HasState(0, Animator.StringToHash($"Base Layer.{stateName}"));
    }

    private void PlayPMState(string stateName)
    {
        if (animator == null || _pmCurrentStateName == stateName)
            return;

        animator.Play(stateName, 0, 0f);
        _pmCurrentStateName = stateName;
    }
}
