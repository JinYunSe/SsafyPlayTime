using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    // EP2 PuppetMaster 캐릭터용 애니메이션 상태 이름
    private const string PM_IdleState = "Idle01";
    private const string PM_WalkState = "WalkFWD";
    private const string PM_SprintState = "SprintFWD";
    private const string PM_PunchLeftState = "PunchLeft";
    private const string PM_PunchRightState = "PunchRight";
    private const string PM_ThrowState = "Throw";
    private const float PM_LocomotionThreshold = 0.1f;
    private const float PM_AttackLockDuration = 0.7f;
    private const float PM_ThrowLockDuration = 0.85f;
    private bool _pmNextAttackLeft;

    // PuppetMaster 애니메이션 모드 런타임 상태
    private bool _usePuppetMasterAnimation;
    private bool _hasExternalAnimationDriver; // PartyMonsterAnimationDriver가 존재하면 true
    private PartyMonsterAnimationDriver _externalAnimationDriver; // 캐시된 드라이버 참조
    private bool _pmHasMovementSpeedParam;
    private string _pmCurrentStateName;
    private float _pmActionLockedUntil;

    public override void Render()
    {
        UpdateAnimationParameters();
        ApplyReplicatedAnimationEvent();

        if (Object == null || !Object.IsValid)
            return;

        // ── 플레이어 타입별 3분기 ──
        if (HasStateAuthority)
        {
            // AuthorityOwner: 물리 시뮬레이션이 직접 뼈를 구동 → 보간 불필요
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
            InterpolateRemoteBoneRotations();
            SyncRemoteActiveRagdollState();
            // grab/carry 애니메이터 파라미터 동기화
            SyncGrabbingAnimatorFromNetwork();
        }
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
        bool isInConfirmedRagdoll = !_isActiveRagdoll || NetworkedIsBeingGrabbed;
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
        if (_bodyPartPhysicsManager != null)
        {
            _bodyPartPhysicsManager.SetState(isRecovering
                ? SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.Recovering
                : SSAFYPlayTime.Character.BodyPartPhysicsProfile.CharacterPhysicsState.Stunned);
        }

        Debug.Log($"[Remote] ActiveRagdoll 전환: {(isRecovering ? "회복" : "기절")}");
    }

    private void LateUpdate()
    {
        if (Runner == null)
            UpdateAnimationParameters();
    }

    private void InterpolateRemoteBoneRotations()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            return;

        var interpolator = new NetworkBehaviourBufferInterpolator(this);

        // Hips(muscles[0]) 절대 위치 보간 — 잡기로 끌려갈 때 원격에서 위치 추적
        // Human Fall Flat 방식: 루트 뼈 절대 위치를 직접 동기화
        if (syncPhysicsObjects.Length > 0 && syncPhysicsObjects[0] != null)
        {
            var hipsTarget = NetworkedHipsPosition;
            var hipsCurrent = syncPhysicsObjects[0].transform.position;

            // 텔레포트 방지: 거리가 너무 크면 즉시 스냅 (HFF 방식, sqrMag > 15)
            if ((hipsTarget - hipsCurrent).sqrMagnitude > 15f)
                syncPhysicsObjects[0].transform.position = hipsTarget;
            else
                syncPhysicsObjects[0].transform.position = Vector3.Lerp(
                    hipsCurrent, hipsTarget, interpolator.Alpha);
        }

        // 뼈 회전 보간
        for (int i = 0; i < syncPhysicsObjects.Length; i++)
        {
            if (syncPhysicsObjects[i] == null) continue;
            syncPhysicsObjects[i].transform.localRotation = Quaternion.Slerp(
                syncPhysicsObjects[i].transform.localRotation,
                BoneRotations.Get(i),
                interpolator.Alpha);
        }
    }

    private void UpdateAnimationParameters()
    {
        if (animator == null)
            return;

        // PartyMonsterAnimationDriver가 로코모션/전투 애니메이션을 모두 제어하므로 스킵
        if (_hasExternalAnimationDriver)
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

        if (_pmHasMovementSpeedParam)
            animator.SetFloat(H_MovementSpeed, speed);

        string targetState;
        if (speed <= PM_LocomotionThreshold)
            targetState = PM_IdleState;
        else
            targetState = Input.GetKey(KeyCode.LeftShift) ? PM_SprintState : PM_WalkState;

        PlayPMState(targetState);
    }

    private (float speed, int state) ResolveAnimationParameters()
    {
        if (Runner != null && Object != null && Object.IsValid)
            return (NetworkedMoveSpeed, NetworkedMotorState);

        return (_localMoveSpeed, _localMotorState);
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
        if (animator == null || Runner == null || Object == null || !Object.IsValid)
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

        // 피호스트 로컬 플레이어: 펀치/던지기는 HandleInput()에서 이미 로컬 예측 재생했으므로
        // 네트워크 이벤트에서 중복 재생 방지. 기절/피격 등 비예측 이벤트만 적용.
        if (HasInputAuthority && !HasStateAuthority)
        {
            if (eventType == AnimationEventType.Punch || eventType == AnimationEventType.Throw)
                return;
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
        // PartyMonsterAnimationDriver가 있으면 애니메이션은 거기서 직접 제어
        if (animator != null && !_hasExternalAnimationDriver)
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

    private void ApplyPuppetMasterAnimationEvent(AnimationEventType eventType)
    {
        switch (eventType)
        {
            case AnimationEventType.Punch:
                var punchState = _pmNextAttackLeft ? PM_PunchLeftState : PM_PunchRightState;
                _pmNextAttackLeft = !_pmNextAttackLeft;
                PlayPMLockedAction(punchState, PM_AttackLockDuration);
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

    private void PlayPMLockedAction(string stateName, float duration)
    {
        _pmActionLockedUntil = Time.time + duration;
        PlayPMState(stateName);
    }

    private void PlayPMState(string stateName)
    {
        if (animator == null || _pmCurrentStateName == stateName)
            return;

        animator.Play(stateName, 0, 0f);
        _pmCurrentStateName = stateName;
    }
}
