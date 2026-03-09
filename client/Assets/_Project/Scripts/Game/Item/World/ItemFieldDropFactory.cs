/*
 * 파일 개요:
 * - ItemFieldDropFactory 스크립트가 들어 있는 파일이다.
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
    /// 아이템 정의를 기반으로 필드 드랍 오브젝트를 생성한다.
    /// </summary>
    public sealed class ItemFieldDropFactory
    {
        private const string BlackholeFieldEffectAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple.prefab";
        private const float WaterMelonSwordScaleMultiplier = 1.3f;
        private readonly IItemFieldPrefabResolver _prefabResolver;
        private GameObject _blackholeEffectPrefabCache;

        public ItemFieldDropFactory(IItemFieldPrefabResolver prefabResolver)
        {
            _prefabResolver = prefabResolver;
        }

        public ItemFieldDrop Create(ItemDefinition definition, Vector3 position, Transform parent = null)
        {
            if (definition == null)
            {
                return null;
            }

            GameObject instance;
            var prefab = _prefabResolver?.Resolve(definition.Master.PrefabPath);
            if (prefab != null)
            {
                instance = Object.Instantiate(prefab, position, Quaternion.identity, parent);
            }
            else
            {
                if (string.Equals(definition.Master.ItemId, ItemIds.BlackholeBomb, System.StringComparison.Ordinal))
                {
                    // 블랙홀은 프리팹 우선 정책을 따르되, 누락 시 임시 생성으로 동작을 유지한다.
                    instance = CreateBlackholeFieldDropRoot(position, parent);
                    Debug.LogWarning(
                        $"[ItemFieldDropFactory] Missing blackhole prefab: {definition.Master.PrefabPath}. Using fallback sphere.",
                        instance);
                }
                else
                {
                    instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    instance.transform.position = position;
                    instance.transform.localScale = Vector3.one * 0.45f;
                    if (parent != null)
                    {
                        instance.transform.SetParent(parent, true);
                    }

                    Debug.LogWarning(
                        $"[ItemFieldDropFactory] Missing item prefab: {definition.Master.PrefabPath}. Using fallback cube for {definition.Master.ItemId}.",
                        instance);
                }
            }

            if (string.Equals(definition.Master.ItemId, ItemIds.WaterMelonSword, System.StringComparison.Ordinal))
            {
                ApplyScaleMultiplier(instance, WaterMelonSwordScaleMultiplier);
                ApplyUrpMaterialFallback(instance);
            }

            if (string.Equals(definition.Master.ItemId, ItemIds.BlackholeBomb, System.StringComparison.Ordinal))
            {
                ConfigureBlackholeVisual(instance);
            }

            instance.name = $"FieldItem_{definition.Master.ItemId}";
            var fieldDrop = instance.GetComponent<ItemFieldDrop>();
            if (fieldDrop == null)
            {
                fieldDrop = instance.AddComponent<ItemFieldDrop>();
            }

            fieldDrop.SetItemId(definition.Master.ItemId);
            EnsureCollider(instance);
            EnsureDynamicRigidbody(instance);
            return fieldDrop;
        }

        private void ConfigureBlackholeVisual(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            ApplyBlackholeShellTransparency(root);
            AttachOrRefreshBlackholeEffect(root.transform);
        }

        private GameObject CreateBlackholeFieldDropRoot(Vector3 position, Transform parent)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.transform.position = position;
            root.transform.localScale = Vector3.one * 0.45f;
            if (parent != null)
            {
                root.transform.SetParent(parent, true);
            }

            ApplyBlackholeShellTransparency(root);

            AttachOrRefreshBlackholeEffect(root.transform);

            return root;
        }

        private void AttachOrRefreshBlackholeEffect(Transform blackholeRoot)
        {
            if (blackholeRoot == null)
            {
                return;
            }

            var effectPrefab = TryLoadBlackholeFieldEffectPrefab();
            if (effectPrefab == null)
            {
                return;
            }

            var existing = blackholeRoot.Find("Item_BlackholeFx");
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
            }

            var effectInstance = Object.Instantiate(effectPrefab, blackholeRoot);
            effectInstance.name = "Item_BlackholeFx";
            effectInstance.transform.localPosition = Vector3.zero;
            effectInstance.transform.localRotation = Quaternion.identity;
            effectInstance.transform.localScale = Vector3.one * 0.7f;
            DisableColliders(effectInstance);
            DisableUnsupportedDistortionRenderers(effectInstance);
        }

        private static void ApplyBlackholeShellTransparency(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var renderer = root.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            // 내부 이펙트가 가려지지 않도록 외곽 구체를 반투명으로 설정한다.
            var material = renderer.material;
            if (material == null)
            {
                renderer.enabled = false;
                return;
            }

            var shellColor = new Color(0.07f, 0.07f, 0.08f, 0.4f);
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
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private GameObject TryLoadBlackholeFieldEffectPrefab()
        {
            if (_blackholeEffectPrefabCache != null)
            {
                return _blackholeEffectPrefabCache;
            }

#if UNITY_EDITOR
            _blackholeEffectPrefabCache = AssetDatabase.LoadAssetAtPath<GameObject>(BlackholeFieldEffectAssetPath);
            if (_blackholeEffectPrefabCache != null)
            {
                return _blackholeEffectPrefabCache;
            }
#endif
            _blackholeEffectPrefabCache = Resources.Load<GameObject>("Effect_02_BlackHole");
            return _blackholeEffectPrefabCache;
        }

        private static void DisableColliders(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private static void DisableUnsupportedDistortionRenderers(GameObject root)
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

        private static void EnsureCollider(GameObject target)
        {
            if (target.GetComponentInChildren<Collider>() != null)
            {
                return;
            }

            target.AddComponent<SphereCollider>();
        }

        private static void EnsureDynamicRigidbody(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            var body = target.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = target.AddComponent<Rigidbody>();
                // 필드 스폰 아이템은 기본적으로 물리 반응이 가능하도록 구성한다.
                body.mass = 1f;
                body.drag = 0f;
                body.angularDrag = 0.05f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            body.useGravity = true;
            body.isKinematic = false;
            body.WakeUp();
        }

        private static void ApplyScaleMultiplier(GameObject target, float multiplier)
        {
            if (target == null)
            {
                return;
            }

            var safeMultiplier = Mathf.Max(0.01f, multiplier);
            target.transform.localScale *= safeMultiplier;
        }

        private static void ApplyUrpMaterialFallback(GameObject root)
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
                    if (source == null || !NeedsUrpFallback(source))
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

                    // URP 미지원 셰이더(마젠타)를 런타임 호환 머티리얼로 치환한다.
                    var fallback = new Material(fallbackShader)
                    {
                        name = $"{source.name}_URPFallback"
                    };
                    CopyMainSurfaceProperties(source, fallback);
                    materials[m] = fallback;
                    replaced = true;
                }

                if (replaced)
                {
                    renderer.materials = materials;
                }
            }
        }

        private static bool NeedsUrpFallback(Material material)
        {
            if (material == null || material.shader == null)
            {
                return true;
            }

            if (!material.shader.isSupported)
            {
                return true;
            }

            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
            {
                return false;
            }

            var shaderName = material.shader.name ?? string.Empty;
            return string.Equals(shaderName, "Standard", System.StringComparison.OrdinalIgnoreCase) ||
                   shaderName.StartsWith("Legacy Shaders/", System.StringComparison.OrdinalIgnoreCase) ||
                   shaderName.StartsWith("Mobile/", System.StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyMainSurfaceProperties(Material source, Material destination)
        {
            if (source == null || destination == null)
            {
                return;
            }

            var color = Color.white;
            if (source.HasProperty("_BaseColor"))
            {
                color = source.GetColor("_BaseColor");
            }
            else if (source.HasProperty("_Color"))
            {
                color = source.GetColor("_Color");
            }

            if (destination.HasProperty("_BaseColor"))
            {
                destination.SetColor("_BaseColor", color);
            }
            if (destination.HasProperty("_Color"))
            {
                destination.SetColor("_Color", color);
            }

            Texture mainTexture = null;
            if (source.HasProperty("_BaseMap"))
            {
                mainTexture = source.GetTexture("_BaseMap");
            }
            else if (source.HasProperty("_MainTex"))
            {
                mainTexture = source.GetTexture("_MainTex");
            }

            if (mainTexture != null)
            {
                if (destination.HasProperty("_BaseMap"))
                {
                    destination.SetTexture("_BaseMap", mainTexture);
                }
                if (destination.HasProperty("_MainTex"))
                {
                    destination.SetTexture("_MainTex", mainTexture);
                }
            }
        }
    }
}

