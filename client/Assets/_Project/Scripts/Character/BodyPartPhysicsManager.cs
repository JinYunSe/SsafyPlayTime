using UnityEngine;
using RootMotion.Dynamics;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// 캐릭터 상태 전이에 따라 PuppetMaster의 부위별 가중치와 PhysicMaterial을 전환.
    /// NetworkPlayer에서 상태 변경 시 SetState()를 호출하면
    /// BodyPartPhysicsProfile에 정의된 부위별 프리셋을 각 Muscle에 적용한다.
    /// </summary>
    public class BodyPartPhysicsManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private BodyPartPhysicsProfile profile;
        [SerializeField] private PuppetMaster puppetMaster;

        [Header("Transition")]
        [Tooltip("상태 전환 시 보간 속도. 0이면 즉시 적용.")]
        [SerializeField] private float lerpSpeed = 8f;

        private BodyPartPhysicsProfile.CharacterPhysicsState _currentState = BodyPartPhysicsProfile.CharacterPhysicsState.Normal;
        private BodyPartPhysicsProfile.CharacterPhysicsState _targetState = BodyPartPhysicsProfile.CharacterPhysicsState.Normal;
        public BodyPartPhysicsProfile.CharacterPhysicsState CurrentState => _currentState;

        // 부위별 런타임 PhysicMaterial 캐시 (각 Muscle의 Collider에 할당)
        private PhysicMaterial[] _muscleMaterials;
        // Muscle → BodyPartCategory 매핑 캐시
        private BodyPartPhysicsProfile.BodyPartCategory[] _muscleCategories;
        // 보간용 현재 값 캐시
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

        /// <summary>외부(NetworkPlayer 등)에서 상태를 전환할 때 호출.</summary>
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

        /// <summary>프로파일 런타임 교체.</summary>
        public void SetProfile(BodyPartPhysicsProfile newProfile)
        {
            profile = newProfile;
            _initialized = false;
        }

        // ============================================================
        // 초기화
        // ============================================================

        private void EnsureInitialized()
        {
            if (_initialized) return;
            if (puppetMaster == null || puppetMaster.muscles == null) return;

            var count = puppetMaster.muscles.Length;
            _muscleCategories = new BodyPartPhysicsProfile.BodyPartCategory[count];
            _muscleMaterials = new PhysicMaterial[count];
            _currentPinWeights = new float[count];
            _currentMuscleWeights = new float[count];

            for (int i = 0; i < count; i++)
            {
                var muscle = puppetMaster.muscles[i];
                _muscleCategories[i] = MapGroupToCategory(muscle.props.group);
                _currentPinWeights[i] = muscle.props.pinWeight;
                _currentMuscleWeights[i] = muscle.props.muscleWeight;

                // 런타임 PhysicMaterial 생성 및 할당
                var col = muscle.joint != null ? muscle.joint.GetComponent<Collider>() : null;
                if (col != null)
                {
                    var mat = new PhysicMaterial($"BodyPart_{_muscleCategories[i]}_{i}");
                    col.material = mat;
                    _muscleMaterials[i] = mat;
                }
            }

            _initialized = true;
        }

        // ============================================================
        // 적용
        // ============================================================

        private void ApplyImmediate(BodyPartPhysicsProfile.CharacterPhysicsState state)
        {
            var stateProfile = profile.GetProfile(state);
            var count = puppetMaster.muscles.Length;

            for (int i = 0; i < count; i++)
            {
                var settings = BodyPartPhysicsProfile.GetSettingsForCategory(stateProfile, _muscleCategories[i]);
                ApplyToMuscle(i, settings);
                _currentPinWeights[i] = settings.pinWeight;
                _currentMuscleWeights[i] = settings.muscleWeight;
            }
        }

        private void LerpToTarget(float dt)
        {
            var targetProfile = profile.GetProfile(_targetState);
            var count = puppetMaster.muscles.Length;
            var t = lerpSpeed > 0f ? Mathf.Clamp01(lerpSpeed * dt) : 1f;
            var allReached = true;

            for (int i = 0; i < count; i++)
            {
                var target = BodyPartPhysicsProfile.GetSettingsForCategory(targetProfile, _muscleCategories[i]);

                _currentPinWeights[i] = Mathf.Lerp(_currentPinWeights[i], target.pinWeight, t);
                _currentMuscleWeights[i] = Mathf.Lerp(_currentMuscleWeights[i], target.muscleWeight, t);

                var muscle = puppetMaster.muscles[i];
                muscle.props.pinWeight = _currentPinWeights[i];
                muscle.props.muscleWeight = _currentMuscleWeights[i];
                muscle.props.mappingWeight = Mathf.Lerp(muscle.props.mappingWeight, target.mappingWeight, t);

                // PhysicMaterial은 보간 대신 즉시 적용 (마찰 전환은 즉시가 자연스러움)
                if (_currentState != _targetState && _muscleMaterials[i] != null)
                {
                    _muscleMaterials[i].staticFriction = target.staticFriction;
                    _muscleMaterials[i].dynamicFriction = target.dynamicFriction;
                    _muscleMaterials[i].frictionCombine = target.frictionCombine;
                }

                if (Mathf.Abs(_currentPinWeights[i] - target.pinWeight) > 0.01f ||
                    Mathf.Abs(_currentMuscleWeights[i] - target.muscleWeight) > 0.01f)
                    allReached = false;
            }

            if (allReached)
                _currentState = _targetState;
        }

        private void ApplyToMuscle(int index, BodyPartPhysicsProfile.BodyPartSettings settings)
        {
            var muscle = puppetMaster.muscles[index];
            muscle.props.pinWeight = settings.pinWeight;
            muscle.props.muscleWeight = settings.muscleWeight;
            muscle.props.mappingWeight = settings.mappingWeight;

            if (_muscleMaterials[index] != null)
            {
                _muscleMaterials[index].staticFriction = settings.staticFriction;
                _muscleMaterials[index].dynamicFriction = settings.dynamicFriction;
                _muscleMaterials[index].frictionCombine = settings.frictionCombine;
            }
        }

        // ============================================================
        // Muscle.Group → BodyPartCategory 매핑
        // ============================================================

        private static BodyPartPhysicsProfile.BodyPartCategory MapGroupToCategory(Muscle.Group group)
        {
            return group switch
            {
                Muscle.Group.Hand => BodyPartPhysicsProfile.BodyPartCategory.Hand,
                Muscle.Group.Arm  => BodyPartPhysicsProfile.BodyPartCategory.Arm,
                Muscle.Group.Leg  => BodyPartPhysicsProfile.BodyPartCategory.Leg,
                Muscle.Group.Foot => BodyPartPhysicsProfile.BodyPartCategory.Leg,
                // Hips, Spine, Head → Torso
                _ => BodyPartPhysicsProfile.BodyPartCategory.Torso
            };
        }
    }
}
