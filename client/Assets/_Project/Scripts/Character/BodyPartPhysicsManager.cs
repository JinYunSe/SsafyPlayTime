using System.Collections.Generic;
using RootMotion.Dynamics;
using UnityEngine;

namespace SSAFYPlayTime.Character
{
    public class BodyPartPhysicsManager : MonoBehaviour
    {
        private const float DefaultWobbleBlendSpeed = 6f;
        private const float DefaultSpeedForMaxWobble = 4f;
        private const float DefaultTurnRateForMaxWobble = 220f;
        private const float DefaultAirborneWobbleBonus = 0.08f;
        private const float DefaultHeadPinMultiplier = 0.72f;
        private const float DefaultHeadMuscleMultiplier = 0.58f;
        private const float DefaultArmPinMultiplier = 0.64f;
        private const float DefaultArmMuscleMultiplier = 0.52f;
        private const float DefaultHandPinMultiplier = 0.82f;
        private const float DefaultHandMuscleMultiplier = 0.72f;
        private const float DefaultGrabbedLooseBonus = 0.16f;
        private const float DefaultUnstableLooseBonus = 0.10f;
        private const float DefaultDraggedLooseBonus = 0.08f;

        [Header("References")]
        [SerializeField] private BodyPartPhysicsProfile profile;
        [SerializeField] private PuppetMaster puppetMaster;
        [SerializeField] private Transform motionReference;
        [SerializeField] private Rigidbody motionRigidbody;
        [SerializeField] private NetworkPlayer networkPlayer;

        [Header("Transition")]
        [SerializeField] private float lerpSpeed = 8f;

        [Header("Dynamic Wobble")]
        [SerializeField] private bool enableDynamicWobble = true;
        [SerializeField] private float wobbleBlendSpeed = 6f;
        [SerializeField] private float speedForMaxWobble = 4f;
        [SerializeField] private float turnRateForMaxWobble = 220f;
        [SerializeField, Range(0f, 0.5f)] private float airborneWobbleBonus = 0.08f;
        [SerializeField, Range(0.25f, 1f)] private float headPinMultiplier = 0.72f;
        [SerializeField, Range(0.25f, 1f)] private float headMuscleMultiplier = 0.58f;
        [SerializeField, Range(0.25f, 1f)] private float armPinMultiplier = 0.64f;
        [SerializeField, Range(0.25f, 1f)] private float armMuscleMultiplier = 0.52f;
        [SerializeField, Range(0.25f, 1f)] private float handPinMultiplier = 0.82f;
        [SerializeField, Range(0.25f, 1f)] private float handMuscleMultiplier = 0.72f;
        [SerializeField, Range(0f, 0.5f)] private float grabbedLooseBonus = 0.16f;
        [SerializeField, Range(0f, 0.5f)] private float unstableLooseBonus = 0.10f;
        [SerializeField, Range(0f, 0.5f)] private float draggedLooseBonus = 0.08f;

        private BodyPartPhysicsProfile.CharacterPhysicsState _currentState = BodyPartPhysicsProfile.CharacterPhysicsState.Normal;
        private BodyPartPhysicsProfile.CharacterPhysicsState _targetState = BodyPartPhysicsProfile.CharacterPhysicsState.Normal;
        public BodyPartPhysicsProfile.CharacterPhysicsState CurrentState => _currentState;

        private List<PhysicMaterial>[] _muscleMaterials;
        private readonly List<PhysicMaterial> _rootDriveMaterials = new();
        private BodyPartPhysicsProfile.BodyPartCategory[] _muscleCategories;
        private float[] _currentPinWeights;
        private float[] _currentMuscleWeights;
        private bool _initialized;

        private Vector3 _lastMotionPosition;
        private float _lastMotionYaw;
        private float _wobbleAmount;
        private bool _motionSampleInitialized;

        private void Awake()
        {
            EnsureDynamicDefaults();

            if (puppetMaster == null)
                puppetMaster = GetComponentInChildren<PuppetMaster>();

            ResolveNetworkPlayer();
            ResolveMotionReferences();
        }

        private void OnValidate()
        {
            EnsureDynamicDefaults();
        }

        private void LateUpdate()
        {
            if (profile == null)
                return;

            if (puppetMaster == null)
                puppetMaster = GetComponentInChildren<PuppetMaster>();

            if (puppetMaster == null)
                return;

            EnsureInitialized();
            if (!_initialized)
                return;

            SyncStateFromNetworkPlayer();

            if (_currentState != _targetState)
                LerpToTarget(Time.deltaTime);

            ApplyDynamicWobble(Time.deltaTime);
        }

