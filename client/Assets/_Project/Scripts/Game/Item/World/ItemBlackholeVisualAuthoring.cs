/*
 * 파일 개요:
 * - ItemBlackholeVisualAuthoring 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 블랙홀 프리팹의 외형(코어 투명도 + 이펙트 자식)을 자동으로 구성한다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ItemBlackholeVisualAuthoring : MonoBehaviour
    {
        private const string BlackholeEffectAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple.prefab";
        private const string BlackholeEffectResourcePath = "Effect_02_BlackHole";
        private const string EffectChildName = "Item_BlackholeFx";

        [Header("비주얼")]
        [SerializeField] private float shellAlpha = 0.4f;
        [SerializeField] private Vector3 effectLocalScale = Vector3.one * 0.6f;
        [SerializeField] private Color effectTintColor = new(0.55f, 0.18f, 0.95f, 0.85f);
        [SerializeField] private float effectEmissionMultiplier = 1.8f;

        private void OnEnable()
        {
            RefreshVisual();
        }

        private void OnValidate()
        {
            shellAlpha = Mathf.Clamp01(shellAlpha);
            if (effectLocalScale.x <= 0f || effectLocalScale.y <= 0f || effectLocalScale.z <= 0f)
            {
                effectLocalScale = Vector3.one * 0.6f;
            }

            RefreshVisual();
        }

        /// <summary>
        /// 코어 쉘과 이펙트를 현재 설정값으로 갱신한다.
        /// </summary>
        public void RefreshVisual()
        {
            if (IsPrefabAssetContext())
            {
                return;
            }

            ApplyShellTransparency();
            EnsureEffectChild();
        }

        private void ApplyShellTransparency()
        {
            if (IsPrefabAssetContext())
            {
                return;
            }

            var rootRenderer = GetComponent<Renderer>();
            if (rootRenderer == null)
            {
                return;
            }

            var material = Application.isPlaying
                ? rootRenderer.material
                : rootRenderer.sharedMaterial;
            if (material == null)
            {
                return;
            }

            // 가운데 구체는 요청값에 맞춰 0.4 투명도를 기본값으로 유지한다.
            var shellColor = new Color(0.07f, 0.07f, 0.08f, shellAlpha);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", shellColor);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", shellColor);
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }
            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
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
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            rootRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private bool IsPrefabAssetContext()
        {
#if UNITY_EDITOR
            return PrefabUtility.IsPartOfPrefabAsset(gameObject);
#else
            return false;
#endif
        }

        private void EnsureEffectChild()
        {
            if (IsPrefabAssetContext())
            {
                return;
            }

            var effectRoot = transform.Find(EffectChildName);
            if (effectRoot == null)
            {
                var effectPrefab = LoadEffectPrefab();
                if (effectPrefab == null)
                {
                    return;
                }

                var effectInstance = InstantiateEffect(effectPrefab);
                if (effectInstance == null)
                {
                    return;
                }

                effectInstance.name = EffectChildName;
                effectRoot = effectInstance.transform;
            }

            effectRoot.localPosition = Vector3.zero;
            effectRoot.localRotation = Quaternion.identity;
            effectRoot.localScale = effectLocalScale;
            DisableColliders(effectRoot.gameObject);
            ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(effectRoot.gameObject);
            DisableUnsupportedDistortionRenderers(effectRoot.gameObject);
            ApplyEffectTint(effectRoot.gameObject);
        }

        private GameObject LoadEffectPrefab()
        {
            var runtimePrefab = Resources.Load<GameObject>(BlackholeEffectResourcePath);
            if (runtimePrefab != null)
            {
                return runtimePrefab;
            }

#if UNITY_EDITOR
            var editorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlackholeEffectAssetPath);
            if (editorPrefab != null)
            {
                return editorPrefab;
            }
#endif
            return null;
        }

        private GameObject InstantiateEffect(GameObject effectPrefab)
        {
            if (effectPrefab == null)
            {
                return null;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return PrefabUtility.InstantiatePrefab(effectPrefab, transform) as GameObject;
            }
#endif
            return Instantiate(effectPrefab, transform);
        }

        private static void DisableColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private static void DisableUnsupportedDistortionRenderers(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var rendererName = renderer.gameObject.name ?? string.Empty;
                if (rendererName.IndexOf("Distort", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    renderer.enabled = false;
                    continue;
                }

                var materials = renderer.sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    var shaderName = material.shader != null ? material.shader.name : string.Empty;
                    if (shaderName.IndexOf("Distortion Effect", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        shaderName.IndexOf("Grab", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("Distort", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        renderer.enabled = false;
                        break;
                    }
                }
            }
        }

        private void ApplyEffectTint(GameObject root)
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

                var materials = Application.isPlaying ? renderer.materials : renderer.sharedMaterials;
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    // 한국어: 블랙홀 이펙트는 빌드에서 fallback 머티리얼로 바뀌며 흰색이 되는 경우가 있어,
                    // 한국어: 보라 계열 틴트와 emission을 다시 강제로 넣어 원본 분위기를 유지한다.
                    if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", effectTintColor);
                    }
                    if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", effectTintColor);
                    }
                    if (material.HasProperty("_TintColor"))
                    {
                        material.SetColor("_TintColor", effectTintColor);
                    }
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.SetColor("_EmissionColor", effectTintColor * Mathf.Max(0f, effectEmissionMultiplier));
                        material.EnableKeyword("_EMISSION");
                    }
                }

                if (!Application.isPlaying)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }
    }
}

