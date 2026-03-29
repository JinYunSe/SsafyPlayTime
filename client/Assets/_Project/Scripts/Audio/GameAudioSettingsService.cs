using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSAFYPlayTime
{
    [DefaultExecutionOrder(-1000)]
    public sealed class GameAudioSettingsService : MonoBehaviour
    {
        private readonly struct RegisteredSource
        {
            public RegisteredSource(GameAudioCategory category, float baseVolume)
            {
                Category = category;
                BaseVolume = baseVolume;
            }

            public GameAudioCategory Category { get; }
            public float BaseVolume { get; }
        }

        private const string MasterVolumeKey = "audio.master_volume";
        private const string EffectVolumeKey = "audio.effect_volume";
        private const string BackgroundVolumeKey = "audio.background_volume";

        private static GameAudioSettingsService _instance;

        private readonly Dictionary<AudioSource, RegisteredSource> _registeredSources = new();

        private float _masterVolume = 1f;
        private float _effectVolume = 1f;
        private float _backgroundVolume = 1f;

        public static bool HasInstance => _instance != null;
        public static float MasterVolume => EnsureInstance()._masterVolume;
        public static float EffectVolume => EnsureInstance()._effectVolume;
        public static float BackgroundVolume => EnsureInstance()._backgroundVolume;

        public static GameAudioSettingsService EnsureInstance()
        {
            if (_instance != null)
            {
                return _instance;
            }

            _instance = FindFirstObjectByType<GameAudioSettingsService>(FindObjectsInactive.Include);
            if (_instance != null)
            {
                return _instance;
            }

            var root = new GameObject("GameAudioSettingsService");
            _instance = root.AddComponent<GameAudioSettingsService>();
            return _instance;
        }

        public static void RegisterSource(AudioSource source, GameAudioCategory category, float? baseVolumeOverride = null)
        {
            if (source == null)
            {
                return;
            }

            EnsureInstance().RegisterSourceInternal(source, category, baseVolumeOverride ?? source.volume);
        }

        public static void UnregisterSource(AudioSource source)
        {
            if (_instance == null || source == null)
            {
                return;
            }

            _instance._registeredSources.Remove(source);
        }

        public static void SetMasterVolume(float value)
        {
            var service = EnsureInstance();
            service._masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(MasterVolumeKey, service._masterVolume);
            PlayerPrefs.Save();
            service.ApplyAudioListenerVolume();
        }

        public static void SetEffectVolume(float value)
        {
            var service = EnsureInstance();
            service._effectVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(EffectVolumeKey, service._effectVolume);
            PlayerPrefs.Save();
            service.ReapplyRegisteredSources();
        }

        public static void SetBackgroundVolume(float value)
        {
            var service = EnsureInstance();
            service._backgroundVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(BackgroundVolumeKey, service._backgroundVolume);
            PlayerPrefs.Save();
            service.ReapplyRegisteredSources();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSettings();
            ApplyAudioListenerVolume();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Start()
        {
            // Awake()가 아닌 Start()에서 호출해야 한다.
            // Awake()에서 호출하면 GameAudioSource.OnEnable()보다 먼저 실행되어
            // AudioSource.volume을 카테고리 볼륨 * 0 = 0으로 만든 뒤,
            // GameAudioSource.OnEnable()이 그 0을 _baseVolume으로 캡처하는 버그가 발생한다.
            // Start()에서 호출하면 OnEnable()이 먼저 원본 볼륨을 캡처하고
            // _baseVolumeCaptured = true가 되어 재캡처가 방지된다.
            RegisterSceneAudioSources();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _instance = null;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyAudioListenerVolume();
            RegisterSceneAudioSources();
        }

        private void LoadSettings()
        {
            _masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterVolumeKey, 1f));
            _effectVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(EffectVolumeKey, 1f));
            _backgroundVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BackgroundVolumeKey, 1f));
        }

        private void RegisterSceneAudioSources()
        {
            var sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var source in sources)
            {
                if (source == null || !source.gameObject.scene.IsValid())
                {
                    continue;
                }

                var tagged = source.GetComponent<GameAudioSource>();
                if (tagged != null)
                {
                    tagged.RegisterOrUpdate();
                    continue;
                }

                RegisterSourceInternal(source, GuessCategory(source), source.volume);
            }
        }

        private void RegisterSourceInternal(AudioSource source, GameAudioCategory category, float baseVolume)
        {
            if (source == null)
            {
                return;
            }

            _registeredSources[source] = new RegisteredSource(category, Mathf.Clamp01(baseVolume));
            ApplySourceVolume(source, _registeredSources[source]);
        }

        private void ReapplyRegisteredSources()
        {
            var staleSources = ListPool<AudioSource>.Get();
            var snapshot = ListPool<KeyValuePair<AudioSource, RegisteredSource>>.Get();
            snapshot.AddRange(_registeredSources);

            for (var i = 0; i < snapshot.Count; i++)
            {
                var pair = snapshot[i];
                if (pair.Key == null)
                {
                    staleSources.Add(pair.Key);
                    continue;
                }

                ApplySourceVolume(pair.Key, pair.Value);
            }

            foreach (var stale in staleSources)
            {
                _registeredSources.Remove(stale);
            }

            ListPool<KeyValuePair<AudioSource, RegisteredSource>>.Release(snapshot);
            ListPool<AudioSource>.Release(staleSources);
        }

        private void ApplyAudioListenerVolume()
        {
            AudioListener.volume = _masterVolume;
        }

        private void ApplySourceVolume(AudioSource source, RegisteredSource registered)
        {
            if (source == null)
            {
                return;
            }

            source.volume = Mathf.Clamp01(registered.BaseVolume * GetCategoryVolume(registered.Category));
        }

        private float GetCategoryVolume(GameAudioCategory category)
        {
            return category == GameAudioCategory.BackgroundSound
                ? _backgroundVolume
                : _effectVolume;
        }

        private static GameAudioCategory GuessCategory(AudioSource source)
        {
            var combinedName = $"{source.gameObject.name} {source.clip?.name}";
            if (source.loop)
            {
                if (combinedName.IndexOf("BGM", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    combinedName.IndexOf("Music", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    combinedName.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return GameAudioCategory.BackgroundSound;
                }
            }

            return GameAudioCategory.EffectSound;
        }

        private static class ListPool<T>
        {
            private static readonly Stack<List<T>> Pool = new();

            public static List<T> Get()
            {
                return Pool.Count > 0 ? Pool.Pop() : new List<T>();
            }

            public static void Release(List<T> list)
            {
                list.Clear();
                Pool.Push(list);
            }
        }
    }
}
