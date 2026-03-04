using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
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
                // 투척 구체는 시각 전용이므로 충돌은 비활성화한다.
                bombCollider.enabled = false;

                // 발동 이후 범위 구체 메시는 숨기고 이펙트만 표시한다.
                var bombRenderer = bomb.GetComponent<Renderer>();
                if (bombRenderer != null)
                {
                    bombRenderer.enabled = false;
                }

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

                var overlapCount = Physics.OverlapSphereNonAlloc(
                    center,
                    radius,
                    _blackholeOverlapBuffer,
                    physicsMask,
                    QueryTriggerInteraction.Ignore);

                for (var i = 0; i < overlapCount; i++)
                {
                    var hitCollider = _blackholeOverlapBuffer[i];
                    if (hitCollider == null)
                    {
                        continue;
                    }

                    var body = hitCollider.attachedRigidbody;
                    if (body == null || body.isKinematic)
                    {
                        continue;
                    }

                    var toCenter = center - body.worldCenterOfMass;
                    if (!ShouldPullByBlackhole(hitCollider, body, toCenter))
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

            DisableUnsupportedGrabPassRenderers(instance);
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

            if (HasVfxLikeComponent(hitCollider.transform))
            {
                return false;
            }

            if (IsPlayerTarget(root))
            {
                return true;
            }

            if (HasDamageReceiverComponent(root))
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

            if (root != null && (HasDamageReceiverComponent(root) || HasItemKeyword(root.name) || HasItemLikeComponent(root)))
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
                   objectName.IndexOf("dummy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("pickup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("drop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf("loot", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DisableUnsupportedGrabPassRenderers(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            // URP에서 GrabPass 기반 셰이더는 콘솔 경고를 유발하므로 해당 렌더러를 비활성화한다.
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                var rendererName = renderer.gameObject.name ?? string.Empty;
                if (rendererName.IndexOf("Distort", StringComparison.OrdinalIgnoreCase) >= 0)
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
                    if (shaderName.IndexOf("Distortion Effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        shaderName.IndexOf("Grab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        material.name.IndexOf("Distort", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        renderer.enabled = false;
                        break;
                    }
                }
            }
        }
    }
}
