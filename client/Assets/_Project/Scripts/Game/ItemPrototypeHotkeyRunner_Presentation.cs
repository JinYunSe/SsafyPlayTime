using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SSAFYPlayTime
{
    public sealed partial class ItemPrototypeHotkeyRunner
    {
        private void PlayItemUsePresentation(string itemId, Vector3 worldPosition, bool forceLoopSfx = false)
        {
            if (!enablePresentationFromTables || !_assetTablesApplied || _dataCatalog == null)
            {
                return;
            }

            if (TryResolveUseSfx(itemId, forceLoopSfx, out var sfxId, out var loop, out var volume, out var spatial))
            {
                PlaySfxById(sfxId, worldPosition, loop, volume, spatial);
            }

            if (TryResolveStartVfx(itemId, out var vfxId))
            {
                SpawnVfxById(vfxId, worldPosition);
            }
        }

        private bool TryResolveUseSfx(
            string itemId,
            bool forceLoopSfx,
            out string sfxId,
            out bool loop,
            out float volume,
            out bool spatial)
        {
            sfxId = string.Empty;
            loop = forceLoopSfx;
            volume = defaultPrototypeSfxVolume;
            spatial = true;

            if (_itemTableRows != null && _itemTableRows.TryGetValue(itemId, out var rowFromItem))
            {
                sfxId = rowFromItem.SfxId;
            }

            if (_dataCatalog.PresentationRows.TryGetValue(itemId, out var presentation))
            {
                if (!string.IsNullOrWhiteSpace(presentation.UseSfxId))
                {
                    sfxId = presentation.UseSfxId;
                }
            }

            if (string.IsNullOrWhiteSpace(sfxId))
            {
                return false;
            }

            if (_dataCatalog.SoundAssetRows.TryGetValue(sfxId, out var soundRow))
            {
                loop = forceLoopSfx || soundRow.Loop;
                volume = Mathf.Clamp01(soundRow.DefaultVolume);
                spatial = soundRow.Spatial;
            }

            return true;
        }

        private bool TryResolveStartVfx(string itemId, out string vfxId)
        {
            vfxId = string.Empty;
            if (_itemTableRows != null && _itemTableRows.TryGetValue(itemId, out var rowFromItem))
            {
                vfxId = rowFromItem.VfxId;
            }

            if (_dataCatalog.PresentationRows.TryGetValue(itemId, out var presentation))
            {
                if (!string.IsNullOrWhiteSpace(presentation.StartVfxId))
                {
                    vfxId = presentation.StartVfxId;
                }
            }

            return !string.IsNullOrWhiteSpace(vfxId);
        }

        private void PlaySfxById(string sfxId, Vector3 worldPosition, bool loop, float volume, bool spatial)
        {
            if (string.IsNullOrWhiteSpace(sfxId))
            {
                return;
            }

            if (!_dataCatalog.SoundAssetRows.TryGetValue(sfxId, out var row))
            {
                return;
            }

            var clip = LoadAudioClipFromAssetKey(row.AssetKey);
            if (clip == null)
            {
                return;
            }

            if (loop)
            {
                PlayLoopingSfx(sfxId, clip, worldPosition, volume, spatial);
                return;
            }

            PlayOneShotSfx(clip, worldPosition, volume, spatial);
        }

        private void SpawnVfxById(string vfxId, Vector3 worldPosition)
        {
            if (string.IsNullOrWhiteSpace(vfxId))
            {
                return;
            }

            if (!_dataCatalog.VfxAssetRows.TryGetValue(vfxId, out var row))
            {
                return;
            }

            var prefab = LoadVfxPrefabFromAssetKey(row.AssetKey);
            if (prefab == null)
            {
                return;
            }

            var instance = Instantiate(prefab, worldPosition, Quaternion.identity);
            if (row.LifetimeSec > 0f)
            {
                // 테이블 수명값이 있으면 자동 정리한다.
                Destroy(instance, row.LifetimeSec);
            }
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

            var go = new GameObject($"PrototypeLoopSfx_{sfxId}");
            if (targetRoot != null)
            {
                go.transform.SetParent(targetRoot, false);
                go.transform.localPosition = Vector3.zero;
            }
            else
            {
                go.transform.position = worldPosition;
            }

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = Mathf.Clamp01(volume);
            source.spatialBlend = spatial ? 1f : 0f;
            source.Play();
            _loopingSfxSources[sfxId] = source;
        }

        private void PlayOneShotSfx(AudioClip clip, Vector3 worldPosition, float volume, bool spatial)
        {
            var go = new GameObject($"PrototypeSfx_{clip.name}");
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

        private void StopLoopingSfx(string sfxId)
        {
            if (!_loopingSfxSources.TryGetValue(sfxId, out var source))
            {
                return;
            }

            _loopingSfxSources.Remove(sfxId);
            if (source == null)
            {
                return;
            }

            source.Stop();
            Destroy(source.gameObject);
        }

        private void StopItemUseLoopSfx(string itemId)
        {
            if (!TryResolveUseSfx(itemId, false, out var sfxId, out _, out _, out _))
            {
                return;
            }

            StopLoopingSfx(sfxId);
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

            if (assetKey.Equals("TBD", System.StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var normalized = assetKey.Replace('\\', '/').Trim();
            const string assetsResourcesPrefix = "Assets/Resources/";
            if (normalized.StartsWith(assetsResourcesPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(assetsResourcesPrefix.Length);
            }
            else if (normalized.StartsWith("Resources/", System.StringComparison.OrdinalIgnoreCase))
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
            if (assetKey.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
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
            if (assetKey.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetKey);
                return prefab != null;
            }
#endif

            return false;
        }
    }
}
