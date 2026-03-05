using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 버프 상태를 캐릭터 시각/수치 배율로 변환한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemCharacterBuffApplier : MonoBehaviour
    {
        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform visualRoot;

        [Header("투명화")]
        [SerializeField] private float invisibilityAlpha = 0.35f;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private Vector3 _baseVisualScale = Vector3.one;

        public float CurrentScaleMultiplier { get; private set; } = 1f;
        public float CurrentMoveSpeedMultiplier { get; private set; } = 1f;
        public float CurrentBaseDamageMultiplier { get; private set; } = 1f;
        public float CurrentKnockbackResistMultiplier { get; private set; } = 1f;
        public float CurrentGravityMultiplier { get; private set; } = 1f;
        public float CurrentJumpMultiplier { get; private set; } = 1f;
        public bool IsSuperArmorActive { get; private set; }
        public bool IsInvisibilityActive { get; private set; }

        public event Action BuffApplied;

        private void Awake()
        {
            ResolveReferences();
            CacheBaseScale();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CacheBaseScale();
            BindEvents();
        }

        private void OnDisable()
        {
            UnbindEvents();
            RestoreVisualScale();
            RestoreRendererVisibility();
            ResetRuntimeMultipliers();
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            if (itemRuntimeHost == runtimeHost)
            {
                return;
            }

            UnbindEvents();
            itemRuntimeHost = runtimeHost;
            ResolveReferences();
            BindEvents();
        }

        public void SetCharacterRoot(Transform root)
        {
            characterRoot = root;
            ResolveReferences();
            CacheBaseScale();
        }

        private void BindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BuffStateChanged -= HandleBuffStateChanged;
            itemRuntimeHost.BuffStateChanged += HandleBuffStateChanged;
        }

        private void UnbindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BuffStateChanged -= HandleBuffStateChanged;
        }

        private void HandleBuffStateChanged(ItemBuffMask activeBuffMask, ItemBuffRuntimeState buffState)
        {
            ResetRuntimeMultipliers();

            if ((activeBuffMask & ItemBuffMask.Growth) != 0 && TryGetItemDefinition(ItemIds.Growth, out var growth))
            {
                ApplyDefinitionMultipliers(growth);
            }

            if ((activeBuffMask & ItemBuffMask.Shrink) != 0 && TryGetItemDefinition(ItemIds.Shrink, out var shrink))
            {
                ApplyDefinitionMultipliers(shrink);
            }

            IsSuperArmorActive = (activeBuffMask & ItemBuffMask.SuperArmor) != 0;
            IsInvisibilityActive = (activeBuffMask & ItemBuffMask.Invisibility) != 0;

            ApplyVisualScale();
            ApplyRendererVisibility();
            BuffApplied?.Invoke();
            DebugLog(
                $"Buff applied: scale={CurrentScaleMultiplier:0.00}, move={CurrentMoveSpeedMultiplier:0.00}, invis={IsInvisibilityActive}, superArmor={IsSuperArmorActive}");
        }

        private void ApplyDefinitionMultipliers(ItemDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            CurrentScaleMultiplier *= Mathf.Max(0.05f, definition.Master.ScaleMultiplier);
            CurrentMoveSpeedMultiplier *= Mathf.Max(0.05f, definition.Master.MoveSpeedMultiplier);
            CurrentBaseDamageMultiplier *= Mathf.Max(0.05f, definition.Master.BaseDamageMultiplier);
            CurrentKnockbackResistMultiplier *= Mathf.Max(0.05f, definition.Master.KnockbackResistMultiplier);
            CurrentGravityMultiplier *= Mathf.Max(0.05f, definition.Master.GravityMultiplier);
            CurrentJumpMultiplier *= Mathf.Max(0.05f, definition.Master.JumpMultiplier);
        }

        private void ApplyVisualScale()
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.localScale = _baseVisualScale * CurrentScaleMultiplier;
        }

        private void RestoreVisualScale()
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.localScale = _baseVisualScale;
        }

        private void ApplyRendererVisibility()
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

                renderer.shadowCastingMode = IsInvisibilityActive
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : UnityEngine.Rendering.ShadowCastingMode.On;

                var targetAlpha = IsInvisibilityActive ? Mathf.Clamp01(invisibilityAlpha) : 1f;
                ApplyRendererAlpha(renderer, targetAlpha);
            }
        }

        private static void ApplyRendererAlpha(Renderer renderer, float alpha)
        {
            var materials = renderer.materials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty("_BaseColor"))
                {
                    var color = material.GetColor("_BaseColor");
                    color.a = alpha;
                    material.SetColor("_BaseColor", color);
                }

                if (material.HasProperty("_Color"))
                {
                    var color = material.GetColor("_Color");
                    color.a = alpha;
                    material.SetColor("_Color", color);
                }

                if (alpha < 0.99f)
                {
                    if (material.HasProperty("_Surface"))
                    {
                        material.SetFloat("_Surface", 1f);
                    }
                    if (material.HasProperty("_SrcBlend"))
                    {
                        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    }
                    if (material.HasProperty("_DstBlend"))
                    {
                        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    }
                    if (material.HasProperty("_ZWrite"))
                    {
                        material.SetFloat("_ZWrite", 0f);
                    }
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                }
            }
        }

        private void RestoreRendererVisibility()
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

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                ApplyRendererAlpha(renderer, 1f);
            }
        }

        private bool TryGetItemDefinition(string itemId, out ItemDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            var options = itemRuntimeHost != null
                ? itemRuntimeHost.CatalogLoadOptions
                : ItemCatalogLoader.CreateDefaultOptions();
            if (!_catalogProvider.TryGetCatalog(options, out var catalog, out _))
            {
                return false;
            }

            return catalog.TryGetDefinition(itemId, out definition);
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

            if (visualRoot == null)
            {
                var model = characterRoot != null ? characterRoot.Find("Model") : null;
                visualRoot = model != null ? model : characterRoot;
            }
        }

        private void CacheBaseScale()
        {
            if (visualRoot == null)
            {
                return;
            }

            _baseVisualScale = visualRoot.localScale;
        }

        private void ResetRuntimeMultipliers()
        {
            CurrentScaleMultiplier = 1f;
            CurrentMoveSpeedMultiplier = 1f;
            CurrentBaseDamageMultiplier = 1f;
            CurrentKnockbackResistMultiplier = 1f;
            CurrentGravityMultiplier = 1f;
            CurrentJumpMultiplier = 1f;
            IsSuperArmorActive = false;
            IsInvisibilityActive = false;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemCharacterBuffApplier] {message}", this);
        }
    }
}
