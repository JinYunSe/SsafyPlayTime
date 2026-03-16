using UnityEngine;
using RootMotion.Dynamics;

/// <summary>
/// 멀티플레이 준비: PuppetMaster 상태/이벤트 동기화 추상화 레이어
/// TARGET 프로젝트에서는 Fusion RPC로 SendState/ReceiveState를 교체하면 됨.
/// </summary>
public class PuppetStateSync : MonoBehaviour
{
    [Header("References")]
    [SerializeField] PuppetMaster puppetMaster;
    [SerializeField] BehaviourPuppet behaviourPuppet;
    [SerializeField] PuppetLifecycleManager lifecycleManager;

    [Header("Sync Settings")]
    [SerializeField] float syncInterval = 0.1f;

    float _nextSyncTime;
    PuppetSyncState _lastSentState;

    public struct PuppetSyncState
    {
        public PuppetMaster.State state;
        public PuppetMaster.Mode mode;
        public Vector3 position;
        public Quaternion rotation;
        public float pinWeight;
        public float muscleWeight;
    }

    void Awake()
    {
        if (puppetMaster == null)
            puppetMaster = GetComponentInChildren<PuppetMaster>();
        if (behaviourPuppet == null)
            behaviourPuppet = GetComponentInChildren<BehaviourPuppet>();
        if (lifecycleManager == null)
            lifecycleManager = GetComponent<PuppetLifecycleManager>();
    }

    void Update()
    {
        if (puppetMaster == null) return;
        if (Time.time < _nextSyncTime) return;
        _nextSyncTime = Time.time + syncInterval;

        var currentState = CaptureState();

        if (HasStateChanged(currentState, _lastSentState))
        {
            SendState(currentState);
            _lastSentState = currentState;
        }
    }

    PuppetSyncState CaptureState()
    {
        return new PuppetSyncState
        {
            state = puppetMaster.state,
            mode = puppetMaster.mode,
            position = transform.position,
            rotation = transform.rotation,
            pinWeight = puppetMaster.pinWeight,
            muscleWeight = puppetMaster.muscleWeight
        };
    }

    bool HasStateChanged(PuppetSyncState a, PuppetSyncState b)
    {
        return a.state != b.state ||
               a.mode != b.mode ||
               Vector3.SqrMagnitude(a.position - b.position) > 0.01f ||
               Mathf.Abs(a.pinWeight - b.pinWeight) > 0.05f;
    }

    /// <summary>
    /// 상태 전송 — Fusion 도입 시 Runner.SendRpc로 교체
    /// </summary>
    void SendState(PuppetSyncState state)
    {
        // TODO: Fusion RPC로 교체
    }

    public void ReceiveState(PuppetSyncState state)
    {
        if (puppetMaster == null) return;

        if (puppetMaster.state != state.state)
        {
            if (state.state == PuppetMaster.State.Dead && lifecycleManager != null)
                lifecycleManager.Die();
            else if (state.state == PuppetMaster.State.Alive)
                puppetMaster.state = PuppetMaster.State.Alive;
        }

        if (puppetMaster.mode != state.mode)
            puppetMaster.mode = state.mode;
    }

    public void SendImpulse(Vector3 force, Vector3 position, int muscleIndex)
    {
        ApplyImpulse(force, position, muscleIndex);
    }

    public void ApplyImpulse(Vector3 force, Vector3 position, int muscleIndex)
    {
        if (puppetMaster == null) return;
        if (muscleIndex < 0 || muscleIndex >= puppetMaster.muscles.Length) return;

        var rb = puppetMaster.muscles[muscleIndex].rigidbody;
        if (rb != null)
            rb.AddForceAtPosition(force, position, ForceMode.Impulse);
    }
}
