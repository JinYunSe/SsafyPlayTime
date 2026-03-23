using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public enum NetworkedItemEffectKind : byte
    {
        None = 0,
        Blackhole = 1,
        SatelliteProjectile = 2,
        SatelliteCharge = 3,
        SatelliteBeam = 4,
        Flamethrower = 5
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NetworkTransform))]
    public sealed class NetworkedItemEffectProxy : NetworkBehaviour
    {
        private static readonly Quaternion BlackholeVisualLocalRotation = Quaternion.identity;
        private static readonly Quaternion SatelliteVisualLocalRotation = Quaternion.Euler(-90f, 0f, 0f);

        private const string BlackholeVisualAssetPath = "Assets/_Project/Prefabs/Items/BlackholeBomb.prefab";
        private const string BlackholeVisualResourcePath = "_Project/Prefabs/Items/BlackholeBomb";
        private const string FlamethrowerEffectAssetPath = "Assets/Polygon Arsenal/Prefabs/Misc/FlamethrowerBlocky.prefab";
        private const string FlamethrowerEffectResourcePath = "Polygon Arsenal/Prefabs/Misc/FlamethrowerBlocky";
        private const string SatelliteProjectileAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Combat/Missiles/Sci-Fi/Antimatter/AntimatterMissileBlue.prefab";
        private const string SatelliteProjectileResourcePath =
            "Polygon Arsenal/Prefabs/Combat/Missiles/Sci-Fi/Antimatter/AntimatterMissileBlue";
        private const string SatelliteChargeupAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Chargeup/BeamupChargeupBlue.prefab";
        private const string SatelliteChargeupResourcePath =
            "Polygon Arsenal/Prefabs/Interactive/BeamUp/Chargeup/BeamupChargeupBlue";
        private const string SatelliteCloudAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Cloud/BeamupCloudBlue.prefab";
        private const string SatelliteCloudResourcePath =
            "Polygon Arsenal/Prefabs/Interactive/BeamUp/Cloud/BeamupCloudBlue";
        private const string SatelliteCylinderAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Cylinder/BeamupCylinderBlue.prefab";
        private const string SatelliteCylinderResourcePath =
            "Polygon Arsenal/Prefabs/Interactive/BeamUp/Cylinder/BeamupCylinderBlue";

        [SerializeField] private bool enableDebugLog;

        [Networked] public NetworkedItemEffectKind EffectKind { get; set; }
        [Networked] public float Radius { get; set; }
        [Networked] public float Range { get; set; }
        [Networked] public NetworkBool Activated { get; set; }
        [Networked] public Vector3 Forward { get; set; }

        private readonly DefaultItemFieldPrefabResolver _resolver = new();
        private GameObject _visualRoot;
        private Rigidbody _rigidbody;
        private Collider _collider;
        private NetworkTransform _networkTransform;
        private bool _visualInitialized;
        private bool _lastActivatedState;

        public override void Spawned()
        {
            CachePhysics();
            InitializeNetworkTransformState();
            ApplyAuthorityPhysicsState();
            TryEnsureVisual();
        }

        public override void FixedUpdateNetwork()
        {
            CachePhysics();
            ApplyAuthorityPhysicsState();
        }

        public override void Render()
        {
            TryEnsureVisual();
            RefreshVisualState();
        }

        public void InitializeBlackhole(float radius, Vector3 forward)
        {
            EffectKind = NetworkedItemEffectKind.Blackhole;
            Radius = radius;
            Forward = forward;
            Activated = false;
        }

        public void InitializeSatelliteProjectile(float radius, Vector3 forward)
        {
            EffectKind = NetworkedItemEffectKind.SatelliteProjectile;
            Radius = radius;
            Forward = forward;
        }

        public void InitializeSatelliteCharge(float radius)
        {
            EffectKind = NetworkedItemEffectKind.SatelliteCharge;
            Radius = radius;
        }

        public void InitializeSatelliteBeam(float radius)
        {
            EffectKind = NetworkedItemEffectKind.SatelliteBeam;
            Radius = radius;
        }

        public void InitializeFlamethrower(float range, float radius, Vector3 forward)
        {
            EffectKind = NetworkedItemEffectKind.Flamethrower;
            Range = range;
            Radius = radius;
            Forward = forward;
        }

        public void SetActivated(bool activated)
        {
            Activated = activated;
            if (_lastActivatedState != activated)
            {
                _lastActivatedState = activated;
                if (activated && _visualRoot != null)
                {
                    PlayAllParticles(_visualRoot);
                }
            }
        }

        public void SyncNetworkPose(
            Vector3 position,
            Quaternion rotation,
            bool zeroVelocity = false,
            bool teleportNetwork = true)
        {
            CachePhysics();

            transform.SetPositionAndRotation(position, rotation);

            if (_rigidbody != null)
            {
                _rigidbody.position = position;
                _rigidbody.rotation = rotation;

                if (zeroVelocity && !_rigidbody.isKinematic)
                {
                    _rigidbody.velocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
                }
            }

            if (HasStateAuthority && _networkTransform != null && teleportNetwork)
            {
                _networkTransform.Teleport(position, rotation);
            }
        }

        private void CachePhysics()
        {
            if (_rigidbody == null)
            {
                _rigidbody = GetComponent<Rigidbody>();
            }

            if (_collider == null)
            {
                _collider = GetComponent<Collider>();
            }

            if (_networkTransform == null)
            {
                _networkTransform = GetComponent<NetworkTransform>();
            }
        }

        private void InitializeNetworkTransformState()
        {
            if (!HasStateAuthority || _networkTransform == null)
            {
                return;
            }

            if (_rigidbody != null)
            {
                _rigidbody.position = transform.position;
                _rigidbody.rotation = transform.rotation;
                if (!_rigidbody.isKinematic)
                {
                    _rigidbody.velocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
                }
            }

            _networkTransform.Teleport(transform.position, transform.rotation);
        }

        private void ApplyAuthorityPhysicsState()
        {
            if (_rigidbody == null)
            {
                return;
            }

            var isAuthorityBlackholeProjectile =
                EffectKind == NetworkedItemEffectKind.Blackhole &&
                !Activated &&
                HasStateAuthority;

            if (isAuthorityBlackholeProjectile)
            {
                if (_collider != null && !_collider.enabled)
                {
                    _collider.enabled = true;
                }

                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;
                _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                return;
            }

            if (_collider != null && _collider.enabled)
            {
                _collider.enabled = false;
            }

            if (!_rigidbody.isKinematic)
            {
                _rigidbody.velocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }

        private void TryEnsureVisual()
        {
            if (_visualInitialized || EffectKind == NetworkedItemEffectKind.None)
            {
                return;
            }

            _visualRoot = BuildVisual();
            _visualInitialized = _visualRoot != null;
            RefreshVisualState();
        }

        private void RefreshVisualState()
        {
            if (_visualRoot == null)
            {
                return;
            }

            switch (EffectKind)
            {
                case NetworkedItemEffectKind.Blackhole:
                    RefreshBlackholeVisualState();
                    break;
                case NetworkedItemEffectKind.SatelliteProjectile:
                    _visualRoot.transform.localScale = Vector3.one * Mathf.Max(0.001f, Radius * 0.08f);
                    break;
                case NetworkedItemEffectKind.SatelliteCharge:
                    _visualRoot.transform.localScale = Vector3.one * Mathf.Max(1f, Radius * 0.35f);
                    break;
                case NetworkedItemEffectKind.SatelliteBeam:
                    _visualRoot.transform.localScale = Vector3.one * Mathf.Max(1f, Radius * 0.3f);
                    break;
                case NetworkedItemEffectKind.Flamethrower:
                    _visualRoot.transform.localScale = Vector3.one * 2f;
                    _visualRoot.transform.localRotation = Quaternion.identity;
                    break;
            }
        }

        private void RefreshBlackholeVisualState()
        {
            var targetScale = Activated
                ? Mathf.Clamp(Radius * 0.3f, 0.9f, 6f)
                : 0.08f;
            _visualRoot.transform.localScale = Vector3.one * targetScale;

            if (_lastActivatedState != Activated)
            {
                _lastActivatedState = Activated;
                if (Activated)
                {
                    PlayAllParticles(_visualRoot);
                }
            }

            var renderer = _visualRoot.GetComponent<Renderer>();
            if (renderer != null)
            {
                var color = Activated
                    ? new Color(0.07f, 0.07f, 0.08f, 0f)
                    : new Color(0.07f, 0.07f, 0.08f, 0.025f);
                ApplyTransparentColor(renderer, color);
            }
        }

        private GameObject BuildVisual()
        {
            switch (EffectKind)
            {
                case NetworkedItemEffectKind.Blackhole:
                    return InstantiateVisual(TryLoadBlackholeVisual(), "BlackholeVisual");
                case NetworkedItemEffectKind.SatelliteProjectile:
                    return InstantiateVisual(TryLoadAsset(SatelliteProjectileAssetPath, SatelliteProjectileResourcePath), "SatelliteProjectileVisual");
                case NetworkedItemEffectKind.SatelliteCharge:
                    return BuildSatelliteChargeVisual();
                case NetworkedItemEffectKind.SatelliteBeam:
                    return BuildSatelliteBeamVisual();
                case NetworkedItemEffectKind.Flamethrower:
                    return InstantiateVisual(TryLoadAsset(FlamethrowerEffectAssetPath, FlamethrowerEffectResourcePath), "FlamethrowerVisual");
                default:
                    return null;
            }
        }

        private GameObject BuildSatelliteChargeVisual()
        {
            var root = new GameObject("SatelliteChargeVisual");
            root.transform.SetParent(transform, false);
            InstantiateVisual(TryLoadAsset(SatelliteChargeupAssetPath, SatelliteChargeupResourcePath), "Chargeup", root.transform);
            InstantiateVisual(TryLoadAsset(SatelliteCloudAssetPath, SatelliteCloudResourcePath), "Cloud", root.transform);
            return root;
        }

        private GameObject BuildSatelliteBeamVisual()
        {
            var root = new GameObject("SatelliteBeamVisual");
            root.transform.SetParent(transform, false);
            InstantiateVisual(TryLoadAsset(SatelliteCloudAssetPath, SatelliteCloudResourcePath), "Cloud", root.transform);
            InstantiateVisual(TryLoadAsset(SatelliteCylinderAssetPath, SatelliteCylinderResourcePath), "Beam", root.transform);
            return root;
        }

        private GameObject InstantiateVisual(GameObject prefab, string name, Transform parentOverride = null)
        {
            if (prefab == null)
            {
                return null;
            }

            var parent = parentOverride != null ? parentOverride : transform;
            var instance = Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = ResolveVisualLocalRotation();
            instance.transform.localScale = Vector3.one;
            PrepareVisualInstance(instance);
            RefreshBlackholePrefabVisual(instance);
            return instance;
        }

        private Quaternion ResolveVisualLocalRotation()
        {
            switch (EffectKind)
            {
                case NetworkedItemEffectKind.Blackhole:
                    return BlackholeVisualLocalRotation;
                case NetworkedItemEffectKind.SatelliteProjectile:
                case NetworkedItemEffectKind.SatelliteCharge:
                case NetworkedItemEffectKind.SatelliteBeam:
                    return SatelliteVisualLocalRotation;
                default:
                    return Quaternion.identity;
            }
        }

        private void RefreshBlackholePrefabVisual(GameObject instance)
        {
            if (EffectKind != NetworkedItemEffectKind.Blackhole || instance == null)
            {
                return;
            }

            var authoring = instance.GetComponent<ItemBlackholeVisualAuthoring>();
            if (authoring != null)
            {
                authoring.RefreshVisual();
            }
        }

        private void PrepareVisualInstance(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            // NetworkedItemFieldDrop은 [RequireComponent(typeof(NetworkTransform))]를 선언하므로
            // NetworkTransform보다 먼저 즉시 제거해야 의존성 에러가 발생하지 않는다.
            var networkedDrop = instance.GetComponent<NetworkedItemFieldDrop>();
            if (networkedDrop != null)
            {
                DestroyImmediate(networkedDrop);
            }

            var itemDrop = instance.GetComponent<ItemFieldDrop>();
            if (itemDrop != null)
            {
                Destroy(itemDrop);
            }

            var networkTransform = instance.GetComponent<NetworkTransform>();
            if (networkTransform != null)
            {
                Destroy(networkTransform);
            }

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null)
            {
                Destroy(networkObject);
            }

            var behaviours = instance.GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }

            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            var rigidbodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].useGravity = false;
            }

            if (EffectKind != NetworkedItemEffectKind.Blackhole)
            {
                ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(instance);
            }
            PlayAllParticles(instance);
        }

        private static void PlayAllParticles(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                if (particle == null)
                {
                    continue;
                }

                particle.Play(true);
            }
        }

        private static void ApplyTransparentColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            var material = renderer.material;
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
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

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private GameObject TryLoadBlackholeVisual()
        {
            return TryLoadAsset(BlackholeVisualAssetPath, BlackholeVisualResourcePath);
        }

        private GameObject TryLoadAsset(string assetPath, string resourcePath)
        {
            var prefab = _resolver.Resolve(assetPath);
            if (prefab != null)
            {
                return prefab;
            }

            return Resources.Load<GameObject>(resourcePath);
        }
    }
}
