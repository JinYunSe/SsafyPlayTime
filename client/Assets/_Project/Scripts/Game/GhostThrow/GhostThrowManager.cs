using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    public class GhostThrowManager : MonoBehaviour
    {
        [Header("Activation")]
        [SerializeField] private bool enableOnlyAfterDeath = true;
        [SerializeField] private string localPlayerTag = "Player";
        [SerializeField] private Transform manualLocalTarget;

        [Header("Settings")]
        public float cooldown = 1f;
        public float throwForce = 15f;
        public float spawnForwardOffset = 2f;
        public LayerMask hitLayer = ~0;

        [Header("Bomb Prefabs")]
        public GameObject cubePrefabOffline;
        public NetworkPrefabRef cubePrefabOnline;
        [SerializeField] private GameObject cubePrefabOnlineObject;

        [Header("Banana Prefabs")]
        public GameObject bananaPrefabOffline;
        public NetworkPrefabRef bananaPrefabOnline;
        [SerializeField] private GameObject bananaPrefabOnlineObject;

        private float _lastBombThrowTime = -100f;
        private float _lastBananaThrowTime = -100f;
        private PlayerStats _localPlayerStats;
        private bool _isGhostThrowEnabled;

        private void Start()
        {
            BindLocalPlayer();
            _isGhostThrowEnabled = !enableOnlyAfterDeath;
        }

        private void OnDestroy()
        {
            if (_localPlayerStats != null)
                _localPlayerStats.OnDied -= HandleLocalPlayerDied;
        }

        private void Update()
        {
            if (_localPlayerStats == null)
                BindLocalPlayer();

            RefreshGhostThrowStateFromLocalPlayer();

            if (!_isGhostThrowEnabled)
                return;

            if (Input.GetMouseButtonDown(0))
                TryThrow(isBanana: false);

            if (Input.GetMouseButtonDown(1))
                TryThrow(isBanana: true);
        }

        private void BindLocalPlayer()
        {
            GameObject localPlayer = null;

            if (manualLocalTarget != null)
                localPlayer = manualLocalTarget.gameObject;
            else
                localPlayer = GameObject.FindGameObjectWithTag(localPlayerTag);

            if (localPlayer == null)
            {
                var allPlayerStats = FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
                for (var i = 0; i < allPlayerStats.Length; i++)
                {
                    var stats = allPlayerStats[i];
                    if (stats == null)
                        continue;

                    var netObj = stats.GetComponent<NetworkObject>();
                    if (netObj != null && netObj.HasInputAuthority)
                    {
                        localPlayer = stats.gameObject;
                        break;
                    }
                }
            }

            if (localPlayer == null)
            {
                Debug.LogWarning($"GhostThrowManager: local player not found. tag={localPlayerTag}");
                return;
            }

            _localPlayerStats = localPlayer.GetComponent<PlayerStats>();
            if (_localPlayerStats == null)
            {
                Debug.LogWarning($"GhostThrowManager: PlayerStats missing on {localPlayer.name}");
                return;
            }

            _localPlayerStats.OnDied -= HandleLocalPlayerDied;
            _localPlayerStats.OnDied += HandleLocalPlayerDied;

            if (_localPlayerStats.IsDead)
                _isGhostThrowEnabled = true;
        }

        private void HandleLocalPlayerDied(PlayerStats deadPlayer)
        {
            ForceEnableGhostThrow($"dead player {deadPlayer.name}");
        }

        public void ForceEnableGhostThrow(string reason = null)
        {
            _isGhostThrowEnabled = true;
            Debug.Log($"GhostThrowManager: ghost throw enabled{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})")}");
        }

        private void RefreshGhostThrowStateFromLocalPlayer()
        {
            if (_isGhostThrowEnabled || !enableOnlyAfterDeath)
                return;

            if (_localPlayerStats != null && _localPlayerStats.IsDead)
            {
                ForceEnableGhostThrow($"PlayerStats death state on {_localPlayerStats.name}");
                return;
            }

            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];
                if (player == null)
                    continue;

                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject == null || !networkObject.HasInputAuthority)
                    continue;

                if (!player.IsDeadNetworked)
                    continue;

                ForceEnableGhostThrow($"NetworkPlayer death state on {player.name}");
                return;
            }
        }

        private void TryThrow(bool isBanana)
        {
            ref var lastThrowTime = ref (isBanana ? ref _lastBananaThrowTime : ref _lastBombThrowTime);
            if (Time.time < lastThrowTime + cooldown)
            {
                var remain = lastThrowTime + cooldown - Time.time;
                Debug.Log($"GhostThrow [{(isBanana ? "banana" : "bomb")}]: cooldown {remain:F1}s");
                return;
            }

            if (Camera.main == null)
            {
                Debug.LogWarning("GhostThrowManager: main camera not found.");
                return;
            }

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var targetPoint = Physics.Raycast(ray, out RaycastHit hit, 1000f, hitLayer)
                ? hit.point
                : ray.GetPoint(100f);

            lastThrowTime = Time.time;

            var spawnPos = Camera.main.transform.position + Camera.main.transform.forward * spawnForwardOffset;
            var throwDirection = (targetPoint - spawnPos).normalized;

            var runner = FindAnyObjectByType<NetworkRunner>();
            if (runner != null && runner.IsRunning)
            {
                var localNetworkPlayer = ResolveLocalNetworkPlayer();
                if (localNetworkPlayer != null && localNetworkPlayer.TryRequestGhostThrow(isBanana, spawnPos, throwDirection))
                {
                    var label = isBanana ? "banana" : "bomb";
                    Debug.Log($"GhostThrow [Request]: requested {label} at {spawnPos}");
                    return;
                }
            }

            if (runner != null && runner.IsRunning && runner.IsServer)
            {
                SpawnOnline(runner, isBanana, spawnPos, throwDirection);
            }
            else
            {
                SpawnOffline(isBanana, spawnPos, throwDirection);
            }
        }

        internal bool TrySpawnOnlineFromRequest(bool isBanana, Vector3 spawnPos, Vector3 dir)
        {
            var runner = FindAnyObjectByType<NetworkRunner>();
            if (runner == null || !runner.IsRunning || !runner.IsServer)
                return false;

            return SpawnOnline(runner, isBanana, spawnPos, dir);
        }

        private bool SpawnOnline(NetworkRunner runner, bool isBanana, Vector3 spawnPos, Vector3 dir)
        {
            var prefabRef = isBanana ? bananaPrefabOnline : cubePrefabOnline;
            var prefabObject = isBanana ? bananaPrefabOnlineObject : cubePrefabOnlineObject;
            var label = isBanana ? "banana" : "bomb";

            if (prefabObject != null)
            {
                var spawnedByObject = runner.Spawn(prefabObject, spawnPos, Quaternion.LookRotation(dir));
                if (spawnedByObject == null)
                {
                    Debug.LogError($"GhostThrowManager [Online]: failed to spawn {label} prefab object.");
                    return false;
                }

                ApplyThrowForce(spawnedByObject.gameObject, dir, isBanana);
                Debug.Log($"GhostThrow [Online]: threw {label} at {spawnPos}");
                return true;
            }

            if (!prefabRef.IsValid)
            {
                Debug.LogError($"GhostThrowManager [Online]: {label} prefab is not assigned.");
                return false;
            }

            var spawnedObj = runner.Spawn(prefabRef, spawnPos, Quaternion.LookRotation(dir));
            ApplyThrowForce(spawnedObj.gameObject, dir, isBanana);
            Debug.Log($"GhostThrow [Online]: threw {label} at {spawnPos}");
            return true;
        }

        private void SpawnOffline(bool isBanana, Vector3 spawnPos, Vector3 dir)
        {
            var prefab = isBanana ? bananaPrefabOffline : cubePrefabOffline;
            var label = isBanana ? "banana" : "bomb";

            if (prefab == null)
            {
                Debug.LogWarning($"GhostThrowManager [Offline]: {label} prefab is not assigned.");
                return;
            }

            var spawnedObj = Instantiate(prefab, spawnPos, Quaternion.LookRotation(dir));
            ApplyThrowForce(spawnedObj, dir, isBanana);
            Debug.Log($"GhostThrow [Offline]: threw {label} at {spawnPos}");
        }

        private void ApplyThrowForce(GameObject obj, Vector3 direction, bool isBanana)
        {
            var rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            var upBias = isBanana ? 0.05f : 0.2f;
            var adjustedDir = (direction + Vector3.up * upBias).normalized;
            rb.AddForce(adjustedDir * throwForce, ForceMode.VelocityChange);
        }

        private NetworkPlayer ResolveLocalNetworkPlayer()
        {
            if (_localPlayerStats != null)
            {
                var player = _localPlayerStats.GetComponent<NetworkPlayer>();
                if (player != null)
                    return player;
            }

            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            for (var i = 0; i < allPlayers.Length; i++)
            {
                var player = allPlayers[i];
                if (player == null)
                    continue;

                var networkObject = player.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.HasInputAuthority)
                    return player;
            }

            return null;
        }
    }
}
