/*
 * 파일 개요:
 * - ItemGameplayRunner.BlackholeOutline 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Presentation 계층에서 블랙홀 내부 대상의 외곽선 시각화를 담당한다.
 * - 블랙홀 판정과 분리된 로컬 연출 계층이므로, 게임 규칙을 바꾸지 않고도 켜고 끌 수 있는 구조를 유지한다.
 */
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void ApplyBlackholeOutlineForTarget(Transform root)
        {
            if (!enableBlackholeTargetOutline || root == null)
            {
                return;
            }

            if (_blackholeOutlineStates.TryGetValue(root, out var existingState))
            {
                existingState.RefCount++;
                return;
            }

            var state = new BlackholeOutlineState
            {
                RefCount = 1
            };

            CreateBlackholeOutlineProxies(root, state);
            if (state.ProxyObjects.Count == 0)
            {
                return;
            }

            _blackholeOutlineStates[root] = state;
        }

        private void ReleaseBlackholeOutlineForTarget(Transform root)
        {
            if (root == null)
            {
                return;
            }

            if (!_blackholeOutlineStates.TryGetValue(root, out var state))
            {
                return;
            }

            state.RefCount--;
            if (state.RefCount > 0)
            {
                return;
            }

            DestroyBlackholeOutlineState(root, state);
            _blackholeOutlineStates.Remove(root);
        }

        private void ReleaseAllBlackholeTargetOutlines()
        {
            if (_blackholeOutlineStates.Count == 0)
            {
                return;
            }

            foreach (var pair in _blackholeOutlineStates)
            {
                DestroyBlackholeOutlineState(pair.Key, pair.Value);
            }

            _blackholeOutlineStates.Clear();
        }

        private void CreateBlackholeOutlineProxies(Transform root, BlackholeOutlineState state)
        {
            if (root == null || state == null)
            {
                return;
            }

            var outlineMaterial = GetOrCreateBlackholeTargetOutlineMaterial();
            if (outlineMaterial == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!ShouldCreateBlackholeOutlineProxy(renderer))
                {
                    continue;
                }

                GameObject proxyObject = null;
                if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                {
                    proxyObject = CreateBlackholeOutlineProxyForSkinnedRenderer(skinnedMeshRenderer, outlineMaterial);
                }
                else if (renderer is MeshRenderer meshRenderer)
                {
                    proxyObject = CreateBlackholeOutlineProxyForMeshRenderer(meshRenderer, outlineMaterial);
                }

                if (proxyObject != null)
                {
                    state.ProxyObjects.Add(proxyObject);
                }
            }
        }

        private GameObject CreateBlackholeOutlineProxyForMeshRenderer(MeshRenderer renderer, Material outlineMaterial)
        {
            if (renderer == null || outlineMaterial == null)
            {
                return null;
            }

            if (!renderer.TryGetComponent<MeshFilter>(out var meshFilter) || meshFilter.sharedMesh == null)
            {
                return null;
            }

            var proxy = new GameObject($"{renderer.gameObject.name}_BlackholeOutline");
            proxy.transform.SetParent(renderer.transform, false);
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.identity;
            proxy.transform.localScale = Vector3.one * Mathf.Max(1f, blackholeTargetOutlineScaleMultiplier);
            proxy.layer = renderer.gameObject.layer;

            var proxyFilter = proxy.AddComponent<MeshFilter>();
            proxyFilter.sharedMesh = meshFilter.sharedMesh;

            var proxyRenderer = proxy.AddComponent<MeshRenderer>();
            proxyRenderer.sharedMaterials = BuildOutlineMaterialArray(meshFilter.sharedMesh.subMeshCount, outlineMaterial);
            ConfigureOutlineRenderer(proxyRenderer);
            return proxy;
        }

        private GameObject CreateBlackholeOutlineProxyForSkinnedRenderer(SkinnedMeshRenderer renderer, Material outlineMaterial)
        {
            if (renderer == null || outlineMaterial == null || renderer.sharedMesh == null)
            {
                return null;
            }

            var proxy = new GameObject($"{renderer.gameObject.name}_BlackholeOutline");
            proxy.transform.SetParent(renderer.transform, false);
            proxy.transform.localPosition = Vector3.zero;
            proxy.transform.localRotation = Quaternion.identity;
            proxy.transform.localScale = Vector3.one * Mathf.Max(1f, blackholeTargetOutlineScaleMultiplier);
            proxy.layer = renderer.gameObject.layer;

            var proxyFilter = proxy.AddComponent<MeshFilter>();

            var proxyRenderer = proxy.AddComponent<MeshRenderer>();
            proxyRenderer.sharedMaterials = BuildOutlineMaterialArray(renderer.sharedMesh.subMeshCount, outlineMaterial);
            ConfigureOutlineRenderer(proxyRenderer);

            var proxyUpdater = proxy.AddComponent<BlackholeSkinnedOutlineProxy>();
            proxyUpdater.Initialize(renderer, proxyFilter);
            return proxy;
        }

        private static Material[] BuildOutlineMaterialArray(int subMeshCount, Material outlineMaterial)
        {
            var count = Mathf.Max(1, subMeshCount);
            var materials = new Material[count];
            for (var i = 0; i < count; i++)
            {
                materials[i] = outlineMaterial;
            }

            return materials;
        }

        private static void ConfigureOutlineRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
        }

        private bool ShouldCreateBlackholeOutlineProxy(Renderer renderer)
        {
            if (renderer == null || !renderer.enabled)
            {
                return false;
            }

            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
            {
                return false;
            }

            var owner = renderer.gameObject;
            if (owner == null)
            {
                return false;
            }

            var objectName = owner.name ?? string.Empty;
            if (objectName.IndexOf("BlackholeOutline", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("Item_BlackholeFx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                objectName.IndexOf("OuterLayer", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return owner.GetComponentInParent<ParticleSystem>() == null &&
                   owner.GetComponentInParent<LineRenderer>() == null &&
                   owner.GetComponentInParent<TrailRenderer>() == null;
        }

        private Material GetOrCreateBlackholeTargetOutlineMaterial()
        {
            if (_blackholeTargetOutlineMaterial != null)
            {
                return _blackholeTargetOutlineMaterial;
            }

            var shader =
                Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                return null;
            }

            _blackholeTargetOutlineMaterial = new Material(shader)
            {
                name = "Item_Blackhole_TargetOutline",
                hideFlags = HideFlags.DontSave
            };

            ApplyBlackholeTargetOutlineMaterialProperties(_blackholeTargetOutlineMaterial);
            return _blackholeTargetOutlineMaterial;
        }

        private void ApplyBlackholeTargetOutlineMaterialProperties(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", blackholeTargetOutlineColor);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", blackholeTargetOutlineColor);
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
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            if (material.HasProperty("_Cull"))
            {
                // 한국어: 살짝 확대한 메쉬의 앞면을 제거해 외곽선 실루엣만 남긴다.
                material.SetFloat("_Cull", (float)CullMode.Front);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent + 40;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        private void ReleaseBlackholeTargetOutlineMaterial()
        {
            if (_blackholeTargetOutlineMaterial == null)
            {
                return;
            }

            Destroy(_blackholeTargetOutlineMaterial);
            _blackholeTargetOutlineMaterial = null;
        }

        private static void DestroyBlackholeOutlineState(Transform root, BlackholeOutlineState state)
        {
            if (state == null)
            {
                return;
            }

            for (var i = 0; i < state.ProxyObjects.Count; i++)
            {
                var proxy = state.ProxyObjects[i];
                if (proxy != null)
                {
                    Destroy(proxy);
                }
            }

            state.ProxyObjects.Clear();

            if (root == null)
            {
                return;
            }
        }
    }
}
