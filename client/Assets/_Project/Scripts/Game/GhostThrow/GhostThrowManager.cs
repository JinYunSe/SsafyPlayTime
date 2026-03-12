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

        [Header("Prefabs")]
        [Tooltip("던질 큐브 프리팹 (로컬/테스트 용)")]
        public GameObject cubePrefabOffline;
        [Tooltip("던질 큐브 프리팹 (네트워크/온라인 용)")]
        public NetworkPrefabRef cubePrefabOnline;

        private float _lastThrowTime = -100f; // 초기 쿨타임 무시

        private void Update()
        {
            // 마우스 좌클릭 시 동작 (MonoBehaviour이므로 권한 체크 불필요)
            if (Input.GetMouseButtonDown(0))
            {
                TryThrowCube();
            }

            // 임시 방편: 현재 테스트 씬에 DeathZone이 없으므로,
            // 플레이어가 맵 아래(y < -10)로 떨어지면 강제로 데미지를 입혀 죽게 만듦.
            CheckOutOfBoundsDeath();
        }

        private void CheckOutOfBoundsDeath()
        {
            // 사실상 모든 NetworkPlayer를 찾아서 Y값을 검사
            var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
            foreach (var p in allPlayers)
            {
                if (p.transform.position.y < 3f)
                {
                    // 로컬 테스트의 경우 여기서 바로 PlayerStats에 접근해서 죽임
                    PlayerStats stats = p.GetComponent<PlayerStats>();
                    if (stats != null && stats.currentHealth > 0)
                    {
                        stats.TakeDamage(9999);
                        Debug.Log($"GhostThrowManager: {p.name} 이(가) 맵 밖으로 떨어져 사망 처리됨.");
                    }
                }
            }
        }

        private void TryThrowCube()
        {
            // 쿨타임 체크 (로컬 기준)
            if (Time.time < _lastThrowTime + cooldown)
            {
                Debug.Log($"GhostThrow: 쿨타임 진행 중입니다. 남은 시간: {(_lastThrowTime + cooldown - Time.time):F1}초");
                return;
            }

            // 메인 카메라 찾기
            if (Camera.main == null)
            {
                Debug.LogWarning("GhostThrowManager: 메인 카메라를 찾을 수 없습니다.");
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Vector3 targetPoint;

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, hitLayer))
            {
                targetPoint = hit.point;
            }
            else
            {
                // 바닥이나 오브젝트 대신 허공(하늘)을 클릭했을 경우, 레이 방향으로 멀리 있는 지점을 목표로 삼음
                targetPoint = ray.GetPoint(100f);
            }

            _lastThrowTime = Time.time;

            // 관전자 카메라 위치 바로 앞쪽에서 구 생성
            Vector3 spawnPos = Camera.main.transform.position + Camera.main.transform.forward * spawnForwardOffset;
            // 목표 지점을 향하는 방향 계산
            Vector3 throwDirection = (targetPoint - spawnPos).normalized;

            NetworkRunner runner = FindAnyObjectByType<NetworkRunner>();
            if (runner != null && runner.IsRunning && runner.IsServer)
            {
                // 온라인 호스트/서버 환경: NetworkRunner를 통한 스폰
                if (cubePrefabOnline.IsValid)
                {
                    var spawnedObj = runner.Spawn(cubePrefabOnline, spawnPos, Quaternion.LookRotation(throwDirection));
                    ApplyThrowForce(spawnedObj.gameObject, throwDirection);
                    Debug.Log($"GhostThrow [Online]: 방해 아이템을 던졌습니다 ({spawnPos})");
                }
                else
                {
                    Debug.LogError("GhostThrowManager [Online]: 온라인용 던질 큐브 프리팹(cubePrefabOnline)이 할당되지 않았습니다!");
                }
            }
            else
            {
                // 오프라인/테스트 환경: 일반 Instantiate 를 통한 스폰
                if (cubePrefabOffline != null)
                {
                    var spawnedObj = Instantiate(cubePrefabOffline, spawnPos, Quaternion.LookRotation(throwDirection));
                    ApplyThrowForce(spawnedObj, throwDirection);
                    Debug.Log($"GhostThrow [Offline]: 방해 아이템을 던졌습니다 ({spawnPos}). 오프라인 혼자 테스트 모드입니다.");
                }
                else
                {
                    Debug.LogWarning("GhostThrowManager [Offline]: 활성화된 호스트 NetworkRunner가 없고, 오프라인용 프리팹(cubePrefabOffline)도 할당되지 않았습니다!");
                }
            }
        }

        private void ApplyThrowForce(GameObject obj, Vector3 direction)
        {
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 포물선을 예쁘게 그리기 위해 앞쪽과 살짝 위쪽으로 힘을 더해줌
                Vector3 adjustedDirection = direction + Vector3.up * 0.2f;
                rb.AddForce(adjustedDirection.normalized * throwForce, ForceMode.VelocityChange);
            }
        }
    }
}

