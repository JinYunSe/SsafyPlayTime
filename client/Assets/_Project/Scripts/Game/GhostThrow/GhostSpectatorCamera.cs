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

        private void Start()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
                Debug.LogError("GhostSpectatorCamera requires a Camera component!");

            // 현재 카메라 위치에서 초기 공전 각도를 계산해 스냅 방지
            var offset = transform.position - mapCenter;
            _orbitAngle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            // A: 왼쪽 공전, D: 오른쪽 공전
            var horizontal = Input.GetAxis("Horizontal");
            _orbitAngle -= horizontal * orbitSpeed * Time.deltaTime;

            var rad = _orbitAngle * Mathf.Deg2Rad;
            var targetPos = mapCenter
                + new Vector3(Mathf.Sin(rad) * orbitRadius, orbitHeight, Mathf.Cos(rad) * orbitRadius);

            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * moveSpeed);

            // 항상 맵 중앙을 바라본다
            var lookDir = mapCenter - transform.position;
            if (lookDir != Vector3.zero)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(lookDir, Vector3.up), Time.deltaTime * moveSpeed);
        }
    }
}
