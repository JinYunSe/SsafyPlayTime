/*
 * 파일 개요:
 * - ItemCharacterHeldItemPresenter 스크립트가 들어 있는 파일이다.
 * - Character 계층에서 캐릭터와 아이템 시스템의 결합 지점을 담당한다.
 * - 입력, 손 장착, 근접 판정, 버프 반영 같은 캐릭터 쪽 연결만 여기서 다루고, 실제 상태 전이는 Runtime 계층에서 유지한다.
 */
using System;
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
        [SerializeField] private Vector3 localPositionOffset = new Vector3(0f, 0.03f, 0.07f);
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

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();
        private GameObject _spawnedHeldVisual;
        private string _replicatedHeldItemId = string.Empty;
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
            RefreshHeldVisual(itemRuntimeHost != null ? itemRuntimeHost.HeldItemId : string.Empty);
        }

        private void OnDisable()
        {
            UnbindEvents();
            ClearHeldVisual();
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
            RefreshHeldVisual(itemRuntimeHost != null ? itemRuntimeHost.HeldItemId : string.Empty);
        }

        public void SetCharacterRoot(Transform root)
        {
            characterRoot = root;
        }

        public void SetHandAnchor(Transform handAnchor)
        {
            handAnchorOverride = handAnchor;
            RefreshHeldVisual(itemRuntimeHost != null ? itemRuntimeHost.HeldItemId : string.Empty);
        }

        public void SetReplicatedHeldItemId(string heldItemId)
        {
            heldItemId ??= string.Empty;
            if (string.Equals(_replicatedHeldItemId, heldItemId, StringComparison.Ordinal))
            {
                return;
            }

            _replicatedHeldItemId = heldItemId;
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
                return;
            }

            if (!TryGetItemDefinition(heldItemId, out var definition))
            {
                DebugLog($"Held visual skipped: missing definition for {heldItemId}");
                return;
            }

            var handAnchor = ResolveHandAnchor();
            var prefab = _prefabResolver.Resolve(definition.Master.PrefabPath);
            if (prefab == null)
            {
                DebugLog($"Held visual skipped: prefab missing for {heldItemId}");
                return;
            }

            _spawnedHeldVisual = Instantiate(prefab, handAnchor);
            _spawnedHeldVisual.name = $"HeldItem_{heldItemId}";
            var isWatermelonSword = string.Equals(heldItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal);
            // 수박칼은 조건과 무관하게 Lit 셰이더로 강제 교체해 마젠타를 방지한다.
            ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(_spawnedHeldVisual, isWatermelonSword);
            ApplyPose(heldItemId, _spawnedHeldVisual.transform);
            DisablePhysicsForHeldVisual(_spawnedHeldVisual);
            DebugLog($"Held visual attached: {heldItemId}");
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

            if (watermelonSwordAutoGripSnap &&
                string.Equals(heldItemId, ItemIds.WaterMelonSword, StringComparison.Ordinal) &&
                TryApplyWatermelonSwordGripCompensation(visualTransform, scale, euler, out var compensation))
            {
                position += compensation + watermelonSwordGripFineTune;
            }

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

        private static void ApplyUrpMaterialFallbackForHeldVisual(GameObject root, bool forceLitOverride)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var materials = renderer.materials;
                var replaced = false;
                for (var m = 0; m < materials.Length; m++)
                {
                    var source = materials[m];
                    if (source == null)
                    {
                        continue;
                    }

                    if (!forceLitOverride && !NeedsUrpFallback(source))
                    {
                        continue;
                    }

                    var fallbackShader =
                        Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Universal Render Pipeline/Simple Lit") ??
                        Shader.Find("Standard");
                    if (fallbackShader == null)
                    {
                        continue;
                    }

                    var fallback = new Material(fallbackShader)
                    {
                        name = $"{source.name}_HeldFallback"
                    };

                    var sourceTexture = source.HasProperty("_BaseMap")
                        ? source.GetTexture("_BaseMap")
                        : source.HasProperty("_MainTex")
                            ? source.GetTexture("_MainTex")
                            : null;
                    if (sourceTexture != null)
                    {
                        if (fallback.HasProperty("_BaseMap"))
                        {
                            fallback.SetTexture("_BaseMap", sourceTexture);
                        }
                        else if (fallback.HasProperty("_MainTex"))
                        {
                            fallback.SetTexture("_MainTex", sourceTexture);
                        }
                    }

                    var sourceColor = source.HasProperty("_BaseColor")
                        ? source.GetColor("_BaseColor")
                        : source.HasProperty("_Color")
                            ? source.GetColor("_Color")
                            : Color.white;
                    if (fallback.HasProperty("_BaseColor"))
                    {
                        fallback.SetColor("_BaseColor", sourceColor);
                    }
                    if (fallback.HasProperty("_Color"))
                    {
                        fallback.SetColor("_Color", sourceColor);
                    }

                    materials[m] = fallback;
                    replaced = true;
                }

                if (replaced)
                {
                    renderer.materials = materials;
                }
            }
        }

        private static bool NeedsUrpFallback(Material source)
        {
            if (source == null)
            {
                return false;
            }

            var shader = source.shader;
            if (shader == null)
            {
                return true;
            }

            if (!shader.isSupported)
            {
                return true;
            }

            var shaderName = shader.name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return true;
            }

            if (shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private void ClearHeldVisual()
        {
            if (_spawnedHeldVisual == null)
            {
                return;
            }

            Destroy(_spawnedHeldVisual);
            _spawnedHeldVisual = null;
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

