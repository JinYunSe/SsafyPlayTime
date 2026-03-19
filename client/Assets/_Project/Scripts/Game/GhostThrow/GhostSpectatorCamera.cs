using UnityEngine;

namespace SSAFYPlayTime.Game.GhostThrow
{
    // 플레이어 사망 시 활성화되는 관전 카메라.
    // A/D 키로 맵 중앙을 기준으로 좌우 공전하며, 항상 맵 중앙을 바라본다.
    public class GhostSpectatorCamera : MonoBehaviour
    {
        [Header("Orbit")]
        [Tooltip("맵 중앙 좌표 (X, Z 기준)")]
        public Vector3 mapCenter = Vector3.zero;
        [Tooltip("맵 중앙으로부터의 공전 반지름")]
        public float orbitRadius = 40f;
        [Tooltip("카메라 높이 (mapCenter.y 기준 오프셋)")]
        public float orbitHeight = 15f;
        [Tooltip("A/D 키 공전 속도 (도/초)")]
        public float orbitSpeed = 60f;
        [Tooltip("위치 이동 부드러움")]
        public float moveSpeed = 5f;

        private Camera _cam;
        private float _orbitAngle;
        private Transform _trackedProjectile;

        // 비용 절감: 일정 주기(0.5s)마다만 씬 전체를 스캔한다
        private float _autoTrackScanTimer;

        private void Start()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
                Debug.LogError("GhostSpectatorCamera requires a Camera component!");

            // 현재 카메라 위치에서 초기 공전 각도를 계산해 스냅 방지
            var offset = transform.position - mapCenter;
            _orbitAngle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 날아가는 투사체를 카메라가 추적하도록 설정한다.
        /// 투사체가 파괴되면 자동으로 맵 중앙 복귀.
        /// </summary>
        public void TrackProjectile(Transform projectile)
        {
            _trackedProjectile = projectile;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            // A: 왼쪽 공전, D: 오른쪽 공전
            // 부호 반전: Sin/Cos 기반 공전에서 angle 증가는 카메라가 +X로 이동하지만
            // 카메라가 항상 중앙을 바라보므로 화면상 장면은 반대 방향으로 흐른다.
            var horizontal = Input.GetAxis("Horizontal");
            _orbitAngle -= horizontal * orbitSpeed * Time.deltaTime;

            var rad = _orbitAngle * Mathf.Deg2Rad;
            var targetPos = mapCenter
                + new Vector3(Mathf.Sin(rad) * orbitRadius, orbitHeight, Mathf.Cos(rad) * orbitRadius);

            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * moveSpeed);

            // 추적 중인 투사체가 없으면 씬에서 자동 탐색 (비호스트 클라이언트 지원)
            if (_trackedProjectile == null)
            {
                _autoTrackScanTimer -= Time.deltaTime;
                if (_autoTrackScanTimer <= 0f)
                {
                    _autoTrackScanTimer = 0.5f;
                    AutoTrackNearest();
                }
            }

            // 투사체 추적 중이면 투사체 방향으로, 파괴됐으면 맵 중앙으로 복귀
            var lookPoint = (_trackedProjectile != null) ? _trackedProjectile.position : mapCenter;
            var lookDir = lookPoint - transform.position;
            if (lookDir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(lookDir, Vector3.up), Time.deltaTime * moveSpeed);
        }

        /// <summary>
        /// 씬에 활성화된 GhostCube / BananaPeel 중 카메라 전방에 가장 가까운 것을 자동으로 추적한다.
        /// NotifySpectatorCameraToTrack()이 호출되지 않는 비호스트 클라이언트에서도
        /// 네트워크 복제된 투사체를 자동으로 카메라에 잡아준다.
        /// </summary>
        private void AutoTrackNearest()
        {
            Transform best = null;
            float bestDot = -1f;
            var forward = transform.forward;

            var cubes = FindObjectsByType<GhostCube>(FindObjectsSortMode.None);
            for (var i = 0; i < cubes.Length; i++)
            {
                if (cubes[i] == null) continue;
                var dir = (cubes[i].transform.position - transform.position).normalized;
                var dot = Vector3.Dot(forward, dir);
                if (dot > bestDot) { bestDot = dot; best = cubes[i].transform; }
            }

            var peels = FindObjectsByType<BananaPeel>(FindObjectsSortMode.None);
            for (var i = 0; i < peels.Length; i++)
            {
                if (peels[i] == null) continue;
                var dir = (peels[i].transform.position - transform.position).normalized;
                var dot = Vector3.Dot(forward, dir);
                if (dot > bestDot) { bestDot = dot; best = peels[i].transform; }
            }

            if (best != null)
                _trackedProjectile = best;
        }
    }
}
