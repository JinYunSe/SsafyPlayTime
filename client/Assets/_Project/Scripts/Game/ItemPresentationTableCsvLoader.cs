using System;
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime
{
    internal static class ItemPresentationTableCsvLoader
    {
        internal readonly struct Row
        {
            public Row(
                string itemId,
                string useSfxId,
                string hitSfxId,
                string startVfxId,
                string impactVfxId,
                string endVfxId)
            {
                ItemId = itemId;
                UseSfxId = useSfxId;
                HitSfxId = hitSfxId;
                StartVfxId = startVfxId;
                ImpactVfxId = impactVfxId;
                EndVfxId = endVfxId;
            }

            public string ItemId { get; }
            public string UseSfxId { get; }
            public string HitSfxId { get; }
            public string StartVfxId { get; }
            public string ImpactVfxId { get; }
            public string EndVfxId { get; }
        }

        internal static bool TryLoadFromDisk(
            string relativeOrAbsolutePath,
            out Dictionary<string, Row> rows,
            out string resolvedPath,
            out string error)
        {
            rows = null;
            resolvedPath = string.Empty;
            error = string.Empty;

            if (!PrototypeCsvUtility.TryReadCsvText(
                    relativeOrAbsolutePath,
                    "ItemPresentationTable",
                    out resolvedPath,
                    out var csvText,
                    out error))
            {
                return false;
            }

            return TryParse(csvText, out rows, out error);
        }

        private static bool TryParse(string csvText, out Dictionary<string, Row> rows, out string error)
        {
            rows = new Dictionary<string, Row>(StringComparer.Ordinal);
            error = string.Empty;

            using var reader = new StringReader(csvText);
            var headerLine = PrototypeCsvUtility.ReadNextDataLine(reader);
            if (headerLine == null)
            {
                error = "ItemPresentationTable header is missing.";
                return false;
            }

            var header = PrototypeCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = PrototypeCsvUtility.BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("itemId"))
            {
                error = "itemId column is missing in ItemPresentationTable.";
                return false;
            }

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var cells = PrototypeCsvUtility.ParseCsvLine(line);
                var itemId = PrototypeCsvUtility.ReadString(cells, headerIndex, "itemId", string.Empty);
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                var row = new Row(
                    itemId,
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "useSfxId", string.Empty),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "hitSfxId", string.Empty),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "startVfxId", string.Empty),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "impactVfxId", string.Empty),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "endVfxId", string.Empty));

                rows[itemId] = row;
            }

            if (rows.Count == 0)
            {
                error = "No rows were parsed from ItemPresentationTable.";
                return false;
            }

            return true;
        }
    }
}
