using System;
using System.IO;
using Fusion;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void OnSfxRequested(string sfxId, Vector3 worldPosition, bool loop)
        {
            if (!enableRuntimePresentation)
            {
                return;
            }

            if (IsRuntimeAudioOutputDisabled())
            {
                return;
            }

            EnsureAudioListenerGuard();

            if (!TryGetSoundRow(sfxId, out var row))
            {
                return;
            }

            var clip = LoadAudioClipFromAssetKey(row.AssetKey);
            if (clip == null)
            {
                return;
            }

            var volume = Mathf.Clamp01(row.DefaultVolume > 0f ? row.DefaultVolume : defaultSfxVolume);
            if (loop || row.Loop)
            {
                PlayLoopingSfx(sfxId, clip, worldPosition, volume, row.Spatial);
                return;
            }

            PlayOneShotSfx(clip, worldPosition, volume, row.Spatial);
        }

        private void OnVfxRequested(string vfxId, Vector3 worldPosition)
        {
            if (!enableRuntimePresentation)
            {
                return;
            }

            if (!TryGetVfxRow(vfxId, out var row))
            {
                return;
            }

            var prefab = LoadVfxPrefabFromAssetKey(row.AssetKey);
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab);
            instance.transform.position = worldPosition;
            instance.transform.rotation = Quaternion.LookRotation(GetTargetForward(), Vector3.up);
            instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, row.Scale);

            if (row.LifetimeSec > 0f)
            {
                Destroy(instance, row.LifetimeSec);
            }

            DisableUnsupportedGrabPassRenderers(instance);
        }

        private bool CanRunItemInput()
        {
            if (!hostAuthorityOnlyWhenRunnerExists)
            {
                return true;
            }

            if (!TryGetRunner(out var runner))
            {
                return true;
            }

            return runner.IsServer;
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

        private bool LoadPresentationTablesIfNeeded()
        {
            if (_presentationLoaded)
            {
                return true;
            }

            _soundRows.Clear();
            _vfxRows.Clear();

            if (!SoundAssetTableCsvLoader.TryLoadFromDisk(
                    soundAssetTableRelativePath,
                    out var loadedSoundRows,
                    out _,
                    out var soundError))
            {
                LogStatus($"SoundAssetTable load failed: {soundError}");
                return false;
            }

            if (!VfxAssetTableCsvLoader.TryLoadFromDisk(
                    vfxAssetTableRelativePath,
                    out var loadedVfxRows,
                    out _,
                    out var vfxError))
            {
                LogStatus($"VfxAssetTable load failed: {vfxError}");
                return false;
            }

            foreach (var pair in loadedSoundRows)
            {
                _soundRows[pair.Key] = pair.Value;
            }

            foreach (var pair in loadedVfxRows)
            {
                _vfxRows[pair.Key] = pair.Value;
            }

            _presentationLoaded = true;
            return true;
        }

        private bool TryGetSoundRow(string sfxId, out SoundAssetTableCsvLoader.Row row)
        {
            row = default;
            if (string.IsNullOrWhiteSpace(sfxId))
            {
                return false;
            }

            return LoadPresentationTablesIfNeeded() && _soundRows.TryGetValue(sfxId, out row);
        }

        private bool TryGetVfxRow(string vfxId, out VfxAssetTableCsvLoader.Row row)
        {
            row = default;
            if (string.IsNullOrWhiteSpace(vfxId))
            {
                return false;
            }

            return LoadPresentationTablesIfNeeded() && _vfxRows.TryGetValue(vfxId, out row);
        }

        private void PlayLoopingSfx(string sfxId, AudioClip clip, Vector3 worldPosition, float volume, bool spatial)
        {
            if (_loopingSfxSources.TryGetValue(sfxId, out var existing) && existing != null)
            {
                if (!existing.isPlaying)
                {
                    existing.Play();
                }

                return;
            }

            var go = new GameObject($"ItemLoopSfx_{sfxId}");
            go.transform.position = worldPosition;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = Mathf.Clamp01(volume);
            source.spatialBlend = spatial ? 1f : 0f;
            source.Play();
            _loopingSfxSources[sfxId] = source;
        }

        private static void PlayOneShotSfx(AudioClip clip, Vector3 worldPosition, float volume, bool spatial)
        {
            if (clip == null)
            {
                return;
            }

            var go = new GameObject($"ItemSfx_{clip.name}");
            go.transform.position = worldPosition;
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = false;
            source.playOnAwake = false;
            source.volume = Mathf.Clamp01(volume);
            source.spatialBlend = spatial ? 1f : 0f;
            source.Play();
            Destroy(go, clip.length + 0.1f);
        }

        private void StopAllLoopingSfx()
        {
            foreach (var pair in _loopingSfxSources)
            {
                var source = pair.Value;
                if (source == null)
                {
                    continue;
                }

                source.Stop();
                Destroy(source.gameObject);
            }

            _loopingSfxSources.Clear();
        }

        private AudioClip LoadAudioClipFromAssetKey(string assetKey)
        {
            if (TryLoadAudioClipFromAssetPath(assetKey, out var clipFromAssetPath))
            {
                return clipFromAssetPath;
            }

            var resourcesPath = NormalizeAssetKeyToResourcesPath(assetKey);
            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                return null;
            }

            if (_audioClipCache.TryGetValue(resourcesPath, out var cachedClip))
            {
                return cachedClip;
            }

            var clip = Resources.Load<AudioClip>(resourcesPath);
            _audioClipCache[resourcesPath] = clip;
            return clip;
        }

        private static GameObject LoadVfxPrefabFromAssetKey(string assetKey)
        {
            if (TryLoadPrefabFromAssetPath(assetKey, out var prefabFromAssetPath))
            {
                return prefabFromAssetPath;
            }

            var resourcesPath = NormalizeAssetKeyToResourcesPath(assetKey);
            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                return null;
            }

            return Resources.Load<GameObject>(resourcesPath);
        }

        private static string NormalizeAssetKeyToResourcesPath(string assetKey)
        {
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                return string.Empty;
            }

            if (assetKey.Equals("TBD", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var normalized = assetKey.Replace('\\', '/').Trim();
            const string assetsResourcesPrefix = "Assets/Resources/";
            if (normalized.StartsWith(assetsResourcesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(assetsResourcesPrefix.Length);
            }
            else if (normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Resources/".Length);
            }

            var extension = Path.GetExtension(normalized);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                normalized = normalized.Substring(0, normalized.Length - extension.Length);
            }

            return normalized;
        }

        private static bool TryLoadAudioClipFromAssetPath(string assetKey, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                return false;
            }

#if UNITY_EDITOR
            if (assetKey.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetKey);
                return clip != null;
            }
#endif

            return false;
        }

        private static bool TryLoadPrefabFromAssetPath(string assetKey, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                return false;
            }

#if UNITY_EDITOR
            if (assetKey.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetKey);
                return prefab != null;
            }
#endif

            return false;
        }
    }
}
