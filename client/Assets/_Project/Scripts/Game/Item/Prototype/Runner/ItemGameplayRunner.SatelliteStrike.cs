using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private const float SatelliteStrikeDefaultChargeDurationSec = 3f;
        private const float SatelliteStrikeDefaultBeamDurationSec = 2f;
        private const float SatelliteStrikeDefaultHealthDamage = 50f;
        private const float SatelliteStrikeDefaultStunDamage = 100f;
        private const float SatelliteStrikeBeamDamageHeight = 8f;
        private const float SatelliteStrikeBeamTickSec = 0.25f;
        private const float SatelliteStrikeBeamFallbackHeight = 24f;
        private const float SatelliteStrikeGroundOffset = 0.02f;
        private const float SatelliteStrikeExplosionUpwardModifier = 0.35f;
        private const float SatelliteStrikeProjectileLifetimeSec = 3f;
        private const float SatelliteStrikeProjectileCollisionRadius = 0.18f;

        private const string SatelliteStrikeFxRootName = "Item_SatelliteStrikeFx";
        private const string SatelliteStrikeChargeupName = "BeamChargeup";
        private const string SatelliteStrikeCloudName = "BeamCloud";
        private const string SatelliteStrikeCylinderName = "BeamCylinder";

        private readonly Collider[] _satelliteStrikeOverlapBuffer = new Collider[256];
        private readonly HashSet<Coroutine> _activeSatelliteStrikeRoutines = new();
        private readonly HashSet<int> _satelliteDamageTargetIds = new();
        private readonly HashSet<int> _satelliteForceBodyIds = new();

        private IEnumerator CoSatelliteStrikeTracked(SatelliteStrikeRequest request, Action onFinished)
        {
            yield return CoSatelliteStrike(request);
            onFinished?.Invoke();
        }

        private IEnumerator CoSatelliteStrike(SatelliteStrikeRequest request)
        {
            var radius = Mathf.Max(0.1f, request.Radius);
            var chargeDuration = request.WarningSec > 0f
                ? request.WarningSec
                : SatelliteStrikeDefaultChargeDurationSec;
            var beamDuration = request.DurationSec > 0f
                ? request.DurationSec
                : SatelliteStrikeDefaultBeamDurationSec;
            var totalHealthDamage = request.BaseDamage > 0f
                ? request.BaseDamage
                : SatelliteStrikeDefaultHealthDamage;
            var totalStunDamage = request.StunDamage > 0f
                ? request.StunDamage
                : SatelliteStrikeDefaultStunDamage;
            var explosionForce = Mathf.Max(0f, request.Force);

            var fallbackCenter = ResolveSatelliteStrikeGroundCenter(request.Center);
            var throwForward = request.Forward.sqrMagnitude > 0.0001f
                ? request.Forward.normalized
                : GetTargetForward();
            var projectileStart = request.Origin + Vector3.up * 1.2f + throwForward * 0.7f;
            var projectile = CreateSatelliteProjectile(projectileStart);
            var strikeCenter = fallbackCenter;

            yield return MoveSatelliteProjectile(
                projectile,
                projectileStart,
                throwForward,
                fallbackCenter,
                landedCenter => strikeCenter = landedCenter);

            if (projectile != null)
            {
                Destroy(projectile);
            }

            var effectRoot = CreateSatelliteEffectRoot(strikeCenter);
            AttachSatelliteChargeVisuals(effectRoot, strikeCenter, radius);

            if (chargeDuration > 0f)
            {
                yield return new WaitForSeconds(chargeDuration);
            }

            DestroySatelliteChild(effectRoot, SatelliteStrikeChargeupName);
            AttachSatelliteBeamVisual(effectRoot, strikeCenter, radius);

            yield return ApplySatelliteBeamDamage(
                strikeCenter,
                radius,
                beamDuration,
                totalHealthDamage,
                totalStunDamage,
                explosionForce);

            if (effectRoot != null)
            {
                Destroy(effectRoot);
            }
        }

        private IEnumerator MoveSatelliteProjectile(
            GameObject projectile,
            Vector3 start,
            Vector3 throwForward,
            Vector3 fallbackCenter,
            Action<Vector3> onLanded)
        {
            var direction = (throwForward + Vector3.up * blackholeThrowArc).normalized;
            var velocity = direction * Mathf.Max(0.1f, blackholeThrowSpeed);
            var gravity = Physics.gravity;
            var elapsed = 0f;
            var current = start;

            while (elapsed < SatelliteStrikeProjectileLifetimeSec)
            {
                var step = Mathf.Max(0.001f, Time.deltaTime);
                var nextVelocity = velocity + gravity * step;
                var next = current + velocity * step;

                if (Physics.SphereCast(
                        current,
                        SatelliteStrikeProjectileCollisionRadius,
                        (next - current).normalized,
                        out var hit,
                        Vector3.Distance(current, next),
                        physicsMask,
                        QueryTriggerInteraction.Ignore))
                {
                    var landed = hit.point + Vector3.up * SatelliteStrikeGroundOffset;
                    if (projectile != null)
                    {
                        projectile.transform.position = landed;
                    }

                    onLanded?.Invoke(landed);
                    yield break;
                }

                if (projectile != null)
                {
                    projectile.transform.position = next;
                    var lookDirection = nextVelocity.sqrMagnitude > 0.0001f ? nextVelocity.normalized : velocity.normalized;
                    if (lookDirection.sqrMagnitude > 0.0001f)
                    {
                        projectile.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
                    }
                }

                current = next;
                velocity = nextVelocity;
                elapsed += step;
                yield return null;
            }

            onLanded?.Invoke(fallbackCenter);
        }

        private IEnumerator ApplySatelliteBeamDamage(
            Vector3 center,
            float radius,
            float beamDuration,
            float totalHealthDamage,
            float totalStunDamage,
            float explosionForce)
        {
            var duration = Mathf.Max(0.01f, beamDuration);
            var tickCount = Mathf.Max(1, Mathf.CeilToInt(duration / SatelliteStrikeBeamTickSec));
            var damagePerTick = totalHealthDamage / tickCount;
            var stunPerTick = totalStunDamage / tickCount;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                ApplySatelliteBeamImpact(
                    center,
                    radius,
                    SatelliteStrikeBeamDamageHeight,
                    damagePerTick,
                    stunPerTick,
                    explosionForce);

                var waitSec = Mathf.Min(SatelliteStrikeBeamTickSec, duration - elapsed);
                elapsed += waitSec;
                if (waitSec > 0f)
                {
                    yield return new WaitForSeconds(waitSec);
                }
                else
                {
                    yield return null;
                }
            }
        }

        private Vector3 ResolveSatelliteStrikeGroundCenter(Vector3 requestedCenter)
        {
            var rayOrigin = requestedCenter + Vector3.up * 30f;
            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out var hit,
                    80f,
                    physicsMask,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * SatelliteStrikeGroundOffset;
            }

            return requestedCenter + Vector3.up * SatelliteStrikeGroundOffset;
        }

        private GameObject CreateSatelliteProjectile(Vector3 startPosition)
        {
            var prefab = TryLoadSatelliteProjectilePrefab();
            if (prefab != null)
            {
                var projectileVisual = Instantiate(prefab, startPosition, prefab.transform.rotation);
                projectileVisual.name = "Item_SatelliteStrikeProjectile";
                projectileVisual.transform.localScale =
                    Vector3.one * Mathf.Max(0.001f, satelliteProjectileVisualScale);
                PrepareSatelliteVisualInstance(projectileVisual);
                return projectileVisual;
            }

            var projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "Item_SatelliteStrikeProjectile";
            projectile.transform.position = startPosition;
            projectile.transform.localScale = Vector3.one * 0.3f;
            PrepareSatelliteVisualInstance(projectile);
            ApplyTransparentColor(projectile, new Color(0.95f, 0.3f, 0.3f, 0.7f));
            return projectile;
        }

        private static GameObject CreateSatelliteEffectRoot(Vector3 center)
        {
            var root = new GameObject(SatelliteStrikeFxRootName);
            root.transform.position = center;
            return root;
        }

        private void AttachSatelliteChargeVisuals(GameObject effectRoot, Vector3 center, float radius)
        {
            if (effectRoot == null)
            {
                return;
            }

            TryAttachSatelliteBeamChild(
                effectRoot.transform,
                TryLoadSatelliteBeamChargeupPrefab(),
                SatelliteStrikeChargeupName,
                center,
                GetSatelliteChargeupScale(radius));
            TryAttachSatelliteBeamChild(
                effectRoot.transform,
                TryLoadSatelliteBeamCloudPrefab(),
                SatelliteStrikeCloudName,
                center,
                GetSatelliteCloudScale(radius));
        }

        private void AttachSatelliteBeamVisual(GameObject effectRoot, Vector3 center, float radius)
        {
            if (effectRoot == null)
            {
                return;
            }

            if (effectRoot.transform.Find(SatelliteStrikeCloudName) == null)
            {
                TryAttachSatelliteBeamChild(
                    effectRoot.transform,
                    TryLoadSatelliteBeamCloudPrefab(),
                    SatelliteStrikeCloudName,
                    center,
                    GetSatelliteCloudScale(radius));
            }

            if (TryAttachSatelliteBeamChild(
                    effectRoot.transform,
                    TryLoadSatelliteBeamCylinderPrefab(),
                    SatelliteStrikeCylinderName,
                    center,
                    GetSatelliteCylinderScale(radius)))
            {
                return;
            }

            // 에셋을 불러오지 못하는 경우에만 임시 실린더로 빔 범위를 표시한다.
            var fallbackBeam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fallbackBeam.name = SatelliteStrikeCylinderName;
            fallbackBeam.transform.position = center + Vector3.up * (SatelliteStrikeBeamFallbackHeight * 0.5f);
            fallbackBeam.transform.localScale = new Vector3(
                Mathf.Max(0.35f, radius * 0.28f),
                SatelliteStrikeBeamFallbackHeight * 0.5f,
                Mathf.Max(0.35f, radius * 0.28f));
            fallbackBeam.transform.SetParent(effectRoot.transform, true);
            PrepareSatelliteVisualInstance(fallbackBeam);
            ApplyTransparentColor(fallbackBeam, new Color(0.35f, 0.7f, 1f, 0.45f));
        }

        private Vector3 GetSatelliteChargeupScale(float radius)
        {
            return Vector3.one * Mathf.Max(0.001f, satelliteBeamChargeupScale * Mathf.Max(1f, radius * 0.35f));
        }

        private Vector3 GetSatelliteCloudScale(float radius)
        {
            return Vector3.one * Mathf.Max(0.001f, satelliteBeamCloudScale * Mathf.Max(1f, radius * 0.3f));
        }

        private Vector3 GetSatelliteCylinderScale(float radius)
        {
            return Vector3.one * Mathf.Max(0.001f, satelliteBeamCylinderScale * Mathf.Max(1f, radius * 0.3f));
        }

        private void ApplySatelliteBeamImpact(
            Vector3 center,
            float radius,
            float height,
            float healthDamage,
            float stunDamage,
            float explosionForce)
        {
            var capsuleOffset = Mathf.Max(0f, (height * 0.5f) - radius);
            var bottom = center + Vector3.down * capsuleOffset;
            var top = center + Vector3.up * capsuleOffset;
            var overlapCount = Physics.OverlapCapsuleNonAlloc(
                bottom,
                top,
                radius,
                _satelliteStrikeOverlapBuffer,
                physicsMask,
                QueryTriggerInteraction.Ignore);

            _satelliteDamageTargetIds.Clear();
            _satelliteForceBodyIds.Clear();

            for (var i = 0; i < overlapCount; i++)
            {
                var hitCollider = _satelliteStrikeOverlapBuffer[i];
                if (hitCollider == null)
                {
                    continue;
                }

                var targetRoot = ResolveSatelliteStrikeTargetRoot(hitCollider);
                if (targetRoot != null && _satelliteDamageTargetIds.Add(targetRoot.GetInstanceID()))
                {
                    ApplySatelliteStrikeDamage(targetRoot, healthDamage, stunDamage, explosionForce);
                }

                var body = hitCollider.attachedRigidbody;
                if (body != null && !body.isKinematic && _satelliteForceBodyIds.Add(body.GetInstanceID()))
                {
                    ApplySatelliteExplosionForce(body, center, explosionForce, radius);
                }
            }
        }

        private void ApplySatelliteStrikeDamage(Transform targetRoot, float healthDamage, float stunDamage, float explosionForce)
        {
            if (targetRoot == null)
            {
                return;
            }

            var healthApplied = false;
            var stunApplied = false;
            var healthInt = Mathf.Max(0, Mathf.RoundToInt(healthDamage));

            var playerStats = targetRoot.GetComponentInParent<PlayerStats>();
            if (playerStats != null && healthInt > 0)
            {
                playerStats.TakeDamage(healthInt);
                healthApplied = true;
            }

            var networkPlayer = targetRoot.GetComponentInParent<NetworkPlayer>();
            if (networkPlayer != null && stunDamage > 0f)
            {
                // 빔에 닿아 있는 동안 스턴 게이지가 계속 누적되도록 틱 단위로 적용한다.
                networkPlayer.ApplyStunDamage(stunDamage, 1f, 0f, explosionForce);
                stunApplied = true;
            }

            var pendingHealth = healthApplied ? 0f : healthDamage;
            var pendingStun = stunApplied ? 0f : stunDamage;
            if (pendingHealth > 0f || pendingStun > 0f)
            {
                TryApplyDamageToTarget(targetRoot, pendingHealth, pendingStun, "SatelliteStrike");
            }
        }

        private static Transform ResolveSatelliteStrikeTargetRoot(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return null;
            }

            var networkPlayer = hitCollider.GetComponentInParent<NetworkPlayer>();
            if (networkPlayer != null)
            {
                return networkPlayer.transform;
            }

            var playerStats = hitCollider.GetComponentInParent<PlayerStats>();
            if (playerStats != null)
            {
                return playerStats.transform;
            }

            if (hitCollider.attachedRigidbody != null)
            {
                return hitCollider.attachedRigidbody.transform.root;
            }

            return hitCollider.transform.root;
        }

        private static void ApplySatelliteExplosionForce(Rigidbody body, Vector3 center, float force, float radius)
        {
            if (body == null || body.isKinematic || force <= 0f)
            {
                return;
            }

            var offset = body.worldCenterOfMass - center;
            var planarOffset = new Vector3(offset.x, 0f, offset.z);
            var distance = Mathf.Max(0.1f, planarOffset.magnitude);
            var falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, radius));
            var direction = planarOffset.sqrMagnitude > 0.0001f
                ? planarOffset.normalized
                : UnityEngine.Random.insideUnitSphere.normalized;
            direction.y = Mathf.Max(0.15f, SatelliteStrikeExplosionUpwardModifier);
            direction.Normalize();

            body.AddForce(direction * (force * Mathf.Max(0.15f, falloff)), ForceMode.VelocityChange);
        }

        private void PrepareSatelliteVisualInstance(GameObject visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var colliders = visualRoot.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            var bodies = visualRoot.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < bodies.Length; i++)
            {
                var body = bodies[i];
                body.isKinematic = true;
                body.useGravity = false;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private bool TryAttachSatelliteBeamChild(
            Transform parent,
            GameObject prefab,
            string childName,
            Vector3 worldPosition,
            Vector3 worldScale)
        {
            if (parent == null || prefab == null)
            {
                return false;
            }

            var instance = Instantiate(prefab, worldPosition, prefab.transform.rotation, parent);
            instance.name = childName;
            instance.transform.localScale = worldScale;
            PrepareSatelliteVisualInstance(instance);
            return true;
        }

        private static void DestroySatelliteChild(GameObject effectRoot, string childName)
        {
            if (effectRoot == null || string.IsNullOrWhiteSpace(childName))
            {
                return;
            }

            var child = effectRoot.transform.Find(childName);
            if (child != null)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private GameObject TryLoadSatelliteProjectilePrefab()
        {
            if (_satelliteProjectilePrefabCache != null)
            {
                return _satelliteProjectilePrefabCache;
            }

            TryLoadPrefabFromAssetPath(satelliteProjectilePrefabAssetPath, out _satelliteProjectilePrefabCache);
            return _satelliteProjectilePrefabCache;
        }

        private GameObject TryLoadSatelliteBeamChargeupPrefab()
        {
            if (_satelliteBeamChargeupPrefabCache != null)
            {
                return _satelliteBeamChargeupPrefabCache;
            }

            TryLoadPrefabFromAssetPath(satelliteBeamChargeupPrefabAssetPath, out _satelliteBeamChargeupPrefabCache);
            return _satelliteBeamChargeupPrefabCache;
        }

        private GameObject TryLoadSatelliteBeamCloudPrefab()
        {
            if (_satelliteBeamCloudPrefabCache != null)
            {
                return _satelliteBeamCloudPrefabCache;
            }

            TryLoadPrefabFromAssetPath(satelliteBeamCloudPrefabAssetPath, out _satelliteBeamCloudPrefabCache);
            return _satelliteBeamCloudPrefabCache;
        }

        private GameObject TryLoadSatelliteBeamCylinderPrefab()
        {
            if (_satelliteBeamCylinderPrefabCache != null)
            {
                return _satelliteBeamCylinderPrefabCache;
            }

            TryLoadPrefabFromAssetPath(satelliteBeamCylinderPrefabAssetPath, out _satelliteBeamCylinderPrefabCache);
            return _satelliteBeamCylinderPrefabCache;
        }

        private static void ApplyTransparentColor(GameObject target, Color color)
        {
            if (target == null)
            {
                return;
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var material = renderer.material;
            if (material == null)
            {
                renderer.enabled = false;
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
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3f);
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void StopAllSatelliteStrikeRoutines()
        {
            if (_activeSatelliteStrikeRoutines.Count == 0)
            {
                return;
            }

            foreach (var routine in _activeSatelliteStrikeRoutines)
            {
                if (routine == null)
                {
                    continue;
                }

                StopCoroutine(routine);
            }

            _activeSatelliteStrikeRoutines.Clear();
        }
    }
}
