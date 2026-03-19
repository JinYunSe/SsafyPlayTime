/*
 * 파일 개요:
 * - ItemFieldDropSpawner 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
using System;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 필드 아이템 배치/드랍 스폰 흐름을 오케스트레이션한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldDropSpawner : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform spawnCenter;
        [SerializeField] private Transform spawnRoot;

        [SerializeField] private float spawnHeightOffset = 0.2f;

        [Header("드랍 이벤트")]
        [SerializeField] private bool spawnWhenItemDropped = true;
        [SerializeField] private float droppedScatterRadius = 1f;
        [SerializeField] private bool applyDropImpulse = true;
        [SerializeField] private float dropImpulse = 2.2f;

        [Header("판정")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private bool useGroundRaycast = true;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog = true;

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();
        private ItemFieldDropFactory _dropFactory;
        private NetworkRunner _runnerCache;
        private float _nextRunnerLookupTime;

        public event Action<ItemFieldDrop> FieldDropSpawned;
        public ItemRuntimeHost RuntimeHost => itemRuntimeHost;

        private void Awake()
        {
            ResolveReferences();
            InitializeFactory();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindEvents();
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            itemRuntimeHost = runtimeHost;
        }

        public void RefreshCatalogCache()
        {
            _catalogProvider.Invalidate();
        }

        public bool TrySpawnItem(string itemId, Vector3 worldPosition, out ItemFieldDrop spawnedDrop)
        {
            return TrySpawnItem(itemId, worldPosition, string.Empty, out spawnedDrop);
        }

        public bool TrySpawnItem(string itemId, Vector3 worldPosition, string instanceId, out ItemFieldDrop spawnedDrop)
        {
            spawnedDrop = null;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            if (!TryGetCatalog(out var catalog))
            {
                return false;
            }

            if (!catalog.TryGetDefinition(itemId, out var definition))
            {
                DebugLog($"Spawn failed: unknown itemId {itemId}");
                return false;
            }

            var resolvedPosition = ItemFieldPositionUtility.ResolveGroundPosition(
                worldPosition,
                useGroundRaycast,
                groundMask,
                spawnHeightOffset);

            if (TryGetRunner(out var runner) && runner != null && runner.IsRunning)
            {
                return TrySpawnNetworkedDefinition(
                    runner,
                    definition,
                    resolvedPosition,
                    instanceId,
                    false,
                    Vector3.zero,
                    out spawnedDrop);
            }

            spawnedDrop = SpawnDefinition(definition, resolvedPosition, false, Vector3.zero);
            if (spawnedDrop != null && !string.IsNullOrWhiteSpace(instanceId))
            {
                spawnedDrop.SetInstanceId(instanceId);
            }
            return spawnedDrop != null;
        }

        private void InitializeFactory()
        {
            _dropFactory = new ItemFieldDropFactory(new DefaultItemFieldPrefabResolver());
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = FindObjectOfType<ItemRuntimeHost>();
            }
        }

        private void BindEvents()
        {
            if (!spawnWhenItemDropped || itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.ItemDropped -= HandleItemDropped;
            itemRuntimeHost.ItemDropped += HandleItemDropped;
        }

        private void UnbindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.ItemDropped -= HandleItemDropped;
        }

        private void HandleItemDropped(string itemId, ItemDropReason reason)
        {
            if (ShouldSkipRuntimeDropSpawn())
            {
                DebugLog($"Skip runtime drop spawn in network mode: {itemId}");
                return;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (!TryGetCatalog(out var catalog))
            {
                return;
            }

            if (!catalog.TryGetDefinition(itemId, out var definition))
            {
                DebugLog($"Drop ignored: unknown itemId {itemId}");
                return;
            }

            var origin = itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null
                ? itemRuntimeHost.OwnerTransform.position
                : ResolveSpawnCenter();
            var randomOffset2D = UnityEngine.Random.insideUnitCircle * Mathf.Max(0f, droppedScatterRadius);
            var candidate = origin + new Vector3(randomOffset2D.x, spawnHeightOffset, randomOffset2D.y);
            var resolvedPosition = ItemFieldPositionUtility.ResolveGroundPosition(
                candidate,
                useGroundRaycast,
                groundMask,
                spawnHeightOffset);

            var impulseDirection = (Vector3.up + new Vector3(randomOffset2D.x, 0f, randomOffset2D.y).normalized * 0.35f).normalized;
            var spawned = SpawnDefinition(definition, resolvedPosition, applyDropImpulse, impulseDirection);
            if (spawned != null)
            {
                DebugLog($"Dropped to field: {itemId} ({reason})");
            }
        }

        private ItemFieldDrop SpawnDefinition(
            ItemDefinition definition,
            Vector3 position,
            bool useImpulse,
            Vector3 impulseDirection)
        {
            if (_dropFactory == null)
            {
                InitializeFactory();
            }

            var parent = spawnRoot;
            var fieldDrop = _dropFactory.Create(definition, position, parent);
            if (fieldDrop == null)
            {
                return null;
            }

            if (useImpulse)
            {
                ApplyDropImpulse(fieldDrop.gameObject, impulseDirection);
            }

            FieldDropSpawned?.Invoke(fieldDrop);
            return fieldDrop;
        }

        private bool TrySpawnNetworkedDefinition(
            NetworkRunner runner,
            ItemDefinition definition,
            Vector3 position,
            string instanceId,
            bool useImpulse,
            Vector3 impulseDirection,
            out ItemFieldDrop fieldDrop)
        {
            fieldDrop = null;
            if (runner == null || !runner.IsRunning)
            {
                return false;
            }

            if (!runner.IsServer)
            {
                DebugLog($"Network spawn denied without server authority: {definition.Master.ItemId}");
                return false;
            }

            var prefab = ItemFieldDropFactory.TryLoadNetworkedDropPrefab(_prefabResolver);
            if (prefab == null)
            {
                DebugLog($"Network spawn failed: missing networked wrapper prefab for {definition.Master.ItemId}");
                return false;
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                DebugLog($"Network spawn failed: wrapper prefab missing NetworkObject for {definition.Master.ItemId}");
                return false;
            }

            var requestedInstanceId = string.IsNullOrWhiteSpace(instanceId)
                ? Guid.NewGuid().ToString("N")
                : instanceId;

            var spawnedObject = runner.Spawn(
                prefab,
                position,
                Quaternion.identity,
                onBeforeSpawned: (_, obj) =>
                {
                    var drop = obj.GetComponent<ItemFieldDrop>();
                    if (drop != null)
                    {
                        drop.SetItemId(definition.Master.ItemId);
                        drop.SetInstanceId(requestedInstanceId);
                    }

                    var networkedDrop = obj.GetComponent<NetworkedItemFieldDrop>();
                    if (networkedDrop != null)
                    {
                        networkedDrop.InitializeMetadata(definition.Master.ItemId);
                    }
                });

            if (spawnedObject == null)
            {
                DebugLog($"Network spawn failed: runner returned null for {definition.Master.ItemId}");
                return false;
            }

            fieldDrop = spawnedObject.GetComponent<ItemFieldDrop>();
            if (fieldDrop == null)
            {
                DebugLog($"Network spawn failed: spawned prefab missing ItemFieldDrop {definition.Master.ItemId}");
                runner.Despawn(spawnedObject);
                return false;
            }

            if (useImpulse)
            {
                ApplyDropImpulse(fieldDrop.gameObject, impulseDirection);
            }

            FieldDropSpawned?.Invoke(fieldDrop);
            return true;
        }

        private void ApplyDropImpulse(GameObject target, Vector3 impulseDirection)
        {
            if (target == null)
            {
                return;
            }

            var body = target.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = target.AddComponent<Rigidbody>();
            }

            var direction = impulseDirection.sqrMagnitude > 0.0001f ? impulseDirection.normalized : Vector3.up;
            body.AddForce(direction * Mathf.Max(0f, dropImpulse), ForceMode.VelocityChange);
        }

        private bool TryGetCatalog(out ItemCatalog catalog)
        {
            var options = itemRuntimeHost != null
                ? itemRuntimeHost.CatalogLoadOptions
                : ItemCatalogLoader.CreateDefaultOptions();
            if (_catalogProvider.TryGetCatalog(options, out catalog, out var error))
            {
                return true;
            }

            DebugLog($"Catalog load failed: {error}");
            return false;
        }

        private Vector3 ResolveSpawnCenter()
        {
            if (spawnCenter != null)
            {
                return spawnCenter.position;
            }

            if (itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null)
            {
                return itemRuntimeHost.OwnerTransform.position;
            }

            return transform.position;
        }

        private bool ShouldSkipRuntimeDropSpawn()
        {
            if (!TryGetRunner(out var runner))
            {
                return false;
            }

            // 한국어: 네트워크 플레이 중에는 NetworkPlayer 복제 경로가 드랍 생성을 담당하므로
            // ItemDropped 이벤트 기반 자동 스폰은 막아서 로컬 중복 생성을 방지한다.
            return runner != null && runner.IsRunning;
        }

        private bool TryGetRunner(out NetworkRunner runner)
        {
            if (_runnerCache != null && _runnerCache.IsRunning)
            {
                runner = _runnerCache;
                return true;
            }

            if (Time.unscaledTime < _nextRunnerLookupTime)
            {
                runner = null;
                return false;
            }

            _nextRunnerLookupTime = Time.unscaledTime + 1f;
            _runnerCache = FindObjectOfType<NetworkRunner>(true);
            if (_runnerCache != null && _runnerCache.IsRunning)
            {
                runner = _runnerCache;
                return true;
            }

            runner = null;
            return false;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemFieldDropSpawner] {message}", this);
        }
    }
}

