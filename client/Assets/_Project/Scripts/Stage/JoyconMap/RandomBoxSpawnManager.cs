using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 스폰 존에 랜덤 박스를 주기적으로 스폰하는 관리자.
/// ItemRandomSpawnManager의 스폰 존 해석 로직을 재사용하며,
/// JoyconGimmickData에서 설정을 받아 초기화된다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class RandomBoxSpawnManager : NetworkBehaviour
{
    private const string DefaultSpawnZoneRootName = "ItemSpawnZone";
    private const string DefaultSpawnZonePrefix = "ItemSpawnZone_";
    private const int DefaultMaxActiveBoxCount = 10;

    // ── 스폰 설정 ──
    [Header("스폰 설정")]
    [SerializeField] private GameObject randomBoxPrefab;
    [SerializeField] private float spawnIntervalSec = 5f;
    [SerializeField] private int maxActiveBoxCount = DefaultMaxActiveBoxCount;

    // ── 스폰 존 ──
    [Header("스폰 존")]
    [SerializeField] private Transform spawnZoneRoot;
    [SerializeField] private string spawnZoneRootName = DefaultSpawnZoneRootName;
    [SerializeField] private string spawnZoneNamePrefix = DefaultSpawnZonePrefix;
    [SerializeField] private float spawnHeightOffset = 1.0f;

    // ── 박스 파라미터 (JoyconGimmickData에서 주입) ──
    [Header("박스 파라미터")]
    [SerializeField] private float boxDespawnDelay = 5f;

    // ── 디버그 ──
    [Header("디버그")]
    [SerializeField] private bool enableDebugLog = true;

    // ── 네트워크 상태 ──
    [Networked] private NetworkBool SpawnLoopEnabled { get; set; }
    [Networked] private TickTimer NextSpawnTimer { get; set; }

    // ── 로컬 상태 ──
    private readonly List<Transform> _spawnPoints = new();
    private readonly List<NetworkedRandomBox> _managedBoxes = new();
    private bool _initialized;

    // ────────────────────────────────────────
    // 외부 초기화 (MapManager에서 호출)
    // ────────────────────────────────────────

    /// <summary>
    /// JoyconGimmickData로부터 설정을 받아 초기화 및 스폰 루프 시작.
    /// </summary>
    public void Initialize(JoyconGimmickData data)
    {
        if (data == null)
            return;

        if (data.boxPrefab != null)
            randomBoxPrefab = data.boxPrefab;

        boxDespawnDelay = data.boxDestroyDelay;
        _initialized = true;

        RefreshSpawnPoints();
        StartSpawnLoop();

        DebugLog($"Initialized from JoyconGimmickData: prefab={randomBoxPrefab?.name}, despawnDelay={boxDespawnDelay:F1}s.");
    }

    // ────────────────────────────────────────
    // Fusion 라이프사이클
    // ────────────────────────────────────────

    public override void Spawned()
    {
        RefreshSpawnPoints();
    }

    public override void FixedUpdateNetwork()
    {
        CleanupDeadEntries();

        if (!HasStateAuthority)
            return;

        if (!SpawnLoopEnabled)
            return;

        if (_spawnPoints.Count == 0)
            RefreshSpawnPoints();

        if (!NextSpawnTimer.IsRunning)
        {
            ScheduleNextSpawn();
            return;
        }

        if (NextSpawnTimer.Expired(Runner))
        {
            SpawnRandomBox();
            ScheduleNextSpawn();
        }
    }

    // ────────────────────────────────────────
    // 스폰 루프 제어
    // ────────────────────────────────────────

    public void StartSpawnLoop()
    {
        if (!HasStateAuthority)
            return;

        SpawnLoopEnabled = true;
        ScheduleNextSpawn();
        DebugLog("Spawn loop started.");
    }

    public void StopSpawnLoop()
    {
        if (!HasStateAuthority)
            return;

        SpawnLoopEnabled = false;
        NextSpawnTimer = default;
        DebugLog("Spawn loop stopped.");
    }

    private void ScheduleNextSpawn()
    {
        if (Runner == null)
            return;

        NextSpawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.5f, spawnIntervalSec));
    }

    // ────────────────────────────────────────
    // 스폰 존 해석 (ItemRandomSpawnManager와 동일 로직)
    // ────────────────────────────────────────

    private void RefreshSpawnPoints()
    {
        _spawnPoints.Clear();

        var root = ResolveSpawnZoneRoot();
        if (root == null)
        {
            DebugLog("SpawnZone root was not found.");
            return;
        }

        var directChildren = new List<Transform>();
        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child == null)
                continue;

            directChildren.Add(child);
            if (string.IsNullOrWhiteSpace(spawnZoneNamePrefix) ||
                child.name.StartsWith(spawnZoneNamePrefix, StringComparison.Ordinal))
            {
                _spawnPoints.Add(child);
            }
        }

        if (_spawnPoints.Count == 0)
            _spawnPoints.AddRange(directChildren);

        DebugLog($"Cached spawn points: count={_spawnPoints.Count}");
    }

    private Transform ResolveSpawnZoneRoot()
    {
        if (spawnZoneRoot != null)
            return spawnZoneRoot;

        if (string.Equals(name, spawnZoneRootName, StringComparison.Ordinal))
        {
            spawnZoneRoot = transform;
            return spawnZoneRoot;
        }

        var candidates = FindObjectsOfType<Transform>(true);
        for (var i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            if (candidate != null &&
                string.Equals(candidate.name, spawnZoneRootName, StringComparison.Ordinal))
            {
                spawnZoneRoot = candidate;
                return spawnZoneRoot;
            }
        }

        return null;
    }

    // ────────────────────────────────────────
    // 랜덤 박스 스폰
    // ────────────────────────────────────────

    private void SpawnRandomBox()
    {
        if (Runner == null || _spawnPoints.Count == 0 || randomBoxPrefab == null)
        {
            DebugLog("Spawn skipped: missing prefab, spawn points, or runner.");
            return;
        }

        var activeCount = CountActiveBoxes();
        if (activeCount >= Mathf.Max(1, maxActiveBoxCount))
        {
            DebugLog($"Spawn skipped: active box count ({activeCount}) reached limit ({maxActiveBoxCount}).");
            return;
        }

        // 네트워크 프리팹의 NetworkObject 확인
        if (randomBoxPrefab.GetComponent<NetworkObject>() == null)
        {
            DebugLog("Spawn skipped: randomBoxPrefab is missing NetworkObject component.");
            return;
        }

        // 랜덤 스폰 포인트 선택
        var spawnPoint = _spawnPoints[UnityEngine.Random.Range(0, _spawnPoints.Count)];
        var spawnPosition = ResolveSpawnPosition(spawnPoint);

        var spawnedObject = Runner.Spawn(
            randomBoxPrefab,
            spawnPosition,
            Quaternion.identity);

        if (spawnedObject == null)
        {
            DebugLog("Runner.Spawn failed for random box.");
            return;
        }

        var randomBox = spawnedObject.GetComponent<NetworkedRandomBox>();
        if (randomBox != null)
        {
            _managedBoxes.Add(randomBox);
        }

        DebugLog($"Spawned random box at {spawnPosition} (zone: {spawnPoint.name}).");
    }

    private Vector3 ResolveSpawnPosition(Transform spawnPoint)
    {
        if (spawnPoint == null)
            return Vector3.up * spawnHeightOffset;

        var boxCollider = spawnPoint.GetComponent<BoxCollider>();
        if (boxCollider != null)
        {
            var transformRef = boxCollider.transform;
            var scaledSize = Vector3.Scale(boxCollider.size, transformRef.lossyScale);
            var worldCenter = transformRef.TransformPoint(boxCollider.center);
            var extents = scaledSize * 0.5f;
            var min = worldCenter - extents;
            var max = worldCenter + extents;

            var randomX = UnityEngine.Random.Range(min.x, max.x);
            var randomZ = UnityEngine.Random.Range(min.z, max.z);
            var y = max.y + spawnHeightOffset;
            return new Vector3(randomX, y, randomZ);
        }

        var collider = spawnPoint.GetComponent<Collider>();
        if (collider != null)
        {
            var bounds = collider.bounds;
            var randomX = UnityEngine.Random.Range(bounds.min.x, bounds.max.x);
            var randomZ = UnityEngine.Random.Range(bounds.min.z, bounds.max.z);
            var y = bounds.max.y + spawnHeightOffset;
            return new Vector3(randomX, y, randomZ);
        }

        return spawnPoint.position + Vector3.up * spawnHeightOffset;
    }

    // ────────────────────────────────────────
    // 관리
    // ────────────────────────────────────────

    private int CountActiveBoxes()
    {
        var count = 0;
        for (var i = _managedBoxes.Count - 1; i >= 0; i--)
        {
            if (_managedBoxes[i] == null)
            {
                _managedBoxes.RemoveAt(i);
                continue;
            }

            if (_managedBoxes[i].gameObject.activeInHierarchy)
                count++;
        }

        return count;
    }

    private void CleanupDeadEntries()
    {
        for (var i = _managedBoxes.Count - 1; i >= 0; i--)
        {
            if (_managedBoxes[i] == null)
                _managedBoxes.RemoveAt(i);
        }
    }

    // ────────────────────────────────────────
    // 디버그
    // ────────────────────────────────────────

    private void DebugLog(string message)
    {
        if (!enableDebugLog)
            return;

        Debug.Log($"[RandomBoxSpawnManager] {message}", this);
    }
}
