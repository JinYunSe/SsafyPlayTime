using Fusion;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class SeaLevelController : NetworkBehaviour
{
    public readonly struct MigrationState
    {
        public MigrationState(float waterLevelY)
        {
            WaterLevelY = waterLevelY;
        }

        public float WaterLevelY { get; }
    }

    private const int PhaseRising = 1;
    private const int PhaseCompleted = 2;

    [Header("Height Settings")]
    // 상승 속도
    public float sinkingSpeed = 0.05f;
    // 도달할 최대 높이
    public float maxWaterLevel = 5.0f;
    // 상승을 시작할 주기 (예: 1초마다 조금씩)
    public float checkInterval = 1.0f;
    // 한 스텝을 부드럽게 올리는 시간
    public float riseDuration = 0.5f;

    [Networked] private int WaterPhase { get; set; }
    [Networked] private int PhaseStartTick { get; set; }
    [Networked] private float PhaseStartY { get; set; }
    [Networked] private NetworkBool IsInitialized { get; set; }

    private float _localPhaseStartTime;
    private float _localPhaseStartY;
    private int _localWaterPhase;
    private bool _warnedMissingNetworkObject;

    private float PhaseDuration => Mathf.Max(0f, riseDuration) + Mathf.Max(0f, checkInterval);

    private void Awake()
    {
        _localPhaseStartTime = Time.time;
        _localPhaseStartY = transform.position.y;
        _localWaterPhase = transform.position.y >= maxWaterLevel ? PhaseCompleted : PhaseRising;

        // 방장(StateAuthority)이 나가도 이 오브젝트가 제거되지 않도록 설정한다.
        // Fusion은 기본적으로 StateAuthority가 떠나면 오브젝트를 파괴할 수 있으므로
        // 씬에 배치된 환경 오브젝트는 이 플래그를 해제해야 한다.
        var no = GetComponent<NetworkObject>();
        if (no != null)
            no.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            EnsureNetworkStateInitialized();
        }

        ApplyWaterHeight(EvaluateNetworkWaterHeight());
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
        {
            EnsureNetworkStateInitialized();
            AdvanceNetworkPhaseIfNeeded();
        }

        ApplyWaterHeight(EvaluateNetworkWaterHeight());
    }

    public override void Render()
    {
        if (Runner != null && Object != null && Object.IsValid)
        {
            var h = EvaluateNetworkWaterHeight();
            ApplyWaterHeight(h);
            // 방장 이전 등으로 러너가 재시작될 때 로컬 폴백이 현재 수위에서 이어지도록 동기화한다.
            // Awake()에서 설정된 씬 초기 Y값 대신 마지막으로 적용된 수위를 사용하게 된다.
            _localPhaseStartY = h;
            _localPhaseStartTime = Time.time;
            _localWaterPhase = h >= maxWaterLevel ? PhaseCompleted : PhaseRising;
        }
    }

    public string GetMigrationKey()
    {
        return BuildHierarchyPath(transform);
    }

    public MigrationState CaptureMigrationState()
    {
        return new MigrationState(transform.position.y);
    }

    public void RestoreMigrationState(MigrationState state)
    {
        var restoredY = Mathf.Clamp(state.WaterLevelY, float.MinValue, maxWaterLevel);
        ApplyWaterHeight(restoredY);
        ResetLocalPhaseState(restoredY);

        if (Runner != null && Object != null && Object.IsValid && HasStateAuthority)
        {
            PhaseStartY = restoredY;
            PhaseStartTick = Runner.Tick;
            WaterPhase = restoredY >= maxWaterLevel ? PhaseCompleted : PhaseRising;
            IsInitialized = true;
        }
    }

    private void Update()
    {
        if (Runner != null && Object != null && Object.IsValid)
            return;

        if (!_warnedMissingNetworkObject && GetComponent<NetworkObject>() == null)
        {
            _warnedMissingNetworkObject = true;
            Debug.LogWarning($"[{nameof(SeaLevelController)}] NetworkObject is missing on '{name}'. Water level is running in local fallback mode only.");
        }

        AdvanceLocalPhaseIfNeeded();
        ApplyWaterHeight(EvaluateLocalWaterHeight());
    }

    private void EnsureNetworkStateInitialized()
    {
        if (IsInitialized)
            return;

        // _localPhaseStartY는 Render()에서 네트워크 수위와 항상 동기화된다.
        // 방장 이전 후 씬이 유지된 경우 transform.position.y보다 _localPhaseStartY가
        // 더 정확한 시작점이 된다. 씬이 재로드된 경우 RestoreEnvironmentStatesAfterMigration()이
        // Spawned() 이후에 올바른 수위로 덮어쓴다.
        var startY = _localPhaseStartY;
        PhaseStartY = startY;
        PhaseStartTick = Runner.Tick;
        WaterPhase = startY >= maxWaterLevel ? PhaseCompleted : PhaseRising;
        IsInitialized = true;
    }

    private void AdvanceNetworkPhaseIfNeeded()
    {
        if (WaterPhase == PhaseCompleted)
            return;

        var elapsed = GetNetworkPhaseElapsedSeconds();
        if (elapsed < PhaseDuration)
            return;

        var nextStartY = Mathf.Min(PhaseStartY + sinkingSpeed, maxWaterLevel);
        PhaseStartY = nextStartY;
        PhaseStartTick = Runner.Tick;
        WaterPhase = nextStartY >= maxWaterLevel ? PhaseCompleted : PhaseRising;
    }

    private float EvaluateNetworkWaterHeight()
    {
        if (Runner == null || !IsInitialized)
            return transform.position.y;

        if (WaterPhase == PhaseCompleted)
            return maxWaterLevel;

        var targetY = Mathf.Min(PhaseStartY + sinkingSpeed, maxWaterLevel);
        var duration = Mathf.Max(0.0001f, riseDuration);
        var elapsed = GetNetworkPhaseElapsedSeconds();
        var t = Mathf.Clamp01(elapsed / duration);
        return Mathf.Lerp(PhaseStartY, targetY, t);
    }

    private float GetNetworkPhaseElapsedSeconds()
    {
        if (Runner == null)
            return 0f;

        int startTick = PhaseStartTick;
        int currentTick = Runner.Tick;
        int deltaTicks = Mathf.Max(0, currentTick - startTick);
        return deltaTicks / (float)Runner.TickRate;
    }

    private void AdvanceLocalPhaseIfNeeded()
    {
        if (_localWaterPhase == PhaseCompleted)
            return;

        var elapsed = Time.time - _localPhaseStartTime;
        if (elapsed < PhaseDuration)
            return;

        _localPhaseStartY = Mathf.Min(_localPhaseStartY + sinkingSpeed, maxWaterLevel);
        _localPhaseStartTime = Time.time;
        _localWaterPhase = _localPhaseStartY >= maxWaterLevel ? PhaseCompleted : PhaseRising;
    }

    private float EvaluateLocalWaterHeight()
    {
        if (_localWaterPhase == PhaseCompleted)
            return maxWaterLevel;

        var targetY = Mathf.Min(_localPhaseStartY + sinkingSpeed, maxWaterLevel);
        var duration = Mathf.Max(0.0001f, riseDuration);
        var elapsed = Time.time - _localPhaseStartTime;
        var t = Mathf.Clamp01(elapsed / duration);
        return Mathf.Lerp(_localPhaseStartY, targetY, t);
    }

    private void ApplyWaterHeight(float y)
    {
        var position = transform.position;
        if (Mathf.Approximately(position.y, y))
            return;

        position.y = y;
        transform.position = position;
    }

    private void ResetLocalPhaseState(float currentY)
    {
        _localPhaseStartTime = Time.time;
        _localPhaseStartY = currentY;
        _localWaterPhase = currentY >= maxWaterLevel ? PhaseCompleted : PhaseRising;
    }

    private static string BuildHierarchyPath(Transform target)
    {
        if (target == null)
            return string.Empty;

        var path = target.name;
        var current = target.parent;
        while (current != null)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }

        return path;
    }
}
