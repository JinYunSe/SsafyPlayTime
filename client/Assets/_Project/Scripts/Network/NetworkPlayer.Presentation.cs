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
    private const string PM_DefaultAerialKickState = "Attack02";
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
    private const float CarryResidualRootGapThreshold = 0.50f;
    private const float CarryRootDebugGapThreshold = 1.35f;
    private const float CarryProxyHipsSoftAlignDistance = 0.35f;
    private const float CarryReleaseSettleHipsSoftAlignDistance = 0.55f;
    private const float CarryReleaseSettleHipsSnapDistance = 1.35f;
    private const float CarryReleaseSettleEmergencySnapDistance = 2.25f;
    private const float CarryProxyTargetCacheWindow = 0.18f;
    private const float CarryProxyMinimumHipsAlpha = 0.72f;
    private const float RemoteStablePresentationRootFollowSpeed = 10f;
    private const float RemoteBufferedPresentationRootFollowSpeed = 7f;
    private const float OwnerBufferedPresentationRootFollowSpeed = 9f;
    private const float ProxyPresentationRootSnapDistance = 2.75f;
    private const float IncomingHeldVictimStableRootOffset = 0.1f;
    private const float IncomingHeldVictimRecoveringRootOffset = 0.16f;
    private const float IncomingHeldVictimStableBoneBlendWeight = 0.42f;
    private const float IncomingHeldVictimRecoveringBoneBlendWeight = 0.58f;
    private const float IncomingHeldVictimMinSeparation = 0.2f;
    private const float IncomingHeldVictimMaxSeparation = 1.15f;
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

    internal void PlayProceduralKickPresentation(bool isLeft)
    {
        TriggerProceduralKick(isLeft);
    }

    internal float ResolveKickPresentationLockDuration()
    {
        var kickLeg = GetOrCreateProceduralKickLeg();
        var proceduralDuration = kickLeg != null ? kickLeg.TotalKickDuration : 0f;
        return Mathf.Max(GetConfiguredKickCooldown(), proceduralDuration > 0f ? proceduralDuration : 0.45f);
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
    private Vector3 _proxyCarrySupportRootOffset;
    private bool _hasProxyCarrySupportRootOffset;
    private PhysicalPhase _lastInterpolatedPhase = PhysicalPhase.Stable;
    private PhysicalPhase _cachedProxyCarryTargetPhase = PhysicalPhase.Stable;
    private Vector3 _cachedProxyCarryAnchorTarget;
    private Vector3 _cachedProxyCarryRootTarget;
    private float _cachedProxyCarryTargetUntilTime = float.NegativeInfinity;

    // PuppetMaster 애니메이션 모드 런타임 상태
    private bool _usePuppetMasterAnimation;
    private bool _hasExternalAnimationDriver; // PartyMonsterAnimationDriver가 존재하면 true
    private PartyMonsterAnimationDriver _externalAnimationDriver; // 캐시된 드라이버 참조
    private bool _pmHasMovementSpeedParam;
    private string _pmCurrentStateName;
    private float _pmActionLockedUntil;
    private SSAFYPlayTime.Character.GrabAnchorPoint.AnchorId _incomingHeldVictimBlendAnchorId =
        SSAFYPlayTime.Character.GrabAnchorPoint.AnchorId.None;

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

        if (isRecovering && !HasStateAuthority)
            ResetProxyCarryPresentationState(resetCarryTracking: true);

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

        // 비호스트 비주얼 모드 동기화: 호스트의 SetStunVisualMode 호출을 미러링
        // 기절 진입 → 래그돌 메시 표시, 회복 → 애니메이션 메시 복원
        SetStunVisualMode(!isRecovering);

        // 회복 시: 호스트의 CompleteRecoveryStandUpHandoff → RaiseAnimationEvent(StunRecover)는
        // RecoverStabilizing 종료 시점(~0.4초 후)에 도착하지만, ShouldUseHardPhysicsVisualMode가
        // 이미 false를 반환하여 TryRestoreAnimatorDrivenPresentation이 먼저 실행된다.
        // 이 시점에 recoveryQueued가 false이면 스탠드업 애니메이션이 누락되므로
        // NetworkedRecoveryAnimationVariant(ForceRecover에서 동일 틱에 설정됨)를 사용해 미리 큐잉한다.
        if (isRecovering)
            QueueRecoveryAnimationForVisuals();

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
               phase == PhysicalPhase.CarryingStunned ||
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

        return ShouldUseBufferedProxyPoseInterpolation();
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
        var hasIncomingHeldVictimPresentation = TryResolveIncomingHeldVictimPresentation(
            out var incomingHeldRootOffset,
            out var incomingHeldRecoveringTarget);
        if (hasIncomingHeldVictimPresentation && incomingHeldRootOffset.sqrMagnitude > 0.0001f)
            targetPosition += incomingHeldRootOffset;

        var shouldSmoothPresentationRoot =
            ShouldSmoothProxyPresentationRoot(presentationRoot) ||
            hasIncomingHeldVictimPresentation;

        if (!shouldSmoothPresentationRoot)
        {
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

        var followSpeed = ResolveProxyPresentationRootFollowSpeed();
        if (hasIncomingHeldVictimPresentation)
            followSpeed = Mathf.Max(followSpeed, incomingHeldRecoveringTarget ? 12f : 10f);

        var alpha = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        presentationRoot.position = Vector3.Lerp(presentationRoot.position, targetPosition, alpha);
        _proxyPresentationRootSmoothingActive = true;
    }

    private void ClearIncomingHeldVictimPresentation()
    {
        if (_incomingHeldVictimBlendAnchorId == SSAFYPlayTime.Character.GrabAnchorPoint.AnchorId.None)
            return;

        ClearAnchorGrabBoneBlend(_incomingHeldVictimBlendAnchorId);
        _incomingHeldVictimBlendAnchorId = SSAFYPlayTime.Character.GrabAnchorPoint.AnchorId.None;
    }

    private void ApplyIncomingHeldVictimBoneBlend(
        SSAFYPlayTime.Character.GrabAnchorPoint.AnchorId anchorId,
        bool isRecoveringTarget)
    {
        if (anchorId == SSAFYPlayTime.Character.GrabAnchorPoint.AnchorId.None)
        {
            ClearIncomingHeldVictimPresentation();
            return;
        }

        if (_incomingHeldVictimBlendAnchorId != SSAFYPlayTime.Character.GrabAnchorPoint.AnchorId.None &&
            _incomingHeldVictimBlendAnchorId != anchorId)
        {
            ClearAnchorGrabBoneBlend(_incomingHeldVictimBlendAnchorId);
        }

        _incomingHeldVictimBlendAnchorId = anchorId;
        SetAnchorGrabBoneBlend(
            anchorId,
            isRecoveringTarget
                ? IncomingHeldVictimRecoveringBoneBlendWeight
                : IncomingHeldVictimStableBoneBlendWeight);
    }

    private bool TryResolveIncomingHeldVictimPresentation(
        out Vector3 rootOffset,
        out bool isRecoveringTarget)
    {
        rootOffset = Vector3.zero;
        isRecoveringTarget = false;

        if (HasStateAuthority || ShouldUseHardPhysicsVisualMode())
        {
            ClearIncomingHeldVictimPresentation();
            return false;
        }

        var phase = GetPhysicalPhase();
        if (phase == PhysicalPhase.Stunned ||
            phase == PhysicalPhase.StunnedCollapse ||
            phase == PhysicalPhase.BeingCarriedStunned ||
            phase == PhysicalPhase.CarryingStunned)
        {
            ClearIncomingHeldVictimPresentation();
            return false;
        }

        if (!TryGetIncomingHeldPresentationData(
                out _,
                out var holderWorld,
                out var anchorId,
                out isRecoveringTarget))
        {
            ClearIncomingHeldVictimPresentation();
            return false;
        }

        ApplyIncomingHeldVictimBoneBlend(anchorId, isRecoveringTarget);

        var planarToHolder = Vector3.ProjectOnPlane(holderWorld - transform.position, Vector3.up);
        var separation = planarToHolder.magnitude;
        if (separation <= 0.0001f)
            return true;

        var pullStrength = 1f - Mathf.InverseLerp(
            IncomingHeldVictimMinSeparation,
            IncomingHeldVictimMaxSeparation,
            separation);
        if (pullStrength <= 0.001f)
            return true;

        var maxOffset = isRecoveringTarget
            ? IncomingHeldVictimRecoveringRootOffset
            : IncomingHeldVictimStableRootOffset;
        rootOffset = planarToHolder.normalized * (maxOffset * pullStrength);
        return true;
    }

    private bool TryApplyCarryProxyRootCorrection(
        Vector3 carryRootTarget,
        Vector3 residualRootTarget,
        float residualGapHint,
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
        return false;
    }

    private void CacheProxyCarryTargets(PhysicalPhase phase, Vector3 carryAnchorTarget, Vector3 carryRootTarget)
    {
        _cachedProxyCarryTargetPhase = phase;
        _cachedProxyCarryAnchorTarget = carryAnchorTarget;
        _cachedProxyCarryRootTarget = carryRootTarget;
        _cachedProxyCarryTargetUntilTime = Time.time + CarryProxyTargetCacheWindow;
    }

    private void ClearCachedProxyCarryTargets()
    {
        _cachedProxyCarryTargetPhase = PhysicalPhase.Stable;
        _cachedProxyCarryAnchorTarget = Vector3.zero;
        _cachedProxyCarryRootTarget = Vector3.zero;
        _cachedProxyCarryTargetUntilTime = float.NegativeInfinity;
    }

    private bool TryGetCachedProxyCarryTargets(
        PhysicalPhase phase,
        out Vector3 carryAnchorTarget,
        out Vector3 carryRootTarget)
    {
        carryAnchorTarget = _cachedProxyCarryAnchorTarget;
        carryRootTarget = _cachedProxyCarryRootTarget;

        if (!IsCarryPhysicalPhase(phase))
            return false;

        if (_cachedProxyCarryTargetPhase != phase)
            return false;

        if (Time.time > _cachedProxyCarryTargetUntilTime)
            return false;

        return carryAnchorTarget != Vector3.zero || carryRootTarget != Vector3.zero;
    }

    private Vector3 ResolveProxyCarryDesiredHipsPosition(
        PhysicalPhase phase,
        Vector3 desiredHipsPosition,
        Vector3 carryAnchorTarget,
        out bool didOverride,
        out bool didSnap,
        out float carryAnchorGap)
    {
        carryAnchorGap = Vector3.Distance(desiredHipsPosition, carryAnchorTarget);
        didOverride = false;
        didSnap = false;

        if (HasStateAuthority || carryAnchorGap <= CarryProxyHipsSoftAlignDistance)
            return desiredHipsPosition;

        var hardSnapDistance = phase == PhysicalPhase.BeingCarriedStunned
            ? CarryHipsImmediateSnapDistance
            : CarryHipsImmediateSnapDistance * 1.35f;

        didOverride = true;
        if (carryAnchorGap >= hardSnapDistance)
        {
            didSnap = true;
            return carryAnchorTarget;
        }

        var blend = Mathf.InverseLerp(CarryProxyHipsSoftAlignDistance, hardSnapDistance, carryAnchorGap);
        blend = Mathf.Lerp(0.45f, 1f, blend);
        return Vector3.Lerp(desiredHipsPosition, carryAnchorTarget, blend);
    }

    private Vector3 ResolveCarryReleaseSettleDesiredHipsPosition(
        PhysicalPhase phase,
        Vector3 desiredHipsPosition,
        out bool didOverride,
        out bool didSnap,
        out float exitAnchorGap)
    {
        didOverride = false;
        didSnap = false;
        exitAnchorGap = 0f;

        if (HasStateAuthority || _carryExitSnapshotAnchor == Vector3.zero)
            return desiredHipsPosition;

        exitAnchorGap = Vector3.Distance(desiredHipsPosition, _carryExitSnapshotAnchor);
        if (exitAnchorGap <= CarryReleaseSettleHipsSoftAlignDistance)
            return desiredHipsPosition;

        didOverride = true;
        var hardSnapDistance = phase == PhysicalPhase.Recovering
            ? CarryReleaseSettleHipsSnapDistance
            : CarryReleaseSettleHipsSnapDistance * 1.15f;

        if (exitAnchorGap >= hardSnapDistance)
        {
            didSnap = true;
            return _carryExitSnapshotAnchor;
        }

        var blend = Mathf.InverseLerp(CarryReleaseSettleHipsSoftAlignDistance, hardSnapDistance, exitAnchorGap);
        blend = Mathf.Lerp(0.45f, 1f, blend);
        return Vector3.Lerp(desiredHipsPosition, _carryExitSnapshotAnchor, blend);
    }

    private void ApplyProxyCarryRootPosition(Vector3 nextRootPosition, bool isSettling = false)
    {
        // settle 중에는 rigidbody position을 건드리지 않음 — 물리 velocity 기반 이동 보존
        // Keep the proxy root transform and root Rigidbody aligned during carry correction.
        if (!HasStateAuthority && rigidbody3D != null)
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
            var hasVictimAnchor = (bool)NetworkedVictimAnchorValid;
            var hasVictimCarryRoot = (bool)NetworkedVictimCarryRootValid;

            if (!hasVictimAnchor && !hasVictimCarryRoot)
            {
                if (TryGetCachedProxyCarryTargets(phase, out carryAnchorTarget, out carryRootTarget))
                    return true;

                TraceCarryDebugSample(
                    "TryResolveProxyCarryTargets",
                    $"missingVictimAnchor desiredHips={FormatCarryDebugVector(desiredHipsPosition)} " +
                    $"rootOffsetValid={(bool)NetworkedVictimRootOffsetValid} " +
                    $"victimCarryRootValid={hasVictimCarryRoot}",
                    forceSample: false);
                return false;
            }

            if (hasVictimAnchor)
                carryAnchorTarget = NetworkedVictimAnchorPosition;

            if (hasVictimCarryRoot)
            {
                carryRootTarget = NetworkedVictimCarryRootPosition;
            }
            else
            {
                carryRootTarget = carryAnchorTarget;
                if ((bool)NetworkedVictimRootOffsetValid)
                    carryRootTarget += NetworkedVictimRootOffset;
            }

            if (!hasVictimAnchor && hasVictimCarryRoot)
            {
                TraceCarryDebugSample(
                    "TryResolveProxyCarryTargets",
                    $"usingVictimCarryRootFallback desiredHips={FormatCarryDebugVector(desiredHipsPosition)} " +
                    $"victimCarryRoot={FormatCarryDebugVector(NetworkedVictimCarryRootPosition)}",
                    forceSample: false);
            }

            CacheProxyCarryTargets(phase, carryAnchorTarget, carryRootTarget);
            return true;
        }

        if (phase == PhysicalPhase.CarryingStunned)
        {
            if ((bool)NetworkedCarrierAnchorValid)
            {
                carryAnchorTarget = NetworkedCarrierAnchorPosition;
                var rootOffset = _hasProxyCarrySupportRootOffset
                    ? _proxyCarrySupportRootOffset
                    : Vector3.ClampMagnitude(desiredHipsPosition - carryAnchorTarget, 1.75f);
                carryRootTarget = carryAnchorTarget + rootOffset;
                CacheProxyCarryTargets(phase, carryAnchorTarget, carryRootTarget);
            }
            else
            {
                if (TryGetCachedProxyCarryTargets(phase, out carryAnchorTarget, out carryRootTarget))
                    return true;

                TraceCarryDebugSample(
                    "TryResolveProxyCarryTargets",
                    $"missingCarrierAnchor desiredHips={FormatCarryDebugVector(desiredHipsPosition)} " +
                    $"supportOffsetCached={_hasProxyCarrySupportRootOffset}",
                    forceSample: false);
            }

            return true;
        }

        return false;
    }

    private void CaptureProxyCarrySupportRootOffset(Vector3 rootPosition, Vector3 supportAnchorPosition)
    {
        _proxyCarrySupportRootOffset = Vector3.ClampMagnitude(rootPosition - supportAnchorPosition, 1.75f);
        _hasProxyCarrySupportRootOffset = true;
    }

    private void ClearProxyCarrySupportRootOffset()
    {
        _proxyCarrySupportRootOffset = Vector3.zero;
        _hasProxyCarrySupportRootOffset = false;
    }

    private void ResetProxyCarryPresentationState(bool resetCarryTracking)
    {
        if (HasStateAuthority)
            return;

        _carryExitSnapshotAnchor = Vector3.zero;
        _carryReleaseSettleRemaining = 0f;
        _lastCarryAnchorPosition = Vector3.zero;
        ClearProxyCarrySupportRootOffset();
        ClearCachedProxyCarryTargets();

        var snapshotSeed = transform.position;
        if (syncPhysicsObjects != null && syncPhysicsObjects.Length > 0 && syncPhysicsObjects[0] != null)
            snapshotSeed = syncPhysicsObjects[0].transform.position;

        _hipsSnapshotFrom = snapshotSeed;
        _hipsSnapshotTo = snapshotSeed;

        if (_boneSnapshotFrom != null && _boneSnapshotTo != null && syncPhysicsObjects != null)
        {
            var count = Mathf.Min(syncPhysicsObjects.Length, Mathf.Min(_boneSnapshotFrom.Length, _boneSnapshotTo.Length));
            for (int i = 0; i < count; i++)
            {
                var currentRotation = syncPhysicsObjects[i] != null
                    ? syncPhysicsObjects[i].transform.localRotation
                    : Quaternion.identity;
                _boneSnapshotFrom[i] = currentRotation;
                _boneSnapshotTo[i] = currentRotation;
            }
        }

        if (resetCarryTracking)
            _wasCarryPhaseLastFrame = false;
    }

    private void InterpolateRemoteBoneRotations()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            return;

        var interpolator = new NetworkBehaviourBufferInterpolator(this);
        int boneCount = syncPhysicsObjects.Length;
        var phase = GetPhysicalPhase();
        var isCarryPhase = IsCarryPhysicalPhase(phase);
        var phaseChanged = phase != _lastInterpolatedPhase;

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

        if (!HasStateAuthority &&
            phase == PhysicalPhase.BeingCarriedStunned &&
            changed)
        {
            TraceCarryDebugSample(
                "ProxyCarryFrame",
                $"phaseFromGetPhysicalPhase={phase} hipsUpdated=true " +
                $"hipsFrom={FormatCarryDebugVector(_hipsSnapshotFrom)} hipsTo={FormatCarryDebugVector(_hipsSnapshotTo)} " +
                $"latestHips={FormatCarryDebugVector(latestHips)} " +
                $"victimAnchorValid={(bool)NetworkedVictimAnchorValid}",
                forceSample: true);
        }

        // ── CarrySolveFrame: carry 진입/종료 시 snapshot 재시드 ──
        {
            var isCarryNow = isCarryPhase;
            var currentCarryMode = GetLocalCarryMode();
            if (isCarryNow && currentCarryMode != SSAFYPlayTime.Character.CarryPhysicsProfile.CarryMode.None)
                _lastObservedCarryMode = currentCarryMode;
            if (isCarryNow && (!_wasCarryPhaseLastFrame || phaseChanged))
            {
                _hipsSnapshotFrom = syncPhysicsObjects[0] != null
                    ? syncPhysicsObjects[0].transform.position
                    : latestHips;
                _hipsSnapshotTo = latestHips;
                _carryExitSnapshotAnchor = Vector3.zero;
                _carryReleaseSettleRemaining = 0f;
                ClearCachedProxyCarryTargets();

                if (!HasStateAuthority &&
                    TryResolveProxyCarryTargets(phase, latestHips, out _, out var carryRootTarget))
                {
                    _lastCarryAnchorPosition = carryRootTarget;

                    if (phase == PhysicalPhase.CarryingStunned && (bool)NetworkedCarrierAnchorValid)
                        CaptureProxyCarrySupportRootOffset(transform.position, NetworkedCarrierAnchorPosition);
                }
            }
            else if (isCarryNow)
            {
                if (!HasStateAuthority)
                {
                    if (phase == PhysicalPhase.CarryingStunned &&
                        !_hasProxyCarrySupportRootOffset &&
                        (bool)NetworkedCarrierAnchorValid)
                    {
                        CaptureProxyCarrySupportRootOffset(transform.position, NetworkedCarrierAnchorPosition);
                    }
                    else if (phase != PhysicalPhase.CarryingStunned && _hasProxyCarrySupportRootOffset)
                    {
                        ClearProxyCarrySupportRootOffset();
                    }

                    if (TryResolveProxyCarryTargets(phase, latestHips, out _, out var carryRootTarget))
                        _lastCarryAnchorPosition = carryRootTarget;
                    else
                        _lastCarryAnchorPosition = latestHips;
                }
            }
            else if (!isCarryNow && _wasCarryPhaseLastFrame)
            {
                if (!HasStateAuthority && phase == PhysicalPhase.Recovering)
                {
                    ResetProxyCarryPresentationState(resetCarryTracking: true);
                }
                else
                {
                    _carryExitSnapshotAnchor = Vector3.zero;
                    ClearProxyCarrySupportRootOffset();
                    ClearCachedProxyCarryTargets();
                    _hipsSnapshotFrom = syncPhysicsObjects[0] != null
                        ? syncPhysicsObjects[0].transform.position
                        : latestHips;
                    _hipsSnapshotTo = latestHips;
                    _carryReleaseSettleRemaining = 0f;
                }
            }
            _wasCarryPhaseLastFrame = isCarryNow;
            _lastInterpolatedPhase = phase;
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
            var deadzone = isCarryPhase ? 0f : ResolveOwnerProxyHipsDeadzone();
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
                hipsAlpha = isCarryPhase
                    ? Mathf.Clamp01(Mathf.Max(interpolator.Alpha, CarryProxyMinimumHipsAlpha) * Mathf.Max(slowMoAlphaScale, 0.5f))
                    : ResolveHipsInterpolationAlpha(interpolator.Alpha) * slowMoAlphaScale;
                var interpolatedHips = Vector3.Lerp(hipsFrom, hipsTo, hipsAlpha);

                if (!isCarryPhase && deadzone > 0f && (interpolatedHips - hipsCurrent).sqrMagnitude <= deadzone * deadzone)
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
            var didCarryHipsOverride = false;
            var didCarryHipsSnap = false;
            var carryAnchorGap = 0f;
            var didReleaseSettleHipsOverride = false;
            var didReleaseSettleHipsSnap = false;
            var releaseSettleExitGap = 0f;
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
                else
                {
                    desiredHipsPosition = ResolveProxyCarryDesiredHipsPosition(
                        phase,
                        desiredHipsPosition,
                        proxyCarryAnchor,
                        out didCarryHipsOverride,
                        out didCarryHipsSnap,
                        out carryAnchorGap);
                }

                var residualGapBeforeCorrection = Vector3.Distance(rootBeforeCorrection, desiredHipsPosition);

                didApplyRootCorrection = TryApplyCarryProxyRootCorrection(
                    proxyCarryRootTarget,
                    desiredHipsPosition,
                    residualGapBeforeCorrection,
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
                var settleRootTarget = _carryExitSnapshotAnchor != Vector3.zero
                    ? _carryExitSnapshotAnchor
                    : desiredHipsPosition;
                desiredHipsPosition = ResolveCarryReleaseSettleDesiredHipsPosition(
                    phase,
                    desiredHipsPosition,
                    out didReleaseSettleHipsOverride,
                    out didReleaseSettleHipsSnap,
                    out releaseSettleExitGap);

                didApplyRootCorrection = TryApplyCarryProxyRootCorrection(
                    settleRootTarget,
                    desiredHipsPosition,
                    Vector3.Distance(transform.position, desiredHipsPosition),
                    settleMode,
                    slowMoAlphaScale,
                    out rootBeforeCorrection,
                    out rootAfterCorrection,
                    out rootGapBeforeCorrection,
                    out rootGapAfterCorrection,
                    out didRootSnap,
                    isSettling: true);

                if (_carryReleaseSettleRemaining <= 0f ||
                    (_carryExitSnapshotAnchor != Vector3.zero &&
                     !didReleaseSettleHipsOverride &&
                     releaseSettleExitGap <= CarryPresentationTraceGapThreshold))
                    _carryExitSnapshotAnchor = Vector3.zero;
            }

            syncPhysicsObjects[0].transform.position = desiredHipsPosition;

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
                        $"hipsAlpha={hipsAlpha:F2} deadzone={deadzone:F2} hipsSnap={didHipsSnap} rootSnap={didRootSnap} rootMoved={didApplyRootCorrection} " +
                        $"carryAnchorGap={carryAnchorGap:F2} carryHipsOverride={didCarryHipsOverride} carryHipsSnap={didCarryHipsSnap}",
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
                    $"remaining={_carryReleaseSettleRemaining:F2} hipsAlpha={hipsAlpha:F2} rootSnap={didRootSnap} rootMoved={didApplyRootCorrection} " +
                    $"exitGap={releaseSettleExitGap:F2} settleHipsOverride={didReleaseSettleHipsOverride} settleHipsSnap={didReleaseSettleHipsSnap}");
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
            case AnimationEventType.KickLeft:
                TriggerProceduralKick(true);
                break;
            case AnimationEventType.KickRight:
                TriggerProceduralKick(false);
                break;
            case AnimationEventType.AerialKick:
                TriggerFallbackAerialKickAnimation();
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
            // Host-side proxies have state authority, so Render() will not replay their
            // replicated events. Apply the action immediately on that local copy.
            if ((eventType == AnimationEventType.KickLeft || eventType == AnimationEventType.KickRight || eventType == AnimationEventType.AerialKick)
                ? (HasStateAuthority || Runner == null)
                : (HasStateAuthority && !HasInputAuthority))
                ApplyExternalDriverAnimationEvent(eventType);
        }
        else if (animator != null)
        {
            if (_usePuppetMasterAnimation)
                ApplyPuppetMasterAnimationEvent(eventType);
            else if (eventType == AnimationEventType.AerialKick)
                TriggerFallbackAerialKickAnimation();
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
            case AnimationEventType.KickLeft:
                _externalAnimationDriver.PlayKickLeft();
                break;
            case AnimationEventType.KickRight:
                _externalAnimationDriver.PlayKickRight();
                break;
            case AnimationEventType.AerialKick:
                _externalAnimationDriver.PlayAerialKick();
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
            case AnimationEventType.KickLeft:
                _pmActionLockedUntil = Time.time + ResolvePMKickLockDuration();
                TriggerProceduralKick(true);
                break;
            case AnimationEventType.KickRight:
                _pmActionLockedUntil = Time.time + ResolvePMKickLockDuration();
                TriggerProceduralKick(false);
                break;
            case AnimationEventType.AerialKick:
                PlayPMAerialKick();
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

    private void TriggerProceduralKick(bool isLeft)
    {
        var kickLeg = GetOrCreateProceduralKickLeg();
        if (kickLeg == null)
            return;

        var forward = _targetRoot != null ? _targetRoot.forward : transform.forward;
        if (isLeft)
            kickLeg.TriggerLeftKick(forward);
        else
            kickLeg.TriggerRightKick(forward);
    }

    private ProceduralKickLeg GetOrCreateProceduralKickLeg()
    {
        var kickLeg = GetComponent<ProceduralKickLeg>();
        if (kickLeg != null || !Application.isPlaying)
            return kickLeg;

        return gameObject.AddComponent<ProceduralKickLeg>();
    }

    private string ResolveConfiguredAerialKickStateName()
    {
        var stat = CombatSettings.Instance?.GetAttackStat(AerialKickCombatStatId);
        if (stat.HasValue && !string.IsNullOrWhiteSpace(stat.Value.AnimationClip))
            return stat.Value.AnimationClip;

        return PM_DefaultAerialKickState;
    }

    private void TriggerFallbackAerialKickAnimation()
    {
        var aerialKickState = ResolveConfiguredAerialKickStateName();
        if (animator != null && animator.HasState(0, Animator.StringToHash(aerialKickState)))
        {
            animator.CrossFadeInFixedTime(aerialKickState, 0.06f, 0, 0f);
            return;
        }

        TriggerProceduralKick(false);
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

    private float ResolvePMKickLockDuration()
    {
        return ResolveKickPresentationLockDuration();
    }

    private void PlayPMAerialKick()
    {
        _pmActionLockedUntil = Time.time + ResolvePMAerialKickLockDuration();
        var aerialKickState = ResolveConfiguredAerialKickStateName();

        if (animator != null && animator.HasState(0, Animator.StringToHash(aerialKickState)))
        {
            RestorePMPunchSpeed();
            animator.Play(aerialKickState, 0, 0f);
            _pmCurrentStateName = aerialKickState;
            return;
        }

        TriggerProceduralKick(false);
    }

    private float ResolvePMAerialKickLockDuration()
    {
        return 0.72f;
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
