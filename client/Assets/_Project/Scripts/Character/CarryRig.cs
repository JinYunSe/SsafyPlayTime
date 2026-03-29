using RootMotion.Dynamics;
using UnityEngine;

namespace SSAFYPlayTime.Character
{
    /// <summary>
    /// CarrySolveFrame 통합 기준점.
    /// 프리팹의 puppetMaster.targetRoot 아래에 배치하며,
    /// carrier/victim 양쪽의 손, root, proxy hips, presentation root가
    /// 모두 이 기준점을 참조하게 만든다.
    ///
    /// Anchor 종류:
    /// - CarryFrontAnchor:    한손 캐리 기준점 (hips-chest 사이, 앞쪽)
    /// - CarryOverheadAnchor: 양손 오버헤드 캐리 기준점 (head 근처)
    /// - CarryDualAnchor:     양손 캐리 기준점 (chest 높이)
    /// - CarryVictimAnchor:   피운반자 측 기준점 (hips 0.65 + chest 0.35 가중 평균)
    /// - CarryChestSample:    chest 샘플 포인트 (victim anchor 계산용)
    /// </summary>
    public sealed class CarryRig : MonoBehaviour
    {
        [Header("Carrier Anchors")]
        [SerializeField] private Transform carryFrontAnchor;
        [SerializeField] private Transform carryOverheadAnchor;
        [SerializeField] private Transform carryDualAnchor;

        [Header("Victim Anchors")]
        [SerializeField] private Transform carryVictimAnchor;
        [SerializeField] private Transform carryChestSample;

        [Header("References")]
        [SerializeField] private PuppetMaster puppetMaster;

        [Header("Victim Anchor Weights")]
        [SerializeField] private float hipsWeight = 0.65f;
        [SerializeField] private float chestWeight = 0.35f;

        [Header("Victim Root Offsets")]
        [SerializeField] private Vector3 frontCarryVictimRootLocalOffset = new Vector3(0f, -0.95f, 0.18f);
        [SerializeField] private Vector3 overheadCarryVictimRootLocalOffset = new Vector3(0f, -1.05f, 0.02f);
        [SerializeField] private Vector3 dualCarryVictimRootLocalOffset = new Vector3(0f, -1.00f, 0.05f);

        private static readonly Vector3 StunnedSingleCarrierAnchorLift = new Vector3(0f, 0.48f, 0.14f);
        private static readonly Vector3 StunnedDualCarrierAnchorLift = new Vector3(0f, 0.60f, 0.12f);
        private static readonly Vector3 StunnedSingleVictimRootLift = new Vector3(0f, 0.42f, -0.08f);
        private static readonly Vector3 StunnedDualVictimRootLift = new Vector3(0f, 0.34f, -0.05f);

        // 캐시
        private Transform _hipsTransform;
        private Transform _chestTransform;
        private bool _resolved;

        public Transform FrontAnchor => carryFrontAnchor;
        public Transform OverheadAnchor => carryOverheadAnchor;
        public Transform DualAnchor => carryDualAnchor;
        public Transform VictimAnchor => carryVictimAnchor;
        public Transform ChestSample => carryChestSample;

        private void Awake()
        {
            TryResolveReferences();
        }

        private void TryResolveReferences()
        {
            if (_resolved) return;

            if (puppetMaster == null)
                puppetMaster = GetComponentInParent<PuppetMaster>();
            if (puppetMaster != null && puppetMaster.muscles != null && puppetMaster.muscles.Length > 0)
            {
                // muscles[0] = Hips
                _hipsTransform = puppetMaster.muscles[0].transform;

                // Chest/Spine1/Spine2 탐색
                foreach (var muscle in puppetMaster.muscles)
                {
                    if (muscle.transform == null) continue;
                    var n = muscle.transform.name;
                    if (n == "Chest" || n == "Spine1" || n == "Spine2")
                    {
                        _chestTransform = muscle.transform;
                        break;
                    }
                }
            }

            // muscles가 아직 초기화되지 않았으면 다음 호출에서 재시도
            _resolved = _hipsTransform != null;
        }

