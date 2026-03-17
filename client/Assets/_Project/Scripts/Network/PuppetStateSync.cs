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
    int _lastAppliedPuppetState = -1;
    int _lastAppliedPuppetMode = -1;

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
                _networkPlayer.WritePuppetSyncState(
                    currentState.pinWeight,
                    currentState.muscleWeight,
                    (int)currentState.state,
                    (int)currentState.mode);
                _lastSentState = currentState;
            }
        }
        // 원격 클라이언트: Networked 속성 읽어 로컬 PuppetMaster에 적용
        else
        {
            _networkPlayer.ReadPuppetSyncState(out var pinW, out var muscleW, out var pState, out var pMode);
            puppetMaster.pinWeight = pinW;
            puppetMaster.muscleWeight = muscleW;

            // PuppetMaster.State 동기화 (Alive ↔ Dead)
            if (pState != _lastAppliedPuppetState)
            {
                _lastAppliedPuppetState = pState;
                var targetState = (PuppetMaster.State)pState;
                if (puppetMaster.state != targetState)
                {
                    if (targetState == PuppetMaster.State.Dead && lifecycleManager != null)
                        lifecycleManager.Die();
                    else if (targetState == PuppetMaster.State.Alive)
                        puppetMaster.state = PuppetMaster.State.Alive;
                }
            }

            // PuppetMaster.Mode 동기화 — 비호스트에서는 스킵.
            // 비호스트의 PuppetMaster mode는 NetworkPlayer.SynchronizePhysicsPresentationState()에서
            // presentation 상태(기절/잡힘 ↔ 정상)에 따라 직접 제어한다.
            // 호스트는 항상 Active인데 비호스트에서 그대로 동기화하면
            // frozen kinematic muscle의 Map()이 Animator 출력을 덮어쓰는 문제가 발생한다.

            // IsActiveRagdoll 상태 반영 — 원격에서도 기절/회복 상태를 알 수 있도록
            if (_networkPlayer.NetworkedIsActiveRagdoll != _networkPlayer.IsActiveRagdoll)
            {
                // NetworkPlayer 내부 _isActiveRagdoll은 private이므로
                // BehaviourPuppet 상태로 간접 제어
                if (!_networkPlayer.NetworkedIsActiveRagdoll && behaviourPuppet != null)
                {
                    // 기절 상태: puppet 모드에서 빠져나감
                    // pinWeight/muscleWeight가 0으로 내려가면 자연스럽게 넘어짐
                }
            }
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
               Mathf.Abs(a.pinWeight - b.pinWeight) > 0.05f ||
               Mathf.Abs(a.muscleWeight - b.muscleWeight) > 0.05f;
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
