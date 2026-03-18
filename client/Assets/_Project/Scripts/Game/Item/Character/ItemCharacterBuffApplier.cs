/*
 * 파일 개요:
 * - ItemCharacterBuffApplier 스크립트가 들어 있는 파일이다.
 * - Consumable 아이템의 실제 적용 결과를 캐릭터 시각, 콜라이더, 물리 보정, 지속 이펙트에 반영한다.
 * - 기존 프로토타입 버프 처리와 분리해, 최종 스냅샷 중심으로 적용/동기화되도록 새로 구성했다.
 */
using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// Consumable 버프를 캐릭터에 실제로 적용한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemCharacterBuffApplier : MonoBehaviour
    {
        private const string ShieldPrefabPath = "Assets/Polygon Arsenal/Prefabs/Combat/Shield/ShieldYellow.prefab";
        private const string InvisibilityDustPrefabPath = "Assets/Polygon Arsenal/Prefabs/Environment/Dust/DustCalm.prefab";
        private const string ShieldObjectName = "ConsumableShieldEffect";
        private const string ShieldFlatRingObjectName = "FlatRing";
        private const string ShieldStartObjectName = "ShieldStart";
        private const string ShieldSparksObjectName = "Sparks";
        private const string DustObjectName = "ConsumableDustEffect";
        private const float MinimumShieldScaleAxis = 2f;

        [Serializable]
        private struct ColliderSnapshot
        {
            public Collider collider;
            public Vector3 center;
            public Vector3 size;
            public float radius;
            public float height;
        }

        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform visualRoot;

        [Header("투명화")]
        [SerializeField] private float invisibilityAlpha = 0.35f;

        [Header("지속 이펙트")]
        [SerializeField] private Vector3 shieldLocalOffset = new(0f, 1.05f, 0f);
        [SerializeField] private Vector3 shieldScale = Vector3.one * 2f;
        [SerializeField] private Vector3 dustLocalOffset = new(0f, 0.05f, 0f);
        [SerializeField] private Vector3 dustScale = Vector3.one * 0.45f;

        [Header("디버그")]
        [SerializeField] private bool enableDebugLog;

        private readonly ItemFieldCatalogProvider _catalogProvider = new();
        private readonly DefaultItemFieldPrefabResolver _prefabResolver = new();
        private readonly List<ColliderSnapshot> _cachedColliders = new();

        private NetworkPlayer _networkPlayer;
        private NetworkObject _networkObject;
        private Rigidbody _rootRigidbody;

        private GameObject _shieldEffectInstance;
        private GameObject _dustEffectInstance;
        private Transform _animatedVisualRoot;
        private Vector3 _baseVisualScale = Vector3.one;
        private float _baseDrag;
        private float _baseAngularDrag;
        private ItemBuffSnapshot _localSnapshot = ItemBuffSnapshot.Default;
        private ItemBuffSnapshot _appliedSnapshot = ItemBuffSnapshot.Default;

        public float CurrentScaleMultiplier { get; private set; } = 1f;
        public float CurrentMoveSpeedMultiplier { get; private set; } = 1f;
        public float CurrentKnockbackResistMultiplier { get; private set; } = 1f;
        public float CurrentGravityMultiplier { get; private set; } = 1f;
        public float CurrentJumpMultiplier { get; private set; } = 1f;
        public float CurrentOutgoingStunDamageMultiplier { get; private set; } = 1f;
        public bool IsSuperArmorActive { get; private set; }
        public bool IsInvisibilityActive { get; private set; }

        public event Action BuffApplied;

        private void Awake()
        {
            ResolveReferences();
            CacheBaseState();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CacheBaseState();
            BindEvents();
            SynchronizeFromNetworkSnapshot(forceApply: true);
        }

        private void OnDisable()
        {
            UnbindEvents();
            PublishSnapshot(ItemBuffSnapshot.Default);
            ApplySnapshot(ItemBuffSnapshot.Default, forceApply: true);
            DestroySupportEffects();
        }

        private void Update()
        {
            SynchronizeFromNetworkSnapshot(forceApply: false);
            RefreshSupportEffectTransforms();
        }

        private void FixedUpdate()
        {
            if (!CanApplyAuthorityPhysics())
            {
                return;
            }

            ApplyRuntimeGravityModifier();
        }

        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            if (itemRuntimeHost == runtimeHost)
            {
                return;
            }

            UnbindEvents();
            itemRuntimeHost = runtimeHost;
            ResolveReferences();
            BindEvents();
            SynchronizeFromNetworkSnapshot(forceApply: true);
        }

        public void SetCharacterRoot(Transform root)
        {
            characterRoot = root;
            ResolveReferences();
            CacheBaseState();
            SynchronizeFromNetworkSnapshot(forceApply: true);
        }

        private void BindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BuffStateChanged -= HandleBuffStateChanged;
            itemRuntimeHost.BuffStateChanged += HandleBuffStateChanged;
        }

        private void UnbindEvents()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BuffStateChanged -= HandleBuffStateChanged;
        }

        private void HandleBuffStateChanged(ItemBuffMask activeBuffMask, ItemBuffRuntimeState buffState)
        {
            _localSnapshot = BuildSnapshot(activeBuffMask);
            PublishSnapshot(_localSnapshot);
            ApplySnapshot(_localSnapshot, forceApply: true);
            ItemRuntimeLog.Info("Consumable", $"버프 적용 요청: mask={activeBuffMask}, growth={buffState.GrowthRemainSec:0.0}, shrink={buffState.ShrinkRemainSec:0.0}, superArmor={buffState.SuperArmorRemainSec:0.0}, invis={buffState.InvisibilityRemainSec:0.0}", this);
            DebugLog(
                $"Consumable snapshot updated: mask={activeBuffMask}, growth={buffState.GrowthRemainSec:0.0}, shrink={buffState.ShrinkRemainSec:0.0}, superArmor={buffState.SuperArmorRemainSec:0.0}, invis={buffState.InvisibilityRemainSec:0.0}");
        }

        private ItemBuffSnapshot BuildSnapshot(ItemBuffMask activeBuffMask)
        {
            var scaleMultiplier = 1f;
            var moveSpeedMultiplier = 1f;
            var gravityMultiplier = 1f;
            var jumpMultiplier = 1f;
            var knockbackResistMultiplier = 1f;
            var outgoingStunMultiplier = 1f;

            if ((activeBuffMask & ItemBuffMask.Growth) != 0 && TryGetItemDefinition(ItemIds.Growth, out var growthDefinition))
            {
                scaleMultiplier *= Mathf.Max(0.05f, growthDefinition.Master.ScaleMultiplier);
                moveSpeedMultiplier *= Mathf.Max(0.05f, growthDefinition.Master.MoveSpeedMultiplier);
                gravityMultiplier *= Mathf.Max(0.05f, growthDefinition.Master.GravityMultiplier);
                jumpMultiplier *= Mathf.Max(0.05f, growthDefinition.Master.JumpMultiplier);
                knockbackResistMultiplier *= Mathf.Max(0.05f, growthDefinition.Master.KnockbackResistMultiplier);
                outgoingStunMultiplier *= Mathf.Max(1f, growthDefinition.Master.StunDamage);
            }

            if ((activeBuffMask & ItemBuffMask.Shrink) != 0 && TryGetItemDefinition(ItemIds.Shrink, out var shrinkDefinition))
            {
                scaleMultiplier *= Mathf.Max(0.05f, shrinkDefinition.Master.ScaleMultiplier);
                moveSpeedMultiplier *= Mathf.Max(0.05f, shrinkDefinition.Master.MoveSpeedMultiplier);
                gravityMultiplier *= Mathf.Max(0.05f, shrinkDefinition.Master.GravityMultiplier);
                jumpMultiplier *= Mathf.Max(0.05f, shrinkDefinition.Master.JumpMultiplier);
                knockbackResistMultiplier *= Mathf.Max(0.05f, shrinkDefinition.Master.KnockbackResistMultiplier);
            }

            var isSuperArmorActive = (activeBuffMask & ItemBuffMask.SuperArmor) != 0;
            var isInvisibilityActive = (activeBuffMask & ItemBuffMask.Invisibility) != 0;

            return new ItemBuffSnapshot(
                activeBuffMask,
                scaleMultiplier,
                moveSpeedMultiplier,
                gravityMultiplier,
                jumpMultiplier,
                knockbackResistMultiplier,
                outgoingStunMultiplier,
                isSuperArmorActive,
                isInvisibilityActive);
        }

        private void PublishSnapshot(ItemBuffSnapshot snapshot)
        {
            if (_networkPlayer == null)
            {
                return;
            }

            _networkPlayer.SetItemBuffSnapshot(snapshot);
        }

        private void SynchronizeFromNetworkSnapshot(bool forceApply)
        {
            if (_networkPlayer == null || _networkPlayer.CanWriteItemBuffSnapshot())
            {
                if (forceApply)
                {
                    ApplySnapshot(_localSnapshot, forceApply: true);
                }

                return;
            }

            var snapshot = _networkPlayer.GetItemBuffSnapshot();
            ApplySnapshot(snapshot, forceApply);
        }

        private void ApplySnapshot(ItemBuffSnapshot snapshot, bool forceApply)
        {
            if (!forceApply && _appliedSnapshot.ApproximatelyEquals(snapshot))
            {
                return;
            }

            _appliedSnapshot = snapshot;
            CurrentScaleMultiplier = snapshot.ScaleMultiplier;
            CurrentMoveSpeedMultiplier = snapshot.MoveSpeedMultiplier;
            CurrentGravityMultiplier = snapshot.GravityMultiplier;
            CurrentJumpMultiplier = snapshot.JumpMultiplier;
            CurrentKnockbackResistMultiplier = snapshot.KnockbackResistMultiplier;
            CurrentOutgoingStunDamageMultiplier = snapshot.OutgoingStunMultiplier;
            IsSuperArmorActive = snapshot.IsSuperArmorActive;
            IsInvisibilityActive = snapshot.IsInvisibilityActive;

            ApplyVisualScale(snapshot.ScaleMultiplier);
            ApplyColliderScale(snapshot.ScaleMultiplier);
            ApplyRootDrag(snapshot.KnockbackResistMultiplier);
            ApplyRendererVisibility();
            UpdateShieldEffect(snapshot);
            UpdateDustEffect(snapshot);
            BuffApplied?.Invoke();
        }

        private void ApplyRuntimeGravityModifier()
        {
            if (_rootRigidbody == null)
            {
                return;
            }

            if (Mathf.Approximately(CurrentGravityMultiplier, 1f))
            {
                return;
            }

            var gravityDelta = Physics.gravity * (CurrentGravityMultiplier - 1f);
            _rootRigidbody.AddForce(gravityDelta, ForceMode.Acceleration);
        }

        private void ApplyVisualScale(float scaleMultiplier)
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.localScale = _baseVisualScale * scaleMultiplier;
        }

        private void ApplyColliderScale(float scaleMultiplier)
        {
            for (var i = 0; i < _cachedColliders.Count; i++)
            {
                var snapshot = _cachedColliders[i];
                var collider = snapshot.collider;
                if (collider == null)
                {
                    continue;
                }

                if (collider is BoxCollider boxCollider)
                {
                    boxCollider.center = snapshot.center * scaleMultiplier;
                    boxCollider.size = snapshot.size * scaleMultiplier;
                    continue;
                }

                if (collider is SphereCollider sphereCollider)
                {
                    sphereCollider.center = snapshot.center * scaleMultiplier;
                    sphereCollider.radius = snapshot.radius * scaleMultiplier;
                    continue;
                }

                if (collider is CapsuleCollider capsuleCollider)
                {
                    capsuleCollider.center = snapshot.center * scaleMultiplier;
                    capsuleCollider.radius = snapshot.radius * scaleMultiplier;
                    capsuleCollider.height = snapshot.height * scaleMultiplier;
                }
            }
        }

        private void ApplyRootDrag(float knockbackResistMultiplier)
        {
            if (_rootRigidbody == null)
            {
                return;
            }

            if (!CanApplyAuthorityPhysics())
            {
                _rootRigidbody.drag = _baseDrag;
                _rootRigidbody.angularDrag = _baseAngularDrag;
                return;
            }

            var resistStrength = Mathf.Max(0f, (1f / Mathf.Max(0.05f, knockbackResistMultiplier)) - 1f);
            _rootRigidbody.drag = _baseDrag + resistStrength * 1.5f;
            _rootRigidbody.angularDrag = _baseAngularDrag + resistStrength * 0.75f;
        }

        private void ApplyRendererVisibility()
        {
            if (visualRoot == null)
            {
                return;
            }

            var hideForRemote = IsInvisibilityActive && !ShouldShowLocalTranslucentCharacter();
            var targetAlpha = IsInvisibilityActive && !hideForRemote ? Mathf.Clamp01(invisibilityAlpha) : 1f;
            var renderers = visualRoot.GetComponentsInChildren<Renderer>(true);

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!IsUnderAnimatedVisualRoot(renderer.transform))
                {
                    continue;
                }

                renderer.enabled = !hideForRemote;
                renderer.shadowCastingMode = IsInvisibilityActive
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : UnityEngine.Rendering.ShadowCastingMode.On;

                if (!hideForRemote)
                {
                    ApplyRendererAlpha(renderer, targetAlpha);
                }
            }
        }

        private bool ShouldShowLocalTranslucentCharacter()
        {
            if (!IsInvisibilityActive)
            {
                return false;
            }

            if (_networkPlayer == null)
            {
                return true;
            }

            if (_networkPlayer.Runner == null || _networkPlayer.Object == null || !_networkPlayer.Object.IsValid)
            {
                return true;
            }

            // 한국어: 투명화 본인 반투명 표시는 입력 권한을 가진 로컬 플레이어에게만 허용한다.
            return _networkPlayer.HasInputAuthority ||
                   (_networkObject != null && _networkObject.HasInputAuthority);
        }

        private static void ApplyRendererAlpha(Renderer renderer, float alpha)
        {
            var materials = renderer.materials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty("_BaseColor"))
                {
                    var color = material.GetColor("_BaseColor");
                    color.a = alpha;
                    material.SetColor("_BaseColor", color);
                }

                if (material.HasProperty("_Color"))
                {
                    var color = material.GetColor("_Color");
                    color.a = alpha;
                    material.SetColor("_Color", color);
                }

                if (alpha < 0.99f)
                {
                    if (material.HasProperty("_Surface"))
                    {
                        material.SetFloat("_Surface", 1f);
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

                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    continue;
                }

                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 0f);
                }
                if (material.HasProperty("_SrcBlend"))
                {
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                }
                if (material.HasProperty("_DstBlend"))
                {
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                }
                if (material.HasProperty("_ZWrite"))
                {
                    material.SetFloat("_ZWrite", 1f);
                }

                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = -1;
            }
        }

        private void UpdateShieldEffect(ItemBuffSnapshot snapshot)
        {
            var shouldShowShield = snapshot.IsSuperArmorActive;
            if (!shouldShowShield)
            {
                DestroyShieldEffect();
                return;
            }

            if (_shieldEffectInstance == null)
            {
                _shieldEffectInstance = CreateEffectInstance(ShieldPrefabPath, ShieldObjectName);
            }

            if (_shieldEffectInstance == null)
            {
                return;
            }

            _shieldEffectInstance.transform.localPosition = shieldLocalOffset;
            _shieldEffectInstance.transform.localRotation = Quaternion.identity;
            _shieldEffectInstance.transform.localScale = Vector3.Scale(ResolveShieldScale(), Vector3.one * snapshot.ScaleMultiplier);
            EnsureShieldEffectVisualState();
        }

        private void UpdateDustEffect(ItemBuffSnapshot snapshot)
        {
            var shouldShowDust = snapshot.IsInvisibilityActive;
            if (!shouldShowDust)
            {
                DestroyDustEffect();
                return;
            }

            if (_dustEffectInstance == null)
            {
                _dustEffectInstance = CreateEffectInstance(InvisibilityDustPrefabPath, DustObjectName);
            }

            if (_dustEffectInstance == null)
            {
                return;
            }

            _dustEffectInstance.transform.localPosition = dustLocalOffset;
            _dustEffectInstance.transform.localRotation = Quaternion.identity;
            _dustEffectInstance.transform.localScale = Vector3.Scale(dustScale, Vector3.one * snapshot.ScaleMultiplier);
        }

        private GameObject CreateEffectInstance(string prefabPath, string objectName)
        {
            if (characterRoot == null)
            {
                return null;
            }

            var prefab = _prefabResolver.Resolve(prefabPath);
            if (prefab == null)
            {
                DebugLog($"Effect prefab missing: {prefabPath}");
                ItemRuntimeLog.Warn(objectName, $"보조 이펙트 프리팹 누락: path={prefabPath}", this);
                return null;
            }

            var instance = Instantiate(prefab, characterRoot);
            instance.name = objectName;
            ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(instance);
            DisableRuntimePhysics(instance);
            ItemRuntimeLog.Info(objectName, $"보조 이펙트 생성: path={prefabPath}", this);
            return instance;
        }

        private Vector3 ResolveShieldScale()
        {
            return new Vector3(
                Mathf.Max(MinimumShieldScaleAxis, shieldScale.x),
                Mathf.Max(MinimumShieldScaleAxis, shieldScale.y),
                Mathf.Max(MinimumShieldScaleAxis, shieldScale.z));
        }

        private void EnsureShieldEffectVisualState()
        {
            if (_shieldEffectInstance == null)
            {
                return;
            }

            var renderers = _shieldEffectInstance.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                ApplyShieldRendererColors(renderer);
            }

            var particleSystems = _shieldEffectInstance.GetComponentsInChildren<ParticleSystem>(true);
            for (var i = 0; i < particleSystems.Length; i++)
            {
                var particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                var particleObject = particleSystem.gameObject;
                if (particleObject != null && !particleObject.activeSelf)
                {
                    particleObject.SetActive(true);
                }

                var main = particleSystem.main;
                if (string.Equals(particleObject.name, ShieldStartObjectName, StringComparison.Ordinal))
                {
                    main.loop = true;
                }

                if (main.loop && !particleSystem.isPlaying)
                {
                    particleSystem.Play(withChildren: true);
                }
            }
        }

        private static void ApplyShieldRendererColors(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var rendererName = renderer.gameObject.name ?? string.Empty;
            var baseColor = string.Equals(rendererName, ShieldSparksObjectName, StringComparison.Ordinal)
                ? new Color(1f, 0.84f, 0.2f, 0.85f)
                : new Color(1f, 0.78f, 0.14f, 0.68f);
            var rimColor = new Color(1f, 0.84f, 0.2f, 0.95f);
            var innerColor = new Color(0.42f, 0.22f, 0.02f, 0.92f);

            var materials = renderer.materials;
            for (var i = 0; i < materials.Length; i++)
            {
                var material = materials[i];
                if (material == null)
                {
                    continue;
                }

                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", baseColor);
                }
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", baseColor);
                }
                if (material.HasProperty("_TintColor"))
                {
                    material.SetColor("_TintColor", baseColor);
                }
                if (material.HasProperty("_RimColor"))
                {
                    material.SetColor("_RimColor", rimColor);
                }
                if (material.HasProperty("_InnerColor"))
                {
                    material.SetColor("_InnerColor", innerColor);
                }
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", rimColor * 0.65f);
                    material.EnableKeyword("_EMISSION");
                }
            }

            renderer.materials = materials;
        }

        private static void DisableRuntimePhysics(GameObject root)
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

            var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < rigidbodies.Length; i++)
            {
                rigidbodies[i].isKinematic = true;
                rigidbodies[i].useGravity = false;
            }
        }

        private void RefreshSupportEffectTransforms()
        {
            if (_shieldEffectInstance != null)
            {
                _shieldEffectInstance.transform.localPosition = shieldLocalOffset;
                _shieldEffectInstance.transform.localRotation = Quaternion.identity;
                EnsureShieldEffectVisualState();
            }

            if (_dustEffectInstance != null)
            {
                _dustEffectInstance.transform.localPosition = dustLocalOffset;
            }
        }

        private void DestroySupportEffects()
        {
            DestroyShieldEffect();
            DestroyDustEffect();
        }

        private void DestroyShieldEffect()
        {
            if (_shieldEffectInstance == null)
            {
                return;
            }

            Destroy(_shieldEffectInstance);
            _shieldEffectInstance = null;
        }

        private void DestroyDustEffect()
        {
            if (_dustEffectInstance == null)
            {
                return;
            }

            Destroy(_dustEffectInstance);
            _dustEffectInstance = null;
        }

        private bool CanApplyAuthorityPhysics()
        {
            if (_networkObject == null)
            {
                return true;
            }

            return _networkObject.HasStateAuthority;
        }

        private bool TryGetItemDefinition(string itemId, out ItemDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            var options = itemRuntimeHost != null
                ? itemRuntimeHost.CatalogLoadOptions
                : ItemCatalogLoader.CreateDefaultOptions();
            if (!_catalogProvider.TryGetCatalog(options, out var catalog, out _))
            {
                return false;
            }

            return catalog.TryGetDefinition(itemId, out definition);
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (characterRoot == null)
            {
                characterRoot = itemRuntimeHost != null && itemRuntimeHost.OwnerTransform != null
                    ? itemRuntimeHost.OwnerTransform
                    : transform;
            }

            if (visualRoot == null)
            {
                var model = characterRoot != null ? characterRoot.Find("Model") : null;
                visualRoot = model != null ? model : characterRoot;
            }

            _networkPlayer = characterRoot != null ? characterRoot.GetComponent<NetworkPlayer>() : null;
            _networkObject = characterRoot != null ? characterRoot.GetComponent<NetworkObject>() : null;
            _rootRigidbody = characterRoot != null ? characterRoot.GetComponent<Rigidbody>() : null;
            _animatedVisualRoot = FindAnimatedVisualRoot();
        }

        private void CacheBaseState()
        {
            _cachedColliders.Clear();

            if (visualRoot != null)
            {
                _baseVisualScale = visualRoot.localScale;
            }

            if (_rootRigidbody != null)
            {
                _baseDrag = _rootRigidbody.drag;
                _baseAngularDrag = _rootRigidbody.angularDrag;
            }

            if (characterRoot == null)
            {
                return;
            }

            var colliders = characterRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null || collider.isTrigger)
                {
                    continue;
                }

                var snapshot = new ColliderSnapshot
                {
                    collider = collider,
                    center = Vector3.zero,
                    size = Vector3.one,
                    radius = 0f,
                    height = 0f
                };

                if (collider is BoxCollider boxCollider)
                {
                    snapshot.center = boxCollider.center;
                    snapshot.size = boxCollider.size;
                    _cachedColliders.Add(snapshot);
                    continue;
                }

                if (collider is SphereCollider sphereCollider)
                {
                    snapshot.center = sphereCollider.center;
                    snapshot.radius = sphereCollider.radius;
                    _cachedColliders.Add(snapshot);
                    continue;
                }

                if (collider is CapsuleCollider capsuleCollider)
                {
                    snapshot.center = capsuleCollider.center;
                    snapshot.radius = capsuleCollider.radius;
                    snapshot.height = capsuleCollider.height;
                    _cachedColliders.Add(snapshot);
                }
            }
        }

        private Transform FindAnimatedVisualRoot()
        {
            if (visualRoot == null)
            {
                return null;
            }

            for (var i = 0; i < visualRoot.childCount; i++)
            {
                var child = visualRoot.GetChild(i);
                if (child.name.Contains("Animated") || child.name == "_AnimationDriver")
                {
                    return child;
                }
            }

            return null;
        }

        private bool IsUnderAnimatedVisualRoot(Transform target)
        {
            if (_animatedVisualRoot == null)
            {
                return true;
            }

            return target == _animatedVisualRoot || target.IsChildOf(_animatedVisualRoot);
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemCharacterBuffApplier] {message}", this);
        }
    }
}