        /// <summary>
        /// Victim anchor 월드 위치를 hips-chest 가중 평균으로 계산.
        /// 명시적 victimAnchor Transform이 있으면 그것을 반환,
        /// 없으면 런타임에 hips/chest에서 계산.
        /// </summary>
        public bool TryGetVictimAnchorWorld(out Vector3 position, out Vector3 forward)
        {
            return TryGetVictimSupportFrameWorld(out position, out forward);
        }

        /// <summary>
        /// 피해자 hips/chest 기반 support frame을 반환.
        /// carry victim root follow와 stunned attach target의 공통 기준점이다.
        /// </summary>
        public bool TryGetVictimSupportFrameWorld(out Vector3 position, out Vector3 forward)
        {
            if (carryVictimAnchor != null)
            {
                position = carryVictimAnchor.position;
                forward = carryVictimAnchor.forward;
                return true;
            }

            TryResolveReferences();

            if (_hipsTransform != null)
            {
                if (_chestTransform != null)
                {
                    position = Vector3.Lerp(_hipsTransform.position, _chestTransform.position, chestWeight);
                    forward = Vector3.Slerp(_hipsTransform.forward, _chestTransform.forward, chestWeight).normalized;
                }
                else
                {
                    position = _hipsTransform.position;
                    forward = _hipsTransform.forward;
                }
                return true;
            }

            position = transform.position;
            forward = transform.forward;
            return false;
        }

        /// <summary>
        /// CarryMode에 따라 적절한 carrier anchor를 반환.
        /// </summary>
        public bool TryGetCarrierAnchorWorld(CarryPhysicsProfile.CarryMode mode, out Vector3 position, out Vector3 forward)
        {
            return TryGetCarrierAnchorWorld(mode, CharacterGrabController.HoldVariant.None, out position, out forward);
        }

        public bool TryGetCarrierAnchorWorld(
            CarryPhysicsProfile.CarryMode mode,
            CharacterGrabController.HoldVariant holdVariant,
            out Vector3 position,
            out Vector3 forward)
        {
            var anchor = ResolveCarrierAnchor(mode, holdVariant);

            if (anchor != null)
            {
                position = anchor.position;
                forward = anchor.forward;
                ApplyCarrierAnchorLift(mode, holdVariant, ref position, ref forward);
                return true;
            }

            return TryGetCarrierSupportFrameWorld(mode, holdVariant, out position, out forward);
        }

        /// <summary>
        /// carrier torso 기반 support frame을 반환.
        /// 손이 특정 anchor transform 자체를 쫓기보다 몸통 기준 frame을 따라가게 만들기 위한 기준점이다.
        /// </summary>
        public bool TryGetCarrierSupportFrameWorld(CarryPhysicsProfile.CarryMode mode, out Vector3 position, out Vector3 forward)
        {
            return TryGetCarrierSupportFrameWorld(mode, CharacterGrabController.HoldVariant.None, out position, out forward);
        }

        public bool TryGetCarrierSupportFrameWorld(
            CarryPhysicsProfile.CarryMode mode,
            CharacterGrabController.HoldVariant holdVariant,
            out Vector3 position,
            out Vector3 forward)
        {
            var anchor = ResolveCarrierAnchor(mode, holdVariant);
            TryResolveReferences();

            if (_hipsTransform != null)
            {
                var supportBlend = Mathf.Clamp01(chestWeight + 0.15f);
                if (_chestTransform != null)
                {
                    position = Vector3.Lerp(_hipsTransform.position, _chestTransform.position, supportBlend);
                    forward = Vector3.Slerp(_hipsTransform.forward, _chestTransform.forward, supportBlend).normalized;
                }
                else
                {
                    position = _hipsTransform.position;
                    forward = _hipsTransform.forward;
                }

                if (anchor != null)
                {
                    var supportToAnchor = anchor.position - position;
                    supportToAnchor.y = 0f;
                    if (supportToAnchor.sqrMagnitude > 0.0001f)
                        forward = Vector3.Slerp(forward, supportToAnchor.normalized, 0.5f).normalized;
                }

                if (forward.sqrMagnitude <= 0.0001f)
                    forward = transform.forward;

                return true;
            }

            if (anchor != null)
            {
                position = anchor.position;
                forward = anchor.forward;
                return true;
            }

            position = transform.position;
            forward = transform.forward;
            return false;
        }

