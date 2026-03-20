using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Stage
{
    /// <summary>
    /// Map 루트 GameObject에 부착한다.
    /// 캐릭터가 Map 콜라이더를 통과(터널링)해 아래로 떨어지는 현상을 Map 측에서만 방지한다.
    ///
    /// [동작 방식]
    /// 1) Awake: Map 하위 Collider의 contactOffset을 키워 충돌 조기 감지 범위를 확보한다.
    /// 2) FixedUpdate: Character·Ragdoll 레이어의 비-kinematic Rigidbody를 주기적으로 수집한다.
    ///    각 Rigidbody 위에서 SphereCast를 아래로 쏘아 Map 표면 Y를 구한 뒤,
    ///    Rigidbody가 그 면 아래에 있으면 위치와 하강 속도를 복원한다.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class MapTunnelGuard : MonoBehaviour
    {
        [Header("Contact Offset")]
        [Tooltip("Map 하위 Collider에 적용할 contactOffset. 기본값(0.01)보다 크게 설정해 조기 충돌 감지를 유도한다.")]
        [SerializeField] private float mapContactOffset = 0.12f;

        [Header("Tunnel Detection")]
        [Tooltip("비-kinematic Rigidbody 목록 갱신 주기 (초)")]
        [SerializeField] private float refreshInterval = 2f;
        [Tooltip("SphereCast 시작 높이: Rigidbody.position 기준 위 방향 오프셋 (m)")]
        [SerializeField] private float castStartOffset = 1.5f;
        [Tooltip("SphereCast 반지름. 캐릭터 캡슐 반지름 근사값.")]
        [SerializeField] private float castRadius = 0.25f;
        [Tooltip("복원 시 Map 표면 위로 띄울 추가 여유 (m)")]
        [SerializeField] private float recoverOffset = 0.08f;

        // Layer 인덱스 (TagManager.asset 기준)
        private const int LayerCharacter = 8;
        private const int LayerRagdoll   = 9;
        private const int LayerMap       = 3;

        private LayerMask _mapMask;
        private Rigidbody[] _tracked = new Rigidbody[0];
        private float _nextRefreshTime;

        // ──────────────────────────────────────────────

        private void Awake()
        {
            _mapMask = 1 << LayerMap;
            ApplyContactOffsets();
        }

        /// <summary>Map 하위 콜라이더 전체의 contactOffset을 일괄 적용한다 (일회성).</summary>
        private void ApplyContactOffsets()
        {
            var cols = GetComponentsInChildren<Collider>(true);
            foreach (var col in cols)
            {
                if (col.isTrigger) continue;
                if (col.gameObject.layer == LayerMap)
                    col.contactOffset = mapContactOffset;
            }
        }

        private void FixedUpdate()
        {
            if (Time.fixedTime >= _nextRefreshTime)
            {
                RefreshRigidbodies();
                _nextRefreshTime = Time.fixedTime + refreshInterval;
            }

            CorrectTunneledBodies();
        }

        /// <summary>씬 내 Character·Ragdoll 레이어의 비-kinematic Rigidbody를 수집한다.</summary>
        private void RefreshRigidbodies()
        {
            var all = FindObjectsOfType<Rigidbody>();
            var list = new List<Rigidbody>(all.Length);
            foreach (var rb in all)
            {
                var layer = rb.gameObject.layer;
                if ((layer == LayerCharacter || layer == LayerRagdoll) && !rb.isKinematic)
                    list.Add(rb);
            }
            _tracked = list.ToArray();
        }

        /// <summary>
        /// 각 Rigidbody 위에서 SphereCast를 내리쏴 Map 표면을 감지하고,
        /// Rigidbody가 그 면 아래에 있으면 표면 위로 복원한다.
        /// </summary>
        private void CorrectTunneledBodies()
        {
            foreach (var rb in _tracked)
            {
                if (rb == null || rb.isKinematic) continue;

                var pos = rb.position;
                // castStartOffset 높이에서 아래로 castStartOffset + 여유만큼 검사
                var origin = pos + Vector3.up * castStartOffset;
                var maxDist = castStartOffset + 0.5f;

                if (!Physics.SphereCast(origin, castRadius, Vector3.down, out var hit,
                        maxDist, _mapMask, QueryTriggerInteraction.Ignore))
                    continue;

                var surfaceY = hit.point.y;
                if (pos.y >= surfaceY) continue;

                // 터널링 감지: 표면 위로 복원
                rb.position = new Vector3(pos.x, surfaceY + recoverOffset, pos.z);

                var vel = rb.velocity;
                if (vel.y < 0f)
                    rb.velocity = new Vector3(vel.x, 0f, vel.z);
            }
        }
    }
}
