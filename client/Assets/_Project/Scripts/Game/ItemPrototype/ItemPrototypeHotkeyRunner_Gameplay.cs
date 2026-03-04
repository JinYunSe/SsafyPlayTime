using System.Collections;
using Fusion;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime
{
    public sealed partial class ItemPrototypeHotkeyRunner
    {
        private void TickTimedStates()
        {
            if (_isScaleBuffActive && Time.time >= _scaleBuffEndTime)
            {
                if (_scaleRoutine != null)
                {
                    StopCoroutine(_scaleRoutine);
                    _scaleRoutine = null;
                }

                RestoreScale();
                _isScaleBuffActive = false;
                SetStatus($"{_scaleBuffLabel}: end");
                _scaleBuffLabel = string.Empty;
            }

            if (_isInvisibilityActive && Time.time >= _invisibilityEndTime)
            {
                if (_invisibilityRoutine != null)
                {
                    StopCoroutine(_invisibilityRoutine);
                    _invisibilityRoutine = null;
                }

                RestoreRendererColors();
                _isInvisibilityActive = false;
                EnsureTargetVisibleForPrototype();
                SetStatus("Invisibility: end");
            }

            if (!_isSuperArmorActive)
            {
                return;
            }

            if (TryGetRunner(out var runner) && _superArmorTickTimer.IsRunning)
            {
                if (_superArmorTickTimer.Expired(runner))
                {
                    _isSuperArmorActive = false;
                    _superArmorTickTimer = TickTimer.None;
                    SetStatus("Americano(super armor) ended");
                }

                return;
            }

            if (Time.time >= _superArmorEndTime)
            {
                _isSuperArmorActive = false;
                _superArmorTickTimer = TickTimer.None;
                SetStatus("Americano(super armor) ended");
            }
        }

        private void TickFlamethrower()
        {
            if (!_isFlamethrowerActive)
            {
                return;
            }

            if (TryGetRunner(out var runner) && _flamethrowerEndTickTimer.IsRunning)
            {
                if (_flamethrowerEndTickTimer.Expired(runner))
                {
                    StopFlamethrower("Flamethrower ended (5s)");
                    return;
                }
            }
            else if (Time.time >= _flamethrowerEndTime)
            {
                StopFlamethrower("Flamethrower ended (5s)");
                return;
            }

            var origin = GetTargetPosition() + Vector3.up * 1.2f + GetTargetForward() * 0.7f;
            var forward = GetTargetForward();
            UpdateFlamethrowerParticlePose(origin, forward);

            Debug.DrawRay(origin, forward * flamethrowerRange, Color.red);

            if (TryGetRunner(out runner))
            {
                if (_flamethrowerTickTimer.IsRunning && !_flamethrowerTickTimer.Expired(runner))
                {
                    return;
                }

                _flamethrowerTickTimer = TickTimer.CreateFromSeconds(runner, flamethrowerTickIntervalSec);
            }
            else
            {
                if (Time.time < _nextFlamethrowerTickTime)
                {
                    return;
                }

                _nextFlamethrowerTickTime = Time.time + flamethrowerTickIntervalSec;
            }

            var start = origin;
            var end = origin + forward * flamethrowerRange;
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                start,
                end,
                flamethrowerRadius,
                _flamethrowerOverlapBuffer,
                physicsMask,
                QueryTriggerInteraction.Ignore);

            _flamethrowerUniqueTargetIds.Clear();
            var hitCount = 0;
            var damageTotal = 0f;
            var stunDamageTotal = 0f;

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
                if (hitTransform == null)
                {
                    continue;
                }

                if (targetRoot != null && hitTransform == targetRoot)
                {
                    continue;
                }

                var targetId = hitTransform.GetInstanceID();
                if (!_flamethrowerUniqueTargetIds.Add(targetId))
                {
                    continue;
                }

                if (hitTransform.TryGetComponent<PrototypeDamageDummy>(out var dummy))
                {
                    dummy.ApplyDamage(flamethrowerDamagePerTick, flamethrowerStunDamagePerTick, "Flamethrower");
                    damageTotal += flamethrowerDamagePerTick;
                    stunDamageTotal += flamethrowerStunDamagePerTick;
                }
                else
                {
                    var dummyInParent = hitTransform.GetComponentInParent<PrototypeDamageDummy>();
                    if (dummyInParent != null)
                    {
                        dummyInParent.ApplyDamage(flamethrowerDamagePerTick, flamethrowerStunDamagePerTick, "Flamethrower");
                        damageTotal += flamethrowerDamagePerTick;
                        stunDamageTotal += flamethrowerStunDamagePerTick;
                    }
                }

                var body = hitCollider.attachedRigidbody;
                if (body != null && !body.isKinematic)
                {
                    body.AddForce(forward * flamethrowerPushForce, ForceMode.Acceleration);
                }

                hitCount++;
            }

            _lastFlamethrowerTickHitCount = hitCount;
            _lastFlamethrowerTickDamage = damageTotal;
            _lastFlamethrowerTickStunDamage = stunDamageTotal;
            SetStatus($"Flamethrower tick: hits={hitCount}, hpDmg={damageTotal:0.0}, stunDmg={stunDamageTotal:0.0}");
        }

        private void TriggerBlackholeBomb()
        {
            ResolveTarget();
            PlayItemUsePresentation(ItemIdBlackholeBomb, GetTargetPosition());
            StartCoroutine(CoBlackholeBomb());
        }

        private IEnumerator CoBlackholeBomb()
        {
            SetStatus("Blackhole bomb: throw");

            var startPos = GetTargetPosition() + Vector3.up * 1.2f + GetTargetForward() * 0.7f;
            var throwDirection = (GetTargetForward() + Vector3.up * 0.35f).normalized;

            var bomb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bomb.name = PrototypeBlackholeName;
            bomb.transform.position = startPos;
            bomb.transform.localScale = Vector3.one * 0.45f;
            var bombBody = bomb.AddComponent<Rigidbody>();
            bombBody.mass = 4f;
            bombBody.drag = 0.3f;
            bombBody.angularDrag = 0.1f;
            bombBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            bombBody.interpolation = RigidbodyInterpolation.Interpolate;
            bombBody.AddForce(throwDirection * 8f, ForceMode.VelocityChange);
            bombBody.AddTorque(Random.onUnitSphere * 4f, ForceMode.VelocityChange);

            var renderer = bomb.GetComponent<Renderer>();
            if (renderer != null)
            {
                ConfigureBlackholeVisualMaterial(renderer.material);
            }

            SetStatus($"Blackhole bomb: armed ({blackholeDelaySec:0.0}s)");
            yield return new WaitForSeconds(blackholeDelaySec);
            var center = bomb != null ? bomb.transform.position : startPos;

            if (bombBody != null)
            {
                // 폭발 시점 위치를 고정하기 위해 속도를 제거한다.
                bombBody.velocity = Vector3.zero;
                bombBody.angularVelocity = Vector3.zero;
                bombBody.isKinematic = true;
            }

            if (bomb != null && bomb.TryGetComponent<Collider>(out var bombCollider))
            {
                // 블랙홀 시각 구체는 충돌을 만들지 않아야 한다.
                bombCollider.enabled = false;
            }

            if (bomb != null)
            {
                bomb.transform.position = center;
                // 던지는 구체가 아니라, 흡입 시작 구간의 구체에 블랙홀 이펙트를 부착한다.
                TryAttachBlackholeEffect(bomb.transform);
            }

            SetStatus($"Blackhole bomb: gravity ({blackholeDurationSec:0.0}s)");
            var elapsed = 0f;
            var expandDuration = Mathf.Max(0.01f, blackholeDurationSec / Mathf.Max(0.01f, blackholeExpandSpeedMultiplier));
            while (elapsed < blackholeDurationSec)
            {
                elapsed += Time.deltaTime;
                var ramp = Mathf.Clamp01(elapsed / expandDuration);

                var overlaps = Physics.OverlapSphere(center, blackholeRadius, physicsMask, QueryTriggerInteraction.Ignore);
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
                        (blackholeForce * blackholePullStrengthMultiplier * (0.4f + ramp * 1.6f)) /
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
                    bomb.transform.Rotate(Vector3.up, 220f * Time.deltaTime, Space.World);
                    bomb.transform.localScale = Vector3.one * Mathf.Lerp(0.45f, blackholeRadius * 2f, ramp);
                }
                yield return null;
            }

            if (bomb != null)
            {
                Destroy(bomb);
            }

            ApplyFlamethrowerRangeBoostAfterBlackhole();
            SetStatus($"Blackhole bomb: end (FlameRange={flamethrowerRange:0.0})");
        }

        private void TryAttachBlackholeEffect(Transform bombTransform)
        {
            if (bombTransform == null)
            {
                return;
            }

            var prefab = TryLoadBlackholeEffectPrefab();
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab, bombTransform);
            instance.name = "Prototype_BlackholeFx";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.001f, blackholeEffectScale);
            DisableAllChildColliders(instance);
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

            var guids = AssetDatabase.FindAssets("Effect_02_BlackHole t:Prefab");
            if (guids != null && guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _blackholeEffectPrefabCache = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (_blackholeEffectPrefabCache != null)
                {
                    blackholeEffectPrefabAssetPath = path;
                    return _blackholeEffectPrefabCache;
                }
            }
