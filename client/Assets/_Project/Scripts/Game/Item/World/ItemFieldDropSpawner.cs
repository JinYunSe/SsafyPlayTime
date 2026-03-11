/*
 * 파일 개요:
 * - ItemFieldDropSpawner 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
using System;
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
        private ItemFieldDropFactory _dropFactory;

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

            var fieldDrop = _dropFactory.Create(definition, position, transform);
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

