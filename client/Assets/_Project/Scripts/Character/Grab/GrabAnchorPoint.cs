using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// 상대 캐릭터 몸에 붙는 "잡히는 포인트" 정의.
    /// 래그돌 본(Hips, Spine1, UpperArm 등)의 자식 오브젝트에 부착.
    /// SphereCollider(isTrigger=true) + GrabHurtbox 레이어와 함께 사용.
    /// </summary>
    public class GrabAnchorPoint : MonoBehaviour
    {
        /// <summary>신체 부위 식별 enum - 네트워크 동기화 시 byte로 전송</summary>
        public enum AnchorId : byte
        {
            None = 0,
            Chest = 1,
            Hips = 2,
            LeftUpperArm = 3,
            RightUpperArm = 4,
            LeftForearm = 5,
            RightForearm = 6,
            Head = 7
        }

        [Header("Anchor Identity")]
        [SerializeField] private AnchorId anchorId = AnchorId.None;

        [Header("Grab Settings")]
        [Tooltip("이 앵커가 잡힐 수 있는 유효 반경 (SphereCollider radius와 일치 권장)")]
        [SerializeField] private float grabRadius = 0.12f;

        [Tooltip("점수 보정값 - 높을수록 우선 잡힘 (Chest/Hips > Forearm > Head)")]
        [SerializeField] private float grabPriority = 1f;

        [Header("Grip Point")]
        [Tooltip("손이 실제로 붙을 로컬 좌표 - 본 표면 위 정확한 위치")]
        [SerializeField] private Vector3 localGripOffset = Vector3.zero;

        [Tooltip("손이 붙을 때의 로컬 회전 - 손바닥 방향 맞춤")]
        [SerializeField] private Quaternion localGripRotation = Quaternion.identity;

        // 캐시
        private Rigidbody _parentBoneRigidbody;
        private Collider _parentBoneCollider;
        private NetworkPlayer _ownerPlayer;
        private bool _resolved;

        // --- 프로퍼티 ---
        public AnchorId Id => anchorId;
        public float GrabRadius => grabRadius;
        public float GrabPriority => grabPriority;
        public Vector3 LocalGripOffset => localGripOffset;
        public Quaternion LocalGripRotation => localGripRotation;

        public void ConfigureRuntime(
            AnchorId id,
            float radius,
            float priority,
            Vector3 gripOffset,
            Quaternion gripRotation)
        {
            anchorId = id;
            grabRadius = radius;
            grabPriority = priority;
            localGripOffset = gripOffset;
            localGripRotation = gripRotation;
            _resolved = false;
            ResolveReferences();
        }

        /// <summary>이 앵커가 붙어있는 래그돌 본의 Rigidbody</summary>
        public Rigidbody ParentBoneRigidbody
        {
            get
            {
                if (!_resolved) ResolveReferences();
                return _parentBoneRigidbody;
            }
        }

        /// <summary>이 앵커가 붙어있는 래그돌 본의 물리 Collider (선택적 충돌 무시용)</summary>
        public Collider ParentBoneCollider
        {
            get
            {
                if (!_resolved) ResolveReferences();
                return _parentBoneCollider;
            }
        }

        /// <summary>이 앵커를 소유한 NetworkPlayer</summary>
        public NetworkPlayer OwnerPlayer
        {
            get
            {
                if (!_resolved) ResolveReferences();
                return _ownerPlayer;
            }
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (_resolved) return;

            // 부모 본에서 Rigidbody/Collider 탐색 (최대 3단계)
            var current = transform.parent;
            for (int depth = 0; current != null && depth < 3; depth++)
            {
                if (_parentBoneRigidbody == null)
                    _parentBoneRigidbody = current.GetComponent<Rigidbody>();
                if (_parentBoneCollider == null)
                {
                    var col = current.GetComponent<Collider>();
                    if (col != null && !col.isTrigger)
                        _parentBoneCollider = col;
                }
                if (_parentBoneRigidbody != null && _parentBoneCollider != null)
                    break;
                current = current.parent;
            }

            _ownerPlayer = transform.root.GetComponent<NetworkPlayer>();
            _resolved = true;
        }

        /// <summary>grip 포인트의 월드 좌표 반환</summary>
        public Vector3 GetGripWorldPosition()
        {
            return transform.TransformPoint(localGripOffset);
        }

        /// <summary>grip 포인트의 월드 회전 반환</summary>
        public Quaternion GetGripWorldRotation()
        {
            return transform.rotation * localGripRotation;
        }

        /// <summary>
        /// 부모 본 Rigidbody 로컬 좌표계에서의 grip 오프셋.
        /// ConfigurableJoint.connectedAnchor에 직접 사용 가능.
        /// </summary>
        public Vector3 GetGripLocalInBone()
        {
            if (_parentBoneRigidbody == null)
            {
                if (!_resolved) ResolveReferences();
                if (_parentBoneRigidbody == null) return Vector3.zero;
            }
            return _parentBoneRigidbody.transform.InverseTransformPoint(GetGripWorldPosition());
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, grabRadius);

            // grip point 표시
            var gripWorld = GetGripWorldPosition();
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(gripWorld, 0.015f);
            Gizmos.DrawLine(transform.position, gripWorld);
        }
#endif
    }
}
