/*
 * 파일 개요:
 * - ItemCharacterHeldItemPresenter 스크립트가 들어 있는 파일이다.
 * - Character 계층에서 캐릭터와 아이템 시스템의 결합 지점을 담당한다.
 * - 입력, 손 장착, 근접 판정, 버프 반영 같은 캐릭터 쪽 연결만 여기서 다루고, 실제 상태 전이는 Runtime 계층에서 유지한다.
 */
using System;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 보유 아이템을 캐릭터 손(오른손)에 시각적으로 장착한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemCharacterHeldItemPresenter : MonoBehaviour
    {
        [Serializable]
        private struct HeldPoseOverride
        {
            public string itemId;
            public Vector3 localPositionOffset;
            public Vector3 localEulerOffset;
            public Vector3 localScale;
        }

        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform handAnchorOverride;

        [Header("장착 위치")]
        [SerializeField] private Vector3 localPositionOffset = new Vector3(0.003f, -0.002f, 0.012f);
        [SerializeField] private Vector3 localEulerOffset = new Vector3(0f, 90f, 90f);
        [SerializeField] private Vector3 localScale = Vector3.one * 0.25f;

        [Header("아이템별 장착 보정")]
        [SerializeField] private HeldPoseOverride[] heldPoseOverrides;
        [SerializeField] private bool useDefaultWatermelonSwordPose = true;
        [SerializeField] private Vector3 watermelonSwordLocalPositionOffset = new Vector3(0f, 0f, 0f);
        [SerializeField] private Vector3 watermelonSwordLocalEulerOffset = new Vector3(0f, 90f, 90f);
        [SerializeField] private Vector3 watermelonSwordAdditionalEulerOffset = new Vector3(0f, 180f, 0f);
        [SerializeField] private Vector3 watermelonSwordLocalScale = Vector3.one * 0.3f;
        [SerializeField] private float watermelonSwordHeldScaleMultiplier = 2.5f;
        [SerializeField] private bool watermelonSwordAutoGripSnap = true;
        [SerializeField] private bool watermelonSwordUseMaxOnPrimaryAxis;
        [SerializeField] private bool watermelonSwordFlipGripDirection;
        [SerializeField] private Vector3 watermelonSwordGripFineTune = new Vector3(0f, 0.02f, 0f);
        [SerializeField] private bool useDefaultFlamethrowerPose = true;
        [SerializeField] private Vector3 flamethrowerLocalPositionOffset = new Vector3(0.05f, 0.05f, 0.05f);
        [SerializeField] private Vector3 flamethrowerLocalEulerOffset = new Vector3(-90f, -90f, -90f);
        [SerializeField] private Vector3 flamethrowerAimEulerOffset = new Vector3(0f, 0f, 180f);
        [SerializeField] private Vector3 flamethrowerLocalScale = Vector3.one * 0.7f;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();
        private GameObject _spawnedHeldVisual;
        private string _replicatedHeldItemId = string.Empty;
        private string _currentHeldItemId = string.Empty;
        private Vector3 _currentHeldLocalPositionOffset = Vector3.zero;
        private Vector3 _currentHeldEulerOffset = Vector3.zero;
        private Vector3 _currentHeldLocalScale = Vector3.one;
        private Vector3 _muzzleAimTarget;
        private bool _hasMuzzleAimTarget;
        private Vector3 _muzzleRotationOffset = Vector3.zero;
        public Transform CurrentHeldVisualRoot => _spawnedHeldVisual != null ? _spawnedHeldVisual.transform : null;

        private static readonly string[] HandNameCandidates =
        {
            "RightHand",
            "Hand_R",
            "R_Hand",
            "Right Hand",
            "mixamorig:RightHand"
        };

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindEvents();
            RefreshHeldVisual(ResolveCurrentHeldItemId());
        }

        private void OnDisable()
        {
            UnbindEvents();
            ClearHeldVisual();
        }

        private void LateUpdate()
        {
            EnsureHeldVisualExists();
            UpdateHeldVisualFollow();
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            if (itemRuntimeHost == runtimeHost)
            {
                return;
            }

            UnbindEvents();
            itemRuntimeHost = runtimeHost;
            BindEvents();
            RefreshHeldVisual(ResolveCurrentHeldItemId());
        }

        public void SetCharacterRoot(Transform root)
        {
            characterRoot = root;
            RefreshHeldVisual(ResolveCurrentHeldItemId());
        }

        public void SetMuzzleAimTarget(Vector3 target)
        {
            _muzzleAimTarget = target;
            _hasMuzzleAimTarget = true;
        }

        public void ClearMuzzleAimTarget()
        {
            _hasMuzzleAimTarget = false;
        }

        public void SetHandAnchor(Transform handAnchor)
        {
            handAnchorOverride = handAnchor;
            RefreshHeldVisual(ResolveCurrentHeldItemId());
        }

        public bool TryGetHeldPointWorldPosition(Vector3 localOffset, out Vector3 worldPosition)
        {
            worldPosition = default;
            var visualRoot = CurrentHeldVisualRoot;
            if (visualRoot == null)
            {
                return false;
            }

            worldPosition = visualRoot.TransformPoint(localOffset);
            return true;
        }

        public bool TryGetHeldWorldRotation(Vector3 localEulerOffset, out Quaternion worldRotation)
        {
            worldRotation = Quaternion.identity;
            var visualRoot = CurrentHeldVisualRoot;
            if (visualRoot == null)
            {
                return false;
            }

            worldRotation = visualRoot.rotation * Quaternion.Euler(localEulerOffset);
            return true;
        }

        public void SetReplicatedHeldItemId(string heldItemId)
        {
            heldItemId ??= string.Empty;
            if (string.Equals(_replicatedHeldItemId, heldItemId, StringComparison.Ordinal))
            {
                if (_spawnedHeldVisual == null && !string.IsNullOrWhiteSpace(heldItemId))
                {
                    RefreshHeldVisual(heldItemId);
                }
                return;
            }

            _replicatedHeldItemId = heldItemId;
            RefreshHeldVisual(heldItemId);
        }

        private void EnsureHeldVisualExists()
        {
            if (_spawnedHeldVisual != null)
            {
                return;
            }

            var heldItemId = ResolveCurrentHeldItemId();
            if (string.IsNullOrWhiteSpace(heldItemId))
            {
                return;
            }

            RefreshHeldVisual(heldItemId);
        }

        private void BindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.HeldItemChanged -= RefreshHeldVisual;
            itemRuntimeHost.HeldItemChanged += RefreshHeldVisual;
        }

        private void UnbindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.HeldItemChanged -= RefreshHeldVisual;
        }

        private void RefreshHeldVisual(string heldItemId)
        {
            ClearHeldVisual();
            if (string.IsNullOrWhiteSpace(heldItemId))
            {
                ItemRuntimeLog.Info("HeldVisual", "손 장착 해제");
                return;
            }

            if (!TryGetItemDefinition(heldItemId, out var definition))
            {
                DebugLog($"Held visual skipped: missing definition for {heldItemId}");
                ItemRuntimeLog.Warn(heldItemId, "손 장착 비주얼 실패: 아이템 정의를 찾지 못함", this);
                return;
            }

            var handAnchor = ResolveHandAnchor();
            var prefab = _prefabResolver.Resolve(definition.Master.PrefabPath);
            if (prefab == null)
            {
                DebugLog($"Held visual skipped: prefab missing for {heldItemId}");
                ItemRuntimeLog.Warn(heldItemId, $"손 장착 비주얼 실패: prefab 누락 path={definition.Master.PrefabPath}", this);
                return;
            }

            _spawnedHeldVisual = Instantiate(prefab, handAnchor);
            _spawnedHeldVisual.name = $"HeldItem_{heldItemId}";
            var isWatermelonSword = string.Equals(heldItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal);
            var isBlackhole = string.Equals(heldItemId, ItemIds.BlackholeBomb, StringComparison.Ordinal);
            if (!isBlackhole && !ShouldSkipHeldFallback(heldItemId))
            {
                // 수박칼은 조건과 무관하게 Lit 셰이더로 강제 교체해 마젠타를 방지한다.
                ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(_spawnedHeldVisual, isWatermelonSword);
            }
            RefreshBlackholeHeldVisualIfNeeded(heldItemId, _spawnedHeldVisual);
            StripNetworkComponentsForHeldVisual(_spawnedHeldVisual);
            DisableNonHeldVisualEffects(heldItemId, _spawnedHeldVisual);
            ApplyPose(heldItemId, _spawnedHeldVisual.transform);
            DisablePhysicsForHeldVisual(_spawnedHeldVisual);
            EnsureHeldVisualRenderersEnabled(_spawnedHeldVisual);
            DebugLog($"Held visual attached: {heldItemId}");
            ItemRuntimeLog.Info(heldItemId, $"손 장착 비주얼 생성: prefab={definition.Master.PrefabPath}, anchor={handAnchor.name}", this);
        }

        private void UpdateHeldVisualFollow()
        {
            if (_spawnedHeldVisual == null)
            {
                return;
            }

            var handAnchor = ResolveHandAnchor();
            if (handAnchor == null)
            {
                return;
            }

            // 부모-자식 관계에만 의존하지 않고 LateUpdate에서 매 프레임 위치/회전을 동기화한다.
            // 무기의 방향을 항상 캐릭터 정면(searchRoot.rotation)으로 강제 고정하여 총구가 바닥을 향하지 않게 한다.
            // 아이템별로 설정된 오프셋 회전을 반영하면서 손의 본(Bone) 회전을 그대로 따른다.
            // 이렇게 함으로써 캐릭터가 위/아래를 볼 때 총구도 같이 기울어지게 된다.
            _spawnedHeldVisual.transform.position = handAnchor.TransformPoint(_currentHeldLocalPositionOffset);

            if (_hasMuzzleAimTarget &&
                string.Equals(_currentHeldItemId, ItemIds.Flamethrower, StringComparison.Ordinal))
            {
                var aimDirection = _muzzleAimTarget - _spawnedHeldVisual.transform.position;
                if (aimDirection.sqrMagnitude > 0.0001f)
                {
                    _spawnedHeldVisual.transform.rotation =
                        Quaternion.LookRotation(aimDirection.normalized, handAnchor.up) *
                        Quaternion.Euler(flamethrowerAimEulerOffset);
                }
            }
            else
            {
                _spawnedHeldVisual.transform.rotation = handAnchor.rotation * Quaternion.Euler(_currentHeldEulerOffset);
            }

            _spawnedHeldVisual.transform.localScale = _currentHeldLocalScale;

        }

        private string ResolveCurrentHeldItemId()
        {
            if (!string.IsNullOrWhiteSpace(_replicatedHeldItemId))
            {
                return _replicatedHeldItemId;
            }

            return itemRuntimeHost != null ? itemRuntimeHost.HeldItemId : string.Empty;
        }

        private void ApplyPose(string heldItemId, Transform visualTransform)
        {
            if (visualTransform == null)
            {
                return;
            }

            var position = localPositionOffset;
            var euler = localEulerOffset;
            var scale = localScale;

            if (TryGetPoseOverride(heldItemId, out var pose))
            {
                position = pose.localPositionOffset;
                euler = pose.localEulerOffset;
                scale = pose.localScale;
            }
            else if (useDefaultWatermelonSwordPose &&
                     string.Equals(heldItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal))
            {
                position = watermelonSwordLocalPositionOffset;
                // 역수로 보이지 않도록 기본 오일러에 보정 회전을 더한다.
                euler = watermelonSwordLocalEulerOffset + watermelonSwordAdditionalEulerOffset;
                scale = watermelonSwordLocalScale * Mathf.Max(0.01f, watermelonSwordHeldScaleMultiplier);
            }
            else if (useDefaultFlamethrowerPose &&
                     string.Equals(heldItemId, ItemIds.Flamethrower, StringComparison.Ordinal))
            {
                // 한국어: 화염방사기는 테스트 씬 장착값을 기본 손 위치 보정으로 사용한다.
                position = flamethrowerLocalPositionOffset;
                euler = flamethrowerLocalEulerOffset;
                scale = flamethrowerLocalScale;
                _muzzleRotationOffset = Vector3.zero;
            }
            else if (ShouldUseFullScaleHeldPose(heldItemId))
            {
                position = localPositionOffset;
                euler = localEulerOffset;
                scale = Vector3.one;
            }
            else
            {
                _muzzleRotationOffset = Vector3.zero;
            }

            if (watermelonSwordAutoGripSnap &&
                string.Equals(heldItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal) &&
                TryApplyWatermelonSwordGripCompensation(visualTransform, scale, euler, out var compensation))
            {
                position += compensation + watermelonSwordGripFineTune;
            }

            _currentHeldItemId = heldItemId ?? string.Empty;
            _currentHeldLocalPositionOffset = position;
            _currentHeldEulerOffset = euler;
            _currentHeldLocalScale = scale;
            visualTransform.localPosition = position;
            visualTransform.localRotation = Quaternion.Euler(euler);
            visualTransform.localScale = scale;
        }

        private bool TryGetPoseOverride(string itemId, out HeldPoseOverride pose)
        {
            pose = default;
            if (heldPoseOverrides == null || heldPoseOverrides.Length == 0 || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            for (var i = 0; i < heldPoseOverrides.Length; i++)
            {
                var candidate = heldPoseOverrides[i];
                if (!string.Equals(candidate.itemId, itemId, StringComparison.Ordinal))
                {
                    continue;
                }

                pose = candidate;
                return true;
            }

            return false;
        }

        private static bool ShouldUseFullScaleHeldPose(string heldItemId)
        {
            return string.Equals(heldItemId, ItemIds.Americano, StringComparison.Ordinal) ||
                   string.Equals(heldItemId, ItemIds.Growth, StringComparison.Ordinal) ||
                   string.Equals(heldItemId, ItemIds.Shrink, StringComparison.Ordinal) ||
                   string.Equals(heldItemId, ItemIds.Invisibility, StringComparison.Ordinal) ||
                   string.Equals(heldItemId, ItemIds.SatelliteStrike, StringComparison.Ordinal);
        }

        private static bool ShouldSkipHeldFallback(string heldItemId)
        {
            return string.Equals(heldItemId, ItemIds.Growth, StringComparison.Ordinal) ||
                   string.Equals(heldItemId, ItemIds.Shrink, StringComparison.Ordinal) ||
                   string.Equals(heldItemId, ItemIds.Invisibility, StringComparison.Ordinal) ||
                   string.Equals(heldItemId, ItemIds.SatelliteStrike, StringComparison.Ordinal);
        }

        private bool TryApplyWatermelonSwordGripCompensation(
            Transform visualTransform,
            Vector3 targetScale,
            Vector3 targetEuler,
            out Vector3 compensation)
        {
            compensation = Vector3.zero;
            if (!TryGetPrimaryMeshBounds(visualTransform, out var localBounds))
            {
                return false;
            }

            var extents = localBounds.extents;
            var primaryAxis = 0;
            var maxExtent = extents.x;
            if (extents.y > maxExtent)
            {
                maxExtent = extents.y;
                primaryAxis = 1;
            }
            if (extents.z > maxExtent)
            {
                primaryAxis = 2;
            }

            var gripLocalPoint = localBounds.center;
            var useMaxOnPrimaryAxis = watermelonSwordUseMaxOnPrimaryAxis;
            if (watermelonSwordFlipGripDirection)
            {
                // 칼끝을 잡는 문제가 있을 때 반대 축 끝점을 사용해 손잡이 쪽으로 보정한다.
                useMaxOnPrimaryAxis = !useMaxOnPrimaryAxis;
            }

            if (primaryAxis == 0)
            {
                gripLocalPoint.x = useMaxOnPrimaryAxis ? localBounds.max.x : localBounds.min.x;
            }
            else if (primaryAxis == 1)
            {
                gripLocalPoint.y = useMaxOnPrimaryAxis ? localBounds.max.y : localBounds.min.y;
            }
            else
            {
                gripLocalPoint.z = useMaxOnPrimaryAxis ? localBounds.max.z : localBounds.min.z;
            }

            var scaledGrip = Vector3.Scale(gripLocalPoint, targetScale);
            compensation = -(Quaternion.Euler(targetEuler) * scaledGrip);
            return true;
        }

        private static bool TryGetPrimaryMeshBounds(Transform visualTransform, out Bounds localBounds)
        {
            localBounds = default;
            if (visualTransform == null)
            {
                return false;
            }

            var meshFilter = visualTransform.GetComponentInChildren<MeshFilter>(true);
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                return false;
            }

            localBounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        private bool TryGetItemDefinition(string itemId, out ItemDefinition definition)
        {
            definition = null;
            var options = itemRuntimeHost != null
                ? itemRuntimeHost.CatalogLoadOptions
                : ItemCatalogLoader.CreateDefaultOptions();
            if (!_catalogProvider.TryGetCatalog(options, out var catalog, out _))
            {
                return false;
            }

            return catalog.TryGetDefinition(itemId, out definition);
        }

        private Transform ResolveHandAnchor()
        {
            if (handAnchorOverride != null)
            {
                return handAnchorOverride;
            }

            var searchRoot = characterRoot != null
                ? characterRoot
                : itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null
                    ? itemRuntimeHost.OwnerTransform
                    : transform;
            var all = searchRoot.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < HandNameCandidates.Length; i++)
            {
                var candidate = HandNameCandidates[i];
                for (var t = 0; t < all.Length; t++)
                {
                    var name = all[t].name;
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return all[t];
                    }
                }
            }

            var animator = searchRoot.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                var bone = animator.GetBoneTransform(HumanBodyBones.RightHand);
                if (bone != null)
                {
                    return bone;
                }
            }

            return searchRoot;
        }

        private static void DisablePhysicsForHeldVisual(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var colliders = visualRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            var bodies = visualRoot.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        private static void EnsureHeldVisualRenderersEnabled(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
            }
        }

        private static void StripNetworkComponentsForHeldVisual(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            // NetworkedItemFieldDrop은 [RequireComponent(typeof(NetworkTransform))]를 선언하므로
            // NetworkBehaviour 루프보다 먼저 즉시 제거해야 의존성 에러가 발생하지 않는다.
            var networkedDrops = visualRoot.GetComponentsInChildren<NetworkedItemFieldDrop>(true);
            for (var i = 0; i < networkedDrops.Length; i++)
            {
                if (networkedDrops[i] != null)
                {
                    DestroyImmediate(networkedDrops[i]);
                }
            }

            var networkBehaviours = visualRoot.GetComponentsInChildren<NetworkBehaviour>(true);
            for (var i = 0; i < networkBehaviours.Length; i++)
            {
                if (networkBehaviours[i] != null)
                {
                    Destroy(networkBehaviours[i]);
                }
            }

            var fieldDrops = visualRoot.GetComponentsInChildren<ItemFieldDrop>(true);
            for (var i = 0; i < fieldDrops.Length; i++)
            {
                if (fieldDrops[i] != null)
                {
                    Destroy(fieldDrops[i]);
                }
            }

            var networkTransforms = visualRoot.GetComponentsInChildren<NetworkTransform>(true);
            for (var i = 0; i < networkTransforms.Length; i++)
            {
                if (networkTransforms[i] != null)
                {
                    Destroy(networkTransforms[i]);
                }
            }

            var networkObjects = visualRoot.GetComponentsInChildren<NetworkObject>(true);
            for (var i = 0; i < networkObjects.Length; i++)
            {
                if (networkObjects[i] != null)
                {
                    Destroy(networkObjects[i]);
                }
            }
        }

        private static void DisableNonHeldVisualEffects(string heldItemId, GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var isSatellite = string.Equals(heldItemId, ItemIds.SatelliteStrike, StringComparison.Ordinal);
            if (!isSatellite)
            {
                return;
            }
        }

        private static void RefreshBlackholeHeldVisualIfNeeded(string heldItemId, GameObject visualRoot)
        {
            if (!string.Equals(heldItemId, ItemIds.BlackholeBomb, StringComparison.Ordinal) || visualRoot == null)
            {
                return;
            }

            var authoring = visualRoot.GetComponent<ItemBlackholeVisualAuthoring>();
            if (authoring != null)
            {
                authoring.RefreshVisual();
            }
        }

        private void ClearHeldVisual()
        {
            if (_spawnedHeldVisual == null)
            {
                return;
            }

            Destroy(_spawnedHeldVisual);
            _spawnedHeldVisual = null;
            _currentHeldItemId = string.Empty;
            _currentHeldLocalPositionOffset = Vector3.zero;
            _currentHeldEulerOffset = Vector3.zero;
            _currentHeldLocalScale = Vector3.one;
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (characterRoot == null)
            {
                characterRoot = itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null
                    ? itemRuntimeHost.OwnerTransform
                    : transform;
            }
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemCharacterHeldItemPresenter] {message}", this);
        }
    }
}