        public bool TryGetCarriedVictimRootTargetWorld(
            CarryPhysicsProfile.CarryMode mode,
            CharacterGrabController.HoldVariant holdVariant,
            out Vector3 position,
            out Vector3 forward)
        {
            if (!TryGetCarrierAnchorWorld(mode, holdVariant, out var anchorPosition, out var anchorForward))
            {
                position = default;
                forward = transform.forward;
                return false;
            }

            var planarForward = Vector3.ProjectOnPlane(anchorForward, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.0001f)
                planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.0001f)
                planarForward = Vector3.forward;

            var carryRotation = Quaternion.LookRotation(planarForward.normalized, Vector3.up);
            position = anchorPosition + carryRotation * ResolveVictimRootLocalOffset(mode, holdVariant);
            forward = carryRotation * Vector3.forward;
            return true;
        }

        /// <summary>
        /// 프리팹에 anchor가 없을 때 런타임에서 자동 생성.
        /// Editor나 Spawned()에서 호출.
        /// </summary>
        public void EnsureAnchorsCreated()
        {
            if (carryFrontAnchor == null)
            {
                carryFrontAnchor = CreateChildAnchor("CarryFrontAnchor", new Vector3(0f, 0.95f, 0.25f));
            }
            if (carryOverheadAnchor == null)
            {
                carryOverheadAnchor = CreateChildAnchor("CarryOverheadAnchor", new Vector3(0f, 1.45f, 0.10f));
            }
            if (carryDualAnchor == null)
            {
                carryDualAnchor = CreateChildAnchor("CarryDualAnchor", new Vector3(0f, 1.15f, 0.15f));
            }
            if (carryVictimAnchor == null)
            {
                // victim anchor는 물리 퍼펫 체인에 종속이므로 여기서는 계산용 더미만 생성
                carryVictimAnchor = CreateChildAnchor("CarryVictimAnchor", new Vector3(0f, 0.85f, 0f));
            }
            if (carryChestSample == null)
            {
                carryChestSample = CreateChildAnchor("CarryChestSample", new Vector3(0f, 1.10f, 0f));
            }
        }

