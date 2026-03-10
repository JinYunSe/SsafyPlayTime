using Fusion;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    public override void Render()
    {
        UpdateAnimationParameters();

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
}
