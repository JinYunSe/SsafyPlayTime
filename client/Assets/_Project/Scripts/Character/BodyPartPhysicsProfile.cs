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
            Recovering,
            StunnedCollapse
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
            hand = new BodyPartSettings { pinWeight = 0.38f, muscleWeight = 0.30f, mappingWeight = 0.88f, staticFriction = 0.95f, dynamicFriction = 0.72f, frictionCombine = PhysicMaterialCombine.Average },
            arm = new BodyPartSettings { pinWeight = 0.34f, muscleWeight = 0.26f, mappingWeight = 0.84f, staticFriction = 0.22f, dynamicFriction = 0.12f, frictionCombine = PhysicMaterialCombine.Average },
            head = new BodyPartSettings { pinWeight = 0.52f, muscleWeight = 0.40f, mappingWeight = 0.92f, staticFriction = 0.16f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Average },
            torso = new BodyPartSettings { pinWeight = 0.74f, muscleWeight = 0.66f, mappingWeight = 0.94f, staticFriction = 0.22f, dynamicFriction = 0.16f, frictionCombine = PhysicMaterialCombine.Average },
            leg = new BodyPartSettings { pinWeight = 0.82f, muscleWeight = 0.76f, mappingWeight = 0.96f, staticFriction = 0.85f, dynamicFriction = 0.65f, frictionCombine = PhysicMaterialCombine.Average }
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

        [Header("Stunned Collapse")]
        public StateProfile stunnedCollapse = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.06f, muscleWeight = 0.04f, mappingWeight = 0.60f, staticFriction = 0.18f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            arm = new BodyPartSettings { pinWeight = 0.08f, muscleWeight = 0.05f, mappingWeight = 0.62f, staticFriction = 0.16f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            head = new BodyPartSettings { pinWeight = 0.10f, muscleWeight = 0.07f, mappingWeight = 0.68f, staticFriction = 0.18f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.12f, muscleWeight = 0.09f, mappingWeight = 0.72f, staticFriction = 0.28f, dynamicFriction = 0.16f, frictionCombine = PhysicMaterialCombine.Minimum },
            leg = new BodyPartSettings { pinWeight = 0.14f, muscleWeight = 0.10f, mappingWeight = 0.74f, staticFriction = 0.34f, dynamicFriction = 0.20f, frictionCombine = PhysicMaterialCombine.Minimum }
        };

        [Header("Stunned")]
        public StateProfile stunned = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.10f, muscleWeight = 0.07f, mappingWeight = 0.78f, staticFriction = 0.24f, dynamicFriction = 0.14f, frictionCombine = PhysicMaterialCombine.Minimum },
            arm = new BodyPartSettings { pinWeight = 0.12f, muscleWeight = 0.09f, mappingWeight = 0.80f, staticFriction = 0.22f, dynamicFriction = 0.14f, frictionCombine = PhysicMaterialCombine.Minimum },
            head = new BodyPartSettings { pinWeight = 0.18f, muscleWeight = 0.14f, mappingWeight = 0.84f, staticFriction = 0.24f, dynamicFriction = 0.16f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.24f, muscleWeight = 0.18f, mappingWeight = 0.86f, staticFriction = 0.42f, dynamicFriction = 0.28f, frictionCombine = PhysicMaterialCombine.Average },
            leg = new BodyPartSettings { pinWeight = 0.28f, muscleWeight = 0.22f, mappingWeight = 0.88f, staticFriction = 0.48f, dynamicFriction = 0.32f, frictionCombine = PhysicMaterialCombine.Average }
        };

        [Header("Carried Stunned (기절 + 운반 중 — 매달려 따라오기용, 자세 유지력 최소화)")]
        public StateProfile carriedStunned = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.10f, muscleWeight = 0.06f, mappingWeight = 0.82f, staticFriction = 0.18f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            arm = new BodyPartSettings { pinWeight = 0.12f, muscleWeight = 0.08f, mappingWeight = 0.80f, staticFriction = 0.16f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Minimum },
            head = new BodyPartSettings { pinWeight = 0.18f, muscleWeight = 0.12f, mappingWeight = 0.86f, staticFriction = 0.14f, dynamicFriction = 0.08f, frictionCombine = PhysicMaterialCombine.Minimum },
            torso = new BodyPartSettings { pinWeight = 0.20f, muscleWeight = 0.14f, mappingWeight = 0.78f, staticFriction = 0.24f, dynamicFriction = 0.14f, frictionCombine = PhysicMaterialCombine.Minimum },
            leg = new BodyPartSettings { pinWeight = 0.14f, muscleWeight = 0.10f, mappingWeight = 0.76f, staticFriction = 0.20f, dynamicFriction = 0.12f, frictionCombine = PhysicMaterialCombine.Minimum }
        };

        [Header("Recovering")]
        public StateProfile recovering = new StateProfile
        {
            hand = new BodyPartSettings { pinWeight = 0.56f, muscleWeight = 0.46f, mappingWeight = 0.90f, staticFriction = 0.95f, dynamicFriction = 0.76f, frictionCombine = PhysicMaterialCombine.Average },
            arm = new BodyPartSettings { pinWeight = 0.42f, muscleWeight = 0.34f, mappingWeight = 0.82f, staticFriction = 0.24f, dynamicFriction = 0.16f, frictionCombine = PhysicMaterialCombine.Average },
            head = new BodyPartSettings { pinWeight = 0.54f, muscleWeight = 0.42f, mappingWeight = 0.92f, staticFriction = 0.16f, dynamicFriction = 0.10f, frictionCombine = PhysicMaterialCombine.Average },
            torso = new BodyPartSettings { pinWeight = 0.76f, muscleWeight = 0.70f, mappingWeight = 0.94f, staticFriction = 0.24f, dynamicFriction = 0.16f, frictionCombine = PhysicMaterialCombine.Average },
            leg = new BodyPartSettings { pinWeight = 0.88f, muscleWeight = 0.84f, mappingWeight = 0.98f, staticFriction = 0.82f, dynamicFriction = 0.62f, frictionCombine = PhysicMaterialCombine.Average }
        };

        public StateProfile GetProfile(CharacterPhysicsState state)
        {
            return state switch
            {
                CharacterPhysicsState.Grabbed => grabbed,
                CharacterPhysicsState.Unstable => unstable,
                CharacterPhysicsState.StunnedCollapse => stunnedCollapse,
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
