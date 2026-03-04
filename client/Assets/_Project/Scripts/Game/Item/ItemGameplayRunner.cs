using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// Item 런타임을 씬에서 직접 구동하는 개발용 러너다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemGameplayRunner : MonoBehaviour
    {
        private const string RuntimeSceneName = "ItemScene";
        private const string BlackholeVisualName = "Item_Blackhole";
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapItemSceneRunner()
        {
            if (!IsItemRuntimeScene(SceneManager.GetActiveScene()))
            {
                return;
            }

            var legacyRunner = FindObjectOfType<SSAFYPlayTime.ItemPrototypeHotkeyRunner>();
            var root = legacyRunner != null ? legacyRunner.gameObject : GameObject.Find("ItemGameplayRunner");
            if (root == null)
            {
                root = new GameObject("ItemGameplayRunner");
            }

            if (legacyRunner != null)
            {
                // 신규 러너 전환 시 중복 입력/중복 스폰을 막기 위해 기존 러너를 끈다.
                legacyRunner.enabled = false;
            }

            if (root.GetComponent<ItemRuntimeHost>() == null)
            {
                root.AddComponent<ItemRuntimeHost>();
            }

            if (root.GetComponent<ItemGameplayRunner>() == null)
            {
                root.AddComponent<ItemGameplayRunner>();
            }
        }

        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform targetRoot;
        [SerializeField] private bool runOnlyInItemScene = true;

        [Header("입력")]
        [SerializeField] private bool enableHotkeys = true;
        [SerializeField] private bool forceReplaceHeldItemOnHotkey = true;

        [Header("블랙홀")]
        [SerializeField] private float blackholeThrowSpeed = 8f;
        [SerializeField] private float blackholeThrowArc = 0.35f;
        [SerializeField] private float blackholePullStrengthMultiplier = 2.25f;
        [SerializeField] private float blackholeFlamethrowerRangeBoostMultiplier = 3f;
        [SerializeField] private float blackholeExpandSpeedMultiplier = 1.5f;
        [SerializeField] private float blackholePlayerPullMultiplier = 1.5f;
        [SerializeField] private float blackholeItemPullMultiplier = 1.5f;
        [SerializeField] private float blackholePlayerEscapeDamping = 0.2f;
        [SerializeField] private string blackholeEffectPrefabAssetPath =
            "Assets/SpecialSkillsEffectsPack/AllEffects/EffectsSet_1(NotScriptBased)/Effects/Effect_02_BlackHole/Effect_02_BlackHole.prefab";
        [SerializeField] private float blackholeEffectScale = 0.07f;

        [Header("화염방사기")]
        [SerializeField] private LayerMask physicsMask = ~0;
        [SerializeField] private bool applyFlamethrowerDamageToDummy = true;
        [SerializeField] private bool applyFlamethrowerPushForce = true;

        [Header("로그")]
        [SerializeField] private bool enableStatusLog = true;

        private readonly Collider[] _flamethrowerOverlapBuffer = new Collider[128];
        private readonly HashSet<int> _flamethrowerUniqueTargetIds = new();

        private bool _eventsBound;
        private float _flamethrowerRangeMultiplier = 1f;
        private Coroutine _blackholeRoutine;
        private ParticleSystem _flamethrowerParticle;
        private GameObject _blackholeEffectPrefabCache;

        private void Awake()
        {
            if (!ShouldRunInCurrentScene())
            {
                enabled = false;
                return;
            }

            ResolveReferences();
            itemRuntimeHost?.SetOwnerTransform(targetRoot);
        }

        private void OnEnable()
        {
            if (!ShouldRunInCurrentScene())
            {
                enabled = false;
                return;
            }

            ResolveReferences();
            BindRuntimeEvents();

            if (itemRuntimeHost != null && !itemRuntimeHost.IsReady)
            {
                if (!itemRuntimeHost.Initialize())
                {
                    LogStatus($"ItemRuntimeHost init failed: {itemRuntimeHost.LastError}");
                }
            }
        }

        private void OnDisable()
        {
            UnbindRuntimeEvents();

            if (_blackholeRoutine != null)
            {
                StopCoroutine(_blackholeRoutine);
                _blackholeRoutine = null;
            }

            StopFlamethrowerParticle();
        }

        private void Update()
        {
            if (!enableHotkeys || itemRuntimeHost == null)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                TriggerItemByHotkey(ItemIds.BlackholeBomb);
            }

            if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                TriggerItemByHotkey(ItemIds.Flamethrower);
            }
        }

        private void TriggerItemByHotkey(string itemId)
        {
            if (!EnsureRuntimeReady())
            {
                return;
            }

            if (!string.Equals(itemRuntimeHost.HeldItemId, itemId, StringComparison.Ordinal))
            {
                if (!itemRuntimeHost.TryPickup(itemId, out var pickupReason))
                {
                    if (!forceReplaceHeldItemOnHotkey)
                    {
                        LogStatus($"Pickup failed: {pickupReason}");
                        return;
                    }

                    itemRuntimeHost.ResetRuntimeState();
                    if (!itemRuntimeHost.TryPickup(itemId, out pickupReason))
                    {
                        LogStatus($"Forced pickup failed: {pickupReason}");
                        return;
                    }
                }
            }

            if (!itemRuntimeHost.TryUseHeldItem(Vector3.zero, out var useReason))
            {
                LogStatus($"Use failed: {useReason}");
            }
        }

        private void BindRuntimeEvents()
        {
            if (_eventsBound || itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BlackholeRequested += OnBlackholeRequested;
            itemRuntimeHost.FlamethrowerStarted += OnFlamethrowerStarted;
            itemRuntimeHost.FlamethrowerTicked += OnFlamethrowerTicked;
            itemRuntimeHost.FlamethrowerStopped += OnFlamethrowerStopped;
            _eventsBound = true;
        }

        private void UnbindRuntimeEvents()
        {
            if (!_eventsBound || itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.BlackholeRequested -= OnBlackholeRequested;
            itemRuntimeHost.FlamethrowerStarted -= OnFlamethrowerStarted;
            itemRuntimeHost.FlamethrowerTicked -= OnFlamethrowerTicked;
            itemRuntimeHost.FlamethrowerStopped -= OnFlamethrowerStopped;
            _eventsBound = false;
        }

        private void OnBlackholeRequested(BlackholeSkillRequest request)
        {
            if (_blackholeRoutine != null)
            {
                StopCoroutine(_blackholeRoutine);
            }

            _blackholeRoutine = StartCoroutine(CoBlackholeSkill(request));
        }

        private void OnFlamethrowerStarted(string itemId, float endAtSec)
        {
            EnsureFlamethrowerParticle();
            LogStatus($"Flamethrower started: {itemId}");
        }

        private void OnFlamethrowerStopped(string itemId)
        {
            StopFlamethrowerParticle();
            LogStatus($"Flamethrower stopped: {itemId}");
        }

        private void OnFlamethrowerTicked(FlamethrowerTickRequest request)
        {
            TickFlamethrower(in request);
        }

        private IEnumerator CoBlackholeSkill(BlackholeSkillRequest request)
        {
            var startPos = GetTargetPosition() + Vector3.up * 1.2f + GetTargetForward() * 0.7f;
            var throwDirection = (GetTargetForward() + Vector3.up * blackholeThrowArc).normalized;

            var bomb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bomb.name = BlackholeVisualName;
            bomb.transform.position = startPos;
            bomb.transform.localScale = Vector3.one * 0.45f;

            var bombBody = bomb.AddComponent<Rigidbody>();
            bombBody.mass = 4f;
            bombBody.drag = 0.3f;
            bombBody.angularDrag = 0.1f;
            bombBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            bombBody.interpolation = RigidbodyInterpolation.Interpolate;
            bombBody.AddForce(throwDirection * blackholeThrowSpeed, ForceMode.VelocityChange);
            bombBody.AddTorque(UnityEngine.Random.onUnitSphere * 4f, ForceMode.VelocityChange);

            var delaySec = Mathf.Max(0f, request.DelaySec);
            if (delaySec > 0f)
            {
                yield return new WaitForSeconds(delaySec);
            }

            var center = bomb != null ? bomb.transform.position : request.Center;
            if (bombBody != null)
            {
                bombBody.velocity = Vector3.zero;
                bombBody.angularVelocity = Vector3.zero;
                bombBody.isKinematic = true;
            }

            if (bomb != null && bomb.TryGetComponent<Collider>(out var bombCollider))
            {
                // 흡입 구체는 시각 전용이므로 충돌을 끈다.
                bombCollider.enabled = false;
                TryAttachBlackholeEffect(bomb.transform);
            }

            var duration = Mathf.Max(0f, request.DurationSec);
            var radius = Mathf.Max(0f, request.Radius);
            var force = Mathf.Max(0f, request.Force);
            var elapsed = 0f;
            var expandDuration = Mathf.Max(0.01f, duration / Mathf.Max(0.01f, blackholeExpandSpeedMultiplier));

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var ramp = Mathf.Clamp01(elapsed / expandDuration);
                var overlaps = Physics.OverlapSphere(center, radius, physicsMask, QueryTriggerInteraction.Ignore);

                for (var i = 0; i < overlaps.Length; i++)
                {
                    var body = overlaps[i].attachedRigidbody;
                    if (body == null || body.isKinematic)
                    {
                        continue;
                    }

                    var toCenter = center - body.worldCenterOfMass;
                    if (!ShouldPullByBlackhole(overlaps[i], body, toCenter))
                    {
                        continue;
                    }

                    var root = body.transform.root;
                    var distance = Mathf.Max(toCenter.magnitude, 0.35f);
                    var pullMultiplier = GetBlackholePullMultiplier(root);
                    var pullStrength =
                        (force * blackholePullStrengthMultiplier * (0.4f + ramp * 1.6f)) /
                        Mathf.Sqrt(distance) * pullMultiplier;

                    if (IsPlayerTarget(root))
                    {
                        ApplyPlayerEscapeDamping(body, toCenter.normalized);
                    }

                    body.AddForce(toCenter.normalized * pullStrength, ForceMode.Acceleration);
                }

                if (bomb != null)
                {
                    bomb.transform.position = center;
                    bomb.transform.localScale = Vector3.one * Mathf.Lerp(0.45f, radius * 2f, ramp);
                    bomb.transform.Rotate(Vector3.up, 220f * Time.deltaTime, Space.World);
                }

                yield return null;
            }

            if (bomb != null)
            {
                Destroy(bomb);
            }

            _blackholeRoutine = null;
            _flamethrowerRangeMultiplier = Mathf.Max(1f, blackholeFlamethrowerRangeBoostMultiplier);
        }

        private void TickFlamethrower(in FlamethrowerTickRequest request)
        {
            var forward = request.Forward.sqrMagnitude > 0.0001f ? request.Forward.normalized : GetTargetForward();
            var range = Mathf.Max(0f, request.Range * Mathf.Max(1f, _flamethrowerRangeMultiplier));
            var radius = Mathf.Max(0f, request.Radius);
            var origin = request.Origin;

            UpdateFlamethrowerParticle(origin, forward, range, radius);

            var start = origin;
            var end = origin + forward * range;
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                start,
                end,
                radius,
                _flamethrowerOverlapBuffer,
                physicsMask,
                QueryTriggerInteraction.Ignore);

            _flamethrowerUniqueTargetIds.Clear();
            for (var i = 0; i < overlapCount; i++)
            {
                var hitCollider = _flamethrowerOverlapBuffer[i];
                if (hitCollider == null)
                {
                    continue;
                }

                var hitTransform = hitCollider.attachedRigidbody != null
                    ? hitCollider.attachedRigidbody.transform
                    : hitCollider.transform.root;
                if (hitTransform == null || (targetRoot != null && hitTransform == targetRoot))
                {
                    continue;
                }

                if (!_flamethrowerUniqueTargetIds.Add(hitTransform.GetInstanceID()))
                {
                    continue;
                }

                if (applyFlamethrowerDamageToDummy)
                {
                    var dummy = hitTransform.GetComponentInParent<SSAFYPlayTime.PrototypeDamageDummy>();
                    if (dummy != null)
                    {
                        dummy.ApplyDamage(request.DamagePerTick, request.StunDamagePerTick, "Flamethrower");
                    }
                }

                if (applyFlamethrowerPushForce)
                {
                    var body = hitCollider.attachedRigidbody;
                    if (body != null && !body.isKinematic)
                    {
                        body.AddForce(forward * request.PushForce, ForceMode.Acceleration);
                    }
                }
            }
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = FindObjectOfType<ItemRuntimeHost>();
            }

            if (targetRoot != null)
            {
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                targetRoot = player.transform;
                return;
            }

            targetRoot = itemRuntimeHost != null ? itemRuntimeHost.transform : transform;
        }

        private bool EnsureRuntimeReady()
        {
            if (itemRuntimeHost == null)
            {
                LogStatus("ItemRuntimeHost missing.");
                return false;
            }

            if (itemRuntimeHost.IsReady)
            {
                return true;
            }

            if (itemRuntimeHost.Initialize())
            {
                return true;
            }

            LogStatus($"ItemRuntimeHost init failed: {itemRuntimeHost.LastError}");
            return false;
        }

        private bool ShouldRunInCurrentScene()
        {
            return !runOnlyInItemScene || IsItemRuntimeScene(gameObject.scene);
        }

        private Vector3 GetTargetPosition()
        {
            return targetRoot != null ? targetRoot.position : transform.position;
        }

        private Vector3 GetTargetForward()
        {
            var forward = targetRoot != null ? targetRoot.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private void EnsureFlamethrowerParticle()
        {
            if (_flamethrowerParticle != null)
            {
                if (!_flamethrowerParticle.isPlaying)
                {
                    _flamethrowerParticle.Play();
                }

                return;
            }

            var fx = new GameObject("Item_FlamethrowerFx");
            fx.transform.SetParent(transform, false);
            _flamethrowerParticle = fx.AddComponent<ParticleSystem>();

            var main = _flamethrowerParticle.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.9f, 0.4f, 0.9f),
                new Color(1f, 0.35f, 0.1f, 0.55f));

            var emission = _flamethrowerParticle.emission;
            emission.rateOverTime = 200f;

            var shape = _flamethrowerParticle.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 24f;
            shape.radius = 0.12f;
            shape.length = 0.6f;
            shape.randomDirectionAmount = 0.2f;

            _flamethrowerParticle.Play();
        }

        private void UpdateFlamethrowerParticle(Vector3 origin, Vector3 forward, float range, float radius)
        {
            EnsureFlamethrowerParticle();
            if (_flamethrowerParticle == null)
            {
                return;
            }

            _flamethrowerParticle.transform.position = origin;
            _flamethrowerParticle.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var main = _flamethrowerParticle.main;
            var speed = Mathf.Max(8f, range * 2.1f);
            var lifetime = Mathf.Max(0.18f, range / speed);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.8f, speed);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.9f, lifetime * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.12f, radius * 0.45f),
                Mathf.Max(0.24f, radius * 0.75f));
        }

        private void StopFlamethrowerParticle()
        {
            if (_flamethrowerParticle == null)
            {
                return;
            }

            _flamethrowerParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void TryAttachBlackholeEffect(Transform blackholeTransform)
        {
            if (blackholeTransform == null)
            {
                return;
            }

            var prefab = TryLoadBlackholeEffectPrefab();
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab, blackholeTransform);
            instance.name = "Item_BlackholeFx";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.001f, blackholeEffectScale);

            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private GameObject TryLoadBlackholeEffectPrefab()
        {
            if (_blackholeEffectPrefabCache != null)
            {
                return _blackholeEffectPrefabCache;
            }

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(blackholeEffectPrefabAssetPath))
            {
                _blackholeEffectPrefabCache = AssetDatabase.LoadAssetAtPath<GameObject>(blackholeEffectPrefabAssetPath);
                if (_blackholeEffectPrefabCache != null)
                {
                    return _blackholeEffectPrefabCache;
                }
            }
