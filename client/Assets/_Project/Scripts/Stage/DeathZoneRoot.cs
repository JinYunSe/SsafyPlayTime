using Fusion;
using UnityEngine;

// 데스존(DeathZone)의 높이를 시간에 따라 점진적으로 올리는 컴포넌트.
// NetworkBehaviour로 동작하며, 방장(StateAuthority)이 주기마다 Y 스케일을 증가시키고
// 모든 클라이언트에 동기화한다. 방장 교체(호스트 마이그레이션) 시에도 상태가 유지된다.
[RequireComponent(typeof(NetworkObject))]
public class DeathZoneRoot : NetworkBehaviour
{
    public readonly struct MigrationState
    {
        public MigrationState(float scaleY) => ScaleY = scaleY;
        public float ScaleY { get; }
    }

    [Header("Height Settings")]
    // 한 번에 올라가는 Y 스케일 증가량
    public float heightStep = 0.5f;

    // 스케일이 증가하는 시간 간격 (초)
    public float heightInterval = 5.0f;

    // 올라갈 수 있는 최대 Y 스케일 값
    public float maxHeight = 3.5f;

    [Networked] private float NetworkScaleY { get; set; }
    [Networked] private int PhaseStartTick { get; set; }
    [Networked] private NetworkBool IsInitialized { get; set; }

    // Runner가 없을 때 로컬 폴백으로 사용하는 상태.
    // Render()에서 네트워크 값과 항상 동기화되므로 방장 교체 후에도 올바른 시작값을 제공한다.
    private float _localScaleY;
    private float _localNextHeightTime;
    private bool _warnedMissingNetworkObject;

    // HasStateAuthority 없이 RestoreMigrationState()가 호출될 때
    // 복원값을 보관했다가 StateAuthority 확보 후 FixedUpdateNetwork()에서 적용한다.
    private float? _pendingRestoreScaleY;

    private void Awake()
    {
        _localScaleY = transform.localScale.y;
        _localNextHeightTime = Time.time + heightInterval;

        // 방장(StateAuthority)이 나가도 이 오브젝트가 제거되지 않도록 설정한다.
        var no = GetComponent<NetworkObject>();
        if (no != null)
            no.Flags &= ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
            EnsureInitialized();

        ApplyScale(Runner != null && IsInitialized ? NetworkScaleY : _localScaleY);
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority)
        {
            // 복원 대기 중인 마이그레이션 상태가 있으면 우선 적용한다.
            // EnsureInitialized()가 잘못된 초기값으로 먼저 설정된 경우에도 덮어쓴다.
            if (_pendingRestoreScaleY.HasValue)
                ApplyPendingRestore();
            else
                EnsureInitialized();

            AdvancePhaseIfNeeded();
        }

        ApplyScale(NetworkScaleY);
    }

    public override void Render()
    {
        if (Runner == null || Object == null || !Object.IsValid)
            return;

        var s = NetworkScaleY;
        ApplyScale(s);
        // 방장 교체 후 새 방장의 EnsureInitialized()가 올바른 값에서 시작하도록 동기화한다.
        _localScaleY = s;
        _localNextHeightTime = Time.time + heightInterval;
    }

    private void Update()
    {
        if (Runner != null && Object != null && Object.IsValid)
            return;

        if (!_warnedMissingNetworkObject && GetComponent<NetworkObject>() == null)
        {
            _warnedMissingNetworkObject = true;
            Debug.LogWarning($"[{nameof(DeathZoneRoot)}] NetworkObject이 없습니다 '{name}'. 로컬 폴백 모드로 동작합니다.");
        }

        // Runner 없이 동작하는 로컬 폴백 (방장 교체 과도기 등)
        if (_localScaleY < maxHeight && Time.time >= _localNextHeightTime)
        {
            _localScaleY = Mathf.Min(_localScaleY + heightStep, maxHeight);
            _localNextHeightTime = Time.time + heightInterval;
        }
        ApplyScale(_localScaleY);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 호스트 마이그레이션 지원
    // ──────────────────────────────────────────────────────────────────────────

    public string GetMigrationKey() => BuildHierarchyPath(transform);

    public MigrationState CaptureMigrationState() => new MigrationState(transform.localScale.y);

    public void RestoreMigrationState(MigrationState state)
    {
        var scaleY = Mathf.Clamp(state.ScaleY, 0f, maxHeight);
        ApplyScale(scaleY);
        _localScaleY = scaleY;
        _localNextHeightTime = Time.time + heightInterval;

        // 복원값을 항상 보관한다.
        // HasStateAuthority가 있으면 즉시 networked 상태에 반영하고,
        // 없으면 FixedUpdateNetwork()에서 StateAuthority 확보 후 적용한다.
        _pendingRestoreScaleY = scaleY;

        if (Runner != null && Object != null && Object.IsValid && HasStateAuthority)
            ApplyPendingRestore();
    }

    private void ApplyPendingRestore()
    {
        if (!_pendingRestoreScaleY.HasValue)
            return;

        var scaleY = _pendingRestoreScaleY.Value;
        _pendingRestoreScaleY = null;

        NetworkScaleY = scaleY;
        PhaseStartTick = Runner.Tick;
        IsInitialized = true;

        Debug.Log($"[{nameof(DeathZoneRoot)}] Applied pending migration restore on '{name}'. scaleY={scaleY:F3}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 내부 구현
    // ──────────────────────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (IsInitialized)
            return;

        // Render()에서 동기화된 _localScaleY를 초기값으로 사용한다.
        // 씬 재진입 시에는 RestoreMigrationState()가 올바른 값으로 덮어쓴다.
        NetworkScaleY = _localScaleY;
        PhaseStartTick = Runner.Tick;
        IsInitialized = true;
    }

    private void AdvancePhaseIfNeeded()
    {
        if (NetworkScaleY >= maxHeight)
            return;

        var elapsed = Mathf.Max(0, Runner.Tick - PhaseStartTick) / (float)Runner.TickRate;
        if (elapsed < heightInterval)
            return;

        NetworkScaleY = Mathf.Min(NetworkScaleY + heightStep, maxHeight);
        PhaseStartTick = Runner.Tick;
    }

    private void ApplyScale(float scaleY)
    {
        var scale = transform.localScale;
        if (Mathf.Approximately(scale.y, scaleY))
            return;

        scale.y = scaleY;
        transform.localScale = scale;
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
