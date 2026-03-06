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
        [SerializeField] private Vector3 effectLocalScale = Vector3.one * 0.7f;

        private void OnEnable()
        {
            RefreshVisual();
        }

        private void OnValidate()
        {
            shellAlpha = Mathf.Clamp01(shellAlpha);
            if (effectLocalScale.x <= 0f || effectLocalScale.y <= 0f || effectLocalScale.z <= 0f)
            {
                effectLocalScale = Vector3.one * 0.7f;
            }

            RefreshVisual();
        }

        /// <summary>
        /// 코어 쉘과 이펙트를 현재 설정값으로 갱신한다.
        /// </summary>
        public void RefreshVisual()
        {
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
            return !Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(gameObject);
#else
            return false;
#endif
        }

        private void EnsureEffectChild()
        {
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
            DisableUnsupportedDistortionRenderers(effectRoot.gameObject);
        }

        private GameObject LoadEffectPrefab()
        {
#if UNITY_EDITOR
            var editorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BlackholeEffectAssetPath);
            if (editorPrefab != null)
            {
                return editorPrefab;
            }
#endif
            return Resources.Load<GameObject>(BlackholeEffectResourcePath);
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
    }
}
