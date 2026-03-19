/*
 * File overview:
 * - Contains ItemBlackholeVisualAuthoring.
 * - Owns the blackhole core shell and inner FX setup in the world layer.
 * - Changes here affect field, held, projectile, and activation visuals together.
 */
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// Builds the blackhole look from a core shell plus inner FX child objects.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ItemBlackholeVisualAuthoring : MonoBehaviour
    {
        private const string BlackholeEffectAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple.prefab";
        private const string BlackholeEffectResourcePath =
            "Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple";
        private const string EffectChildName = "Item_BlackholeFx";
        private const string ShellMaterialAssetPath = "Assets/_Project/Materials/ItemBlackholeShell.mat";
        private const string OuterMaterialAssetPath = "Assets/_Project/Materials/BlackholeFx_Outer_URP.mat";
        private const string GlowMaterialAssetPath = "Assets/_Project/Materials/BlackholeFx_Glow_URP.mat";
        private const string SpriteMaterialAssetPath = "Assets/_Project/Materials/BlackholeFx_Sprite_URP.mat";
        private const string SolidMaterialAssetPath = "Assets/_Project/Materials/BlackholeFx_Solid_URP.mat";

        [Header("Visuals")]
        [SerializeField] private float shellAlpha = 0.4f;
        [SerializeField] private Vector3 effectLocalScale = Vector3.one * 1.2f;
        [SerializeField] private Color effectTintColor = new(0.55f, 0.18f, 0.95f, 0.85f);
        [SerializeField] private float effectEmissionMultiplier = 1.8f;
        [SerializeField] private Material shellMaterial;
        [SerializeField] private Material outerLayerMaterial;
        [SerializeField] private Material glowMaterial;
        [SerializeField] private Material spriteMaterial;
        [SerializeField] private Material solidMaterial;

        private void OnEnable()
        {
            RefreshVisual();
        }

        private void OnValidate()
        {
            shellAlpha = Mathf.Clamp01(shellAlpha);
            if (effectLocalScale.x <= 0f || effectLocalScale.y <= 0f || effectLocalScale.z <= 0f)
            {
                effectLocalScale = Vector3.one * 1.2f;
            }

            RefreshVisual();
        }

        /// <summary>
        /// Refreshes the shell and inner FX using the current inspector settings.
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

            if (!Application.isPlaying)
            {
                ApplyEditorShellMaterial(rootRenderer);
                return;
            }

            // Hide the black core shell during play mode.
            // The root MeshRenderer is only the shell carrier for this authoring component.
            // Actual gameplay visuals should come from the inner FX child only.
            rootRenderer.enabled = false;
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
            var createdEffectChild = false;
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
                createdEffectChild = true;
            }

            effectRoot.localPosition = Vector3.zero;
            effectRoot.localRotation = Quaternion.identity;
            effectRoot.localScale = effectLocalScale;
            DisableColliders(effectRoot.gameObject);

            if (!Application.isPlaying)
            {
                ApplyConfiguredEffectBindings(effectRoot.gameObject);
                return;
            }

            if (createdEffectChild)
            {
                ApplyConfiguredEffectBindings(effectRoot.gameObject);
                ApplyEffectTint(effectRoot.gameObject);
            }

            DisableUnsupportedDistortionRenderers(effectRoot.gameObject);
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

                var materials = Application.isPlaying ? renderer.materials : GetEditableMaterials(renderer);
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    // In builds, blackhole FX materials can fall back and lose their intended tint.
                    // Re-apply the purple tint and emission so the original mood stays intact.
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

        private static Material GetEditableMaterial(Renderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            var shared = renderer.sharedMaterial;
            if (shared == null)
            {
                return null;
            }

            return new Material(shared)
            {
                name = $"{shared.name}_Preview"
            };
        }

        private static Material[] GetEditableMaterials(Renderer renderer)
        {
            if (renderer == null)
            {
                return System.Array.Empty<Material>();
            }

            var shared = renderer.sharedMaterials;
            if (shared == null || shared.Length == 0)
            {
                return System.Array.Empty<Material>();
            }

            var editable = new Material[shared.Length];
            for (var i = 0; i < shared.Length; i++)
            {
                var material = shared[i];
                if (material == null)
                {
                    editable[i] = null;
                    continue;
                }

                editable[i] = new Material(material)
                {
                    name = $"{material.name}_Preview"
                };
            }

            return editable;
        }

