using Fusion;

using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    public class GhostThrowManager : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("쿨타임 (초)")]
        public float cooldown = 1f;
        [Tooltip("던질 때 가해지는 힘")]
        public float throwForce = 15f;
        [Tooltip("카메라에서 생성될 거리")]
        public float spawnForwardOffset = 2f;
        [Tooltip("레이캐스트 충돌 레이어")]
        public LayerMask hitLayer = ~0; // 기본적으로 모든 레이어

        [Header("Bomb Prefabs (좌클릭)")]
        [Tooltip("던질 폭탄 프리팹 (로컬/테스트 용)")]
        public GameObject cubePrefabOffline;
        [Tooltip("던질 폭탄 프리팹 (네트워크/온라인 용)")]
        public NetworkPrefabRef cubePrefabOnline;

        [Header("Banana Prefabs (우클릭)")]
        [Tooltip("던질 바나나 프리팹 (로컬/테스트 용)")]
        public GameObject bananaPrefabOffline;
        [Tooltip("던질 바나나 프리팹 (네트워크/온라인 용)")]
        public NetworkPrefabRef bananaPrefabOnline;

        private float _lastBombThrowTime   = -100f;
        private float _lastBananaThrowTime = -100f;

        private void Update()
        {
            // 좌클릭 → 폭탄
            if (Input.GetMouseButtonDown(0))
                TryThrow(isBanana: false);

            // 우클릭 → 바나나
            if (Input.GetMouseButtonDown(1))
                TryThrow(isBanana: true);

            CheckOutOfBoundsDeath();
        }

        private void CheckOutOfBoundsDeath()
        {
            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            foreach (var p in allPlayers)
            {
                if (p.transform.position.y < 3f)
                {
                    PlayerStats stats = p.GetComponent<PlayerStats>();
                    if (stats != null && stats.currentHealth > 0)
                    {
                        stats.TakeDamage(9999);
                        Debug.Log($"GhostThrowManager: {p.name} 이(가) 맵 밖으로 떨어져 사망 처리됨.");
                    }
                }
            }
        }

        private void TryThrow(bool isBanana)
        {
            // ── 쿨타임 체크 ──
            ref float lastThrowTime = ref (isBanana ? ref _lastBananaThrowTime : ref _lastBombThrowTime);
            if (Time.time < lastThrowTime + cooldown)
            {
                float remain = lastThrowTime + cooldown - Time.time;
                Debug.Log($"GhostThrow [{(isBanana ? "바나나" : "폭탄")}]: 쿨타임 {remain:F1}초 남음");
                return;
            }

            if (Camera.main == null)
            {
                Debug.LogWarning("GhostThrowManager: 메인 카메라를 찾을 수 없습니다.");
                return;
            }

            // ── 목표 지점 레이캐스트 ──
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 targetPoint = Physics.Raycast(ray, out RaycastHit hit, 1000f, hitLayer)
                ? hit.point
                : ray.GetPoint(100f);

            lastThrowTime = Time.time;

            Vector3 spawnPos      = Camera.main.transform.position + Camera.main.transform.forward * spawnForwardOffset;
            Vector3 throwDirection = (targetPoint - spawnPos).normalized;

            // ── 스폰 ──
            NetworkRunner runner = FindAnyObjectByType<NetworkRunner>();
            if (runner != null && runner.IsRunning && runner.IsServer)
                SpawnOnline(runner, isBanana, spawnPos, throwDirection);
            else
                SpawnOffline(isBanana, spawnPos, throwDirection);
        }

        // ── 온라인 스폰 ───────────────────────────────────────────
        private void SpawnOnline(NetworkRunner runner, bool isBanana, Vector3 spawnPos, Vector3 dir)
        {
            NetworkPrefabRef prefabRef = isBanana ? bananaPrefabOnline : cubePrefabOnline;
            string label = isBanana ? "바나나" : "폭탄";

            if (prefabRef.IsValid)
            {
                var spawnedObj = runner.Spawn(prefabRef, spawnPos, Quaternion.LookRotation(dir));
                ApplyThrowForce(spawnedObj.gameObject, dir, isBanana);
                Debug.Log($"GhostThrow [Online]: {label}을 던졌습니다 ({spawnPos})");
            }
            else
            {
                Debug.LogError($"GhostThrowManager [Online]: 온라인용 {label} 프리팹이 할당되지 않았습니다!");
            }
        }

        // ── 오프라인 스폰 ─────────────────────────────────────────
        private void SpawnOffline(bool isBanana, Vector3 spawnPos, Vector3 dir)
        {
            GameObject prefab = isBanana ? bananaPrefabOffline : cubePrefabOffline;
            string label = isBanana ? "바나나" : "폭탄";

            if (prefab != null)
            {
                var spawnedObj = Instantiate(prefab, spawnPos, Quaternion.LookRotation(dir));
                ApplyThrowForce(spawnedObj, dir, isBanana);
                Debug.Log($"GhostThrow [Offline]: {label}을 던졌습니다 ({spawnPos})");
            }
            else
            {
                Debug.LogWarning($"GhostThrowManager [Offline]: {label} 프리팹(Offline)이 할당되지 않았습니다!");
            }
        }

        // ── 던지기 힘 적용 ────────────────────────────────────────
        private void ApplyThrowForce(GameObject obj, Vector3 direction, bool isBanana)
        {
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb == null) return;

            // 바나나는 낮게 포물선, 폭탄은 기존 방식
            float upBias = isBanana ? 0.05f : 0.2f;
            Vector3 adjustedDir = (direction + Vector3.up * upBias).normalized;
            rb.AddForce(adjustedDir * throwForce, ForceMode.VelocityChange);
        }
    }
}
