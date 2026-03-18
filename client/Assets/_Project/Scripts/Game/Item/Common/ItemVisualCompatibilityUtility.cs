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
                var materials = SanitizeMaterials(useInstancedMaterials
                    ? renderer.materials
                    : renderer.sharedMaterials);
                if (materials == null || materials.Length == 0)
                {
                    continue;
                }

                var replaced = false;
                for (var m = 0; m < materials.Length; m++)
                {
                    var source = materials[m];
                    if (source == null)
                    {
                        continue;
                    }

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

            var shaderName = material.shader.name ?? string.Empty;
            if (shaderName.IndexOf("Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (material.shader.isSupported)
            {
                return false;
            }

            if (GraphicsSettings.currentRenderPipeline == null)
            {
                return false;
            }

            if (shaderName.StartsWith("Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase) ||
                shaderName.StartsWith("Hidden/Universal Render Pipeline/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 한국어: 아이템 시스템에서 로드하는 외부 이펙트는 빌드에서 커스텀 셰이더가 자주 깨지므로
            // 한국어: URP 네임스페이스가 아니면 전부 안전한 URP 셰이더로 교체한다.
            return true;
        }

        private static bool ShouldForceGlowFallback(Renderer renderer, Material source)
        {
            if (renderer == null || source == null || GraphicsSettings.currentRenderPipeline == null)
            {
                return false;
            }

            if (source.shader != null && source.shader.isSupported)
            {
                return false;
            }

            var rendererName = renderer.gameObject.name ?? string.Empty;
            var materialName = source.name ?? string.Empty;
            return rendererName.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   rendererName.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolySpriteGlow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolySolidGlow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolySprite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolyProton", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   materialName.IndexOf("PolyTrail", StringComparison.OrdinalIgnoreCase) >= 0;
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

            var materials = SanitizeMaterials(useInstancedMaterials
                ? renderer.materials
                : renderer.sharedMaterials);
            if (materials == null || materials.Length == 0)
            {
                return false;
            }

            var hasValidMaterial = false;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                hasValidMaterial = true;
                if (material.shader == null)
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

            return !hasValidMaterial;
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
                       Shader.Find("Universal Render Pipeline/Unlit") ??
                       Shader.Find("Universal Render Pipeline/Simple Lit");
            }

            if (preferParticleShader)
            {
                // 한국어: 빌드에서 파티클 전용 셰이더 variant가 빠지는 경우가 있어 Unlit만 사용한다.
                return Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                       Shader.Find("Universal Render Pipeline/Particles/Lit") ??
                       Shader.Find("Universal Render Pipeline/Particles/Simple Lit") ??
                       Shader.Find("Universal Render Pipeline/Unlit") ??
                       Shader.Find("Universal Render Pipeline/Lit") ??
                       Shader.Find("Universal Render Pipeline/Simple Lit");
            }

            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("Universal Render Pipeline/Simple Lit");
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

            var destinationShaderName = destination.shader != null ? destination.shader.name ?? string.Empty : string.Empty;
            if (destinationShaderName.IndexOf("/Particles/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (destination.HasProperty("_BaseColor"))
                {
                    destination.SetColor("_BaseColor", sourceColor);
                }
                if (destination.HasProperty("_Color"))
                {
                    destination.SetColor("_Color", sourceColor);
                }
                if (destination.HasProperty("_EmissionColor"))
                {
                    destination.SetColor("_EmissionColor", sourceColor * 1.5f);
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

            var shaderName = source.shader != null ? source.shader.name ?? string.Empty : string.Empty;
            var isParticleLikeShader = shaderName.IndexOf("Particles", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isParticleLikeShader && source.HasProperty("_TintColor"))
            {
                return source.GetColor("_TintColor");
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

        private static Material[] SanitizeMaterials(Material[] materials)
        {
            if (materials == null || materials.Length == 0)
            {
                return Array.Empty<Material>();
            }

            var validCount = 0;
            for (var i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    validCount++;
                }
            }

            if (validCount == materials.Length)
            {
                return materials;
            }

            if (validCount == 0)
            {
                return Array.Empty<Material>();
            }

            var sanitized = new Material[validCount];
            var nextIndex = 0;
            for (var i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null)
                {
                    continue;
                }

                sanitized[nextIndex++] = materials[i];
            }

            return sanitized;
        }
    }
}