        public void SetState(BodyPartPhysicsProfile.CharacterPhysicsState newState)
        {
            _targetState = newState;

            if (profile == null)
                return;

            if (puppetMaster == null)
                puppetMaster = GetComponentInChildren<PuppetMaster>();

            if (puppetMaster == null)
                return;

            EnsureInitialized();
            if (!_initialized)
                return;

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

            if (puppetMaster == null || puppetMaster.muscles == null || puppetMaster.muscles.Length == 0)
                return;

            ResolveNetworkPlayer();
            ResolveMotionReferences();

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

            SyncStateFromNetworkPlayer();
            _initialized = true;
            ApplyImmediate(_targetState);
        }

        private void ResolveMotionReferences()
        {
            if (motionReference == null && puppetMaster != null)
                motionReference = puppetMaster.targetRoot != null ? puppetMaster.targetRoot : puppetMaster.transform;

            if (motionReference == null)
                motionReference = transform;

            if (motionRigidbody == null && motionReference != null)
                motionRigidbody = motionReference.GetComponent<Rigidbody>();

            if (motionRigidbody == null)
                motionRigidbody = GetComponent<Rigidbody>();
        }

        private void ResolveNetworkPlayer()
        {
            if (networkPlayer != null)
                return;

            networkPlayer = GetComponentInParent<NetworkPlayer>();
            if (networkPlayer == null)
                networkPlayer = GetComponent<NetworkPlayer>();
        }

        private void EnsureDynamicDefaults()
        {
            if (wobbleBlendSpeed <= 0f)
                wobbleBlendSpeed = DefaultWobbleBlendSpeed;
            if (speedForMaxWobble <= 0f)
                speedForMaxWobble = DefaultSpeedForMaxWobble;
            if (turnRateForMaxWobble <= 0f)
                turnRateForMaxWobble = DefaultTurnRateForMaxWobble;
            if (airborneWobbleBonus <= 0f)
                airborneWobbleBonus = DefaultAirborneWobbleBonus;
            if (headPinMultiplier <= 0f)
                headPinMultiplier = DefaultHeadPinMultiplier;
            if (headMuscleMultiplier <= 0f)
                headMuscleMultiplier = DefaultHeadMuscleMultiplier;
            if (armPinMultiplier <= 0f)
                armPinMultiplier = DefaultArmPinMultiplier;
            if (armMuscleMultiplier <= 0f)
                armMuscleMultiplier = DefaultArmMuscleMultiplier;
            if (handPinMultiplier <= 0f)
                handPinMultiplier = DefaultHandPinMultiplier;
            if (handMuscleMultiplier <= 0f)
                handMuscleMultiplier = DefaultHandMuscleMultiplier;
            if (grabbedLooseBonus <= 0f)
                grabbedLooseBonus = DefaultGrabbedLooseBonus;
            if (unstableLooseBonus <= 0f)
                unstableLooseBonus = DefaultUnstableLooseBonus;
            if (draggedLooseBonus <= 0f)
                draggedLooseBonus = DefaultDraggedLooseBonus;
        }

        private void SyncStateFromNetworkPlayer()
        {
            ResolveNetworkPlayer();
            if (networkPlayer == null)
                return;

            _targetState = MapPhysicalPhaseToState(networkPlayer.GetPhysicalPhase());
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
            ApplyDynamicWobble(0f);
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
                {
                    allReached = false;
                }
            }

            ApplyRootDriveMaterials(targetProfile);

            if (allReached)
                _currentState = _targetState;
        }

