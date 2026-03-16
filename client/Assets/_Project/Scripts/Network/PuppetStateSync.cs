using UnityEngine;
using RootMotion.Dynamics;

/// <summary>
/// PuppetMaster 상태/이벤트 동기화 레이어.
/// StateAuthority에서 PuppetMaster 상태를 캡처해 NetworkPlayer의 Networked 속성으로 전송.
/// 원격 클라이언트에서는 Networked 속성을 읽어 로컬 PuppetMaster에 적용.
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
    NetworkPlayer _networkPlayer;
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
        _networkPlayer = GetComponentInParent<NetworkPlayer>();
    }

    void Update()
    {
        if (puppetMaster == null) return;
        // Fusion Spawned 전에는 Networked 속성/HasStateAuthority 접근 불가
        if (_networkPlayer == null) return;
        if (_networkPlayer.Object == null || !_networkPlayer.Object.IsValid) return;

        // StateAuthority: 상태 캡처 → NetworkPlayer Networked 속성에 기록
        if (_networkPlayer.HasStateAuthority)
        {
            if (Time.time < _nextSyncTime) return;
            _nextSyncTime = Time.time + syncInterval;

            var currentState = CaptureState();
            if (HasStateChanged(currentState, _lastSentState))
            {
                _networkPlayer.WritePuppetSyncState(currentState.pinWeight, currentState.muscleWeight);
                _lastSentState = currentState;
            }
        }
        // 원격 클라이언트: Networked 속성 읽어 로컬 PuppetMaster에 적용
        else if (!_networkPlayer.HasStateAuthority)
        {
            _networkPlayer.ReadPuppetSyncState(out var pinW, out var muscleW);
            puppetMaster.pinWeight = pinW;
            puppetMaster.muscleWeight = muscleW;
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
