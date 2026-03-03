using System.Collections;
using Fusion;
using UnityEngine;

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
                    dummy.ApplyDamage(flamethrowerDamagePerTick, "Flamethrower");
                    damageTotal += flamethrowerDamagePerTick;
                }
                else
                {
                    var dummyInParent = hitTransform.GetComponentInParent<PrototypeDamageDummy>();
                    if (dummyInParent != null)
                    {
                        dummyInParent.ApplyDamage(flamethrowerDamagePerTick, "Flamethrower");
                        damageTotal += flamethrowerDamagePerTick;
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
            SetStatus($"Flamethrower tick: hits={hitCount}, dmg={damageTotal:0.0}");
        }

        private void TriggerBlackholeBomb()
        {
            ResolveTarget();
            StartCoroutine(CoBlackholeBomb());
        }

        private IEnumerator CoBlackholeBomb()
        {
            SetStatus("Blackhole bomb: throw");

            var startPos = GetTargetPosition() + Vector3.up * 1.2f + GetTargetForward() * 0.7f;
            var center = GetTargetPosition() + GetTargetForward() * 6f;

            var bomb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bomb.name = PrototypeBlackholeName;
            bomb.transform.position = startPos;
            bomb.transform.localScale = Vector3.one * 0.45f;

            var collider = bomb.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = bomb.GetComponent<Renderer>();
            if (renderer != null)
            {
                ConfigureBlackholeVisualMaterial(renderer.material);
            }

            var throwElapsed = 0f;
            const float throwDuration = 0.35f;
            while (throwElapsed < throwDuration)
            {
                throwElapsed += Time.deltaTime;
                var t = Mathf.Clamp01(throwElapsed / throwDuration);
                var arcHeight = 1.2f * (1f - Mathf.Pow(2f * t - 1f, 2f));
                bomb.transform.position = Vector3.Lerp(startPos, center, t) + Vector3.up * arcHeight;
                yield return null;
            }

            bomb.transform.position = center;
            SetStatus($"Blackhole bomb: armed ({blackholeDelaySec:0.0}s)");
            yield return new WaitForSeconds(blackholeDelaySec);

            SetStatus($"Blackhole bomb: gravity ({blackholeDurationSec:0.0}s)");
            var elapsed = 0f;
            while (elapsed < blackholeDurationSec)
            {
                elapsed += Time.deltaTime;
                var ramp = Mathf.Clamp01(elapsed / blackholeDurationSec);

                var overlaps = Physics.OverlapSphere(center, blackholeRadius, physicsMask, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < overlaps.Length; i++)
                {
                    var body = overlaps[i].attachedRigidbody;
                    if (body == null || body.isKinematic)
                    {
                        continue;
                    }

                    var toCenter = center - body.worldCenterOfMass;
                    var distance = Mathf.Max(toCenter.magnitude, 0.35f);
                    var pullStrength =
                        (blackholeForce * blackholePullStrengthMultiplier * (0.4f + ramp * 1.6f)) /
                        Mathf.Sqrt(distance);
                    body.AddForce(toCenter.normalized * pullStrength, ForceMode.Acceleration);
                }

                bomb.transform.Rotate(Vector3.up, 220f * Time.deltaTime, Space.World);
                bomb.transform.localScale = Vector3.one * Mathf.Lerp(0.45f, blackholeRadius * 2f, ramp);
                yield return null;
            }

            Destroy(bomb);
            ApplyFlamethrowerRangeBoostAfterBlackhole();
            SetStatus($"Blackhole bomb: end (FlameRange={flamethrowerRange:0.0})");
        }

        private void TriggerGrowth()
        {
            ResolveTarget();
            StartScaleBuff(growthScaleMultiplier, growthDurationSec, "Growth item");
        }

        private void TriggerShrink()
        {
            ResolveTarget();
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
            SetStatus($"Flamethrower: active ({flamethrowerMaxUseSec:0.0}s)");
        }

        private void StopFlamethrower(string reason)
        {
            _isFlamethrowerActive = false;
            _flamethrowerEndTickTimer = TickTimer.None;
            _flamethrowerTickTimer = TickTimer.None;
            StopFlamethrowerParticle();
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
                shape.angle = 12f;
                shape.radius = 0.04f;
                shape.length = 0.3f;

                var limit = _flamethrowerParticle.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.dampen = 0.35f;
            }

            UpdateFlamethrowerParticleProfile();
            if (!_flamethrowerParticle.isPlaying)
            {
                _flamethrowerParticle.Play();
            }
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
            emission.rateOverTime = Mathf.Lerp(170f, 320f, widthRatio);

            var shape = _flamethrowerParticle.shape;
            shape.radius = Mathf.Max(0.1f, flamethrowerRadius * 0.42f);
            shape.angle = Mathf.Lerp(18f, 30f, widthRatio);
            shape.length = flamethrowerRange;
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
            StopFlamethrowerParticle();
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
