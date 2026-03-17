using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ItemRandomSpawnManager : NetworkBehaviour
    {
        private const string DefaultSpawnZoneRootName = "ItemSpawnZone";
        private const string DefaultSpawnZonePrefix = "ItemSpawnZone_";

        [Header("Test")]
        [SerializeField] private bool enableTestSpawnLoop = true;
        [SerializeField] private float spawnIntervalSec = 3f;
        [SerializeField] private bool keepSpawnedItemStatic;
        [SerializeField] private bool enableDebugLog = true;

        [Header("Spawn Zones")]
        [SerializeField] private Transform spawnZoneRoot;
        [SerializeField] private string spawnZoneRootName = DefaultSpawnZoneRootName;
        [SerializeField] private string spawnZoneNamePrefix = DefaultSpawnZonePrefix;
        [SerializeField] private float spawnHeightOffset = 0.2f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private bool useGroundRaycast = true;

        [Networked] private NetworkBool NetworkSpawnLoopEnabled { get; set; }
        [Networked] private TickTimer NetworkNextSpawnTimer { get; set; }

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();
        private readonly List<Transform> _spawnPoints = new();
        private readonly List<ItemDefinition> _spawnableDefinitions = new();
        private readonly Dictionary<string, ItemFieldDrop> _managedDrops = new(StringComparer.Ordinal);

        public bool IsSpawnLoopEnabled => NetworkSpawnLoopEnabled;

        private void Awake()
        {
            RefreshSpawnPoints();
            RefreshSpawnableDefinitions();
        }

        public override void Spawned()
        {
            RefreshSpawnPoints();
            RefreshSpawnableDefinitions();

            if (!HasStateAuthority)
            {
                return;
            }

            NetworkSpawnLoopEnabled = enableTestSpawnLoop;
            NetworkNextSpawnTimer = default;
            if (NetworkSpawnLoopEnabled)
            {
                ScheduleNextSpawn();
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
                DebugLog("Only StateAuthority can toggle the spawn loop.");
                return;
            }

            NetworkSpawnLoopEnabled = enabled;
            NetworkNextSpawnTimer = enabled
                ? TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, spawnIntervalSec))
                : default;
        }

        public void HandleManagedFieldDropEnteredDeathZone(ItemFieldDrop drop)
        {
            if (drop == null)
            {
                return;
            }

            var networkObject = drop.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.Id.IsValid)
            {
                if (!HasStateAuthority || !networkObject.HasStateAuthority || networkObject.Runner == null)
                {
                    return;
                }

                UnregisterManagedDrop(drop);
                networkObject.Runner.Despawn(networkObject);
                DebugLog($"ImmediateDeath removed networked drop: itemId={drop.ItemId}, id={drop.InstanceId}");
                return;
            }

            if (!HasStateAuthority)
            {
                return;
            }

            UnregisterManagedDrop(drop);
            Destroy(drop.gameObject);
        }

        public bool IsManagedFieldDrop(ItemFieldDrop drop)
        {
            if (drop == null)
            {
                return false;
            }

            return _managedDrops.ContainsKey(GetManagedKey(drop));
        }

        private void RefreshSpawnPoints()
        {
            _spawnPoints.Clear();

            var root = ResolveSpawnZoneRoot();
            if (root == null)
            {
                DebugLog("ItemSpawnZone root was not found.");
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

            DebugLog($"Cached spawn points: count={_spawnPoints.Count}");
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
                DebugLog($"Failed to load item catalog: {error}");
                return;
            }

            foreach (var pair in catalog.Definitions)
            {
                var definition = pair.Value;
                if (definition == null || !definition.Master.Enabled)
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

            DebugLog($"Cached spawnable item definitions: count={_spawnableDefinitions.Count}");
        }

        private void ScheduleNextSpawn()
        {
            if (Runner == null)
            {
                return;
            }

            NetworkNextSpawnTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.1f, spawnIntervalSec));
            DebugLog($"Scheduled next spawn: interval={spawnIntervalSec:F1}s");
        }

        private void SpawnRandomItem()
        {
            if (Runner == null || _spawnPoints.Count == 0 || _spawnableDefinitions.Count == 0)
            {
                DebugLog("Spawn skipped because zones, definitions, or runner are missing.");
                return;
            }

            var spawnPoint = _spawnPoints[UnityEngine.Random.Range(0, _spawnPoints.Count)];
            var definition = _spawnableDefinitions[UnityEngine.Random.Range(0, _spawnableDefinitions.Count)];
            var prefab = ItemFieldDropFactory.TryLoadNetworkedDropPrefab(_prefabResolver);
            if (prefab == null)
            {
                DebugLog($"Missing networked wrapper prefab for {definition.Master.ItemId}");
                return;
            }

            if (prefab.GetComponent<NetworkObject>() == null)
            {
                DebugLog($"Networked wrapper prefab is missing NetworkObject: {definition.Master.ItemId}");
                return;
            }

            var candidate = ResolveSpawnCandidate(spawnPoint);
            var useZonePositionDirectly =
                spawnPoint != null &&
                (spawnPoint.GetComponent<BoxCollider>() != null || spawnPoint.GetComponent<Collider>() != null);
            var resolvedPosition = useZonePositionDirectly
                ? candidate
                : ItemFieldPositionUtility.ResolveGroundPosition(
                    candidate,
                    useGroundRaycast,
                    groundMask,
                    spawnHeightOffset);

            var requestedInstanceId = Guid.NewGuid().ToString("N");
            var spawnedObject = Runner.Spawn(
                prefab,
                resolvedPosition,
                Quaternion.identity,
                onBeforeSpawned: (_, obj) =>
                {
                    var fieldDrop = obj.GetComponent<ItemFieldDrop>();
                    if (fieldDrop != null)
                    {
                        fieldDrop.SetItemId(definition.Master.ItemId);
                        fieldDrop.SetInstanceId(requestedInstanceId);
                    }

                    var networkedDrop = obj.GetComponent<NetworkedItemFieldDrop>();
                    if (networkedDrop != null)
                    {
                        networkedDrop.InitializeMetadata(definition.Master.ItemId);
                    }
                });

            if (spawnedObject == null)
            {
                DebugLog($"Runner.Spawn failed: itemId={definition.Master.ItemId}");
                return;
            }

            var spawnedDrop = spawnedObject.GetComponent<ItemFieldDrop>();
            if (spawnedDrop == null)
            {
                DebugLog($"Spawned object is missing ItemFieldDrop: itemId={definition.Master.ItemId}");
                Runner.Despawn(spawnedObject);
                return;
            }

            ConfigureAuthorityManagedDrop(spawnedDrop);
            RegisterManagedDrop(spawnedDrop, subscribePickup: true);
            DebugLog(
                $"Spawned object runtime state: itemId={definition.Master.ItemId}, requestedPos={resolvedPosition}, actualPos={spawnedObject.transform.position}, authority={spawnedObject.HasStateAuthority}");
            DebugLog($"Spawned network item: itemId={definition.Master.ItemId}, point={spawnPoint.name}, position={resolvedPosition}");
        }

        private Vector3 ResolveSpawnCandidate(Transform spawnPoint)
        {
            if (spawnPoint == null)
            {
                return Vector3.up * Mathf.Max(0f, spawnHeightOffset);
            }

            var fallback = spawnPoint.position + Vector3.up * Mathf.Max(0f, spawnHeightOffset);

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

        private void ConfigureAuthorityManagedDrop(ItemFieldDrop fieldDrop)
        {
            if (fieldDrop == null)
            {
                return;
            }

            var body = fieldDrop.GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }

            if (keepSpawnedItemStatic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.useGravity = false;
                return;
            }

            body.isKinematic = false;
            body.useGravity = true;
        }

        private void RegisterManagedDrop(ItemFieldDrop fieldDrop, bool subscribePickup)
        {
            if (fieldDrop == null)
            {
                return;
            }

            if (subscribePickup)
            {
                fieldDrop.PickedUp -= HandleManagedDropPickedUp;
                fieldDrop.PickedUp += HandleManagedDropPickedUp;
            }

            _managedDrops[GetManagedKey(fieldDrop)] = fieldDrop;
        }

        private void UnregisterManagedDrop(ItemFieldDrop fieldDrop)
        {
            if (fieldDrop == null)
            {
                return;
            }

            fieldDrop.PickedUp -= HandleManagedDropPickedUp;
            _managedDrops.Remove(GetManagedKey(fieldDrop));
        }

        private void HandleManagedDropPickedUp(ItemFieldDrop drop)
        {
            if (drop == null || !HasStateAuthority)
            {
                return;
            }

            UnregisterManagedDrop(drop);

            var networkObject = drop.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.Id.IsValid && networkObject.Runner != null && networkObject.HasStateAuthority)
            {
                networkObject.Runner.Despawn(networkObject);
                DebugLog($"Picked up managed network drop: itemId={drop.ItemId}, id={drop.InstanceId}");
                return;
            }

            Destroy(drop.gameObject);
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

        private static string GetManagedKey(ItemFieldDrop drop)
        {
            return drop == null ? string.Empty : drop.InstanceId ?? string.Empty;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemRandomSpawnManager] {message}", this);
        }

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
