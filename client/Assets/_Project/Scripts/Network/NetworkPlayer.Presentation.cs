using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    public override void Render()
    {
        UpdateAnimationParameters();
        ApplyReplicatedAnimationEvent();

        if (!HasStateAuthority && Object != null && Object.IsValid)
            InterpolateRemoteBoneRotations();
    }

    private void LateUpdate()
    {
        if (Runner == null)
            UpdateAnimationParameters();
    }

    private void InterpolateRemoteBoneRotations()
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

    private void UpdateAnimationParameters()
    {
        if (animator == null)
            return;

        var (speed, state) = ResolveAnimationParameters();
        animator.SetFloat(H_MovementSpeed, speed);
        animator.SetInteger(H_MotorState, state);
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

        switch ((AnimationEventType)NetworkedAnimationEventType)
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
        if (animator != null)
            animator.SetTrigger(triggerHash);

        if (Runner == null || Object == null || !Object.IsValid)
            return;

        NetworkedAnimationEventType = (int)eventType;
        NetworkedAnimationEventSequence = unchecked(NetworkedAnimationEventSequence + 1);
        _lastConsumedAnimationEventSequence = NetworkedAnimationEventSequence;
    }
}