        private void ApplyDynamicWobble(float dt)
        {
            var wobble = enableDynamicWobble ? UpdateWobbleAmount(dt) : 0f;
            var isGrabbed = _currentState == BodyPartPhysicsProfile.CharacterPhysicsState.Grabbed ||
                            _targetState == BodyPartPhysicsProfile.CharacterPhysicsState.Grabbed;
            var isUnstable = _currentState == BodyPartPhysicsProfile.CharacterPhysicsState.Unstable ||
                             _targetState == BodyPartPhysicsProfile.CharacterPhysicsState.Unstable;
            var isDragged = networkPlayer != null && networkPlayer.IsDraggedByPhysics();

            for (var i = 0; i < puppetMaster.muscles.Length; i++)
            {
                var category = _muscleCategories[i];
                var categoryWobble = wobble;

                if (isGrabbed && (category == BodyPartPhysicsProfile.BodyPartCategory.Head ||
                                  category == BodyPartPhysicsProfile.BodyPartCategory.Arm ||
                                  category == BodyPartPhysicsProfile.BodyPartCategory.Hand))
                {
                    categoryWobble = Mathf.Clamp01(categoryWobble + grabbedLooseBonus);
                }

                if (isUnstable && (category == BodyPartPhysicsProfile.BodyPartCategory.Head ||
                                   category == BodyPartPhysicsProfile.BodyPartCategory.Arm ||
                                   category == BodyPartPhysicsProfile.BodyPartCategory.Hand))
                {
                    categoryWobble = Mathf.Clamp01(categoryWobble + unstableLooseBonus);
                }

                if (isDragged && (category == BodyPartPhysicsProfile.BodyPartCategory.Head ||
                                  category == BodyPartPhysicsProfile.BodyPartCategory.Arm ||
                                  category == BodyPartPhysicsProfile.BodyPartCategory.Hand))
                {
                    categoryWobble = Mathf.Clamp01(categoryWobble + draggedLooseBonus);
                }

                var pinMultiplier = 1f;
                var muscleMultiplier = 1f;

                switch (category)
                {
                    case BodyPartPhysicsProfile.BodyPartCategory.Head:
                        pinMultiplier = Mathf.Lerp(1f, headPinMultiplier, categoryWobble);
                        muscleMultiplier = Mathf.Lerp(1f, headMuscleMultiplier, categoryWobble);
                        break;
                    case BodyPartPhysicsProfile.BodyPartCategory.Arm:
                        pinMultiplier = Mathf.Lerp(1f, armPinMultiplier, categoryWobble);
                        muscleMultiplier = Mathf.Lerp(1f, armMuscleMultiplier, categoryWobble);
                        break;
                    case BodyPartPhysicsProfile.BodyPartCategory.Hand:
                        pinMultiplier = Mathf.Lerp(1f, handPinMultiplier, categoryWobble);
                        muscleMultiplier = Mathf.Lerp(1f, handMuscleMultiplier, categoryWobble);
                        break;
                }

                var muscle = puppetMaster.muscles[i];
                muscle.props.pinWeight = Mathf.Clamp01(_currentPinWeights[i] * pinMultiplier);
                muscle.props.muscleWeight = Mathf.Clamp01(_currentMuscleWeights[i] * muscleMultiplier);
            }
        }

        private float UpdateWobbleAmount(float dt)
        {
            ResolveMotionReferences();
            if (motionReference == null)
                return 0f;

            var safeDt = Mathf.Max(dt > 0f ? dt : Time.deltaTime, 0.0001f);
            var currentPosition = motionReference.position;
            var currentYaw = motionReference.eulerAngles.y;

            if (!_motionSampleInitialized)
            {
                _lastMotionPosition = currentPosition;
                _lastMotionYaw = currentYaw;
                _motionSampleInitialized = true;
                _wobbleAmount = 0f;
                return _wobbleAmount;
            }

            var velocity = motionRigidbody != null
                ? motionRigidbody.velocity
                : (currentPosition - _lastMotionPosition) / safeDt;

            var planarSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            var turnRate = Mathf.Abs(Mathf.DeltaAngle(_lastMotionYaw, currentYaw)) / safeDt;

            var speedFactor = speedForMaxWobble > 0f ? Mathf.Clamp01(planarSpeed / speedForMaxWobble) : 0f;
            var turnFactor = turnRateForMaxWobble > 0f ? Mathf.Clamp01(turnRate / turnRateForMaxWobble) : 0f;
            var targetWobble = Mathf.Clamp01(Mathf.Max(speedFactor, turnFactor * 0.85f));

            if (motionRigidbody != null && Mathf.Abs(motionRigidbody.velocity.y) > 1f)
                targetWobble = Mathf.Clamp01(targetWobble + airborneWobbleBonus);

            _wobbleAmount = dt > 0f
                ? Mathf.MoveTowards(_wobbleAmount, targetWobble, wobbleBlendSpeed * dt)
                : targetWobble;

            _lastMotionPosition = currentPosition;
            _lastMotionYaw = currentYaw;
            return _wobbleAmount;
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
                Muscle.Group.Head => BodyPartPhysicsProfile.BodyPartCategory.Head,
                Muscle.Group.Leg => BodyPartPhysicsProfile.BodyPartCategory.Leg,
                Muscle.Group.Foot => BodyPartPhysicsProfile.BodyPartCategory.Leg,
                _ => BodyPartPhysicsProfile.BodyPartCategory.Torso
            };
        }

        private static BodyPartPhysicsProfile.CharacterPhysicsState MapPhysicalPhaseToState(NetworkPlayer.PhysicalPhase phase)
        {
            return phase switch
            {
                NetworkPlayer.PhysicalPhase.BeingGrabbed => BodyPartPhysicsProfile.CharacterPhysicsState.Grabbed,
                NetworkPlayer.PhysicalPhase.Dragged => BodyPartPhysicsProfile.CharacterPhysicsState.Grabbed,
                NetworkPlayer.PhysicalPhase.Unstable => BodyPartPhysicsProfile.CharacterPhysicsState.Unstable,
                NetworkPlayer.PhysicalPhase.Stunned => BodyPartPhysicsProfile.CharacterPhysicsState.Stunned,
                NetworkPlayer.PhysicalPhase.Recovering => BodyPartPhysicsProfile.CharacterPhysicsState.Recovering,
                _ => BodyPartPhysicsProfile.CharacterPhysicsState.Normal
            };
        }
    }
}
