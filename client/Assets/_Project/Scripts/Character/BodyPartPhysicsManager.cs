using System.Collections.Generic;
using RootMotion.Dynamics;
using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// 캐릭터 상태 전이에 따라 PuppetMaster 근육 가중치와 부위별 마찰을 전환한다.
    /// Foot Collider와 루트 drive collider까지 포함해 실제 접촉점 마찰을 제어한다.
    /// </summary>
    public class BodyPartPhysicsManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BodyPartPhysicsProfile profile;
        [SerializeField] private PuppetMaster puppetMaster;

        [Header("Transition")]
        [SerializeField] private float lerpSpeed = 8f;

        private BodyPartPhysicsProfile.CharacterPhysicsState _currentState = BodyPartPhysicsProfile.CharacterPhysicsState.Normal;
        private BodyPartPhysicsProfile.CharacterPhysicsState _targetState = BodyPartPhysicsProfile.CharacterPhysicsState.Normal;
        public BodyPartPhysicsProfile.CharacterPhysicsState CurrentState => _currentState;

        private List<PhysicMaterial>[] _muscleMaterials;
        private readonly List<PhysicMaterial> _rootDriveMaterials = new();
        private BodyPartPhysicsProfile.BodyPartCategory[] _muscleCategories;
        private float[] _currentPinWeights;
        private float[] _currentMuscleWeights;
        private bool _initialized;

        void Awake()
        {
            if (puppetMaster == null)
                puppetMaster = GetComponentInChildren<PuppetMaster>();
        }

        void LateUpdate()
        {
            if (!_initialized || profile == null || puppetMaster == null)
                return;

            if (_currentState == _targetState && lerpSpeed <= 0f)
                return;

            LerpToTarget(Time.deltaTime);
        }

        public void SetState(BodyPartPhysicsProfile.CharacterPhysicsState newState)
        {
            if (profile == null || puppetMaster == null)
                return;

            EnsureInitialized();
            _targetState = newState;

            if (lerpSpeed <= 0f)
            {
                _currentState = newState;
                ApplyImmediate(newState);
            }
        }

        public void SetProfile(BodyPartPhysicsProfile newProfile)
        {
            profile = newProfile;
            _initialized = false;
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;
            if (puppetMaster == null || puppetMaster.muscles == null)
                return;

            var count = puppetMaster.muscles.Length;
            _muscleCategories = new BodyPartPhysicsProfile.BodyPartCategory[count];
            _muscleMaterials = new List<PhysicMaterial>[count];
            _currentPinWeights = new float[count];
            _currentMuscleWeights = new float[count];

            CacheRootDriveMaterials();

            for (var i = 0; i < count; i++)
            {
                var muscle = puppetMaster.muscles[i];
                _muscleCategories[i] = MapGroupToCategory(muscle.props.group);
                _currentPinWeights[i] = muscle.props.pinWeight;
                _currentMuscleWeights[i] = muscle.props.muscleWeight;
                _muscleMaterials[i] = CreateRuntimeMaterialsForMuscle(i, muscle.joint != null ? muscle.joint.transform : null);
            }

            _initialized = true;
            ApplyImmediate(_targetState);
        }

        private void ApplyImmediate(BodyPartPhysicsProfile.CharacterPhysicsState state)
        {
            var stateProfile = profile.GetProfile(state);
            var count = puppetMaster.muscles.Length;

            for (var i = 0; i < count; i++)
            {
                var settings = BodyPartPhysicsProfile.GetSettingsForCategory(stateProfile, _muscleCategories[i]);
                ApplyToMuscle(i, settings);
                _currentPinWeights[i] = settings.pinWeight;
                _currentMuscleWeights[i] = settings.muscleWeight;
            }

            ApplyRootDriveMaterials(stateProfile);
        }

        private void LerpToTarget(float dt)
        {
            var targetProfile = profile.GetProfile(_targetState);
            var count = puppetMaster.muscles.Length;
            var t = lerpSpeed > 0f ? Mathf.Clamp01(lerpSpeed * dt) : 1f;
            var allReached = true;

            for (var i = 0; i < count; i++)
            {
                var target = BodyPartPhysicsProfile.GetSettingsForCategory(targetProfile, _muscleCategories[i]);

                _currentPinWeights[i] = Mathf.Lerp(_currentPinWeights[i], target.pinWeight, t);
                _currentMuscleWeights[i] = Mathf.Lerp(_currentMuscleWeights[i], target.muscleWeight, t);

                var muscle = puppetMaster.muscles[i];
                muscle.props.pinWeight = _currentPinWeights[i];
                muscle.props.muscleWeight = _currentMuscleWeights[i];
                muscle.props.mappingWeight = Mathf.Lerp(muscle.props.mappingWeight, target.mappingWeight, t);
                ApplyMaterialSettings(_muscleMaterials[i], target);

                if (Mathf.Abs(_currentPinWeights[i] - target.pinWeight) > 0.01f ||
                    Mathf.Abs(_currentMuscleWeights[i] - target.muscleWeight) > 0.01f)
                    allReached = false;
            }

            ApplyRootDriveMaterials(targetProfile);

            if (allReached)
                _currentState = _targetState;
        }

        private void ApplyToMuscle(int index, BodyPartPhysicsProfile.BodyPartSettings settings)
        {
            var muscle = puppetMaster.muscles[index];
            muscle.props.pinWeight = settings.pinWeight;
            muscle.props.muscleWeight = settings.muscleWeight;
            muscle.props.mappingWeight = settings.mappingWeight;
            ApplyMaterialSettings(_muscleMaterials[index], settings);
        }

        private void CacheRootDriveMaterials()
        {
            _rootDriveMaterials.Clear();

            var colliders = GetComponents<Collider>();
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider.isTrigger)
                    continue;

                var material = new PhysicMaterial($"BodyPart_RootDrive_{i}");
                collider.material = material;
                _rootDriveMaterials.Add(material);
            }
        }

        private List<PhysicMaterial> CreateRuntimeMaterialsForMuscle(int muscleIndex, Transform jointRoot)
        {
            var materials = new List<PhysicMaterial>();
            if (jointRoot == null)
                return materials;

            CollectColliderMaterialsRecursive(jointRoot, materials, muscleIndex);
            return materials;
        }

        private void CollectColliderMaterialsRecursive(Transform current, List<PhysicMaterial> materials, int muscleIndex)
        {
            if (current == null)
                return;

            var colliders = current.GetComponents<Collider>();
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider.isTrigger)
                    continue;

                var material = new PhysicMaterial($"BodyPart_{_muscleCategories[muscleIndex]}_{muscleIndex}_{materials.Count}");
                collider.material = material;
                materials.Add(material);
            }

            for (var i = 0; i < current.childCount; i++)
            {
                var child = current.GetChild(i);
                if (child == null)
                    continue;

                if (child.GetComponent<Rigidbody>() != null || child.GetComponent<ConfigurableJoint>() != null)
                    continue;

                CollectColliderMaterialsRecursive(child, materials, muscleIndex);
            }
        }

        private static void ApplyMaterialSettings(List<PhysicMaterial> materials, BodyPartPhysicsProfile.BodyPartSettings settings)
        {
            if (materials == null)
                return;

            for (var i = 0; i < materials.Count; i++)
            {
                var material = materials[i];
                if (material == null)
                    continue;

                material.staticFriction = settings.staticFriction;
                material.dynamicFriction = settings.dynamicFriction;
                material.frictionCombine = settings.frictionCombine;
            }
        }

        private void ApplyRootDriveMaterials(BodyPartPhysicsProfile.StateProfile stateProfile)
        {
            // Root sphere collider is the main locomotion contact surface, so reuse leg settings.
            var settings = BodyPartPhysicsProfile.GetSettingsForCategory(
                stateProfile,
                BodyPartPhysicsProfile.BodyPartCategory.Leg);
            ApplyMaterialSettings(_rootDriveMaterials, settings);
        }

        private static BodyPartPhysicsProfile.BodyPartCategory MapGroupToCategory(Muscle.Group group)
        {
            return group switch
            {
                Muscle.Group.Hand => BodyPartPhysicsProfile.BodyPartCategory.Hand,
                Muscle.Group.Arm => BodyPartPhysicsProfile.BodyPartCategory.Arm,
                Muscle.Group.Leg => BodyPartPhysicsProfile.BodyPartCategory.Leg,
                Muscle.Group.Foot => BodyPartPhysicsProfile.BodyPartCategory.Leg,
                _ => BodyPartPhysicsProfile.BodyPartCategory.Torso
            };
        }
    }
}
