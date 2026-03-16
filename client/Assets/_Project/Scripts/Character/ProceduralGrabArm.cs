using UnityEngine;
using RootMotion.Dynamics;
using RootMotion.FinalIK;

/// <summary>
/// 그랩 모드일 때 LimbIK로 팔을 절차적으로 타겟 방향으로 뻗는다.
/// PuppetMaster.OnRead 콜백에서 IK를 풀어 PM이 올바른 포즈를 읽도록 한다.
/// </summary>
public class ProceduralGrabArm : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;

    [Header("IK")]
    [SerializeField] LimbIK leftArmIK;
    [SerializeField] LimbIK rightArmIK;

    [Header("Reach Settings")]
    [SerializeField] float blendSpeed = 8f;
    [SerializeField] float targetScanRadius = 2f;
    [SerializeField] float reachDistance = 0.6f;

    [Header("Hand Physics Force")]
    [SerializeField] float handReachForce = 150f;
    [SerializeField] float handDamping = 10f;

    float _leftBlend;
    float _rightBlend;

    NetworkPlayer _networkPlayer;
    HandGrabHandler[] _handGrabHandlers;

    // IK targets (created at runtime)
    Transform _leftIKTarget;
    Transform _rightIKTarget;

    // Physics hands
    Rigidbody _leftPhysicsHandRb;
    Rigidbody _rightPhysicsHandRb;
    Transform _leftPhysicsHand;
    Transform _rightPhysicsHand;

    Vector3 _leftReachDir;
    Vector3 _rightReachDir;

    void Awake()
    {
        _networkPlayer = GetComponent<NetworkPlayer>();

        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>(true);

        FindPhysicsHands();
    }

    void Start()
    {
        _handGrabHandlers = GetComponentsInChildren<HandGrabHandler>(true);

        // Create IK target transforms
        _leftIKTarget = CreateIKTarget("LeftArm_IKTarget");
        _rightIKTarget = CreateIKTarget("RightArm_IKTarget");

        // Setup LimbIK references
        if (leftArmIK != null)
        {
            leftArmIK.solver.target = _leftIKTarget;
            leftArmIK.solver.SetIKPositionWeight(0f);
            leftArmIK.solver.SetIKRotationWeight(0f);
            leftArmIK.enabled = false; // We solve manually
        }

        if (rightArmIK != null)
        {
            rightArmIK.solver.target = _rightIKTarget;
            rightArmIK.solver.SetIKPositionWeight(0f);
            rightArmIK.solver.SetIKRotationWeight(0f);
            rightArmIK.enabled = false;
        }

        // Register PuppetMaster callbacks
        if (puppetMaster != null)
        {
            puppetMaster.OnRead += OnPuppetMasterRead;
            puppetMaster.OnFixTransforms += OnPuppetMasterFixTransforms;
        }
    }

    Transform CreateIKTarget(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.forward;
        return go.transform;
    }

    void FindPhysicsHands()
    {
        if (puppetMaster == null) return;

        foreach (var muscle in puppetMaster.muscles)
        {
            if (muscle.transform == null) continue;
            if (muscle.transform.name == "LeftHand")
            {
                _leftPhysicsHand = muscle.transform;
                _leftPhysicsHandRb = muscle.transform.GetComponent<Rigidbody>();
            }
            else if (muscle.transform.name == "RightHand")
            {
                _rightPhysicsHand = muscle.transform;
                _rightPhysicsHandRb = muscle.transform.GetComponent<Rigidbody>();
            }
        }
    }

    void Update()
    {
        if (puppetMaster == null) return;

        bool grabActive = _networkPlayer != null && _networkPlayer.IsGrabActive;
        bool leftHolding = IsHandHolding(0);
        bool rightHolding = IsHandHolding(1);

        bool leftShouldReach = grabActive || leftHolding;
        bool rightShouldReach = grabActive || rightHolding;

        float dt = Time.deltaTime * blendSpeed;
        _leftBlend = Mathf.MoveTowards(_leftBlend, leftShouldReach ? 1f : 0f, dt);
        _rightBlend = Mathf.MoveTowards(_rightBlend, rightShouldReach ? 1f : 0f, dt);

        // Update reach directions and IK target positions
        _leftReachDir = GetReachDirection(_leftPhysicsHand, true);
        _rightReachDir = GetReachDirection(_rightPhysicsHand, false);

        Vector3 charPos = puppetMaster.targetRoot.position;
        float charHeight = 0.8f;
        Vector3 shoulderApprox = charPos + Vector3.up * charHeight;

        _leftIKTarget.position = shoulderApprox + _leftReachDir * reachDistance;
        _rightIKTarget.position = shoulderApprox + _rightReachDir * reachDistance;
    }

    void FixedUpdate()
    {
        if (puppetMaster == null) return;

        bool grabActive = _networkPlayer != null && _networkPlayer.IsGrabActive;
        if (grabActive)
        {
            PushPhysicsHand(_leftPhysicsHandRb, _leftReachDir, IsHandHolding(0));
            PushPhysicsHand(_rightPhysicsHandRb, _rightReachDir, IsHandHolding(1));
        }
    }

    void OnPuppetMasterRead()
    {
        if (!enabled) return;

        if (leftArmIK != null)
        {
            leftArmIK.solver.SetIKPositionWeight(_leftBlend);
            leftArmIK.solver.Update();
        }

        if (rightArmIK != null)
        {
            rightArmIK.solver.SetIKPositionWeight(_rightBlend);
            rightArmIK.solver.Update();
        }
    }

    void OnPuppetMasterFixTransforms()
    {
        if (!enabled) return;
        if (leftArmIK != null && leftArmIK.fixTransforms)
            leftArmIK.solver.FixTransforms();
        if (rightArmIK != null && rightArmIK.fixTransforms)
            rightArmIK.solver.FixTransforms();
    }

    void PushPhysicsHand(Rigidbody handRb, Vector3 reachDir, bool isHolding)
    {
        if (handRb == null || isHolding) return;
        handRb.AddForce(reachDir * handReachForce, ForceMode.Acceleration);
        handRb.AddForce(-handRb.velocity * handDamping, ForceMode.Acceleration);
    }

    Vector3 GetReachDirection(Transform physicsHand, bool isLeft)
    {
        Transform charRoot = puppetMaster.targetRoot;
        Vector3 baseDir = charRoot.forward;

        if (physicsHand != null)
        {
            Collider[] hits = Physics.OverlapSphere(physicsHand.position, targetScanRadius);
            float bestDist = float.MaxValue;
            Rigidbody bestTarget = null;

            foreach (var hit in hits)
            {
                Rigidbody rb = hit.attachedRigidbody;
                if (rb == null || rb.isKinematic) continue;
                if (rb.transform.root == transform) continue;

                float dist = Vector3.Distance(physicsHand.position, rb.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = rb;
                }
            }

            if (bestTarget != null)
            {
                Vector3 toTarget = (bestTarget.position - charRoot.position).normalized;
                toTarget.y = Mathf.Clamp(toTarget.y, -0.3f, 0.3f);
                toTarget.Normalize();

                float spread = isLeft ? -0.15f : 0.15f;
                toTarget += charRoot.right * spread;
                return toTarget.normalized;
            }
        }

        float defaultSpread = isLeft ? -0.25f : 0.25f;
        return (baseDir + charRoot.right * defaultSpread).normalized;
    }

    bool IsHandHolding(int handIndex)
    {
        if (_handGrabHandlers == null) return false;
        if (handIndex < 0 || handIndex >= _handGrabHandlers.Length) return false;
        return _handGrabHandlers[handIndex] != null && _handGrabHandlers[handIndex].IsHolding;
    }

    void OnDestroy()
    {
        if (puppetMaster != null)
        {
            puppetMaster.OnRead -= OnPuppetMasterRead;
            puppetMaster.OnFixTransforms -= OnPuppetMasterFixTransforms;
        }
    }
}