#endif

            _blackholeEffectPrefabCache = Resources.Load<GameObject>("Effect_02_BlackHole");
            return _blackholeEffectPrefabCache;
        }

        private static void DisableAllChildColliders(GameObject root)
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

            if (root == transform)
            {
                return false;
            }

            if (HasVfxLikeComponent(hitCollider.transform))
            {
                return false;
            }

            if (root.CompareTag("Player"))
            {
                return true;
            }

            if (root.GetComponentInParent<PrototypeDamageDummy>() != null)
            {
                return true;
            }

            if (HasItemKeyword(root.name))
            {
                return true;
            }

            return HasItemLikeComponent(root);
        }

        private float GetBlackholePullMultiplier(Transform root)
        {
            if (IsPlayerTarget(root))
            {
                return Mathf.Max(0.05f, blackholePlayerPullMultiplier);
            }

            if (IsDummyTarget(root) || IsItemTarget(root))
            {
                return Mathf.Max(0.05f, blackholeItemPullMultiplier);
            }

            return 1f;
        }

        private void ApplyPlayerEscapeDamping(Rigidbody body, Vector3 toCenterDir)
        {
            if (body == null || toCenterDir.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var clampedDamping = Mathf.Clamp01(blackholePlayerEscapeDamping);
            if (clampedDamping <= 0f)
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

        private static bool IsDummyTarget(Transform root)
        {
            return root != null && root.GetComponentInParent<PrototypeDamageDummy>() != null;
        }

        private static bool IsItemTarget(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            return HasItemKeyword(root.name) || HasItemLikeComponent(root);
        }

        private static bool HasVfxLikeComponent(Transform candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (candidate.GetComponentInParent<ParticleSystem>() != null)
            {
                return true;
            }

            if (candidate.GetComponentInParent<TrailRenderer>() != null)
            {
                return true;
            }

            if (candidate.GetComponentInParent<LineRenderer>() != null)
            {
                return true;
            }

            // VFX Graph 컴포넌트 이름을 문자열로 판별해 패키지 의존성을 피한다.
            var components = candidate.GetComponentsInParent<Component>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (component.GetType().Name.IndexOf("VisualEffect", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasItemLikeComponent(Transform root)
        {
            var components = root.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (component is Transform || component is Rigidbody || component is Collider || component is Renderer)
                {
                    continue;
                }

                var typeName = component.GetType().Name;
                if (typeName.IndexOf("Item", System.StringComparison.OrdinalIgnoreCase) >= 0)
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

            return objectName.IndexOf("item", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("pickup", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("drop", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("loot", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void TriggerGrowth()
        {
            ResolveTarget();
            PlayItemUsePresentation(ItemIdGrowth, GetTargetPosition());
            StartScaleBuff(growthScaleMultiplier, growthDurationSec, "Growth item");
        }

        private void TriggerShrink()
        {
            ResolveTarget();
            PlayItemUsePresentation(ItemIdShrink, GetTargetPosition());
            StartScaleBuff(shrinkScaleMultiplier, shrinkDurationSec, "Shrink item");
        }

        private void StartScaleBuff(float scaleMultiplier, float durationSec, string label)
        {
            if (_scaleRoutine != null)
            {
                StopCoroutine(_scaleRoutine);
                RestoreScale();
            }

            _isScaleBuffActive = true;
            _scaleBuffEndTime = Time.time + durationSec;
            _scaleBuffLabel = label;
            _scaleRoutine = StartCoroutine(CoScaleBuff(scaleMultiplier, durationSec, label));
        }

        private IEnumerator CoScaleBuff(float scaleMultiplier, float durationSec, string label)
        {
            if (!_hasBaseScale)
            {
                _baseScale = targetRoot != null ? targetRoot.localScale : Vector3.one;
                _hasBaseScale = true;
            }

            if (targetRoot != null)
            {
                targetRoot.localScale = _baseScale * scaleMultiplier;
            }

            SetStatus($"{label}: active ({durationSec:0.0}s)");
            yield return new WaitForSeconds(durationSec);

            RestoreScale();
            SetStatus($"{label}: end");
            _isScaleBuffActive = false;
            _scaleBuffLabel = string.Empty;
            _scaleRoutine = null;
        }

        private void RestoreScale()
        {
            if (targetRoot != null && _hasBaseScale)
            {
                targetRoot.localScale = _baseScale;
            }
        }

        private void TriggerAmericano()
        {
            ResolveTarget();
            PlayItemUsePresentation(ItemIdAmericano, GetTargetPosition());
            _isSuperArmorActive = true;
            _superArmorEndTime = Time.time + superArmorDurationSec;

            if (TryGetRunner(out var runner))
            {
                _superArmorTickTimer = TickTimer.CreateFromSeconds(runner, superArmorDurationSec);
            }
            else
            {
                _superArmorTickTimer = TickTimer.None;
            }

            SetStatus($"Americano(super armor): active ({superArmorDurationSec:0.0}s)");
        }

        private void TriggerFlamethrower()
        {
            ResolveTarget();

            if (_isFlamethrowerActive)
            {
                StopFlamethrower("Flamethrower stopped");
                return;
            }

            _isFlamethrowerActive = true;
            _flamethrowerEndTime = Time.time + flamethrowerMaxUseSec;
            _nextFlamethrowerTickTime = Time.time;

            if (TryGetRunner(out var runner))
            {
                _flamethrowerEndTickTimer = TickTimer.CreateFromSeconds(runner, flamethrowerMaxUseSec);
                _flamethrowerTickTimer = TickTimer.None;
            }
            else
            {
                _flamethrowerEndTickTimer = TickTimer.None;
                _flamethrowerTickTimer = TickTimer.None;
            }

            EnsureFlamethrowerParticle();
            PlayItemUsePresentation(ItemIdFlamethrower, GetTargetPosition(), forceLoopSfx: true, suppressStartVfx: true);
            SetStatus($"Flamethrower: active ({flamethrowerMaxUseSec:0.0}s)");
        }

        private void StopFlamethrower(string reason)
        {
            _isFlamethrowerActive = false;
            _flamethrowerEndTickTimer = TickTimer.None;
            _flamethrowerTickTimer = TickTimer.None;
            StopFlamethrowerParticle();
            StopItemUseLoopSfx(ItemIdFlamethrower);
            SetStatus(reason);
        }

        private void EnsureFlamethrowerParticle()
        {
            if (_flamethrowerParticle == null)
            {
                var fx = new GameObject("Prototype_FlamethrowerFx");
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
                emission.rateOverTime = 150f;

                var shape = _flamethrowerParticle.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 24f;
                shape.radius = 0.12f;
                shape.length = 0.6f;
                shape.randomDirectionAmount = 0.2f;

                var limit = _flamethrowerParticle.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.dampen = 0.15f;

                var noise = _flamethrowerParticle.noise;
                noise.enabled = true;
                noise.strength = 0.45f;
                noise.frequency = 0.8f;
                noise.scrollSpeed = 0.35f;
            }

            ConfigureFlamethrowerParticleRenderer();
            UpdateFlamethrowerParticleProfile();
            if (!_flamethrowerParticle.isPlaying)
            {
                _flamethrowerParticle.Play();
            }
        }

        private void ConfigureFlamethrowerParticleRenderer()
        {
            if (_flamethrowerParticle == null)
            {
                return;
            }

            if (_flamethrowerFallbackMaterial == null)
            {
                if (!TryCreateFlamethrowerMaterialFromVfxAsset(out _flamethrowerFallbackMaterial))
                {
                    TryCreateFlamethrowerFallbackMaterial(out _flamethrowerFallbackMaterial);
                }
            }

            var renderer = _flamethrowerParticle.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (_flamethrowerFallbackMaterial != null)
            {
                renderer.sharedMaterial = _flamethrowerFallbackMaterial;
            }
        }

        private bool TryCreateFlamethrowerMaterialFromVfxAsset(out Material material)
        {
            material = null;
            if (_dataCatalog == null)
            {
                return false;
            }

            if (!TryResolveStartVfx(ItemIdFlamethrower, out var vfxId))
            {
                return false;
            }

            if (!_dataCatalog.VfxAssetRows.TryGetValue(vfxId, out var row))
            {
                return false;
            }

            var prefab = LoadVfxPrefabFromAssetKey(row.AssetKey);
            if (prefab == null)
            {
                return false;
            }

            var sourceRenderer = prefab.GetComponentInChildren<ParticleSystemRenderer>(true);
            if (sourceRenderer == null || sourceRenderer.sharedMaterial == null)
            {
                return false;
            }

            var sourceMaterial = sourceRenderer.sharedMaterial;
            if (sourceMaterial.shader == null)
            {
                return false;
            }

            material = new Material(sourceMaterial);
            material.name = "PrototypeFlamethrowerFromAsset";
            return true;
        }

        private bool TryCreateFlamethrowerFallbackMaterial(out Material material)
        {
            material = null;
            var shader =
                Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                Shader.Find("Particles/Standard Unlit") ??
                Shader.Find("Legacy Shaders/Particles/Additive") ??
                Shader.Find("Sprites/Default");

            if (shader == null)
            {
                return false;
            }

            material = new Material(shader);
            material.name = "PrototypeFlamethrowerFallback";
            var tint = new Color(1f, 0.45f, 0.12f, 0.9f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", tint);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", tint);
            }

            if (_flamethrowerFallbackTexture == null)
            {
                _flamethrowerFallbackTexture = CreateFlamethrowerFallbackTexture();
            }

            if (_flamethrowerFallbackTexture != null)
            {
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", _flamethrowerFallbackTexture);
                }

                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", _flamethrowerFallbackTexture);
                }
            }

            return true;
        }


        private void DisposeFlamethrowerFallbackMaterial()
        {
            if (_flamethrowerFallbackMaterial == null)
            {
                return;
            }

            Destroy(_flamethrowerFallbackMaterial);
            _flamethrowerFallbackMaterial = null;

            if (_flamethrowerFallbackTexture != null)
            {
                Destroy(_flamethrowerFallbackTexture);
                _flamethrowerFallbackTexture = null;
            }
        }

        private static Texture2D CreateFlamethrowerFallbackTexture()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "PrototypeFlamethrowerSprite";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var center = (size - 1) * 0.5f;
            var maxDistance = center;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var distance = Mathf.Sqrt(dx * dx + dy * dy) / maxDistance;
                    var alpha = Mathf.Clamp01(1f - distance);
                    alpha = Mathf.Pow(alpha, 1.7f);

                    var hot = new Color(1f, 0.8f, 0.35f, 1f);
                    var edge = new Color(1f, 0.35f, 0.08f, 1f);
                    var color = Color.Lerp(edge, hot, alpha);
                    color.a = alpha;
                    texture.SetPixel(x, y, color);
                }
            }

            texture.Apply(false, false);
            return texture;
        }

        private void UpdateFlamethrowerParticlePose(Vector3 origin, Vector3 forward)
        {
            if (_flamethrowerParticle == null)
            {
                return;
            }

            _flamethrowerParticle.transform.position = origin;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            UpdateFlamethrowerParticleProfile();
            _flamethrowerParticle.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void StopFlamethrowerParticle()
        {
            if (_flamethrowerParticle == null)
            {
                return;
            }

            _flamethrowerParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void UpdateFlamethrowerParticleProfile()
        {
            if (_flamethrowerParticle == null)
            {
                return;
            }

            var speed = Mathf.Max(8f, flamethrowerRange * 2.1f);
            var lifetime = Mathf.Max(0.18f, flamethrowerRange / speed);
            var widthRatio = Mathf.Clamp01(flamethrowerRange / 14f);

            var main = _flamethrowerParticle.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.8f, speed);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.9f, lifetime * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                Mathf.Max(0.12f, flamethrowerRadius * 0.45f),
                Mathf.Max(0.24f, flamethrowerRadius * 0.75f));

            var emission = _flamethrowerParticle.emission;
            emission.rateOverTime = Mathf.Lerp(220f, 420f, widthRatio);

            var shape = _flamethrowerParticle.shape;
            shape.radius = Mathf.Max(0.12f, flamethrowerRadius * 0.6f);
            shape.angle = Mathf.Lerp(22f, 36f, widthRatio);
            shape.length = Mathf.Max(0.6f, flamethrowerRange * 0.35f);
        }

        private void CaptureBaseFlamethrowerRangeIfNeeded()
        {
            if (_baseFlamethrowerRange > 0f)
            {
                return;
            }

            _baseFlamethrowerRange = Mathf.Max(0.1f, flamethrowerRange);
        }

        private void ApplyFlamethrowerRangeBoostAfterBlackhole()
        {
            CaptureBaseFlamethrowerRangeIfNeeded();

            var boostedRange = _baseFlamethrowerRange * Mathf.Max(1f, blackholeFlamethrowerRangeBoostMultiplier);
            flamethrowerRange = boostedRange;
            _isFlamethrowerRangeBoostedByBlackhole = true;
        }

        private void RestoreFlamethrowerRangeIfBoosted()
        {
            if (!_isFlamethrowerRangeBoostedByBlackhole)
            {
                return;
            }

            if (_baseFlamethrowerRange > 0f)
            {
                flamethrowerRange = _baseFlamethrowerRange;
            }

            _isFlamethrowerRangeBoostedByBlackhole = false;
        }

        private static void ConfigureBlackholeVisualMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            var color = new Color(0.12f, 0.12f, 0.16f, 0.42f);

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0f);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            // 표준 셰이더/URP 모두에서 반투명으로 보이도록 공통 블렌드 값을 강제한다.
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = 3000;

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.color = color;
            }
        }

        private void TriggerInvisibility()
        {
            ResolveTarget();
            PlayItemUsePresentation(ItemIdInvisibility, GetTargetPosition());

            if (_invisibilityRoutine != null)
            {
                StopCoroutine(_invisibilityRoutine);
                RestoreRendererColors();
            }

            _isInvisibilityActive = true;
            _invisibilityEndTime = Time.time + invisibilityDurationSec;
            _invisibilityRoutine = StartCoroutine(CoInvisibility());
        }

        private IEnumerator CoInvisibility()
        {
            ApplyInvisibility(invisibilityAlpha);
            SetStatus($"Invisibility: active ({invisibilityDurationSec:0.0}s)");

            yield return new WaitForSeconds(invisibilityDurationSec);

            RestoreRendererColors();
            SetStatus("Invisibility: end");
            _isInvisibilityActive = false;
            EnsureTargetVisibleForPrototype();
            _invisibilityRoutine = null;
        }

        private void TriggerSatelliteStrike()
        {
            ResolveTarget();
            PlayItemUsePresentation(ItemIdSatelliteStrike, GetTargetPosition());
            StartCoroutine(CoSatelliteStrike());
        }

        private IEnumerator CoSatelliteStrike()
        {
            var targetPoint = GetTargetPosition() + GetTargetForward() * 6f;

            var warning = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            warning.name = PrototypeSatelliteWarningName;
            warning.transform.position = targetPoint;
            warning.transform.localScale = new Vector3(satelliteRadius * 2f, 0.05f, satelliteRadius * 2f);

            var warningCollider = warning.GetComponent<Collider>();
            if (warningCollider != null)
            {
                Destroy(warningCollider);
            }

            var warningRenderer = warning.GetComponent<Renderer>();
            if (warningRenderer != null)
            {
                warningRenderer.material.color = new Color(1f, 0.15f, 0.15f, 0.55f);
            }

            SetStatus($"Satellite strike: warning ({satelliteWarningSec:0.0}s)");
            yield return new WaitForSeconds(satelliteWarningSec);

            Destroy(warning);

            var explosion = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            explosion.name = PrototypeSatelliteExplosionName;
            explosion.transform.position = targetPoint + Vector3.up * 0.3f;
            explosion.transform.localScale = Vector3.one * (satelliteRadius * 1.5f);

            var explosionCollider = explosion.GetComponent<Collider>();
            if (explosionCollider != null)
            {
                Destroy(explosionCollider);
            }

            var explosionRenderer = explosion.GetComponent<Renderer>();
            if (explosionRenderer != null)
            {
                explosionRenderer.material.color = new Color(1f, 0.5f, 0.1f, 0.75f);
            }

            var hits = Physics.OverlapSphere(targetPoint, satelliteRadius, physicsMask, QueryTriggerInteraction.Ignore);
            var affected = 0;
            for (var i = 0; i < hits.Length; i++)
            {
                var body = hits[i].attachedRigidbody;
                if (body == null || body.isKinematic)
                {
                    continue;
                }

                body.AddExplosionForce(satelliteForce, targetPoint, satelliteRadius, 1f, ForceMode.Impulse);
                affected++;
            }

            SetStatus($"Satellite strike: boom (affected={affected})");
            yield return new WaitForSeconds(0.25f);
            Destroy(explosion);
        }

        private void SimulateStunDrop()
        {
            ApplyStunDrop();
        }

        public void ResetPrototypeStateFromExternal()
        {
            ResetPrototypeRuntimeState("Reset: position and item state initialized");
        }

        private void ResetPrototypeRuntimeState(string statusMessage)
        {
            // 실행 중인 프로토타입 코루틴과 임시 상태를 모두 정리한다.
            StopAllCoroutines();
            _scaleRoutine = null;
            _invisibilityRoutine = null;
            _isScaleBuffActive = false;
            _scaleBuffEndTime = 0f;
            _scaleBuffLabel = string.Empty;
            _isInvisibilityActive = false;
            _invisibilityEndTime = 0f;

            _isSuperArmorActive = false;
            _superArmorTickTimer = TickTimer.None;
            _superArmorEndTime = 0f;

            _isFlamethrowerActive = false;
            _flamethrowerEndTickTimer = TickTimer.None;
            _flamethrowerTickTimer = TickTimer.None;
            _flamethrowerEndTime = 0f;
            _nextFlamethrowerTickTime = 0f;
            _lastFlamethrowerTickHitCount = 0;
            _lastFlamethrowerTickDamage = 0f;
            _lastFlamethrowerTickStunDamage = 0f;
            StopFlamethrowerParticle();
            StopAllLoopingSfx();
            RestoreFlamethrowerRangeIfBoosted();
            _hitDummy?.ResetDummy();

            RestoreScale();
            RestoreRendererColors();
            EnsureTargetVisibleForPrototype();
            CleanupPrototypeVisuals();
            SetStatus(statusMessage);
        }

        private void ApplyStunDrop()
        {
            // 프로토타입에서는 장착 중인 손 아이템(지속형)만 강제 해제 처리
            if (_isFlamethrowerActive)
            {
                StopFlamethrower("Stunned: dropped held item");
            }
            else
            {
                SetStatus("Stunned: no held item");
            }
        }
    }
}
