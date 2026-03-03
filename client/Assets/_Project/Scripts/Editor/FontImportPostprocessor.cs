using UnityEditor;
using UnityEngine;

namespace SSAFYPlayTime.EditorTools
{
    public sealed class FontImportPostprocessor : AssetPostprocessor
    {
        private const string FontFolder = "Assets/_Project/Resources/Fonts/";

        private void OnPreprocessAsset()
        {
            if (!assetPath.StartsWith(FontFolder))
            {
                return;
            }

            if (!assetPath.EndsWith(".ttf") && !assetPath.EndsWith(".otf"))
            {
                return;
            }

            if (assetImporter is TrueTypeFontImporter fontImporter)
            {
                fontImporter.includeFontData = true;
            }
        }
    }
}
