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

        if (!HasStateAuthority && Object != null && Object.IsValid)
        {
            InterpolateRemoteBoneRotations();
            SyncRemoteActiveRagdollState();
        }
    }

    /// <summary>
    /// 원격 클라이언트에서 호스트의 IsActiveRagdoll 상태를 로컬에 반영.
    /// 기절(false) / 회복(true) 전환을 원격에서도 인식하도록 한다.
    /// </summary>
    private void SyncRemoteActiveRagdollState()
    {
        bool networkedActive = NetworkedIsActiveRagdoll;
        if (_isActiveRagdoll != networkedActive)
        {
            _isActiveRagdoll = networkedActive;
        }
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
