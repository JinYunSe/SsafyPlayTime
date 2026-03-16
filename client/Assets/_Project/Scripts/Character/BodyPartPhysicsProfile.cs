using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// 캐릭터 상태(Normal/Grabbed/Stunned/Recovering)별로
    /// 부위(Hand/Arm/Torso/Leg)마다 PuppetMaster 가중치와 PhysicMaterial을 정의.
    /// Party Animals 스타일의 "손은 버티고 몸은 미끄러지고 발은 선다" 접지감을 구현.
    /// </summary>
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/BodyPartPhysicsProfile")]
    public sealed class BodyPartPhysicsProfile : ScriptableObject
    {
        /// <summary>부위별 물리 가중치 + 마찰 설정</summary>
        [System.Serializable]
        public struct BodyPartSettings
        {
            [Range(0f, 1f)] public float pinWeight;
            [Range(0f, 1f)] public float muscleWeight;
            [Range(0f, 1f)] public float mappingWeight;
            [Range(0f, 2f)] public float staticFriction;
            [Range(0f, 2f)] public float dynamicFriction;
            public PhysicMaterialCombine frictionCombine;
        }

        /// <summary>하나의 캐릭터 상태에 대한 부위별 프리셋 묶음</summary>
        [System.Serializable]
        public struct StateProfile
        {
            public BodyPartSettings hand;
            public BodyPartSettings arm;
            public BodyPartSettings torso;
            public BodyPartSettings leg;
        }

        /// <summary>PuppetMaster Muscle.Group → 4개 부위 카테고리</summary>
        public enum BodyPartCategory { Hand, Arm, Torso, Leg }

        /// <summary>캐릭터 물리 상태</summary>
        public enum CharacterPhysicsState { Normal, Grabbed, Stunned, Recovering }

        [Header("Normal — 기본 서있는 상태")]
        public StateProfile normal = new StateProfile
        {
            hand   = new BodyPartSettings { pinWeight = 1f,   muscleWeight = 1f,   mappingWeight = 1f, staticFriction = 1.2f, dynamicFriction = 1.0f, frictionCombine = PhysicMaterialCombine.Average },
            arm    = new BodyPartSettings { pinWeight = 0.8f, muscleWeight = 0.8f, mappingWeight = 1f, staticFriction = 0.4f, dynamicFriction = 0.3f, frictionCombine = PhysicMaterialCombine.Average },
            torso  = new BodyPartSettings { pinWeight = 1f,   muscleWeight = 1f,   mappingWeight = 1f, staticFriction = 0.3f, dynamicFriction = 0.2f, frictionCombine = PhysicMaterialCombine.Minimum },
            leg    = new BodyPartSettings { pinWeight = 1f,   muscleWeight = 1f,   mappingWeight = 1f, staticFriction = 0.8f, dynamicFriction = 0.6f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Grabbed — 다른 플레이어에게 잡힌 상태")]
        public StateProfile grabbed = new StateProfile
        {
            hand   = new BodyPartSettings { pinWeight = 0.5f, muscleWeight = 0.5f, mappingWeight = 0.8f, staticFriction = 1.5f, dynamicFriction = 1.2f, frictionCombine = PhysicMaterialCombine.Maximum },
            arm    = new BodyPartSettings { pinWeight = 0.3f, muscleWeight = 0.3f, mappingWeight = 0.6f, staticFriction = 0.3f, dynamicFriction = 0.2f, frictionCombine = PhysicMaterialCombine.Average },
            torso  = new BodyPartSettings { pinWeight = 0.3f, muscleWeight = 0.3f, mappingWeight = 0.5f, staticFriction = 0.15f, dynamicFriction = 0.1f, frictionCombine = PhysicMaterialCombine.Minimum },
            leg    = new BodyPartSettings { pinWeight = 0.4f, muscleWeight = 0.4f, mappingWeight = 0.7f, staticFriction = 0.6f, dynamicFriction = 0.4f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Stunned — 기절 상태")]
        public StateProfile stunned = new StateProfile
        {
            hand   = new BodyPartSettings { pinWeight = 0f, muscleWeight = 0f, mappingWeight = 0.3f, staticFriction = 0.2f, dynamicFriction = 0.1f, frictionCombine = PhysicMaterialCombine.Minimum },
            arm    = new BodyPartSettings { pinWeight = 0f, muscleWeight = 0f, mappingWeight = 0.3f, staticFriction = 0.2f, dynamicFriction = 0.1f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso  = new BodyPartSettings { pinWeight = 0f, muscleWeight = 0f, mappingWeight = 0.3f, staticFriction = 0.5f, dynamicFriction = 0.3f, frictionCombine = PhysicMaterialCombine.Average },
            leg    = new BodyPartSettings { pinWeight = 0f, muscleWeight = 0f, mappingWeight = 0.3f, staticFriction = 0.3f, dynamicFriction = 0.2f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Recovering — 기절 회복 직후 취약 상태")]
        public StateProfile recovering = new StateProfile
        {
            hand   = new BodyPartSettings { pinWeight = 0.6f, muscleWeight = 0.6f, mappingWeight = 0.8f, staticFriction = 1.0f, dynamicFriction = 0.8f, frictionCombine = PhysicMaterialCombine.Average },
            arm    = new BodyPartSettings { pinWeight = 0.5f, muscleWeight = 0.5f, mappingWeight = 0.7f, staticFriction = 0.3f, dynamicFriction = 0.2f, frictionCombine = PhysicMaterialCombine.Average },
            torso  = new BodyPartSettings { pinWeight = 0.6f, muscleWeight = 0.6f, mappingWeight = 0.8f, staticFriction = 0.3f, dynamicFriction = 0.2f, frictionCombine = PhysicMaterialCombine.Average },
            leg    = new BodyPartSettings { pinWeight = 0.7f, muscleWeight = 0.7f, mappingWeight = 0.9f, staticFriction = 0.7f, dynamicFriction = 0.5f, frictionCombine = PhysicMaterialCombine.Average }
        };

        /// <summary>상태 열거형으로 StateProfile 조회</summary>
        public StateProfile GetProfile(CharacterPhysicsState state)
        {
            return state switch
            {
                CharacterPhysicsState.Grabbed    => grabbed,
                CharacterPhysicsState.Stunned    => stunned,
                CharacterPhysicsState.Recovering => recovering,
                _ => normal
            };
        }

        /// <summary>BodyPartCategory로 StateProfile 내 해당 부위 설정 조회</summary>
        public static BodyPartSettings GetSettingsForCategory(in StateProfile profile, BodyPartCategory category)
        {
            return category switch
            {
                BodyPartCategory.Hand  => profile.hand,
                BodyPartCategory.Arm   => profile.arm,
                BodyPartCategory.Torso => profile.torso,
                BodyPartCategory.Leg   => profile.leg,
                _ => profile.torso
            };
        }
    }
}
