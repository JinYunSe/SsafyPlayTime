using System;
using System.Collections.Generic;

namespace SSAFYPlayTime.Gameplay.Items
{
    public readonly struct ItemCatalogLoadOptions
    {
        public ItemCatalogLoadOptions(
            string itemMasterPath,
            string soundAssetPath,
            string vfxAssetPath,
            string presentationPath)
        {
            ItemMasterPath = itemMasterPath;
            SoundAssetPath = soundAssetPath;
            VfxAssetPath = vfxAssetPath;
            PresentationPath = presentationPath;
        }

        public string ItemMasterPath { get; }
        public string SoundAssetPath { get; }
        public string VfxAssetPath { get; }
        public string PresentationPath { get; }
    }

    public static class ItemCatalogLoader
    {
        public const string DefaultItemMasterPath = "_Project/Data/ItemTable.csv";
        public const string DefaultSoundAssetPath = "_Project/Data/SoundAssetTable.csv";
        public const string DefaultVfxAssetPath = "_Project/Data/VfxAssetTable.csv";
        public const string DefaultPresentationPath = "_Project/Data/ItemPresentationTable.csv";

        public static bool TryLoadFromDisk(
            ItemCatalogLoadOptions options,
            out ItemCatalog catalog,
            out IReadOnlyList<string> warnings,
            out string error)
        {
            catalog = null;
            warnings = Array.Empty<string>();
            error = string.Empty;

            if (!ItemMasterCsvLoader.TryLoadFromDisk(options.ItemMasterPath, out var masterRows, out _, out error))
            {
                return false;
            }

            if (!SoundAssetTableCsvLoader.TryLoadFromDisk(options.SoundAssetPath, out var soundRows, out _, out error))
            {
                return false;
            }

            if (!VfxAssetTableCsvLoader.TryLoadFromDisk(options.VfxAssetPath, out var vfxRows, out _, out error))
            {
                return false;
            }

            if (!ItemPresentationTableCsvLoader.TryLoadFromDisk(options.PresentationPath, out var presentationRows, out _, out error))
            {
                return false;
            }

            var defs = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            foreach (var pair in masterRows)
            {
                var itemId = pair.Key;
                var masterRow = pair.Value;
                var hasPresentation = presentationRows.TryGetValue(itemId, out var presentationRow);
                defs[itemId] = new ItemDefinition(masterRow, hasPresentation ? presentationRow : (ItemPresentationTableCsvLoader.Row?)null);
            }

            catalog = new ItemCatalog(defs, soundRows, vfxRows);
            warnings = ValidateCrossReferences(catalog);
            return true;
        }

        public static ItemCatalogLoadOptions CreateDefaultOptions()
        {
            return new ItemCatalogLoadOptions(
                DefaultItemMasterPath,
                DefaultSoundAssetPath,
                DefaultVfxAssetPath,
                DefaultPresentationPath);
        }

        private static IReadOnlyList<string> ValidateCrossReferences(ItemCatalog catalog)
        {
            var result = new List<string>();
            foreach (var pair in catalog.Definitions)
            {
                var itemId = pair.Key;
                var definition = pair.Value;

                var useSfxId = definition.ResolveUseSfxId();
                if (!string.IsNullOrWhiteSpace(useSfxId) && !catalog.TryGetSound(useSfxId, out _))
                {
                    result.Add($"SFX reference missing: {itemId} -> {useSfxId}");
                }

                var startVfxId = definition.ResolveStartVfxId();
                if (!string.IsNullOrWhiteSpace(startVfxId) && !catalog.TryGetVfx(startVfxId, out _))
                {
                    result.Add($"VFX reference missing: {itemId} -> {startVfxId}");
                }
            }

            return result;
        }
    }
}
