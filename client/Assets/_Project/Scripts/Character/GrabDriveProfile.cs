using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// 그랩 물리 설정을 담는 ScriptableObject.
    /// FixedJoint(딱딱) / ConfigurableJoint(탄성) 모드를 선택 가능.
    /// </summary>
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/GrabDriveProfile")]
    public sealed class GrabDriveProfile : ScriptableObject
    {
        public enum GrabJointMode
        {
            FixedJoint,         // 기존 방식 — 딱딱한 연결, breakForce로 끊김
            ConfigurableJoint   // 파티 애니멀즈 방식 — spring 기반 탄성 연결
        }

        [Header("Joint Mode")]
        [Tooltip("FixedJoint = 딱딱한 연결, ConfigurableJoint = 탄성 있는 연결")]
        public GrabJointMode jointMode = GrabJointMode.ConfigurableJoint;

        [Header("Break Force (FixedJoint 모드)")]
        public float breakForce = 2000f;
        public float breakTorque = 2000f;

        [Header("Spring Drive (ConfigurableJoint 모드)")]
        [Tooltip("잡는 힘. 높을수록 단단히 잡힘")]
        public float grabSpring = 800f;
        [Tooltip("흔들림 감쇠. 높을수록 안정적")]
        public float grabDamper = 40f;
        [Tooltip("최대 힘. 이 이상 저항하면 빠짐 (breakForce 역할)")]
        public float maximumForce = 2000f;
        [Tooltip("잡힌 상태에서 늘어날 수 있는 최대 거리")]
        public float linearLimit = 0.3f;
        [Tooltip("리미트 접근 시 반발 스프링")]
        public float limitSpring = 500f;
        [Tooltip("리미트 반발 댐퍼")]
        public float limitDamper = 30f;

        [Header("Dual Grab")]
        [Tooltip("양손 잡기 시 spring/breakForce 배율")]
        public float dualGrabMultiplier = 2.5f;

        [Header("Opponent Weaken")]
        public float grabbedPinWeight = 0.3f;
        public float grabbedMuscleWeight = 0.3f;

        [Header("Throw")]
        public float throwUpComponent = 0.4f;

        /// <summary>ConfigurableJoint용 드라이브 생성</summary>
        public JointDrive CreateGrabDrive(bool isDualGrab = false)
        {
            var mult = isDualGrab ? dualGrabMultiplier : 1f;
            return new JointDrive
            {
                positionSpring = grabSpring * mult,
                positionDamper = grabDamper,
                maximumForce = maximumForce * mult
            };
        }

        /// <summary>ConfigurableJoint용 리미트 생성</summary>
        public SoftJointLimitSpring CreateLimitSpring()
        {
            return new SoftJointLimitSpring
            {
                spring = limitSpring,
                damper = limitDamper
            };
        }

        public SoftJointLimit CreateLinearLimit()
        {
            return new SoftJointLimit
            {
                limit = linearLimit
            };
        }
    }
}
