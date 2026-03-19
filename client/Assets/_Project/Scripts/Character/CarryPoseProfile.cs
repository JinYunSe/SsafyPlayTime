using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// 그랩 대상 종류별 IK 홀드 포즈 오프셋을 정의하는 ScriptableObject.
    /// ProceduralGrabArm.ResolveHoldTarget()에서 사용.
    /// </summary>
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/CarryPoseProfile")]
    public sealed class CarryPoseProfile : ScriptableObject
    {
        [System.Serializable]
        public struct PoseAnchor
        {
            [Tooltip("좌/우 오프셋 (절대값, 좌/우 반전은 자동)")]
            public float sideOffset;
            [Tooltip("앞쪽 오프셋")]
            public float forwardOffset;
            [Tooltip("높이 오프셋 (body root 기준)")]
            public float heightOffset;
            [Tooltip("수직 클램프 범위")]
            public float verticalClamp;
            [Tooltip("수평 클램프 범위")]
            public float lateralClamp;
            [Tooltip("앵커 블렌드 (0=포즈 고정, 1=앵커 추종)")]
            [Range(0f, 1f)] public float anchorBlend;
        }

        [Header("기본 그랩 (정상 캐릭터/오브젝트)")]
        public PoseAnchor frontGrab = new PoseAnchor
        {
            sideOffset = 0.24f,
            forwardOffset = 0.45f,
            heightOffset = 0.82f,
            verticalClamp = 0.55f,
            lateralClamp = 0.35f,
            anchorBlend = 0.6f
        };

        [Header("한손 운반 (기절자 한손/월드 아이템)")]
        public PoseAnchor frontCarry = new PoseAnchor
        {
            sideOffset = 0.20f,
            forwardOffset = 0.30f,
            heightOffset = 0.85f,
            verticalClamp = 0.45f,
            lateralClamp = 0.30f,
            anchorBlend = 0.5f
        };

        [Header("머리 위 운반 (기절자 양손)")]
        public PoseAnchor overheadCarry = new PoseAnchor
        {
            sideOffset = 0.08f,
            forwardOffset = 0.10f,
            heightOffset = 1.25f,
            verticalClamp = 0.30f,
            lateralClamp = 0.20f,
            anchorBlend = 0.3f
        };

        [Header("양손 무기 장착")]
        public PoseAnchor twoHandWeapon = new PoseAnchor
        {
            sideOffset = 0.15f,
            forwardOffset = 0.40f,
            heightOffset = 0.90f,
            verticalClamp = 0.40f,
            lateralClamp = 0.30f,
            anchorBlend = 0.5f
        };

        [Header("Overhead Carry 리프트 보조")]
        [Tooltip("양손 기절자 운반 시 위쪽으로 밀어 올리는 추가 힘")]
        public float overheadLiftForce = 60f;
        [Tooltip("오버헤드 포즈 전환 블렌드 속도")]
        public float overheadBlendSpeed = 4f;
    }
}
