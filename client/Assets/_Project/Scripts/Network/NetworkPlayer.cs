using SSAFYPlayTime.Character;
using UnityEngine;

public sealed class NetworkPlayer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rigidbody3D;
    [SerializeField] private ConfigurableJoint mainJoint;
    [SerializeField] private Animator animator;
    [SerializeField] private SyncPhysicsObject[] syncPhysicsObjects;

    [Header("Config")]
    [SerializeField] private PlayerMotorConfig config;

    private readonly GroundProbe _groundProbe = new();
    private readonly PlayerMotorStateMachine _stateMachine = new();

    private Vector2 _moveInput;
    private bool _jumpPressed;
    private bool _isGrounded;

    private void Awake()
    {
        if (syncPhysicsObjects == null || syncPhysicsObjects.Length == 0)
            syncPhysicsObjects = GetComponentsInChildren<SyncPhysicsObject>(true);
    }

    private void Update()
    {
        _moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        if (Input.GetKeyDown(KeyCode.Space))
            _jumpPressed = true;
    }

    private void FixedUpdate()
    {
        if (config == null || rigidbody3D == null || mainJoint == null)
            return;

        _isGrounded = _groundProbe.IsGrounded(
            rigidbody3D.position,
            transform,
            config.groundProbeRadius,
            config.groundProbeDistance);

        if (!_isGrounded)
            rigidbody3D.AddForce(Vector3.down * config.extraGravity, ForceMode.Acceleration);

        var inputMagnitude = _moveInput.magnitude;
        var forwardVelocity = Vector3.Dot(transform.forward, rigidbody3D.velocity);

        if (inputMagnitude > 0.001f)
        {
            var desired = Quaternion.LookRotation(new Vector3(_moveInput.x, 0f, -_moveInput.y), transform.up);
            mainJoint.targetRotation = Quaternion.RotateTowards(
                mainJoint.targetRotation,
                desired,
                Time.fixedDeltaTime * config.rotateSpeedDeg);

            if (Mathf.Abs(forwardVelocity) < config.maxSpeed)
                rigidbody3D.AddForce(transform.forward * inputMagnitude * config.acceleration, ForceMode.Acceleration);
        }

        if (_isGrounded && _jumpPressed)
        {
            rigidbody3D.AddForce(Vector3.up * config.jumpImpulse, ForceMode.Impulse);
            _jumpPressed = false;
            _stateMachine.SetJump();
        }

        _stateMachine.Tick(_isGrounded, inputMagnitude, Time.fixedDeltaTime, config);

        if (animator != null)
        {
            animator.SetFloat("movementSpeed", Mathf.Abs(forwardVelocity) * 0.4f);
            animator.SetInteger("MotorState", (int)_stateMachine.CurrentState);
        }

        for (var i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].UpdateJointFromAnimation();
    }
}
