using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SSAFYPlayTime
{
    public static class TmpFontFallbackBootstrap
    {
        private static readonly string[] CandidateFontAssetPaths =
        {
            "Fonts/malgun",
            "Fonts/NanumGothic",
            "Fonts/NotoSansKR-VF"
        };

        public static TMP_FontAsset ActiveFallbackFont { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureKoreanFallbackRegistered();
        }

        public static void EnsureKoreanFallbackRegistered()
        {
            if (ActiveFallbackFont != null)
            {
                RegisterFallbackFont(ActiveFallbackFont);
                PatchLoadedFontAssets(ActiveFallbackFont);
                return;
            }

            foreach (var assetPath in CandidateFontAssetPaths)
            {
                var sourceFont = Resources.Load<Font>(assetPath);
                if (sourceFont == null)
                {
                    continue;
                }

                var fontAsset = TryCreateDynamicFontAsset(sourceFont);
                if (fontAsset == null)
                {
                    continue;
                }

                ActiveFallbackFont = fontAsset;
                RegisterFallbackFont(fontAsset);
                PatchLoadedFontAssets(fontAsset);
                Debug.Log($"[TMP] Korean fallback registered: source={sourceFont.name}, asset={fontAsset.name}");
                return;
            }

            Debug.LogWarning("[TMP] Korean fallback font registration failed. Check Resources/Fonts and Include Font Data.");
        }

        private static TMP_FontAsset TryCreateDynamicFontAsset(Font sourceFont)
        {
            try
            {
                var asset = TMP_FontAsset.CreateFontAsset(
                    sourceFont,
                    samplingPointSize: 90,
                    atlasPadding: 9,
                    renderMode: GlyphRenderMode.SDFAA,
                    atlasWidth: 1024,
                    atlasHeight: 1024,
                    atlasPopulationMode: AtlasPopulationMode.Dynamic,
                    enableMultiAtlasSupport: true);

                if (asset != null)
                {
                    asset.name = $"Runtime Fallback {sourceFont.name}";
                }

                return asset;
            }
            catch
            {
                Debug.LogWarning($"[TMP] Failed to create TMP font asset from source font: {sourceFont.name}");
                return null;
            }
        }

        private static void RegisterFallbackFont(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return;
            }

            var globalFallbacks = TMP_Settings.fallbackFontAssets;
            if (globalFallbacks != null && !globalFallbacks.Contains(fontAsset))
            {
                globalFallbacks.Add(fontAsset);
            }

            var defaultFont = TMP_Settings.defaultFontAsset;
            if (defaultFont == null)
            {
                return;
            }

            var localFallbacks = defaultFont.fallbackFontAssetTable;
            if (localFallbacks != null && !localFallbacks.Contains(fontAsset))
            {
                localFallbacks.Add(fontAsset);
            }
        }

        private static void PatchLoadedFontAssets(TMP_FontAsset fallbackFont)
        {
            var loadedFonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (var i = 0; i < loadedFonts.Length; i++)
            {
                var font = loadedFonts[i];
                if (font == null || font == fallbackFont)
                {
                    continue;
                }

                var fallbacks = font.fallbackFontAssetTable;
                if (fallbacks != null && !fallbacks.Contains(fallbackFont))
                {
                    fallbacks.Add(fallbackFont);
                }
            }
        }
    }
}
