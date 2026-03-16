/*
 * 파일 개요:
 * - 맵의 ItemSpawnZone 하위 포인트 중 하나에 아이템을 주기적으로 랜덤 생성한다.
 * - 기능 자체는 독립형으로 두고, 나중에 게임 시작/종료 로직에서 ON/OFF만 제어할 수 있게 만든다.
 * - 테스트 중에는 enableTestSpawnLoop=true 로 두면 3초마다 아이템을 계속 누적 스폰한다.
 */
using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 맵 아이템 랜덤 스폰을 네트워크 동기화와 함께 관리한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(ItemFieldDropSpawner))]
    public sealed class ItemRandomSpawnManager : NetworkBehaviour
    {
        private const string DefaultSpawnZoneRootName = "ItemSpawnZone";
        private const string DefaultSpawnZonePrefix = "ItemSpawnZone_";

        [Header("스폰 제어")]
        [SerializeField] private bool enableTestSpawnLoop = true;
        [SerializeField] private float spawnIntervalSec = 3f;
        [SerializeField] private bool keepSpawnedItemStatic = false;
        [SerializeField] private bool enableDebugLog = true;

        [Header("스폰 위치")]
        [SerializeField] private Transform spawnZoneRoot;
        [SerializeField] private string spawnZoneRootName = DefaultSpawnZoneRootName;
        [SerializeField] private string spawnZoneNamePrefix = DefaultSpawnZonePrefix;
        [SerializeField] private float spawnHeightOffset = 0.2f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private bool useGroundRaycast = true;

        [Header("참조")]
        [SerializeField] private ItemFieldDropSpawner itemFieldDropSpawner;

        [Networked] private NetworkBool NetworkSpawnLoopEnabled { get; set; }
        [Networked] private TickTimer NetworkNextSpawnTimer { get; set; }

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly List<Transform> _spawnPoints = new();
        private readonly List<ItemDefinition> _spawnableDefinitions = new();
        private readonly Dictionary<string, ItemFieldDrop> _managedDrops = new(StringComparer.Ordinal);

        public bool IsSpawnLoopEnabled => NetworkSpawnLoopEnabled;

        private void Awake()
        {
            ResolveReferences();
            RefreshSpawnPoints();
            RefreshSpawnableDefinitions();
        }

        public override void Spawned()
        {
            ResolveReferences();
            RefreshSpawnPoints();
            RefreshSpawnableDefinitions();

            if (HasStateAuthority)
            {
                NetworkSpawnLoopEnabled = enableTestSpawnLoop;
                NetworkNextSpawnTimer = default;
                if (NetworkSpawnLoopEnabled)
                {
                    ScheduleNextSpawn();
                }
            }
        }

        public override void FixedUpdateNetwork()
        {
            CleanupDeadEntries();

            if (!HasStateAuthority)
            {
                return;
            }

            if (_spawnPoints.Count == 0)
            {
                RefreshSpawnPoints();
            }

            if (_spawnableDefinitions.Count == 0)
            {
                RefreshSpawnableDefinitions();
            }

            if (!NetworkSpawnLoopEnabled)
            {
                return;
            }

            if (!NetworkNextSpawnTimer.IsRunning)
            {
                ScheduleNextSpawn();
                return;
            }

            if (NetworkNextSpawnTimer.Expired(Runner))
            {
                SpawnRandomItem();
                ScheduleNextSpawn();
            }
        }

        public void SetSpawnLoopEnabled(bool enabled)
        {
            if (!HasStateAuthority)
            {
                DebugLog("StateAuthority만 스폰 루프를 변경할 수 있다.");
                return;
            }

            NetworkSpawnLoopEnabled = enabled;
            NetworkNextSpawnTimer = enabled
                ? TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, spawnIntervalSec))
                : default;
        }

        /// <summary>
        /// ImmediateDeath 같은 스테이지 기믹이 호출해 관리 중인 아이템을 제거할 때 사용한다.
        /// </summary>
        public void HandleManagedFieldDropEnteredDeathZone(ItemFieldDrop drop)
        {
            if (drop == null || string.IsNullOrWhiteSpace(drop.InstanceId))
            {
                return;
            }

            var instanceId = drop.InstanceId;
            if (!HasStateAuthority)
            {
                // 한국어: 프록시에서도 자기 로컬 복제품은 바로 없애 둔다.
                DestroyManagedFieldDropLocal(instanceId);
                return;
            }

            DestroyManagedFieldDropLocal(instanceId);
            RPC_DestroyManagedFieldDrop(instanceId);
            DebugLog($"ImmediateDeath 제거: drop={instanceId}");
        }

        public bool IsManagedFieldDrop(ItemFieldDrop drop)
        {
            if (drop == null || string.IsNullOrWhiteSpace(drop.InstanceId))
            {
                return false;
            }

            return _managedDrops.ContainsKey(drop.InstanceId);
        }

        private void ResolveReferences()
        {
            if (itemFieldDropSpawner == null)
            {
                itemFieldDropSpawner = GetComponent<ItemFieldDropSpawner>();
            }
        }

        private void RefreshSpawnPoints()
        {
            _spawnPoints.Clear();
            var root = ResolveSpawnZoneRoot();
            if (root == null)
            {
                DebugLog("ItemSpawnZone 루트를 찾지 못했다.");
                return;
            }

            var directChildren = new List<Transform>();
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                directChildren.Add(child);
                if (string.IsNullOrWhiteSpace(spawnZoneNamePrefix) ||
                    child.name.StartsWith(spawnZoneNamePrefix, StringComparison.Ordinal))
                {
                    _spawnPoints.Add(child);
                }
            }

            if (_spawnPoints.Count == 0)
            {
                _spawnPoints.AddRange(directChildren);
            }

            DebugLog($"스폰 포인트 캐시 완료: count={_spawnPoints.Count}");
        }

        private Transform ResolveSpawnZoneRoot()
        {
            if (spawnZoneRoot != null)
            {
                return spawnZoneRoot;
            }

            if (string.Equals(name, spawnZoneRootName, StringComparison.Ordinal))
            {
                spawnZoneRoot = transform;
                return spawnZoneRoot;
            }

            var candidates = FindObjectsOfType<Transform>(true);
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!string.Equals(candidate.name, spawnZoneRootName, StringComparison.Ordinal))
                {
                    continue;
                }

                spawnZoneRoot = candidate;
                return spawnZoneRoot;
            }

            return null;
        }

        private void RefreshSpawnableDefinitions()
        {
            _spawnableDefinitions.Clear();

            var options = ItemCatalogLoader.CreateDefaultOptions();
            if (!_catalogProvider.TryGetCatalog(options, out var catalog, out var error))
            {
                DebugLog($"아이템 카탈로그 로드 실패: {error}");
                return;
            }

            foreach (var pair in catalog.Definitions)
            {
                var definition = pair.Value;
                if (definition == null)
                {
                    continue;
                }

                if (!definition.Master.Enabled)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.Master.ItemId) ||
                    string.IsNullOrWhiteSpace(definition.Master.PrefabPath))
                {
                    continue;
                }

                _spawnableDefinitions.Add(definition);
            }

            DebugLog($"스폰 가능 아이템 캐시 완료: count={_spawnableDefinitions.Count}");
        }

        private void ScheduleNextSpawn()
        {
            if (Runner == null)
            {
                return;
            }

            NetworkNextSpawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, spawnIntervalSec));
            DebugLog($"다음 스폰 예약: interval={spawnIntervalSec:F1}s");
        }

        private void SpawnRandomItem()
        {
            if (_spawnPoints.Count == 0 || _spawnableDefinitions.Count == 0 || itemFieldDropSpawner == null)
            {
                DebugLog("스폰 실패: 포인트 또는 아이템 목록 또는 스포너가 준비되지 않았다.");
                return;
            }

            var spawnPoint = _spawnPoints[UnityEngine.Random.Range(0, _spawnPoints.Count)];
            var definition = _spawnableDefinitions[UnityEngine.Random.Range(0, _spawnableDefinitions.Count)];
            var candidate = ResolveSpawnCandidate(spawnPoint);
            var resolvedPosition = ItemFieldPositionUtility.ResolveGroundPosition(
                candidate,
                useGroundRaycast,
                groundMask,
                spawnHeightOffset);
            var instanceId = Guid.NewGuid().ToString("N");

            if (!TrySpawnManagedFieldDropLocal(definition.Master.ItemId, resolvedPosition, instanceId, subscribePickup: true, out _))
            {
                DebugLog($"스폰 실패: itemId={definition.Master.ItemId}");
                return;
            }

            RPC_SpawnManagedFieldDrop(definition.Master.ItemId, resolvedPosition, instanceId);
            DebugLog($"아이템 스폰 완료: itemId={definition.Master.ItemId}, point={spawnPoint.name}, position={resolvedPosition}");
        }

        private Vector3 ResolveSpawnCandidate(Transform spawnPoint)
        {
            if (spawnPoint == null)
            {
                return Vector3.up * Mathf.Max(0f, spawnHeightOffset);
            }

            var fallback = spawnPoint.position + Vector3.up * Mathf.Max(0f, spawnHeightOffset);

            // 한국어: ItemSpawnZone용 BoxCollider는 물리/공격 판정에는 쓰지 않고,
            // 스폰 영역 크기 데이터로만 사용한다. 비활성 Collider도 직접 계산해 활용한다.
            var zoneBox = spawnPoint.GetComponent<BoxCollider>();
            if (zoneBox != null)
            {
                return ResolveBoxColliderCandidate(zoneBox);
            }

            var zoneCollider = spawnPoint.GetComponent<Collider>();
            if (zoneCollider == null)
            {
                return fallback;
            }

            var bounds = zoneCollider.bounds;
            var randomX = UnityEngine.Random.Range(bounds.min.x, bounds.max.x);
            var randomZ = UnityEngine.Random.Range(bounds.min.z, bounds.max.z);
            var y = bounds.max.y + Mathf.Max(0f, spawnHeightOffset);
            return new Vector3(randomX, y, randomZ);
        }

        private Vector3 ResolveBoxColliderCandidate(BoxCollider boxCollider)
        {
            var transformRef = boxCollider.transform;
            var scaledSize = Vector3.Scale(boxCollider.size, transformRef.lossyScale);
            var worldCenter = transformRef.TransformPoint(boxCollider.center);
            var extents = scaledSize * 0.5f;
            var min = worldCenter - extents;
            var max = worldCenter + extents;

            var randomX = UnityEngine.Random.Range(min.x, max.x);
            var randomZ = UnityEngine.Random.Range(min.z, max.z);
            var y = max.y + Mathf.Max(0f, spawnHeightOffset);
            return new Vector3(randomX, y, randomZ);
        }

        private bool TrySpawnManagedFieldDropLocal(
            string itemId,
            Vector3 worldPosition,
            string instanceId,
            bool subscribePickup,
            out ItemFieldDrop spawnedDrop)
        {
            spawnedDrop = null;

            if (_managedDrops.TryGetValue(instanceId, out var existing) &&
                existing != null &&
                !existing.IsPickedUp)
            {
                spawnedDrop = existing;
                return true;
            }

            if (!itemFieldDropSpawner.TrySpawnItem(itemId, worldPosition, instanceId, out spawnedDrop) ||
                spawnedDrop == null)
            {
                return false;
            }

            ConfigureManagedFieldDrop(spawnedDrop);
            RegisterManagedDrop(spawnedDrop, subscribePickup);
            return true;
        }

        private void RegisterManagedDrop(ItemFieldDrop fieldDrop, bool subscribePickup)
        {
            if (fieldDrop == null || string.IsNullOrWhiteSpace(fieldDrop.InstanceId))
            {
                return;
            }

            if (subscribePickup)
            {
                fieldDrop.PickedUp -= HandleManagedDropPickedUp;
                fieldDrop.PickedUp += HandleManagedDropPickedUp;
            }

            _managedDrops[fieldDrop.InstanceId] = fieldDrop;
        }

        private void ConfigureManagedFieldDrop(ItemFieldDrop fieldDrop)
        {
            if (fieldDrop == null || !keepSpawnedItemStatic)
            {
                return;
            }

            var body = fieldDrop.GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void HandleManagedDropPickedUp(ItemFieldDrop drop)
        {
            if (drop == null)
            {
                return;
            }

            _managedDrops.Remove(drop.InstanceId);

            if (!HasStateAuthority)
            {
                return;
            }

            RPC_DestroyManagedFieldDrop(drop.InstanceId);
            DebugLog($"관리 스폰 아이템 습득됨: drop={drop.InstanceId}, itemId={drop.ItemId}");
        }

        private void DestroyManagedFieldDropLocal(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            if (_managedDrops.TryGetValue(instanceId, out var managedDrop))
            {
                _managedDrops.Remove(instanceId);
                if (managedDrop != null)
                {
                    managedDrop.PickedUp -= HandleManagedDropPickedUp;
                    Destroy(managedDrop.gameObject);
                    return;
                }
            }

            var fallback = FindFieldDropByInstanceId(instanceId);
            if (fallback != null)
            {
                Destroy(fallback.gameObject);
            }
        }

        private void CleanupDeadEntries()
        {
            if (_managedDrops.Count == 0)
            {
                return;
            }

            var deadIds = ListPool<string>.Get();
            try
            {
                foreach (var pair in _managedDrops)
                {
                    if (pair.Value == null || pair.Value.IsPickedUp)
                    {
                        deadIds.Add(pair.Key);
                    }
                }

                for (var i = 0; i < deadIds.Count; i++)
                {
                    _managedDrops.Remove(deadIds[i]);
                }
            }
            finally
            {
                ListPool<string>.Release(deadIds);
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SpawnManagedFieldDrop(string itemId, Vector3 worldPosition, string instanceId)
        {
            if (HasStateAuthority)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(instanceId))
            {
                return;
            }

            if (itemFieldDropSpawner == null)
            {
                ResolveReferences();
            }

            if (itemFieldDropSpawner == null)
            {
                return;
            }

            TrySpawnManagedFieldDropLocal(itemId, worldPosition, instanceId, subscribePickup: false, out _);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_DestroyManagedFieldDrop(string instanceId)
        {
            if (HasStateAuthority)
            {
                return;
            }

            DestroyManagedFieldDropLocal(instanceId);
        }

        private static ItemFieldDrop FindFieldDropByInstanceId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            var drops = FindObjectsOfType<ItemFieldDrop>(true);
            for (var i = 0; i < drops.Length; i++)
            {
                var drop = drops[i];
                if (drop == null)
                {
                    continue;
                }

                if (string.Equals(drop.InstanceId, instanceId, StringComparison.Ordinal))
                {
                    return drop;
                }
            }

            return null;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemRandomSpawnManager] {message}", this);
        }

        /// <summary>
        /// 간단한 임시 리스트 풀. 아이템 스폰 매니저 내부에서만 사용한다.
        /// </summary>
        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new();

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>();
            }

            public static void Release(List<T> list)
            {
                if (list == null)
                {
                    return;
                }

                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