        private Transform CreateChildAnchor(string anchorName, Vector3 localPos)
        {
            var go = new GameObject(anchorName);
            go.transform.SetParent(transform);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        /// <summary>
        /// FixedUpdate에서 victim anchor를 hips-chest 가중 평균으로 갱신.
        /// 물리 퍼펫 위치가 매 프레임 변하므로 추적이 필요.
        /// </summary>
        public void UpdateVictimAnchor()
        {
            if (carryVictimAnchor == null) return;

            TryResolveReferences();

            if (_hipsTransform == null) return;

            if (_chestTransform != null)
            {
                carryVictimAnchor.position = Vector3.Lerp(
                    _hipsTransform.position,
                    _chestTransform.position,
                    chestWeight);
                carryVictimAnchor.rotation = Quaternion.Slerp(
                    _hipsTransform.rotation,
                    _chestTransform.rotation,
                    chestWeight);
            }
            else
            {
                carryVictimAnchor.position = _hipsTransform.position;
                carryVictimAnchor.rotation = _hipsTransform.rotation;
            }

            // chest sample도 갱신
            if (carryChestSample != null && _chestTransform != null)
            {
                carryChestSample.position = _chestTransform.position;
                carryChestSample.rotation = _chestTransform.rotation;
            }
        }

        private Transform ResolveCarrierAnchor(CarryPhysicsProfile.CarryMode mode)
        {
            return ResolveCarrierAnchor(mode, CharacterGrabController.HoldVariant.None);
        }

        private Transform ResolveCarrierAnchor(
            CarryPhysicsProfile.CarryMode mode,
            CharacterGrabController.HoldVariant holdVariant)
        {
            var resolvedHoldVariant = holdVariant != CharacterGrabController.HoldVariant.None
                ? holdVariant
                : CharacterGrabController.ResolveCarryHoldVariant(mode);
            var variantAnchor = ResolveHoldVariantAnchor(resolvedHoldVariant);
            if (variantAnchor != null)
                return variantAnchor;

            return mode switch
            {
                CarryPhysicsProfile.CarryMode.StunnedDualCarry => SelectFirstAvailable(carryDualAnchor, carryOverheadAnchor, carryFrontAnchor),
                CarryPhysicsProfile.CarryMode.StunnedSingleCarry => SelectFirstAvailable(carryOverheadAnchor, carryDualAnchor, carryFrontAnchor),
                _ => SelectFirstAvailable(carryFrontAnchor, carryOverheadAnchor, carryDualAnchor)
            };
        }

        private Transform ResolveHoldVariantAnchor(CharacterGrabController.HoldVariant holdVariant)
        {
            return holdVariant switch
            {
                CharacterGrabController.HoldVariant.FrontCarry => SelectFirstAvailable(carryFrontAnchor, carryOverheadAnchor, carryDualAnchor),
                CharacterGrabController.HoldVariant.OverheadCarry => SelectFirstAvailable(carryOverheadAnchor, carryFrontAnchor, carryDualAnchor),
                CharacterGrabController.HoldVariant.DualCarry => SelectFirstAvailable(carryDualAnchor, carryOverheadAnchor, carryFrontAnchor),
                _ => null
            };
        }

        private Vector3 ResolveVictimRootLocalOffset(
            CarryPhysicsProfile.CarryMode mode,
            CharacterGrabController.HoldVariant holdVariant)
        {
            var resolvedHoldVariant = holdVariant != CharacterGrabController.HoldVariant.None
                ? holdVariant
                : CharacterGrabController.ResolveCarryHoldVariant(mode);
            return resolvedHoldVariant switch
            {
                CharacterGrabController.HoldVariant.FrontCarry => frontCarryVictimRootLocalOffset,
                CharacterGrabController.HoldVariant.OverheadCarry => overheadCarryVictimRootLocalOffset + StunnedSingleVictimRootLift,
                CharacterGrabController.HoldVariant.DualCarry => dualCarryVictimRootLocalOffset + StunnedDualVictimRootLift,
                _ => mode switch
                {
                    CarryPhysicsProfile.CarryMode.StunnedDualCarry => dualCarryVictimRootLocalOffset + StunnedDualVictimRootLift,
                    CarryPhysicsProfile.CarryMode.StunnedSingleCarry => overheadCarryVictimRootLocalOffset + StunnedSingleVictimRootLift,
                    _ => frontCarryVictimRootLocalOffset
                }
            };
        }

        private void ApplyCarrierAnchorLift(
            CarryPhysicsProfile.CarryMode mode,
            CharacterGrabController.HoldVariant holdVariant,
            ref Vector3 position,
            ref Vector3 forward)
        {
            var localLift = ResolveCarrierAnchorLift(mode, holdVariant);
            if (localLift.sqrMagnitude <= 0.0001f)
                return;

            var planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.0001f)
                planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (planarForward.sqrMagnitude <= 0.0001f)
                planarForward = Vector3.forward;

            var carryRotation = Quaternion.LookRotation(planarForward.normalized, Vector3.up);
            position += carryRotation * localLift;
            forward = carryRotation * Vector3.forward;
        }

        private static Vector3 ResolveCarrierAnchorLift(
            CarryPhysicsProfile.CarryMode mode,
            CharacterGrabController.HoldVariant holdVariant)
        {
            var resolvedHoldVariant = holdVariant != CharacterGrabController.HoldVariant.None
                ? holdVariant
                : CharacterGrabController.ResolveCarryHoldVariant(mode);
            return resolvedHoldVariant switch
            {
                CharacterGrabController.HoldVariant.OverheadCarry => StunnedSingleCarrierAnchorLift,
                CharacterGrabController.HoldVariant.DualCarry => StunnedDualCarrierAnchorLift,
                _ => mode switch
                {
                    CarryPhysicsProfile.CarryMode.StunnedSingleCarry => StunnedSingleCarrierAnchorLift,
                    CarryPhysicsProfile.CarryMode.StunnedDualCarry => StunnedDualCarrierAnchorLift,
                    _ => Vector3.zero
                }
            };
        }

        private static Transform SelectFirstAvailable(Transform primary, Transform secondary, Transform tertiary)
        {
            if (primary != null)
                return primary;

            if (secondary != null)
                return secondary;

            return tertiary;
        }
    }
}
