/*
 * 파일 개요:
 * - ItemFieldDropFactory 스크립트가 들어 있는 파일이다.
 * - World 계층에서 필드 드랍, 획득, 스폰, 배치, 프리팹 해석처럼 월드 오브젝트와 연결되는 책임을 맡는다.
 * - 필드 공통 규칙을 바꾸면 모든 아이템 획득 흐름에 영향이 가므로 개별 아이템 예외와 분리해서 수정해야 한다.
 */
using Fusion;
using UnityEngine;
using System.Collections.Generic;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 정의를 기반으로 필드 드랍 오브젝트를 생성한다.
    /// </summary>
    public sealed class ItemFieldDropFactory
    {
        public const string NetworkedDropAssetPath = "Assets/_Project/Prefabs/Items/NetworkedFieldItemDrop.prefab";
        public const string NetworkedDropResourcePath = "_Project/Prefabs/Items/NetworkedFieldItemDrop";

        private const string VisualRootName = "Visual";
        private const float GrowthFieldColliderRadius = 0.16f;
        private const float ShrinkFieldColliderRadius = 0.18f;
        private const float DefaultFieldColliderRadius = 0.24f;
        private const float ConsumableFieldDrag = 1.75f;
        private const float ConsumableFieldAngularDrag = 12f;
        private const float EquipmentFieldDrag = 0.15f;
        private const float EquipmentFieldAngularDrag = 0.35f;
        private const float SpecialFieldFriction = 1f;
        private const float SpecialFieldBounciness = 0f;
        private static PhysicMaterial s_specialEquipmentFieldMaterial;
        private static readonly Dictionary<string, SphereColliderProfile> s_colliderProfiles = new(System.StringComparer.Ordinal);

        private readonly IItemFieldPrefabResolver _prefabResolver;

        public ItemFieldDropFactory(IItemFieldPrefabResolver prefabResolver)
        {
            _prefabResolver = prefabResolver;
        }

        public ItemFieldDrop Create(ItemDefinition definition, Vector3 position, Transform parent = null)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Master.ItemId))
            {
                return null;
            }

            var root = new GameObject($"FieldItem_{definition.Master.ItemId}");
            root.transform.SetPositionAndRotation(position, Quaternion.identity);
            if (parent != null)
            {
                root.transform.SetParent(parent, true);
            }

            var fieldDrop = root.AddComponent<ItemFieldDrop>();
            fieldDrop.SetItemId(definition.Master.ItemId);
            fieldDrop.SetInstanceId(string.Empty);

            CreateFieldVisualInstance(definition, _prefabResolver, root.transform);
            fieldDrop.EnsureRuntimeSetup();
            return fieldDrop;
        }

        internal static GameObject TryLoadNetworkedDropPrefab(IItemFieldPrefabResolver prefabResolver)
        {
            if (prefabResolver != null)
            {
                var prefab = prefabResolver.Resolve(NetworkedDropAssetPath);
                if (prefab != null)
                {
                    return prefab;
                }
            }

            return Resources.Load<GameObject>(NetworkedDropResourcePath);
        }

        internal static GameObject CreateFieldVisualInstance(
            ItemDefinition definition,
            IItemFieldPrefabResolver prefabResolver,
            Transform parent)
        {
            if (definition == null)
            {
                return null;
            }

            return CreateFieldVisualInstance(
                definition.Master.ItemId,
                definition.Master.PrefabPath,
                prefabResolver,
                parent);
        }

        internal static GameObject CreateFieldVisualInstance(
            string itemId,
            string prefabPath,
            IItemFieldPrefabResolver prefabResolver,
            Transform parent)
        {
            if (parent == null || string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            var pooled = ItemFieldVisualPool.Acquire(itemId, parent);
            if (pooled != null)
            {
                ApplyVisualRuntimeSetup(pooled, itemId);
                return pooled;
            }

            GameObject instance = null;
            var prefab = prefabResolver?.Resolve(prefabPath);
            if (prefab != null)
            {
                instance = Object.Instantiate(prefab, parent);
                instance.name = VisualRootName;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }
            else
            {
                instance = CreateFallbackVisual(itemId, parent);
                Debug.LogWarning(
                    $"[ItemFieldDropFactory] Missing item prefab: {prefabPath}. Using fallback visual for {itemId}.",
                    parent);
            }

            PrepareVisualInstance(instance, itemId);
            ItemFieldVisualPool.RegisterNew(itemId, instance);
            ApplyVisualRuntimeSetup(instance, itemId);
            return instance;
        }

        internal static void ApplyFieldDropRuntimeSetup(GameObject root, string itemId)
        {
            if (root == null || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            var visualRoot = ResolveVisualRoot(root.transform);
            ApplyVisualRuntimeSetup(visualRoot != null ? visualRoot.gameObject : null, itemId);

            EnsureCollider(root, visualRoot, itemId);
            EnsureDynamicRigidbody(root, itemId);
        }

        internal static void ReleaseFieldVisual(GameObject root, string itemId)
        {
            if (root == null || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            var visualRoot = string.Equals(root.name, VisualRootName, System.StringComparison.Ordinal)
                ? root.transform
                : root.transform.Find(VisualRootName);
            if (visualRoot == null)
            {
                return;
            }

            ItemFieldVisualPool.Release(itemId, visualRoot.gameObject);
        }

        private static GameObject CreateFallbackVisual(string itemId, Transform parent)
        {
            var fallback = GameObject.CreatePrimitive(
                string.Equals(itemId, ItemIds.BlackholeBomb, System.StringComparison.Ordinal)
                    ? PrimitiveType.Sphere
                    : PrimitiveType.Cube);
            fallback.name = VisualRootName;
            fallback.transform.SetParent(parent, false);
            fallback.transform.localPosition = Vector3.zero;
            fallback.transform.localRotation = Quaternion.identity;
            fallback.transform.localScale = Vector3.one * 0.45f;
            return fallback;
        }

        private static void PrepareVisualInstance(GameObject instance, string itemId)
        {
            if (instance == null)
            {
                return;
            }

            StripNetworkComponents(instance);
            DisableVisualPhysics(instance);

            if (string.Equals(itemId, ItemIds.BlackholeBomb, System.StringComparison.Ordinal))
            {
                ConfigureBlackholeVisual(instance, true);
            }
        }

        private static void ApplyVisualRuntimeSetup(GameObject visualRoot, string itemId)
        {
            if (visualRoot == null || string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            if (string.Equals(itemId, ItemIds.BlackholeBomb, System.StringComparison.Ordinal))
            {
                ConfigureBlackholeVisual(visualRoot, true);
                return;
            }

            ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(visualRoot);

            if (string.Equals(itemId, ItemIds.WaterMelonSword, System.StringComparison.Ordinal))
            {
                ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(visualRoot, true);
            }
        }

        private static void StripNetworkComponents(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            // NetworkedItemFieldDrop은 [RequireComponent(typeof(NetworkTransform))]를 선언하므로
            // NetworkTransform보다 먼저 즉시 제거해야 의존성 에러가 발생하지 않는다.
            var networkedDrops = root.GetComponentsInChildren<NetworkedItemFieldDrop>(true);
            for (var i = 0; i < networkedDrops.Length; i++)
            {
                if (networkedDrops[i] != null)
                {
                    Object.DestroyImmediate(networkedDrops[i]);
                }
            }

            var itemDrops = root.GetComponentsInChildren<ItemFieldDrop>(true);
            for (var i = 0; i < itemDrops.Length; i++)
            {
                if (itemDrops[i] != null)
                {
                    Object.Destroy(itemDrops[i]);
                }
            }

            var networkTransforms = root.GetComponentsInChildren<NetworkTransform>(true);
            for (var i = 0; i < networkTransforms.Length; i++)
            {
                if (networkTransforms[i] != null)
                {
                    Object.Destroy(networkTransforms[i]);
                }
            }

            var networkObjects = root.GetComponentsInChildren<NetworkObject>(true);
            for (var i = 0; i < networkObjects.Length; i++)
            {
                if (networkObjects[i] != null)
                {
                    Object.Destroy(networkObjects[i]);
                }
            }

            var networkBehaviours = root.GetComponentsInChildren<NetworkBehaviour>(true);
            for (var i = 0; i < networkBehaviours.Length; i++)
            {
                var behaviour = networkBehaviours[i];
                if (behaviour != null &&
                    !(behaviour is NetworkedItemFieldDrop) &&
                    !(behaviour is NetworkTransform))
                {
                    Object.Destroy(behaviour);
                }
            }
        }

        private static void DisableVisualPhysics(GameObject root)
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

            var bodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.useGravity = false;
            }
        }

        private static void ConfigureBlackholeVisual(GameObject visualRoot, bool fieldDropContext)
        {
            if (visualRoot == null)
            {
                return;
            }

            var authoring = visualRoot.GetComponent<ItemBlackholeVisualAuthoring>();
            if (authoring != null)
            {
                authoring.RefreshVisual();
            }

            var effect = visualRoot.transform.Find("Item_BlackholeFx");
            if (effect != null && fieldDropContext)
            {
                effect.localRotation = Quaternion.Euler(-45f, 0f, 0f);
            }
        }

        private static Transform ResolveVisualRoot(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            var visual = root.Find(VisualRootName);
            return visual != null ? visual : root;
        }

        private static void EnsureCollider(GameObject root, Transform visualRoot, string itemId)
        {
            if (root == null)
            {
                return;
            }

            var sphereCollider = root.GetComponent<SphereCollider>();
            if (sphereCollider == null)
            {
                sphereCollider = root.AddComponent<SphereCollider>();
            }

            ConfigureColliderMaterial(sphereCollider, itemId);

            if (string.Equals(itemId, ItemIds.Growth, System.StringComparison.Ordinal))
            {
                sphereCollider.center = Vector3.zero;
                sphereCollider.radius = GrowthFieldColliderRadius;
                return;
            }

            if (string.Equals(itemId, ItemIds.Shrink, System.StringComparison.Ordinal))
            {
                sphereCollider.center = Vector3.zero;
                sphereCollider.radius = ShrinkFieldColliderRadius;
                return;
            }

            if (!TryGetOrCreateColliderProfile(itemId, visualRoot, out var profile))
            {
                sphereCollider.center = Vector3.zero;
                sphereCollider.radius = DefaultFieldColliderRadius;
                return;
            }

            sphereCollider.center = profile.Center;
            sphereCollider.radius = profile.Radius;
        }

        private static bool TryCalculateWorldBounds(Transform visualRoot, out Bounds bounds)
        {
            bounds = default;
            if (visualRoot == null)
            {
                return false;
            }

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return hasBounds;
        }

        private static void EnsureDynamicRigidbody(GameObject target, string itemId)
        {
            if (target == null)
            {
                return;
            }

            var body = target.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = target.AddComponent<Rigidbody>();
            }

            body.mass = 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.useGravity = true;
            body.isKinematic = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            if (IsConsumableItem(itemId))
            {
                body.drag = ConsumableFieldDrag;
                body.angularDrag = ConsumableFieldAngularDrag;
                body.maxAngularVelocity = 1f;
                body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }
            else
            {
                body.drag = EquipmentFieldDrag;
                body.angularDrag = EquipmentFieldAngularDrag;
                body.maxAngularVelocity = 7f;
                body.constraints = RigidbodyConstraints.None;

                if (RequiresSpecialFieldFriction(itemId))
                {
                    body.drag = 0.55f;
                    body.angularDrag = 2.5f;
                }
            }

            body.WakeUp();
        }

        private static void ConfigureColliderMaterial(SphereCollider sphereCollider, string itemId)
        {
            if (sphereCollider == null)
            {
                return;
            }

            sphereCollider.sharedMaterial = RequiresSpecialFieldFriction(itemId)
                ? GetOrCreateSpecialFieldMaterial()
                : null;
        }

        private static bool RequiresSpecialFieldFriction(string itemId)
        {
            return string.Equals(itemId, ItemIds.BlackholeBomb, System.StringComparison.Ordinal) ||
                   string.Equals(itemId, ItemIds.SatelliteStrike, System.StringComparison.Ordinal);
        }

        private static PhysicMaterial GetOrCreateSpecialFieldMaterial()
        {
            if (s_specialEquipmentFieldMaterial != null)
            {
                return s_specialEquipmentFieldMaterial;
            }

            s_specialEquipmentFieldMaterial = new PhysicMaterial("ItemFieldDrop_SpecialEquipment")
            {
                dynamicFriction = SpecialFieldFriction,
                staticFriction = SpecialFieldFriction,
                frictionCombine = PhysicMaterialCombine.Maximum,
                bounciness = SpecialFieldBounciness,
                bounceCombine = PhysicMaterialCombine.Minimum
            };
            return s_specialEquipmentFieldMaterial;
        }

        private static bool IsConsumableItem(string itemId)
        {
            return string.Equals(itemId, ItemIds.Growth, System.StringComparison.Ordinal) ||
                   string.Equals(itemId, ItemIds.Shrink, System.StringComparison.Ordinal) ||
                   string.Equals(itemId, ItemIds.Invisibility, System.StringComparison.Ordinal) ||
                   string.Equals(itemId, ItemIds.Americano, System.StringComparison.Ordinal);
        }

        private static bool TryGetOrCreateColliderProfile(string itemId, Transform visualRoot, out SphereColliderProfile profile)
        {
            if (!string.IsNullOrWhiteSpace(itemId) && s_colliderProfiles.TryGetValue(itemId, out profile))
            {
                return true;
            }

            if (!TryCalculateWorldBounds(visualRoot, out var bounds))
            {
                profile = default;
                return false;
            }

            var extents = bounds.extents;
            profile = new SphereColliderProfile
            {
                Center = visualRoot != null && visualRoot.parent != null
                    ? visualRoot.parent.InverseTransformPoint(bounds.center)
                    : bounds.center,
                Radius = Mathf.Max(
                    DefaultFieldColliderRadius,
                    Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)))
            };

            if (!string.IsNullOrWhiteSpace(itemId))
            {
                s_colliderProfiles[itemId] = profile;
            }

            return true;
        }

        private struct SphereColliderProfile
        {
            public Vector3 Center;
            public float Radius;
        }
    }
}