#endif

            _blackholeEffectPrefabCache = Resources.Load<GameObject>("Effect_02_BlackHole");
            return _blackholeEffectPrefabCache;
        }

        private bool ShouldPullByBlackhole(Collider hitCollider, Rigidbody body, Vector3 toCenter)
        {
            if (hitCollider == null || body == null || toCenter.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            var root = body.transform.root;
            if (root == null)
            {
                return false;
            }

            if (targetRoot != null && root == targetRoot)
            {
                return false;
            }

            if (HasVfxLikeComponent(hitCollider.transform))
            {
                return false;
            }

            if (IsPlayerTarget(root))
            {
                return true;
            }

            if (root.GetComponentInParent<SSAFYPlayTime.PrototypeDamageDummy>() != null)
            {
                return true;
            }

            return HasItemKeyword(root.name) || HasItemLikeComponent(root);
        }

        private float GetBlackholePullMultiplier(Transform root)
        {
            if (IsPlayerTarget(root))
            {
                return Mathf.Max(0.05f, blackholePlayerPullMultiplier);
            }

            if (root != null && (root.GetComponentInParent<SSAFYPlayTime.PrototypeDamageDummy>() != null || HasItemKeyword(root.name) || HasItemLikeComponent(root)))
            {
                return Mathf.Max(0.05f, blackholeItemPullMultiplier);
            }

            return 1f;
        }

        private void ApplyPlayerEscapeDamping(Rigidbody body, Vector3 toCenterDir)
        {
            var clampedDamping = Mathf.Clamp01(blackholePlayerEscapeDamping);
            if (body == null || clampedDamping <= 0f)
            {
                return;
            }

            var awayDir = -toCenterDir;
            var awaySpeed = Vector3.Dot(body.velocity, awayDir);
            if (awaySpeed <= 0f)
            {
                return;
            }

            body.velocity += toCenterDir * (awaySpeed * clampedDamping);
        }

        private static bool IsPlayerTarget(Transform root)
        {
            return root != null && root.CompareTag("Player");
        }

        private static bool HasVfxLikeComponent(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (candidate.GetComponentInParent<ParticleSystem>() != null ||
                candidate.GetComponentInParent<TrailRenderer>() != null ||
                candidate.GetComponentInParent<LineRenderer>() != null)
            {
                return true;
            }

            var components = candidate.GetComponentsInParent<Component>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component != null &&
                    component.GetType().Name.IndexOf("VisualEffect", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasItemLikeComponent(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            var components = root.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null || component is Transform || component is Rigidbody || component is Collider || component is Renderer)
                {
                    continue;
                }

                if (component.GetType().Name.IndexOf("Item", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasItemKeyword(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            return objectName.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("pickup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("drop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogStatus(string message)
        {
            if (!enableStatusLog)
            {
                return;
            }

            Debug.Log($"[ItemGameplayRunner] {message}", this);
        }

        private static bool IsItemRuntimeScene(Scene scene)
        {
            if (!scene.IsValid())
            {
                return false;
            }

            if (string.Equals(scene.name, RuntimeSceneName, StringComparison.Ordinal))
            {
                return true;
            }

            var scenePath = scene.path ?? string.Empty;
            return scenePath.EndsWith("/ItemScene.unity", StringComparison.OrdinalIgnoreCase) ||
                   scenePath.EndsWith("\\ItemScene.unity", StringComparison.OrdinalIgnoreCase);
        }
    }
}
