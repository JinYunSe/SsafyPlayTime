/*
 * 파일 개요:
 * - ItemGameplayRunner.Flamethrower 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Skills 계층에서 블랙홀, 화염방사기, 위성폭격 같은 개별 스킬의 테스트 실행 로직을 담당한다.
 * - 아이템별 연출과 실험용 동작을 다루지만, 공통 상태 전이 규칙은 Runtime 계층과 일관되게 유지해야 한다.
 */
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void TickFlamethrower(in FlamethrowerTickRequest request)
        {
            var forward = request.Forward.sqrMagnitude > 0.0001f ? request.Forward.normalized : GetTargetForward();
            var baseRange = Mathf.Max(0f, request.Range * Mathf.Max(1f, _flamethrowerRangeMultiplier));
            var damageRange = baseRange * Mathf.Clamp(flamethrowerDamageRangeMultiplier, 0.01f, 1f);
            var radius = Mathf.Max(0f, request.Radius);
            var origin = request.Origin;
            _flamethrowerVisualActive = true;
            _flamethrowerVisualRange = damageRange;
            _flamethrowerVisualRadius = radius;

            UpdateFlamethrowerParticle(origin, forward, damageRange, radius);

            var start = origin;
            var end = origin + forward * damageRange;
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
                    TryApplyDamageToTarget(hitTransform, request.DamagePerTick, request.StunDamagePerTick, "Flamethrower");
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

        // 데미지 틱 주기와 분리해서 매 프레임 이펙트 위치를 갱신해 "분사" 느낌을 유지한다.
        private void UpdateFlamethrowerVisualFollow()
        {
            if (!_flamethrowerVisualActive || _flamethrowerFxRoot == null)
            {
                return;
            }

            var forward = GetTargetForward();
            var origin = GetTargetPosition() + Vector3.up * 1.2f + forward * 0.7f;
            UpdateFlamethrowerParticle(origin, forward, _flamethrowerVisualRange, _flamethrowerVisualRadius);
        }

        private void EnsureFlamethrowerParticle()
        {
            if (_flamethrowerFxRoot != null)
            {
                PlayFlamethrowerParticles();
                return;
            }

            var prefab = TryLoadFlamethrowerEffectPrefab();
            if (prefab != null)
            {
                _flamethrowerFxRoot = Instantiate(prefab, transform);
                _flamethrowerFxRoot.name = "Item_FlamethrowerFx";
                _flamethrowerFxRoot.transform.localPosition = Vector3.zero;
                _flamethrowerFxRoot.transform.localRotation = Quaternion.identity;
                _flamethrowerFxRoot.transform.localScale = Vector3.one * Mathf.Max(0.01f, flamethrowerEffectScale);

                var colliders = _flamethrowerFxRoot.GetComponentsInChildren<Collider>(true);
                for (var i = 0; i < colliders.Length; i++)
                {
                    colliders[i].enabled = false;
                }

                ConfigureFlamethrowerPrefabForSpray();
                ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(_flamethrowerFxRoot);
                DisableUnsupportedGrabPassRenderers(_flamethrowerFxRoot);
                _flamethrowerParticles = _flamethrowerFxRoot.GetComponentsInChildren<ParticleSystem>(true);
                _flamethrowerParticle = _flamethrowerParticles.Length > 0 ? _flamethrowerParticles[0] : null;
                ConfigureFlamethrowerParticlesForContinuousSpray();
                PlayFlamethrowerParticles();
                return;
            }

            // 프리팹 로드 실패 시에만 기존 런타임 파티클을 fallback으로 생성한다.
            var fx = new GameObject("Item_FlamethrowerFx");
            fx.transform.SetParent(transform, false);
            _flamethrowerFxRoot = fx;
            _flamethrowerParticle = fx.AddComponent<ParticleSystem>();
            _flamethrowerParticles = new[] { _flamethrowerParticle };

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

            ConfigureFlamethrowerParticlesForContinuousSpray();
            _flamethrowerParticle.Play();
        }

        private void UpdateFlamethrowerParticle(Vector3 origin, Vector3 forward, float range, float radius)
        {
            EnsureFlamethrowerParticle();
            if (_flamethrowerFxRoot == null)
            {
                return;
            }

            _flamethrowerFxRoot.transform.position = origin;
            _flamethrowerFxRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            _flamethrowerFxRoot.transform.localScale = Vector3.one * Mathf.Max(0.01f, flamethrowerEffectScale);

            TuneFlamethrowerParticlesByRange(range, radius);
        }

        private void StopFlamethrowerParticle()
        {
            _flamethrowerVisualActive = false;

            if (_flamethrowerParticles == null || _flamethrowerParticles.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _flamethrowerParticles.Length; i++)
            {
                var particle = _flamethrowerParticles[i];
                if (particle == null)
                {
                    continue;
                }

                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void PlayFlamethrowerParticles()
        {
            if (_flamethrowerParticles == null || _flamethrowerParticles.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _flamethrowerParticles.Length; i++)
            {
                var particle = _flamethrowerParticles[i];
                if (particle == null)
                {
                    continue;
                }

                if (!particle.isPlaying)
                {
                    particle.Play(true);
                }
            }
        }

        private void ConfigureFlamethrowerPrefabForSpray()
        {
            if (_flamethrowerFxRoot == null)
            {
                return;
            }

            var rigidbody = _flamethrowerFxRoot.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                if (!rigidbody.isKinematic)
                {
                    rigidbody.velocity = Vector3.zero;
                    rigidbody.angularVelocity = Vector3.zero;
                }

                rigidbody.isKinematic = true;
                rigidbody.useGravity = false;
            }

            // 투사체 전용 스크립트는 비활성화하고 파티클만 사용한다.
            var behaviours = _flamethrowerFxRoot.GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                behaviour.enabled = false;
            }
        }

        private void ConfigureFlamethrowerParticlesForContinuousSpray()
        {
            if (_flamethrowerParticles == null || _flamethrowerParticles.Length == 0)
            {
                return;
            }

            for (var i = 0; i < _flamethrowerParticles.Length; i++)
            {
                var particle = _flamethrowerParticles[i];
                if (particle == null)
                {
                    continue;
                }

                var main = particle.main;
                main.loop = true;
                main.playOnAwake = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            }
        }

        private void TuneFlamethrowerParticlesByRange(float range, float radius)
        {
            if (_flamethrowerParticles == null || _flamethrowerParticles.Length == 0)
            {
                return;
            }

            var speed = Mathf.Max(4f, range * 2.2f);
            var lifetime = Mathf.Max(0.08f, range / Mathf.Max(0.01f, speed));
            var minSize = Mathf.Max(0.08f, radius * 0.35f);
            var maxSize = Mathf.Max(0.16f, radius * 0.65f);

            for (var i = 0; i < _flamethrowerParticles.Length; i++)
            {
                var particle = _flamethrowerParticles[i];
                if (particle == null)
                {
                    continue;
                }

                var main = particle.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.75f, speed);
                main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime * 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);

                var shape = particle.shape;
                if (shape.enabled && shape.shapeType == ParticleSystemShapeType.Cone)
                {
                    shape.length = Mathf.Max(0.1f, range * 0.2f);
                }
            }
        }

        private GameObject TryLoadFlamethrowerEffectPrefab()
        {
            if (_flamethrowerEffectPrefabCache != null)
            {
                return _flamethrowerEffectPrefabCache;
            }

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(flamethrowerEffectPrefabAssetPath))
            {
                _flamethrowerEffectPrefabCache = AssetDatabase.LoadAssetAtPath<GameObject>(flamethrowerEffectPrefabAssetPath);
                if (_flamethrowerEffectPrefabCache != null)
                {
                    return _flamethrowerEffectPrefabCache;
                }
            }
#endif

            _flamethrowerEffectPrefabCache = Resources.Load<GameObject>("Polygon Arsenal/Prefabs/Misc/FlamethrowerBlocky");
            if (_flamethrowerEffectPrefabCache != null)
            {
                return _flamethrowerEffectPrefabCache;
            }

            if (TryGetVfxRow("VFX_ITEM_FLAME", out var row))
            {
                _flamethrowerEffectPrefabCache = LoadVfxPrefabFromAssetKey(row.AssetKey);
            }

            return _flamethrowerEffectPrefabCache;
        }
    }
}

