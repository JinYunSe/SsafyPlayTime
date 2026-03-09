using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private const float SatelliteStrikeDefaultWarningSec = 3f;
        private const float SatelliteStrikeProjectileTravelSec = 0.55f;
        private const float SatelliteStrikeProjectileArcHeight = 2.5f;
        private const float SatelliteStrikeDefaultHealthDamage = 50f;
        private const float SatelliteStrikeDefaultStunDamage = 100f;
        private const float SatelliteStrikeWarningHeight = 0.12f;
        private const float SatelliteStrikeLaserDurationSec = 0.75f;
        private const float SatelliteStrikeLaserHeight = 24f;
        private const float SatelliteStrikeGroundOffset = 0.02f;
        private const float SatelliteStrikeExplosionUpwardModifier = 0.35f;

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
            var warningSec = request.WarningSec > 0f ? request.WarningSec : SatelliteStrikeDefaultWarningSec;
            var healthDamage = request.BaseDamage > 0f ? request.BaseDamage : SatelliteStrikeDefaultHealthDamage;
            var stunDamage = SatelliteStrikeDefaultStunDamage;
            var explosionForce = Mathf.Max(0f, request.Force);

            var strikeCenter = ResolveSatelliteStrikeGroundCenter(request.Center);
            var projectileStart = GetTargetPosition() + Vector3.up * 1.2f + GetTargetForward() * 0.7f;
            var projectile = CreateSatelliteProjectile(projectileStart);

            yield return MoveSatelliteProjectile(projectile, projectileStart, strikeCenter);

            if (projectile != null)
            {
                Destroy(projectile);
            }

            var warning = CreateSatelliteWarningIndicator(strikeCenter, radius);
            yield return new WaitForSeconds(warningSec);

            var laser = CreateSatelliteLaserVisual(strikeCenter, radius);
            ApplySatelliteStrikeImpact(strikeCenter, radius, healthDamage, stunDamage, explosionForce);
            yield return new WaitForSeconds(SatelliteStrikeLaserDurationSec);

            if (laser != null)
            {
                Destroy(laser);
            }

            if (warning != null)
            {
                Destroy(warning);
            }
        }

        private IEnumerator MoveSatelliteProjectile(GameObject projectile, Vector3 start, Vector3 end)
        {
            var duration = Mathf.Max(0.05f, SatelliteStrikeProjectileTravelSec);
            var controlPoint = Vector3.Lerp(start, end, 0.5f) + Vector3.up * SatelliteStrikeProjectileArcHeight;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var point = EvaluateQuadraticBezier(start, controlPoint, end, t);
                if (projectile != null)
                {
                    projectile.transform.position = point;
                }

                yield return null;
            }

            if (projectile != null)
            {
                projectile.transform.position = end;
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

        private static Vector3 EvaluateQuadraticBezier(Vector3 start, Vector3 control, Vector3 end, float t)
        {
            var oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * start +
                   2f * oneMinusT * t * control +
                   t * t * end;
        }

        private static GameObject CreateSatelliteProjectile(Vector3 startPosition)
        {
            var projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "Item_SatelliteStrikeProjectile";
            projectile.transform.position = startPosition;
            projectile.transform.localScale = Vector3.one * 0.3f;

            if (projectile.TryGetComponent<Collider>(out var collider))
            {
                collider.enabled = false;
            }

            ApplyTransparentColor(projectile, new Color(0.95f, 0.3f, 0.3f, 0.7f));
            return projectile;
        }

        private static GameObject CreateSatelliteWarningIndicator(Vector3 center, float radius)
        {
            var warning = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            warning.name = "Item_SatelliteStrikeWarning";
            warning.transform.position = center + Vector3.up * (SatelliteStrikeWarningHeight * 0.5f);
            warning.transform.localScale = new Vector3(radius * 2f, SatelliteStrikeWarningHeight * 0.5f, radius * 2f);

            if (warning.TryGetComponent<Collider>(out var collider))
            {
                collider.enabled = false;
            }

            ApplyTransparentColor(warning, new Color(1f, 0.15f, 0.15f, 0.22f));
            return warning;
        }

        private static GameObject CreateSatelliteLaserVisual(Vector3 center, float radius)
        {
            var laser = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            laser.name = "Item_SatelliteStrikeLaser";
            laser.transform.position = center + Vector3.up * (SatelliteStrikeLaserHeight * 0.5f);
            laser.transform.localScale = new Vector3(
                Mathf.Max(0.35f, radius * 0.28f),
                SatelliteStrikeLaserHeight * 0.5f,
                Mathf.Max(0.35f, radius * 0.28f));

            if (laser.TryGetComponent<Collider>(out var collider))
            {
                collider.enabled = false;
            }

            ApplyTransparentColor(laser, new Color(1f, 0.3f, 0.3f, 0.45f));
            return laser;
        }

        private void ApplySatelliteStrikeImpact(Vector3 center, float radius, float healthDamage, float stunDamage, float explosionForce)
        {
            var overlapCount = Physics.OverlapSphereNonAlloc(
                center,
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
                // 한국어: 위성 레이저는 즉시 기절시키는 강한 스턴 데미지를 준다.
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
