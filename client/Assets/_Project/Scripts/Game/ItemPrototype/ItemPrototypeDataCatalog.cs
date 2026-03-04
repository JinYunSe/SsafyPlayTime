using System.Collections.Generic;

namespace SSAFYPlayTime
{
    internal sealed class ItemPrototypeDataCatalog
    {
        public ItemPrototypeDataCatalog(
            IReadOnlyDictionary<string, SoundAssetTableCsvLoader.Row> soundAssetRows,
            IReadOnlyDictionary<string, VfxAssetTableCsvLoader.Row> vfxAssetRows,
            IReadOnlyDictionary<string, ItemPresentationTableCsvLoader.Row> presentationRows)
        {
            SoundAssetRows = soundAssetRows;
            VfxAssetRows = vfxAssetRows;
            PresentationRows = presentationRows;
        }

        public IReadOnlyDictionary<string, SoundAssetTableCsvLoader.Row> SoundAssetRows { get; }
        public IReadOnlyDictionary<string, VfxAssetTableCsvLoader.Row> VfxAssetRows { get; }
        public IReadOnlyDictionary<string, ItemPresentationTableCsvLoader.Row> PresentationRows { get; }

        public IReadOnlyList<string> ValidateReferences(IReadOnlyDictionary<string, ItemTableCsvLoader.Row> itemRows)
        {
            var warnings = new List<string>();
            if (itemRows == null || itemRows.Count == 0)
            {
                warnings.Add("ItemTable rows are missing. Cross-table validation skipped.");
                return warnings;
            }

            foreach (var pair in itemRows)
            {
                var itemId = pair.Key;
                var itemRow = pair.Value;

                if (!string.IsNullOrWhiteSpace(itemRow.SfxId) && !SoundAssetRows.ContainsKey(itemRow.SfxId))
                {
                    warnings.Add($"ItemTable.sfxId missing in SoundAssetTable: {itemId} -> {itemRow.SfxId}");
                }

                if (!string.IsNullOrWhiteSpace(itemRow.VfxId) && !VfxAssetRows.ContainsKey(itemRow.VfxId))
                {
                    warnings.Add($"ItemTable.vfxId missing in VfxAssetTable: {itemId} -> {itemRow.VfxId}");
                }

                if (!PresentationRows.TryGetValue(itemId, out var presentation))
                {
                    warnings.Add($"ItemPresentation row missing: {itemId}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(presentation.UseSfxId) && !SoundAssetRows.ContainsKey(presentation.UseSfxId))
                {
                    warnings.Add($"ItemPresentation.useSfxId missing in SoundAssetTable: {itemId} -> {presentation.UseSfxId}");
                }

                if (!string.IsNullOrWhiteSpace(presentation.HitSfxId) && !SoundAssetRows.ContainsKey(presentation.HitSfxId))
                {
                    warnings.Add($"ItemPresentation.hitSfxId missing in SoundAssetTable: {itemId} -> {presentation.HitSfxId}");
                }

                if (!string.IsNullOrWhiteSpace(presentation.StartVfxId) && !VfxAssetRows.ContainsKey(presentation.StartVfxId))
                {
                    warnings.Add($"ItemPresentation.startVfxId missing in VfxAssetTable: {itemId} -> {presentation.StartVfxId}");
                }

                if (!string.IsNullOrWhiteSpace(presentation.ImpactVfxId) && !VfxAssetRows.ContainsKey(presentation.ImpactVfxId))
                {
                    warnings.Add($"ItemPresentation.impactVfxId missing in VfxAssetTable: {itemId} -> {presentation.ImpactVfxId}");
                }

                if (!string.IsNullOrWhiteSpace(presentation.EndVfxId) && !VfxAssetRows.ContainsKey(presentation.EndVfxId))
                {
                    warnings.Add($"ItemPresentation.endVfxId missing in VfxAssetTable: {itemId} -> {presentation.EndVfxId}");
                }
            }

            return warnings;
        }
    }
}
