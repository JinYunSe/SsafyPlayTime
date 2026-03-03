using System;
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime
{
    internal static class VfxAssetTableCsvLoader
    {
        internal readonly struct Row
        {
            public Row(
                string vfxId,
                string assetKey,
                bool pooling,
                int prewarmCount,
                float lifetimeSec,
                string attachType,
                float scale,
                bool enabled,
                string note)
            {
                VfxId = vfxId;
                AssetKey = assetKey;
                Pooling = pooling;
                PrewarmCount = prewarmCount;
                LifetimeSec = lifetimeSec;
                AttachType = attachType;
                Scale = scale;
                Enabled = enabled;
                Note = note;
            }

            public string VfxId { get; }
            public string AssetKey { get; }
            public bool Pooling { get; }
            public int PrewarmCount { get; }
            public float LifetimeSec { get; }
            public string AttachType { get; }
            public float Scale { get; }
            public bool Enabled { get; }
            public string Note { get; }
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
                    "VfxAssetTable",
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
                error = "VfxAssetTable header is missing.";
                return false;
            }

            var header = PrototypeCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = PrototypeCsvUtility.BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("vfxId"))
            {
                error = "vfxId column is missing in VfxAssetTable.";
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
                var vfxId = PrototypeCsvUtility.ReadString(cells, headerIndex, "vfxId", string.Empty);
                if (string.IsNullOrWhiteSpace(vfxId))
                {
                    continue;
                }

                var row = new Row(
                    vfxId,
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "assetKey", string.Empty),
                    PrototypeCsvUtility.ReadBool(cells, headerIndex, "pooling", true),
                    PrototypeCsvUtility.ReadInt(cells, headerIndex, "prewarmCount", 0),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "lifetimeSec", 0f),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "attachType", string.Empty),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "scale", 1f),
                    PrototypeCsvUtility.ReadBool(cells, headerIndex, "enabled", true),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "note", string.Empty));

                if (!row.Enabled)
                {
                    continue;
                }

                rows[vfxId] = row;
            }

            if (rows.Count == 0)
            {
                error = "No enabled rows were parsed from VfxAssetTable.";
                return false;
            }

            return true;
        }
    }
}
