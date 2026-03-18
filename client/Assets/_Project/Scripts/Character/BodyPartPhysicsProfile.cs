using UnityEngine;

namespace SSAFYPlayTime.Character
{
    [CreateAssetMenu(menuName = "SSAFYPlayTime/Character/BodyPartPhysicsProfile")]
    public sealed class BodyPartPhysicsProfile : ScriptableObject
    {
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

        [System.Serializable]
        public struct StateProfile
        {
            public BodyPartSettings hand;
            public BodyPartSettings arm;
            public BodyPartSettings head;
            public BodyPartSettings torso;
            public BodyPartSettings leg;
        }

        public enum BodyPartCategory
        {
            Hand,
            Arm,
            Head,
            Torso,
            Leg
        }

        public enum CharacterPhysicsState
        {
            Normal,
            Grabbed,
            Unstable,
            Stunned,
            CarriedStunned,
            Recovering
        }

        [Header("Normal")]
        public StateProfile normal = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.68f, muscleWeight = 0.60f, mappingWeight = 0.95f, staticFriction = 1.0f, dynamicFriction = 0.85f, frictionCombine = PhysicMaterialCombine.Average },
            arm = new BodyPartSettings { pinWeight = 0.42f, muscleWeight = 0.34f, mappingWeight = 0.90f, staticFriction = 0.20f, dynamicFriction = 0.14f, frictionCombine = PhysicMaterialCombine.Average },
            head = new BodyPartSettings { pinWeight = 0.50f, muscleWeight = 0.34f, mappingWeight = 0.92f, staticFriction = 0.12f, dynamicFriction = 0.08f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.78f, muscleWeight = 0.72f, mappingWeight = 0.95f, staticFriction = 0.20f, dynamicFriction = 0.15f, frictionCombine = PhysicMaterialCombine.Minimum },
            leg = new BodyPartSettings { pinWeight = 0.95f, muscleWeight = 0.90f, mappingWeight = 1f, staticFriction = 1.0f, dynamicFriction = 0.80f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Grabbed")]
        public StateProfile grabbed = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.25f, muscleWeight = 0.18f, mappingWeight = 0.76f, staticFriction = 1.30f, dynamicFriction = 1.05f, frictionCombine = PhysicMaterialCombine.Maximum },
            arm = new BodyPartSettings { pinWeight = 0.12f, muscleWeight = 0.08f, mappingWeight = 0.55f, staticFriction = 0.18f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Average },
            head = new BodyPartSettings { pinWeight = 0.32f, muscleWeight = 0.22f, mappingWeight = 0.85f, staticFriction = 0.12f, dynamicFriction = 0.06f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.28f, muscleWeight = 0.22f, mappingWeight = 0.55f, staticFriction = 0.12f, dynamicFriction = 0.08f, frictionCombine = PhysicMaterialCombine.Minimum },
            leg = new BodyPartSettings { pinWeight = 0.40f, muscleWeight = 0.34f, mappingWeight = 0.68f, staticFriction = 0.55f, dynamicFriction = 0.35f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Unstable")]
        public StateProfile unstable = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.34f, muscleWeight = 0.26f, mappingWeight = 0.80f, staticFriction = 0.45f, dynamicFriction = 0.30f, frictionCombine = PhysicMaterialCombine.Average },
            arm = new BodyPartSettings { pinWeight = 0.18f, muscleWeight = 0.12f, mappingWeight = 0.62f, staticFriction = 0.15f, dynamicFriction = 0.08f, frictionCombine = PhysicMaterialCombine.Average },
            head = new BodyPartSettings { pinWeight = 0.28f, muscleWeight = 0.18f, mappingWeight = 0.86f, staticFriction = 0.10f, dynamicFriction = 0.06f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.45f, muscleWeight = 0.35f, mappingWeight = 0.72f, staticFriction = 0.16f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            leg = new BodyPartSettings { pinWeight = 0.62f, muscleWeight = 0.55f, mappingWeight = 0.86f, staticFriction = 0.55f, dynamicFriction = 0.35f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Stunned")]
        public StateProfile stunned = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.04f, muscleWeight = 0.03f, mappingWeight = 1f, staticFriction = 0.45f, dynamicFriction = 0.28f, frictionCombine = PhysicMaterialCombine.Average },
            arm = new BodyPartSettings { pinWeight = 0.06f, muscleWeight = 0.04f, mappingWeight = 1f, staticFriction = 0.40f, dynamicFriction = 0.25f, frictionCombine = PhysicMaterialCombine.Average },
            head = new BodyPartSettings { pinWeight = 0.08f, muscleWeight = 0.05f, mappingWeight = 1f, staticFriction = 0.35f, dynamicFriction = 0.22f, frictionCombine = PhysicMaterialCombine.Average },
            torso = new BodyPartSettings { pinWeight = 0.24f, muscleWeight = 0.16f, mappingWeight = 1f, staticFriction = 1.20f, dynamicFriction = 0.95f, frictionCombine = PhysicMaterialCombine.Maximum },
            leg = new BodyPartSettings { pinWeight = 0.18f, muscleWeight = 0.12f, mappingWeight = 1f, staticFriction = 1.35f, dynamicFriction = 1.05f, frictionCombine = PhysicMaterialCombine.Maximum }
        };