#if UNITY_EDITOR
        private static void ApplyEditorShellMaterial(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var shellMaterial = AssetDatabase.LoadAssetAtPath<Material>(ShellMaterialAssetPath);
            if (shellMaterial == null)
            {
                return;
            }

            renderer.sharedMaterial = shellMaterial;
        }

        private void ApplyConfiguredEffectBindings(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var resolvedOuter = outerLayerMaterial != null
                ? outerLayerMaterial
                : AssetDatabase.LoadAssetAtPath<Material>(OuterMaterialAssetPath);
            var resolvedGlow = glowMaterial != null
                ? glowMaterial
                : AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialAssetPath);
            var resolvedSprite = spriteMaterial != null
                ? spriteMaterial
                : AssetDatabase.LoadAssetAtPath<Material>(SpriteMaterialAssetPath);
            var resolvedSolid = solidMaterial != null
                ? solidMaterial
                : AssetDatabase.LoadAssetAtPath<Material>(SolidMaterialAssetPath);

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;

                var name = renderer.gameObject.name;
                if (string.Equals(name, "Glow", System.StringComparison.Ordinal) ||
                    string.Equals(name, "Particles", System.StringComparison.Ordinal) ||
                    string.Equals(name, EffectChildName, System.StringComparison.Ordinal))
                {
                    if (resolvedGlow != null)
                    {
                        AssignRendererMaterials(renderer, resolvedGlow);
                    }

                    continue;
                }

                if (string.Equals(name, "Circling", System.StringComparison.Ordinal))
                {
                    if (resolvedOuter != null)
                    {
                        AssignRendererMaterials(renderer, resolvedOuter);
                    }

                    continue;
                }

                if (resolvedSprite != null && resolvedSolid != null)
                {
                    AssignRendererMaterials(renderer, resolvedSprite, resolvedSolid);
                }
                else if (resolvedSprite != null)
                {
                    AssignRendererMaterials(renderer, resolvedSprite);
                }
            }
        }

        private static void AssignRendererMaterials(Renderer renderer, params Material[] materials)
        {
            if (renderer == null || materials == null || materials.Length == 0)
            {
                return;
            }

            renderer.sharedMaterials = materials;

            if (renderer is ParticleSystemRenderer particleRenderer)
            {
                particleRenderer.trailMaterial = materials[materials.Length - 1];
            }
        }
#else
        private static void ApplyEditorShellMaterial(Renderer renderer)
        {
        }

        private void ApplyConfiguredEffectBindings(GameObject root)
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

                renderer.enabled = true;

                var name = renderer.gameObject.name;
                if (string.Equals(name, "Glow", System.StringComparison.Ordinal) ||
                    string.Equals(name, "Particles", System.StringComparison.Ordinal) ||
                    string.Equals(name, EffectChildName, System.StringComparison.Ordinal))
                {
                    if (glowMaterial != null)
                    {
                        renderer.sharedMaterials = new[] { glowMaterial };
                    }

                    continue;
                }

                if (string.Equals(name, "Circling", System.StringComparison.Ordinal))
                {
                    if (outerLayerMaterial != null)
                    {
                        renderer.sharedMaterials = new[] { outerLayerMaterial };
                    }

                    continue;
                }

                if (spriteMaterial != null && solidMaterial != null)
                {
                    renderer.sharedMaterials = new[] { spriteMaterial, solidMaterial };
                    if (renderer is ParticleSystemRenderer particleRenderer)
                    {
                        particleRenderer.trailMaterial = solidMaterial;
                    }
                }
                else if (spriteMaterial != null)
                {
                    renderer.sharedMaterials = new[] { spriteMaterial };
                }
            }
        }
#endif
    }
}

