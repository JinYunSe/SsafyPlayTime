using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace SSAFYPlayTime
{
    public sealed partial class ItemPrototypeHotkeyRunner
    {
        private void ResolveTarget()
        {
            if (targetRoot != null && targetRoot.gameObject.scene.IsValid())
            {
                EnsureHitDummy();
                return;
            }

            if (TryResolveTargetFromRunner())
            {
                EnsureHitDummy();
                return;
            }

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                targetRoot = taggedPlayer.transform;
                CacheBaseScale();
                EnsureTargetVisibleForPrototype();
                EnsureHitDummy();
                return;
            }

            var controller = FindObjectOfType<CharacterController>();
            if (controller != null)
            {
                targetRoot = controller.transform;
                CacheBaseScale();
                EnsureTargetVisibleForPrototype();
                EnsureHitDummy();
                return;
            }

            if (!autoCreateTargetIfMissing)
            {
                return;
            }

            EnsurePrototypeGround();

            var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = "PrototypePlayerTarget";
            dummy.transform.position = new Vector3(0f, 1f, 0f);

            var body = dummy.AddComponent<Rigidbody>();
            body.mass = 80f;
            body.constraints = RigidbodyConstraints.FreezeRotation;

            var dummyRenderer = dummy.GetComponent<Renderer>();
            if (dummyRenderer != null)
            {
                dummyRenderer.material.color = new Color(0.15f, 0.95f, 0.35f, 1f);
            }

            targetRoot = dummy.transform;
            CacheBaseScale();
            EnsureTargetVisibleForPrototype();

            if (autoAttachPrototypeControllerOnDummy)
            {
                EnsurePrototypeController(targetRoot);
            }

            EnsureHitDummy();
            SetStatus("No player found: created PrototypePlayerTarget");
        }

        private void EnsureTargetVisibleForPrototype()
        {
            if (targetRoot == null || _isInvisibilityActive)
            {
                return;
            }

            var renderers = targetRoot.GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;

                var materials = renderer.materials;
                for (var m = 0; m < materials.Length; m++)
                {
                    var material = materials[m];
                    if (material == null)
                    {
                        continue;
                    }

                    if (material.HasProperty("_Color"))
                    {
                        var color = material.color;
                        color.a = 1f;
                        material.color = color;
                    }

                    if (material.HasProperty("_BaseColor"))
                    {
                        var baseColor = material.GetColor("_BaseColor");
                        baseColor.a = 1f;
                        material.SetColor("_BaseColor", baseColor);
                    }
                }
            }
        }


        private void EnsurePrototypeGround()
        {
            if (!autoCreatePrototypeGroundIfMissing)
            {
                return;
            }

            var existing = GameObject.Find("PrototypeGround");
            if (existing != null)
            {
                return;
            }

            // 프로토타입 검증 중 더미 타겟 낙하를 막기 위한 임시 바닥이다.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "PrototypeGround";
            ground.transform.position = new Vector3(0f, prototypeGroundY, 0f);
            ground.transform.localScale = new Vector3(prototypeGroundSize, 1f, prototypeGroundSize);

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.25f, 0.28f, 0.33f, 1f);
            }
        }

        private void EnsurePrototypeController(Transform target)
        {
            if (target == null)
            {
                return;
            }

            var controller = target.GetComponent<PrototypeLocalPlayerController>();
            if (controller == null)
            {
                controller = target.gameObject.AddComponent<PrototypeLocalPlayerController>();
            }

            controller.Configure(target, this);
        }

        private void EnsureHitDummy()
        {
            if (!autoCreateHitDummyIfMissing)
            {
                return;
            }

            if (_hitDummy != null)
            {
                return;
            }

            var existing = GameObject.Find(PrototypeHitDummyName);
            if (existing == null)
            {
                var spawnOrigin = targetRoot != null ? targetRoot.position : Vector3.zero;
                var spawnForward = targetRoot != null ? targetRoot.forward : Vector3.forward;
                spawnForward.y = 0f;
                if (spawnForward.sqrMagnitude < 0.0001f)
                {
                    spawnForward = Vector3.forward;
                }

                existing = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                existing.name = PrototypeHitDummyName;
                existing.transform.position = spawnOrigin + spawnForward.normalized * 7f + Vector3.up * 1f;
                existing.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);

                var body = existing.AddComponent<Rigidbody>();
                body.mass = 120f;
                body.constraints = RigidbodyConstraints.FreezeRotation;

                var renderer = existing.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(0.95f, 0.3f, 0.2f, 1f);
                }
            }

            _hitDummy = existing.GetComponent<PrototypeDamageDummy>();
            if (_hitDummy == null)
            {
                _hitDummy = existing.AddComponent<PrototypeDamageDummy>();
            }

            _hitDummy.SetMaxHp(hitDummyInitialHp);
            _hitDummy.ResetDummy();
        }

        private void CleanupPrototypeVisuals()
        {
            var visualNames = new[] { PrototypeBlackholeName, PrototypeSatelliteWarningName, PrototypeSatelliteExplosionName };
            for (var i = 0; i < visualNames.Length; i++)
            {
                var found = GameObject.Find(visualNames[i]);
                if (found != null)
                {
                    Destroy(found);
                }
            }
        }

        private bool TryResolveTargetFromRunner()
        {
            if (!TryGetRunner(out var runner))
            {
                return false;
            }

            if (!runner.LocalPlayer.IsRealPlayer)
            {
                return false;
            }

            var playerObject = runner.GetPlayerObject(runner.LocalPlayer);
            if (playerObject == null)
            {
                return false;
            }

            targetRoot = playerObject.transform;
            CacheBaseScale();
            EnsureTargetVisibleForPrototype();
            return true;
        }

        private bool TryGetRunner(out NetworkRunner runner)
        {
            if (_runnerCache != null && _runnerCache.IsRunning)
            {
                runner = _runnerCache;
                return true;
            }

            if (Time.unscaledTime < _nextRunnerLookupTime)
            {
                runner = null;
                return false;
            }

            _nextRunnerLookupTime = Time.unscaledTime + RunnerLookupIntervalSec;
            _runnerCache = FindObjectOfType<NetworkRunner>();
            if (_runnerCache != null && _runnerCache.IsRunning)
            {
                runner = _runnerCache;
                return true;
            }

            runner = null;
            return false;
        }

        private void LoadValuesFromItemTableIfNeeded()
        {
            if (!loadValuesFromItemTable || _itemTableApplied)
            {
                return;
            }

            if (!ItemTableCsvLoader.TryLoadFromDisk(itemTableRelativePath, out var rows, out var resolvedPath, out var error))
            {
                Debug.LogWarning($"[ItemPrototype] ItemTable load failed: {error}");
                return;
            }

            ApplyItemTableRow(rows, ItemIdBlackholeBomb, row =>
            {
                blackholeDelaySec = Mathf.Max(3f, row.UseDelaySec);
                blackholeDurationSec = row.DurationSec;
                blackholeRadius = row.Range;
                blackholeForce = row.Force;
            });

            ApplyItemTableRow(rows, ItemIdGrowth, row =>
            {
                growthDurationSec = row.DurationSec;
                growthScaleMultiplier = row.ScaleMultiplier;
            });

            ApplyItemTableRow(rows, ItemIdShrink, row =>
            {
                shrinkDurationSec = row.DurationSec;
                shrinkScaleMultiplier = row.ScaleMultiplier;
            });

            ApplyItemTableRow(rows, ItemIdAmericano, row =>
            {
                superArmorDurationSec = row.DurationSec;
            });

            ApplyItemTableRow(rows, ItemIdFlamethrower, row =>
            {
                if (row.MaxActiveUseSec > 0f)
                {
                    flamethrowerMaxUseSec = row.MaxActiveUseSec;
                }

                if (row.Range > 0f)
                {
                    flamethrowerRange = row.Range;
                }

                if (row.BaseDamage > 0f)
                {
                    flamethrowerDamagePerTick = row.BaseDamage;
                }

                if (row.Force > 0f)
                {
                    flamethrowerPushForce = row.Force;
                }

                if (row.TickIntervalSec > 0f)
                {
                    // 요구사항: 초당 5회 피해 적용(0.2s 간격)
                    flamethrowerTickIntervalSec = 0.2f;
                }
            });

            _baseFlamethrowerRange = Mathf.Max(0.1f, flamethrowerRange);

            ApplyItemTableRow(rows, ItemIdInvisibility, row =>
            {
                invisibilityDurationSec = row.DurationSec;
            });

            ApplyItemTableRow(rows, ItemIdSatelliteStrike, row =>
            {
                satelliteWarningSec = row.WarningTimeSec;
                satelliteRadius = row.Range;
                satelliteForce = row.Force;
            });

            _itemTableRows = rows;
            _itemTableApplied = true;
            Debug.Log($"[ItemPrototype] ItemTable applied: {resolvedPath}");
        }

        private void LoadAssetMetadataTablesIfNeeded()
        {
            if (!loadAssetMetadataTables || _assetTablesApplied)
            {
                return;
            }

            if (!SoundAssetTableCsvLoader.TryLoadFromDisk(
                    soundAssetTableRelativePath,
                    out var soundRows,
                    out var soundPath,
                    out var soundError))
            {
                Debug.LogWarning($"[ItemPrototype] SoundAssetTable load failed: {soundError}");
                return;
            }

            if (!VfxAssetTableCsvLoader.TryLoadFromDisk(
                    vfxAssetTableRelativePath,
                    out var vfxRows,
                    out var vfxPath,
                    out var vfxError))
            {
                Debug.LogWarning($"[ItemPrototype] VfxAssetTable load failed: {vfxError}");
                return;
            }

            if (!ItemPresentationTableCsvLoader.TryLoadFromDisk(
                    itemPresentationTableRelativePath,
                    out var presentationRows,
                    out var presentationPath,
                    out var presentationError))
            {
                Debug.LogWarning($"[ItemPrototype] ItemPresentationTable load failed: {presentationError}");
                return;
            }

            _dataCatalog = new ItemPrototypeDataCatalog(soundRows, vfxRows, presentationRows);
            _assetTablesApplied = true;

            var validationWarnings = _dataCatalog.ValidateReferences(_itemTableRows);
            for (var i = 0; i < validationWarnings.Count; i++)
            {
                Debug.LogWarning($"[ItemPrototype] Data validation: {validationWarnings[i]}");
            }

            Debug.Log(
                $"[ItemPrototype] AssetTables applied: sound={soundPath}, vfx={vfxPath}, presentation={presentationPath}, warnings={validationWarnings.Count}");
        }

        private static void ApplyItemTableRow(
            IReadOnlyDictionary<string, ItemTableCsvLoader.Row> rows,
            string itemId,
            System.Action<ItemTableCsvLoader.Row> applyAction)
        {
            if (!rows.TryGetValue(itemId, out var row))
            {
                return;
            }

            applyAction?.Invoke(row);
        }
    }
}
