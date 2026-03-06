using Fusion;
using SSAFYPlayTime.Character;
using UnityEngine;

// Fusion NetworkBehaviour 기반의 캐릭터 컨트롤러.
// StateAuthority(서버/호스트)에서만 물리 시뮬레이션을 실행하고,
// 클라이언트는 NetworkTransform을 통해 동기화된 위치/회전을 수신한다.
// 입력은 Fusion 입력 파이프라인(OnInput → GetInput)을 통해 서버로 전달된다.
public sealed class NetworkPlayer : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody rigidbody3D;         // 루트 Rigidbody (이동/점프 힘 적용 대상)
    [SerializeField] private ConfigurableJoint mainJoint;   // 루트 회전 방향을 제어하는 관절
    [SerializeField] private Animator animator;             // 애니메이션 재생기
    [SerializeField] private SyncPhysicsObject[] syncPhysicsObjects; // 래그돌 각 부위 동기화 컴포넌트 배열

    [Header("Config")]
    [SerializeField] private PlayerMotorConfig config;                             // 이동·점프 수치 설정 (ScriptableObject)
    [SerializeField] private RuntimeAnimatorController fallbackAnimatorController; // Animator 컨트롤러가 없을 때 사용할 기본값

    // 네트워크로 동기화되는 애니메이션 파라미터.
    // StateAuthority에서 값을 쓰고, 모든 클라이언트의 Render()에서 읽어 Animator를 구동한다.
    [Networked] private float NetworkedMoveSpeed { get; set; }
    [Networked] private int NetworkedMotorState { get; set; }

    // 접지 판정 헬퍼 (SphereCast 결과 버퍼를 재사용)
    private readonly GroundProbe _groundProbe = new();

    // 이동 상태(Idle/Walk/Jump/Fall/FreeFall) 전환 로직
    private readonly PlayerMotorStateMachine _stateMachine = new();

    // 이번 틱의 접지 여부 캐시
    private bool _isGrounded;

    // 스폰 직후 한 번 호출.
    // 래그돌 부위를 수집하고, 비-StateAuthority 클라이언트의 모든 Rigidbody를 kinematic으로 전환한다.
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

    // Fusion 네트워크 틱마다 호출 (StateAuthority = 서버에서만 실행).
    // GetInput으로 이번 틱의 플레이어 입력을 받아 물리 연산을 수행하고 네트워크 상태를 갱신한다.
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || config == null || rigidbody3D == null || mainJoint == null)
            return;

        // 접지 판정
        _isGrounded = _groundProbe.IsGrounded(
            rigidbody3D.position,
            transform,
            config.groundProbeRadius,
            config.groundProbeDistance);

        // 공중이면 추가 중력 적용
        if (!_isGrounded)
            rigidbody3D.AddForce(Vector3.down * config.extraGravity, ForceMode.Acceleration);

var inputMagnitude = 0f;
        
        if (GetInput(out PlayerNetworkInput input))
        {
            var desired = Quaternion.LookRotation(new Vector3(-_moveInput.x, 0f, _moveInput.y), transform.up);
            mainJoint.targetRotation = Quaternion.RotateTowards(
                mainJoint.targetRotation,
                desired,
                Time.fixedDeltaTime * config.rotateSpeedDeg);
            
            inputMagnitude = input.Move.magnitude;
            var forwardVelocity = Vector3.Dot(transform.forward, rigidbody3D.velocity);
            
            // 이동 입력이 있으면 목표 방향으로 회전하고 전진 힘을 가한다
            if (inputMagnitude > 0.001f)
            {
                // 앞서 작성하시던 이동 힘(AddForce) 부여 로직이 이곳에 들어갑니다.
                var desired = Quaternion.LookRotation(new Vector3(input.Move.x, 0f, -input.Move.y), transform.up);
                mainJoint.targetRotation = Quaternion.RotateTowards(
                    mainJoint.targetRotation,
                    desired,
                    Runner.DeltaTime * config.rotateSpeedDeg);

                if (Mathf.Abs(forwardVelocity) < config.maxSpeed)
                    rigidbody3D.AddForce(transform.forward * inputMagnitude * config.acceleration, ForceMode.Acceleration);
            }

            // 접지 상태에서 점프 입력 시 수직 충격량 적용
            if (_isGrounded && input.Jump)
            {
                rigidbody3D.AddForce(Vector3.up * config.jumpImpulse, ForceMode.Impulse);
                _stateMachine.SetJump();
            }

            // 이동 속도를 네트워크 변수에 저장 (클라이언트 Animator에서 읽음)
            NetworkedMoveSpeed = Mathf.Abs(Vector3.Dot(transform.forward, rigidbody3D.velocity)) * 0.4f;
        }

        // 이동 상태 머신 갱신 및 결과를 네트워크 변수에 저장
        _stateMachine.Tick(_isGrounded, inputMagnitude, Runner.DeltaTime, config);
        NetworkedMotorState = (int)_stateMachine.CurrentState;

        // 래그돌 관절 목표 회전을 애니메이션과 동기화 (StateAuthority에서만)
        for (var i = 0; i < syncPhysicsObjects.Length; i++)
            syncPhysicsObjects[i].UpdateJointFromAnimation();
    }

    // 모든 클라이언트에서 매 프레임 호출 (렌더링 직전).
    // 네트워크로 동기화된 값을 읽어 Animator 파라미터를 설정한다.
    public override void Render()
    {
        if (animator == null) return;
        animator.SetFloat("movementSpeed", NetworkedMoveSpeed);
        animator.SetInteger("MotorState", NetworkedMotorState);
    }

    // Animator 컴포넌트가 없거나 컨트롤러가 비어 있을 때 fallback 설정을 적용한다.
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
