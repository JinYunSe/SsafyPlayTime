using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    public class GhostThrowManager : MonoBehaviour
    {
        private const string BombPrefabPath = "Assets/_Project/Prefabs/Items/Bomb.prefab";
        private const string BananaPrefabPath = "Assets/_Project/Prefabs/Items/banana.prefab";

        [Header("Settings")]
        [Tooltip("Throw cooldown in seconds.")]
        public float cooldown = 1f;
        [Tooltip("Throw impulse.")]
        public float throwForce = 15f;
        [Tooltip("Spawn distance in front of camera.")]
        public float spawnForwardOffset = 2f;
        [Tooltip("Raycast layer mask.")]
        public LayerMask hitLayer = ~0;
        [SerializeField] private bool controlEnabled = true;
        [SerializeField] private bool enableOutOfBoundsKillCheck = true;

        [Header("Bomb Prefabs")]
        public GameObject cubePrefabOffline;
        public NetworkPrefabRef cubePrefabOnline;

        [Header("Banana Prefabs")]
        public GameObject bananaPrefabOffline;
        public NetworkPrefabRef bananaPrefabOnline;

        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();

        private float _lastBombThrowTime = -100f;
        private float _lastBananaThrowTime = -100f;

        public void SetGhostControlEnabled(bool enabled)
        {
            controlEnabled = enabled;
        }

        public void SetEnableOutOfBoundsKillCheck(bool enabled)
        {
            enableOutOfBoundsKillCheck = enabled;
        }

        private void Update()
        {
            if (controlEnabled)
            {
                if (Input.GetMouseButtonDown(0))
                    TryThrow(isBanana: false);

                if (Input.GetMouseButtonDown(1))
                    TryThrow(isBanana: true);
            }

            if (enableOutOfBoundsKillCheck)
                CheckOutOfBoundsDeath();
        }

        private void CheckOutOfBoundsDeath()
        {
            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            foreach (var player in allPlayers)
            {
                if (player == null || player.transform.position.y >= 3f || player.IsDeadState)
                    continue;

                player.KillImmediately("GhostOutOfBounds");
            }
        }

        private void TryThrow(bool isBanana)
        {
            ref var lastThrowTime = ref (isBanana ? ref _lastBananaThrowTime : ref _lastBombThrowTime);
            if (Time.time < lastThrowTime + cooldown)
                return;

            if (Camera.main == null)
            {
                Debug.LogWarning("GhostThrowManager: MainCamera is missing.");
                return;
            }

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var targetPoint = Physics.Raycast(ray, out var hit, 1000f, hitLayer)
                ? hit.point
                : ray.GetPoint(100f);

            lastThrowTime = Time.time;

            var spawnPos = Camera.main.transform.position + Camera.main.transform.forward * spawnForwardOffset;
            var throwDirection = (targetPoint - spawnPos).normalized;
            var runner = FindAnyObjectByType<NetworkRunner>();

            if (runner != null && runner.IsRunning && runner.IsServer)
                SpawnOnline(runner, isBanana, spawnPos, throwDirection);
            else
                SpawnOffline(isBanana, spawnPos, throwDirection);
        }

        private void SpawnOnline(NetworkRunner runner, bool isBanana, Vector3 spawnPos, Vector3 dir)
        {
            if (runner == null)
                return;

            var prefabRef = isBanana ? bananaPrefabOnline : cubePrefabOnline;
            if (prefabRef.IsValid)
            {
                var spawnedObj = runner.Spawn(prefabRef, spawnPos, Quaternion.LookRotation(dir));
                ApplyThrowForce(spawnedObj != null ? spawnedObj.gameObject : null, dir, isBanana);
                return;
            }

            var prefab = ResolveSpawnPrefab(isBanana);
            if (prefab == null || prefab.GetComponent<NetworkObject>() == null)
            {
                Debug.LogWarning($"GhostThrowManager: Network prefab missing for {(isBanana ? "banana" : "bomb")}.");
                return;
            }

            var spawned = runner.Spawn(prefab, spawnPos, Quaternion.LookRotation(dir));
            ApplyThrowForce(spawned != null ? spawned.gameObject : null, dir, isBanana);
        }

        private void SpawnOffline(bool isBanana, Vector3 spawnPos, Vector3 dir)
        {
            var prefab = ResolveSpawnPrefab(isBanana);
            if (prefab == null)
            {
                Debug.LogWarning($"GhostThrowManager: Offline prefab missing for {(isBanana ? "banana" : "bomb")}.");
                return;
            }

            var spawned = Instantiate(prefab, spawnPos, Quaternion.LookRotation(dir));
            ApplyThrowForce(spawned, dir, isBanana);
        }

        private GameObject ResolveSpawnPrefab(bool isBanana)
        {
            var explicitPrefab = isBanana ? bananaPrefabOffline : cubePrefabOffline;
            if (explicitPrefab != null)
                return explicitPrefab;

            var resolvedPath = isBanana ? BananaPrefabPath : BombPrefabPath;
            return _prefabResolver.Resolve(resolvedPath);
        }

        private void ApplyThrowForce(GameObject obj, Vector3 direction, bool isBanana)
        {
            if (obj == null)
                return;

            var rb = obj.GetComponent<Rigidbody>();
            if (rb == null)
                return;

            var upBias = isBanana ? 0.05f : 0.2f;
            var adjustedDir = (direction + Vector3.up * upBias).normalized;
            rb.AddForce(adjustedDir * throwForce, ForceMode.VelocityChange);
        }
    }
}
