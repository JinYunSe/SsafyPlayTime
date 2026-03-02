using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    public static class UiKoreanFontBootstrap
    {
        private static bool _initialized;
        private static Font _koreanFont;
        private static GameObject _driverObject;
        private static UiKoreanFontBootstrapDriver _driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            TmpFontFallbackBootstrap.EnsureKoreanFallbackRegistered();
            _koreanFont = Resources.Load<Font>("Fonts/malgun");

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            EnsureDriver();
            ApplyToLoadedUiTexts();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToLoadedUiTexts();
        }

        private static void ApplyToLoadedUiTexts()
        {
            ApplyToTmpTexts();
            ApplyToLegacyTexts();
        }

        private static void ApplyToTmpTexts()
        {
            var fallback = TmpFontFallbackBootstrap.ActiveFallbackFont;
            if (fallback == null)
            {
                TmpFontFallbackBootstrap.EnsureKoreanFallbackRegistered();
                fallback = TmpFontFallbackBootstrap.ActiveFallbackFont;
            }

            if (fallback == null)
            {
                return;
            }

            var texts = Resources.FindObjectsOfTypeAll<TMP_Text>();
            for (var i = 0; i < texts.Length; i++)
            {
                var tmp = texts[i];
                if (tmp == null || !tmp.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (tmp.font != fallback)
                {
                    tmp.font = fallback;
                }
            }
        }

        private static void ApplyToLegacyTexts()
        {
            if (_koreanFont == null)
            {
                return;
            }

            var texts = Resources.FindObjectsOfTypeAll<Text>();
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null || !text.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (text.font == null || text.font.name.Contains("Arial") || text.font.name.Contains("LiberationSans"))
                {
                    text.font = _koreanFont;
                }
            }
        }

        private static void EnsureDriver()
        {
            if (_driver != null)
            {
                return;
            }

            _driverObject = new GameObject("UiKoreanFontBootstrapDriver");
            Object.DontDestroyOnLoad(_driverObject);
            _driver = _driverObject.AddComponent<UiKoreanFontBootstrapDriver>();
        }

        private sealed class UiKoreanFontBootstrapDriver : MonoBehaviour
        {
            private float _nextTick;

            private void Update()
            {
                if (Time.unscaledTime < _nextTick)
                {
                    return;
                }

                _nextTick = Time.unscaledTime + 0.5f;
                ApplyToLoadedUiTexts();
            }
        }

    }
}
