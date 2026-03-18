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
    private const float OwnerRecoveringBoneRotationLerpScale = 0.3f;
    private const float OwnerUnstableBoneRotationLerpScale = 0.55f;
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
        TraceProxyAnimationDiagnostics("Render-Begin");

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
            if (ShouldUsePhysicalPhasePresentation())
                InterpolateRemoteBoneRotations();
            // grab/carry 애니메이터 파라미터 동기화
            SyncGrabbingAnimatorFromNetwork();
        }

        UpdatePhysicsDrivenVisualPose();
        ApplyProxyPresentationRotation();
        UpdateCharacterPresentationEffects();
        TraceProxyAnimationDiagnostics("Render-End");
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
        bool isInConfirmedRagdoll = ShouldUsePhysicalPhasePresentation();
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

        if (Runner == null)
            UpdateAnimationParameters();

        UpdateCharacterPresentationEffects();

        // 비주얼 갱신 완료 후 카메라 앵커 갱신 — CameraRig.LateUpdate에서 읽는다.
        UpdateCameraFollowAnchor();

        TraceProxyAnimationDiagnostics("LateUpdate");
        TraceCameraDeltaDiagnostics();

        // 기절 슬로우모션 timeScale 복원 틱 (로컬 플레이어만)
        TickStunSlowMotion();
        UpdateStunForceDiagnosticsHotkey();
    }

    private void InterpolateRemoteBoneRotations()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            return;

        var interpolator = new NetworkBehaviourBufferInterpolator(this);
        var rotationAlpha = ResolveBoneRotationInterpolationAlpha(interpolator.Alpha);

        // Hips(muscles[0]) 절대 위치 보간 — 잡기로 끌려갈 때 원격에서 위치 추적
        // Human Fall Flat 방식: 루트 뼈 절대 위치를 직접 동기화
        if (syncPhysicsObjects.Length > 0 && syncPhysicsObjects[0] != null)
        {
            var hipsTarget = NetworkedHipsPosition;
            var hipsCurrent = syncPhysicsObjects[0].transform.position;
            var hipsDelta = hipsTarget - hipsCurrent;
            var deadzone = ResolveOwnerProxyHipsDeadzone();

            // 텔레포트 방지: 거리가 너무 크면 즉시 스냅 (HFF 방식, sqrMag > 15)
            if ((hipsTarget - hipsCurrent).sqrMagnitude > 15f)
                syncPhysicsObjects[0].transform.position = hipsTarget;
            else if (deadzone > 0f && hipsDelta.sqrMagnitude <= deadzone * deadzone)
                syncPhysicsObjects[0].transform.position = hipsCurrent;
            else
                syncPhysicsObjects[0].transform.position = Vector3.Lerp(
                    hipsCurrent, hipsTarget, ResolveHipsInterpolationAlpha(interpolator.Alpha));

            TraceProxyStunPresentation("InterpolateRemoteBoneRotations", hipsCurrent, hipsTarget);
        }

        // 뼈 회전 보간
        for (int i = 0; i < syncPhysicsObjects.Length; i++)
        {
            if (syncPhysicsObjects[i] == null) continue;
            syncPhysicsObjects[i].transform.localRotation = Quaternion.Slerp(
                syncPhysicsObjects[i].transform.localRotation,
                BoneRotations.Get(i),
                rotationAlpha);
        }
    }

    private float ResolveHipsInterpolationAlpha(float baseAlpha)
    {
        if (!IsOwnerProxy)
            return baseAlpha;

        return GetPhysicalPhase() switch
        {
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
            : new Vector3(-moveDirection.x, 0f, moveDirection.z);

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

        // 원격 클라이언트에서 GetHit 수신 시 로컬 히트스탑 연출
        if (eventType == AnimationEventType.GetHit && !HasStateAuthority)
        {
            ApplyReplicatedHitStop();
            if (HasInputAuthority)
                TriggerVictimCameraKick(-ResolveCombatForward(), FallbackPunchKnockbackForce);
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
            // Host-side proxies have state authority, so Render() will not replay their
            // replicated events. Apply the action immediately on that local copy.
            if (HasStateAuthority && !HasInputAuthority)
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
            case AnimationEventType.StunFall:
            case AnimationEventType.StunRecover:
                ApplyPuppetMasterAnimationEvent(eventType);
                break;
        }
    }

    private const float PM_PunchAnimSpeed = 2.2f;
    private const float PM_PunchAnimStartOffset = 0.2f;
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
