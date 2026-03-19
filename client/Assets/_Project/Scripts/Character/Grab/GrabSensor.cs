using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// 손에 붙는 "잡는 센서" - 얇은 trigger collider.
    /// GrabAnchorPoint와의 접촉을 감지하여 HandGrabHandler에 전달.
    /// SphereCollider(isTrigger=true) + GrabSensor 레이어와 함께 사용.
    /// </summary>
    public class GrabSensor : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("센서 감지 반경 (SphereCollider radius와 일치 권장)")]
        [SerializeField] private float sensorRadius = 0.06f;

        // 현재 접촉 중인 GrabAnchorPoint 목록
        private readonly List<GrabAnchorPoint> _overlappingAnchors = new();

        // 자기 캐릭터의 NetworkPlayer (자기 자신은 잡지 않기 위해)
        private NetworkPlayer _ownerPlayer;
        private bool _resolved;

        /// <summary>현재 접촉 중인 GrabAnchorPoint 목록 (읽기 전용)</summary>
        public IReadOnlyList<GrabAnchorPoint> OverlappingAnchors => _overlappingAnchors;

        /// <summary>접촉 중인 앵커가 있는지</summary>
        public bool HasOverlappingAnchor => _overlappingAnchors.Count > 0;

        public float SensorRadius => sensorRadius;

        private void Awake()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (_resolved) return;
            _ownerPlayer = transform.root.GetComponent<NetworkPlayer>();
            _resolved = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var anchor = other.GetComponent<GrabAnchorPoint>();
            if (anchor == null) return;

            // 자기 자신의 앵커는 무시
            if (!_resolved) ResolveReferences();
            if (_ownerPlayer != null && anchor.OwnerPlayer == _ownerPlayer) return;

            if (!_overlappingAnchors.Contains(anchor))
                _overlappingAnchors.Add(anchor);
        }

        private void OnTriggerExit(Collider other)
        {
            var anchor = other.GetComponent<GrabAnchorPoint>();
            if (anchor == null) return;

            _overlappingAnchors.Remove(anchor);
        }

        /// <summary>파괴되거나 비활성화된 앵커를 정리</summary>
        private void LateUpdate()
        {
            for (int i = _overlappingAnchors.Count - 1; i >= 0; i--)
            {
                if (_overlappingAnchors[i] == null || !_overlappingAnchors[i].gameObject.activeInHierarchy)
                    _overlappingAnchors.RemoveAt(i);
            }
        }

        /// <summary>
        /// 현재 접촉 중인 앵커 중 가장 우선순위가 높은 것 반환.
        /// 없으면 null.
        /// </summary>
        public GrabAnchorPoint GetBestOverlappingAnchor()
        {
            GrabAnchorPoint best = null;
            float bestPriority = float.MinValue;

            for (int i = 0; i < _overlappingAnchors.Count; i++)
            {
                var anchor = _overlappingAnchors[i];
                if (anchor == null) continue;

                if (anchor.GrabPriority > bestPriority)
                {
                    bestPriority = anchor.GrabPriority;
                    best = anchor;
                }
            }

            return best;
        }

        /// <summary>
        /// 특정 NetworkPlayer 소유의 접촉 중인 앵커 중 가장 우선순위 높은 것 반환.
        /// </summary>
        public GrabAnchorPoint GetBestOverlappingAnchorForPlayer(NetworkPlayer targetPlayer)
        {
            if (targetPlayer == null) return null;

            GrabAnchorPoint best = null;
            float bestPriority = float.MinValue;

            for (int i = 0; i < _overlappingAnchors.Count; i++)
            {
                var anchor = _overlappingAnchors[i];
                if (anchor == null || anchor.OwnerPlayer != targetPlayer) continue;

                if (anchor.GrabPriority > bestPriority)
                {
                    bestPriority = anchor.GrabPriority;
                    best = anchor;
                }
            }

            return best;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, sensorRadius);
        }
#endif
    }
}
