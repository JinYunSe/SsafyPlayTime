using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    internal static class ItemVisualCompatibilityUtility
    {
        internal static void ApplyUrpMaterialFallback(GameObject root, bool forceLitOverride = false)
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

                var useInstancedMaterials = ShouldUseInstancedMaterials(renderer);
                var materials = useInstancedMaterials
                    ? renderer.materials
                    : renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                var replaced = false;
                for (var m = 0; m < materials.Length; m++)
                {
                    var source = materials[m];
                    if (!forceLitOverride && !NeedsFallback(source) && !ShouldForceGlowFallback(renderer, source))
                    {
                        continue;
                    }

                    var preferParticleShader = ShouldPreferParticleFallback(renderer, source);
                    var fallbackShader = ResolveFallbackShader(renderer, source, preferParticleShader, forceLitOverride);
                    if (fallbackShader == null)
                    {
                        continue;
                    }

                    var fallback = new Material(fallbackShader)
                    {
                        name = $"{(source != null ? source.name : "MissingMaterial")}_Compat"
                    };

                    CopySurfaceProperties(source, fallback);
                    ConfigureTransparencyIfNeeded(source, fallback);
                    materials[m] = fallback;
                    replaced = true;
                }

                if (renderer is ParticleSystemRenderer particleRenderer)
                {
                    replaced |= ApplyParticleTrailMaterialFallback(particleRenderer, forceLitOverride);
                }

                if (replaced)
                {
                    if (useInstancedMaterials)
                    {
                        renderer.materials = materials;
                    }
                    else
                    {
                        renderer.sharedMaterials = materials;
                    }
                }

                if (ShouldDisableRenderer(renderer, useInstancedMaterials))
                {
                    renderer.enabled = false;
                }
            }
        }

        private static bool NeedsFallback(Material material)
        {
            if (material == null || material.shader == null)
            {
                return true;
            }

            if (!material.shader.isSupported)
            {
                return true;
            }

            var shaderName = material.shader.name ?? string.Empty;
            if (shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (GraphicsSettings.currentRenderPipeline == null)
            {
                return false;
            }

            return shaderName.StartsWith("Standard", StringComparison.OrdinalIgnoreCase) ||
                   shaderName.StartsWith("Legacy Shaders/", StringComparison.OrdinalIgnoreCase) ||
                   shaderName.StartsWith("Particles/", StringComparison.OrdinalIgnoreCase) ||
                   shaderName.StartsWith("Mobile/", StringComparison.OrdinalIgnoreCase) ||
                   shaderName.StartsWith("PolygonArsenal/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldForceGlowFallback(Renderer renderer, Material source)
        {
            if (renderer == null || source == null || GraphicsSettings.currentRenderPipeline == null)
            {
                return false;
            }

            var rendererName = renderer.gameObject.name ?? string.Empty;
            var materialName = source.name ?? string.Empty;
            return rendererName.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolySpriteGlow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolySolidGlow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolySprite_AB", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldPreferParticleFallback(Renderer renderer, Material source)
        {
            if (renderer is ParticleSystemRenderer)
            {
                return true;
            }

            var rendererName = renderer != null ? renderer.gameObject.name ?? string.Empty : string.Empty;
            var materialName = source != null ? source.name ?? string.Empty : string.Empty;
            var shaderName = source != null && source.shader != null ? source.shader.name ?? string.Empty : string.Empty;
            return rendererName.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("trail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("outerlayer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("circling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("outerlayer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   shaderName.IndexOf("Particles", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldDisableRenderer(Renderer renderer, bool useInstancedMaterials)
        {
            if (renderer == null)
            {
                return false;
            }

            var rendererName = renderer.gameObject.name ?? string.Empty;
            if (rendererName.IndexOf("Distort", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var materials = useInstancedMaterials
                ? renderer.materials
                : renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null || material.shader == null)
                {
                    return true;
                }

                var shader = material.shader;
                var shaderName = shader.name ?? string.Empty;
                if (!shader.isSupported ||
                    shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    shaderName.IndexOf("Grab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    shaderName.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.name.IndexOf("Distort", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldUseInstancedMaterials(Renderer renderer)
        {
            if (!Application.isPlaying || renderer == null)
            {
                return false;
            }

#if UNITY_EDITOR
            if (PrefabUtility.IsPartOfPrefabAsset(renderer.gameObject))
            {
                return false;
            }
#endif
            return true;
        }

        private static bool ApplyParticleTrailMaterialFallback(ParticleSystemRenderer particleRenderer, bool forceLitOverride)
        {
            if (particleRenderer == null)
            {
                return false;
            }

            var trailMaterial = particleRenderer.trailMaterial;
            if (!forceLitOverride &&
                !NeedsFallback(trailMaterial) &&
                !ShouldForceGlowFallback(particleRenderer, trailMaterial))
            {
                return false;
            }

            var fallbackShader = ResolveFallbackShader(particleRenderer, trailMaterial, true, forceLitOverride);
            if (fallbackShader == null)
            {
                return false;
            }

            var fallback = new Material(fallbackShader)
            {
                name = $"{(trailMaterial != null ? trailMaterial.name : "MissingTrailMaterial")}_Compat"
            };

            CopySurfaceProperties(trailMaterial, fallback);
            ConfigureTransparencyIfNeeded(trailMaterial, fallback);
            particleRenderer.trailMaterial = fallback;
            return true;
        }

        private static Shader ResolveFallbackShader(Renderer renderer, Material source, bool preferParticleShader, bool forceLitOverride)
        {
            if (forceLitOverride)
            {
                return Shader.Find("Universal Render Pipeline/Lit") ??
                       Shader.Find("Universal Render Pipeline/Simple Lit") ??
                       Shader.Find("Universal Render Pipeline/Unlit") ??
                       Shader.Find("Standard");
            }

            if (preferParticleShader)
            {
                return Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                       Shader.Find("Universal Render Pipeline/Particles/Lit") ??
                       Shader.Find("Universal Render Pipeline/Unlit") ??
                       Shader.Find("Standard");
            }

            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Universal Render Pipeline/Simple Lit") ??
                   Shader.Find("Standard");
        }

        private static void CopySurfaceProperties(Material source, Material destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            var sourceTexture = source.HasProperty("_BaseMap")
                ? source.GetTexture("_BaseMap")
                : source.HasProperty("_MainTex")
                    ? source.GetTexture("_MainTex")
                    : null;
            if (sourceTexture != null)
            {
                if (destination.HasProperty("_BaseMap"))
                {
                    destination.SetTexture("_BaseMap", sourceTexture);
                }
                if (destination.HasProperty("_MainTex"))
                {
                    destination.SetTexture("_MainTex", sourceTexture);
                }

                if (source.HasProperty("_MainTex"))
                {
                    var scale = source.GetTextureScale("_MainTex");
                    var offset = source.GetTextureOffset("_MainTex");
                    if (destination.HasProperty("_BaseMap"))
                    {
                        destination.SetTextureScale("_BaseMap", scale);
                        destination.SetTextureOffset("_BaseMap", offset);
                    }
                    if (destination.HasProperty("_MainTex"))
                    {
                        destination.SetTextureScale("_MainTex", scale);
                        destination.SetTextureOffset("_MainTex", offset);
                    }
                }
            }

            var sourceColor = ResolveSourceColor(source);
            if (destination.HasProperty("_BaseColor"))
            {
                destination.SetColor("_BaseColor", sourceColor);
            }
            if (destination.HasProperty("_Color"))
            {
                destination.SetColor("_Color", sourceColor);
            }
            if (destination.HasProperty("_TintColor"))
            {
                destination.SetColor("_TintColor", sourceColor);
            }

            var emissionTexture = source.HasProperty("_EmissionMap")
                ? source.GetTexture("_EmissionMap")
                : null;
            if (emissionTexture != null)
            {
                if (destination.HasProperty("_EmissionMap"))
                {
                    destination.SetTexture("_EmissionMap", emissionTexture);
                }
                destination.EnableKeyword("_EMISSION");
            }

            if (source.HasProperty("_EmissionColor"))
            {
                var emissionColor = source.GetColor("_EmissionColor");
                if (destination.HasProperty("_EmissionColor"))
                {
                    destination.SetColor("_EmissionColor", emissionColor);
                }

                if (emissionColor.maxColorComponent > 0.001f)
                {
                    destination.EnableKeyword("_EMISSION");
                }
            }
        }

        private static Color ResolveSourceColor(Material source)
        {
            if (source == null)
            {
                return Color.white;
            }

            if (source.HasProperty("_BaseColor"))
            {
                return source.GetColor("_BaseColor");
            }

            if (source.HasProperty("_Color"))
            {
                return source.GetColor("_Color");
            }

            if (source.HasProperty("_TintColor"))
            {
                return source.GetColor("_TintColor");
            }

            if (source.HasProperty("_RimColor"))
            {
                return source.GetColor("_RimColor");
            }

            if (source.HasProperty("_InnerColor"))
            {
                return source.GetColor("_InnerColor");
            }

            return Color.white;
        }

        private static void ConfigureTransparencyIfNeeded(Material source, Material destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            var useAdditiveBlend = IsAdditiveLike(source);
            var alpha = ResolveSourceColor(source).a;
            if (alpha >= 0.999f && !useAdditiveBlend)
            {
                return;
            }

            if (destination.HasProperty("_Surface"))
            {
                destination.SetFloat("_Surface", 1f);
            }
            if (destination.HasProperty("_Blend"))
            {
                destination.SetFloat("_Blend", 0f);
            }
            if (destination.HasProperty("_SrcBlend"))
            {
                destination.SetFloat("_SrcBlend", (float)(useAdditiveBlend ? BlendMode.SrcAlpha : BlendMode.SrcAlpha));
            }
            if (destination.HasProperty("_DstBlend"))
            {
                destination.SetFloat("_DstBlend", (float)(useAdditiveBlend ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            }
            if (destination.HasProperty("_ZWrite"))
            {
                destination.SetFloat("_ZWrite", 0f);
            }
            if (destination.HasProperty("_Mode"))
            {
                destination.SetFloat("_Mode", useAdditiveBlend ? 4f : 3f);
            }

            destination.SetOverrideTag("RenderType", "Transparent");
            destination.renderQueue = (int)RenderQueue.Transparent;
            destination.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            destination.EnableKeyword("_ALPHABLEND_ON");
            destination.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        private static bool IsAdditiveLike(Material source)
        {
            if (source == null)
            {
                return false;
            }

            var materialName = source.name ?? string.Empty;
            var shaderName = source.shader != null ? source.shader.name ?? string.Empty : string.Empty;
            if (materialName.IndexOf("_ADD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                materialName.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("Additive", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (source.HasProperty("_DstBlend") && Mathf.Approximately(source.GetFloat("_DstBlend"), (float)BlendMode.One))
            {
                return true;
            }

            if (source.HasProperty("_Mode") && Mathf.Approximately(source.GetFloat("_Mode"), 4f))
            {
                return true;
            }

            return false;
        }
    }
}
