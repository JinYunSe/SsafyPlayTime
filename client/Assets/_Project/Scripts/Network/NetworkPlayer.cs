using Fusion;
using SSAFYPlayTime.Character;
using UnityEngine;

public sealed class NetworkPlayer : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rigidbody3D;
    [SerializeField] private ConfigurableJoint mainJoint;
    [SerializeField] private Animator animator;
    [SerializeField] private SyncPhysicsObject[] syncPhysicsObjects;

    [Header("Config")]
    [SerializeField] private PlayerMotorConfig config;
    [SerializeField] private RuntimeAnimatorController fallbackAnimatorController;

    [Networked] private float NetworkedMoveSpeed { get; set; }
    [Networked] private int NetworkedMotorState { get; set; }

    private readonly GroundProbe _groundProbe = new();
    private readonly PlayerMotorStateMachine _stateMachine = new();
    private bool _isGrounded;

    public override void Spawned()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>(true);

        EnsureAnimatorBinding();

        // StateAuthority(서버)만 물리 시뮬레이션을 실행한다.
        // 나머지 클라이언트는 각 부위의 NetworkTransform이 위치를 동기화해 준다.
        var isKinematic = !HasStateAuthority;
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true))
            rb.isKinematic = isKinematic;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || config == null || rigidbody3D == null || mainJoint == null)
            return;

        _isGrounded = _groundProbe.IsGrounded(
            rigidbody3D.position,
            transform,
            config.groundProbeRadius,
            config.groundProbeDistance);

        if (!_isGrounded)
            rigidbody3D.AddForce(Vector3.down * config.extraGravity, ForceMode.Acceleration);

        var inputMagnitude = 0f;
        if (GetInput(out PlayerNetworkInput input))
        {
            inputMagnitude = input.Move.magnitude;
            var forwardVelocity = Vector3.Dot(transform.forward, rigidbody3D.velocity);

            if (inputMagnitude > 0.001f)
            {
                var desired = Quaternion.LookRotation(new Vector3(input.Move.x, 0f, -input.Move.y), transform.up);
                mainJoint.targetRotation = Quaternion.RotateTowards(
                    mainJoint.targetRotation,
                    desired,
                    Runner.DeltaTime * config.rotateSpeedDeg);

                if (Mathf.Abs(forwardVelocity) < config.maxSpeed)
                    rigidbody3D.AddForce(transform.forward * inputMagnitude * config.acceleration, ForceMode.Acceleration);
            }

            if (_isGrounded && input.Jump)
            {
                rigidbody3D.AddForce(Vector3.up * config.jumpImpulse, ForceMode.Impulse);
                _stateMachine.SetJump();
            }

            NetworkedMoveSpeed = Mathf.Abs(Vector3.Dot(transform.forward, rigidbody3D.velocity)) * 0.4f;
        }

        _stateMachine.Tick(_isGrounded, inputMagnitude, Runner.DeltaTime, config);
        NetworkedMotorState = (int)_stateMachine.CurrentState;

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].UpdateJointFromAnimation();
    }

    // Render는 모든 클라이언트에서 매 프레임 호출된다.
    // 네트워크로 동기화된 값으로 애니메이터를 구동한다.
    public override void Render()
    {
        if (animator == null) return;
        animator.SetFloat("movementSpeed", NetworkedMoveSpeed);
        animator.SetInteger("MotorState", NetworkedMotorState);
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
