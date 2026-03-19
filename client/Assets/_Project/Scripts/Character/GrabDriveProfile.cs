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

        [Header("Player Grab Override (사람-사람 그랩 전용)")]
        [Tooltip("활성화하면 사람을 잡을 때 별도 수치 사용")]
        public bool usePlayerGrabOverride = true;
        [Tooltip("사람을 잡을 때 spring (기본보다 약하게)")]
        public float playerGrabSpring = 500f;
        public float playerGrabDamper = 55f;
        [Tooltip("사람을 잡을 때 breakForce (기본보다 약하게 — 빠지기 쉬움)")]
        public float playerGrabBreakForce = 1200f;
        [Tooltip("사람을 잡을 때 최대 늘어남 거리 (기본보다 넓게)")]
        public float playerGrabLinearLimit = 0.5f;
        public bool playerWeakeningEnabled = false;

        [Header("Object Grab Override (오브젝트 그랩 전용)")]
        [Tooltip("활성화하면 오브젝트를 잡을 때 별도 수치 사용")]
        public bool useObjectGrabOverride = true;
        [Tooltip("오브젝트를 잡을 때 spring (단단히)")]
        public float objectGrabSpring = 1200f;
        [Tooltip("오브젝트를 잡을 때 breakForce (잘 안 빠짐)")]
        public float objectGrabBreakForce = 3000f;
        [Tooltip("오브젝트를 잡을 때 최대 늘어남 거리 (짧게)")]
        public float objectGrabLinearLimit = 0.15f;

        [Header("Stunned Player Grab Override (기절자 그랩 전용)")]
        [Tooltip("활성화하면 기절한 캐릭터를 잡을 때 별도 수치 사용")]
        public bool useStunnedPlayerGrabOverride = true;
        [Tooltip("기절자를 잡을 때 spring (부드러운 운반 — 너무 강하면 몸이 찌그러짐)")]
        public float stunnedPlayerGrabSpring = 500f;
        [Tooltip("기절자를 잡을 때 breakForce (적당히 — 너무 강하면 형태 왜곡)")]
        public float stunnedPlayerGrabBreakForce = 1300f;
        [Tooltip("기절자를 잡을 때 최대 늘어남 거리")]
        public float stunnedPlayerGrabLinearLimit = 0.25f;
        [Tooltip("기절자 잡기 시 댐퍼 (높을수록 흔들림 감소, 부드러운 운반)")]
        public float stunnedPlayerGrabDamper = 70f;
        [Tooltip("기절자 잡기 시 시간 경과 약화 적용 여부 (false = 약화 없이 안정적 유지)")]
        public bool stunnedPlayerWeakeningEnabled = false;

        [Header("Weakening Curve (잡힌 시간에 따른 약화)")]
        [Tooltip("잡힌 상태가 지속될수록 breakForce가 줄어드는 커브 (x=시간초, y=0~1 배율)")]
        public AnimationCurve weakeningCurve = AnimationCurve.Linear(0f, 1f, 5f, 0.4f);

        [Header("Opponent Weaken")]
        public float grabbedPinWeight = 0.3f;
        public float grabbedMuscleWeight = 0.3f;

        [Header("Throw")]
        public float throwUpComponent = 0.4f;

        [Header("Conscious Player Push (정상 캐릭터 밀어내기)")]
        [Tooltip("정상 캐릭터를 던질 때 사용할 힘 배율 (약하게 — 밀어내기만)")]
        public float consciousPushForceScale = 0.4f;
        [Tooltip("정상 캐릭터 밀어내기 시 위쪽 성분 (낮게 — 포물선 아닌 수평 밀기)")]
        public float consciousPushUpComponent = 0.1f;

        /// <summary>그랩 타겟 유형</summary>
        public enum GrabTargetType { Default, Player, Object, StunnedPlayer, Weapon }

        /// <summary>ConfigurableJoint용 드라이브 생성</summary>
        public JointDrive CreateGrabDrive(bool isDualGrab = false, GrabTargetType targetType = GrabTargetType.Default)
        {
            var mult = isDualGrab ? dualGrabMultiplier : 1f;
            var spring = ResolveSpring(targetType);
            var maxF = ResolveBreakForce(targetType);

            return new JointDrive
            {
                positionSpring = spring * mult,
                positionDamper = ResolveDamper(targetType),
                maximumForce = maxF * mult
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

        public SoftJointLimit CreateLinearLimit(GrabTargetType targetType = GrabTargetType.Default)
        {
            return new SoftJointLimit
            {
                limit = ResolveLinearLimit(targetType)
            };
        }

        /// <summary>잡힌 시간 경과에 따라 약화된 breakForce 반환</summary>
        public float EvaluateWeakenedBreakForce(GrabTargetType targetType, float holdDuration)
        {
            var baseForce = ResolveBreakForce(targetType);
            var curveValue = weakeningCurve != null ? weakeningCurve.Evaluate(holdDuration) : 1f;
            return baseForce * Mathf.Clamp01(curveValue);
        }

        /// <summary>기절자 잡기 시 시간 경과 약화를 건너뛸지 여부</summary>
        public bool ShouldSkipWeakening(GrabTargetType targetType)
        {
            if (targetType == GrabTargetType.Player && usePlayerGrabOverride && !playerWeakeningEnabled)
                return true;
            if (targetType == GrabTargetType.StunnedPlayer && useStunnedPlayerGrabOverride && !stunnedPlayerWeakeningEnabled)
                return true;
            return false;
        }

        private float ResolveSpring(GrabTargetType targetType)
        {
            if (targetType == GrabTargetType.StunnedPlayer && useStunnedPlayerGrabOverride)
                return stunnedPlayerGrabSpring;
            if (targetType == GrabTargetType.Player && usePlayerGrabOverride)
                return playerGrabSpring;
            if (targetType == GrabTargetType.Object && useObjectGrabOverride)
                return objectGrabSpring;
            return grabSpring;
        }

        private float ResolveBreakForce(GrabTargetType targetType)
        {
            if (targetType == GrabTargetType.StunnedPlayer && useStunnedPlayerGrabOverride)
                return stunnedPlayerGrabBreakForce;
            if (targetType == GrabTargetType.Player && usePlayerGrabOverride)
                return playerGrabBreakForce;
            if (targetType == GrabTargetType.Object && useObjectGrabOverride)
                return objectGrabBreakForce;
            return maximumForce;
        }

        private float ResolveLinearLimit(GrabTargetType targetType)
        {
            if (targetType == GrabTargetType.StunnedPlayer && useStunnedPlayerGrabOverride)
                return stunnedPlayerGrabLinearLimit;
            if (targetType == GrabTargetType.Player && usePlayerGrabOverride)
                return playerGrabLinearLimit;
            if (targetType == GrabTargetType.Object && useObjectGrabOverride)
                return objectGrabLinearLimit;
            return linearLimit;
        }

        private float ResolveDamper(GrabTargetType targetType)
        {
            if (targetType == GrabTargetType.StunnedPlayer && useStunnedPlayerGrabOverride)
                return stunnedPlayerGrabDamper;
            if (targetType == GrabTargetType.Player && usePlayerGrabOverride)
                return playerGrabDamper;
            return grabDamper;
        }
    }
}