        [Header("Carried Stunned (기절 + 운반 중 — 형태 유지용 최소 pin/muscle)")]
        public StateProfile carriedStunned = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.05f, muscleWeight = 0.05f, mappingWeight = 1f, staticFriction = 0.20f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            arm = new BodyPartSettings { pinWeight = 0.08f, muscleWeight = 0.06f, mappingWeight = 1f, staticFriction = 0.20f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            head = new BodyPartSettings { pinWeight = 0.15f, muscleWeight = 0.12f, mappingWeight = 1f, staticFriction = 0.15f, dynamicFriction = 0.08f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.22f, muscleWeight = 0.18f, mappingWeight = 1f, staticFriction = 0.50f, dynamicFriction = 0.30f, frictionCombine = PhysicMaterialCombine.Average },
            leg = new BodyPartSettings { pinWeight = 0.10f, muscleWeight = 0.08f, mappingWeight = 1f, staticFriction = 0.30f, dynamicFriction = 0.20f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Recovering")]
        public StateProfile recovering = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.48f, muscleWeight = 0.40f, mappingWeight = 0.82f, staticFriction = 0.90f, dynamicFriction = 0.70f, frictionCombine = PhysicMaterialCombine.Average },
            arm = new BodyPartSettings { pinWeight = 0.26f, muscleWeight = 0.20f, mappingWeight = 0.70f, staticFriction = 0.20f, dynamicFriction = 0.14f, frictionCombine = PhysicMaterialCombine.Average },
            head = new BodyPartSettings { pinWeight = 0.42f, muscleWeight = 0.30f, mappingWeight = 0.88f, staticFriction = 0.12f, dynamicFriction = 0.08f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.52f, muscleWeight = 0.46f, mappingWeight = 0.78f, staticFriction = 0.20f, dynamicFriction = 0.14f, frictionCombine = PhysicMaterialCombine.Average },
            leg = new BodyPartSettings { pinWeight = 0.76f, muscleWeight = 0.72f, mappingWeight = 0.90f, staticFriction = 0.75f, dynamicFriction = 0.55f, frictionCombine = PhysicMaterialCombine.Average }
        };

        public StateProfile GetProfile(CharacterPhysicsState state)
        {
            return state switch
            {
                CharacterPhysicsState.Grabbed => grabbed,
                CharacterPhysicsState.Unstable => unstable,
                CharacterPhysicsState.Stunned => stunned,
                CharacterPhysicsState.CarriedStunned => carriedStunned,
                CharacterPhysicsState.Recovering => recovering,
                _ => normal
            };
        }

        public static BodyPartSettings GetSettingsForCategory(in StateProfile profile, BodyPartCategory category)
        {
            return category switch
            {
                BodyPartCategory.Hand => profile.hand,
                BodyPartCategory.Arm => profile.arm,
                BodyPartCategory.Head => profile.head,
                BodyPartCategory.Torso => profile.torso,
                BodyPartCategory.Leg => profile.leg,
                _ => profile.torso
            };
        }
    }
}
