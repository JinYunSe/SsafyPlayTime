using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SSAFYPlayTime.Gameplay.Items
{
    internal static class ItemVisualCompatibilityUtility
    {
        internal static void ApplyUrpMaterialFallback(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var useInstancedMaterials = Application.isPlaying;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var materials = useInstancedMaterials
                    ? renderer.materials
                    : renderer.sharedMaterials;
                var replaced = false;
                var preferParticleShader = ShouldPreferParticleFallback(renderer);
                for (var m = 0; m < materials.Length; m++)
                {
                    var source = materials[m];
                    if (!NeedsFallback(source))
                    {
                        continue;
                    }

                    var fallbackShader = ResolveFallbackShader(renderer, preferParticleShader);
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

        private static bool ShouldPreferParticleFallback(Renderer renderer)
        {
            if (renderer is ParticleSystemRenderer)
            {
                return true;
            }

            var rendererName = renderer != null ? renderer.gameObject.name ?? string.Empty : string.Empty;
            return rendererName.IndexOf("particle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("trail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("outerlayer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("circling", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static Shader ResolveFallbackShader(Renderer renderer, bool preferParticleShader)
        {
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

            var alpha = ResolveSourceColor(source).a;
            if (alpha >= 0.999f)
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
                destination.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }
            if (destination.HasProperty("_DstBlend"))
            {
                destination.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }
            if (destination.HasProperty("_ZWrite"))
            {
                destination.SetFloat("_ZWrite", 0f);
            }
            if (destination.HasProperty("_Mode"))
            {
                destination.SetFloat("_Mode", 3f);
            }

            destination.SetOverrideTag("RenderType", "Transparent");
            destination.renderQueue = (int)RenderQueue.Transparent;
            destination.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            destination.EnableKeyword("_ALPHABLEND_ON");
            destination.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
    }
}
