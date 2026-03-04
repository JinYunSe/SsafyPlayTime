using System;
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime
{
    internal static class ItemTableCsvLoader
    {
        internal readonly struct Row
        {
            public Row(
                string itemId,
                string itemName,
                float durationSec,
                float range,
                float baseDamage,
                float stunDamage,
                float force,
                float tickIntervalSec,
                float useDelaySec,
                float warningTimeSec,
                float scaleMultiplier,
                float maxActiveUseSec,
                string sfxId,
                string vfxId,
                bool enabled)
            {
                ItemId = itemId;
                ItemName = itemName;
                DurationSec = durationSec;
                Range = range;
                BaseDamage = baseDamage;
                StunDamage = stunDamage;
                Force = force;
                TickIntervalSec = tickIntervalSec;
                UseDelaySec = useDelaySec;
                WarningTimeSec = warningTimeSec;
                ScaleMultiplier = scaleMultiplier;
                MaxActiveUseSec = maxActiveUseSec;
                SfxId = sfxId;
                VfxId = vfxId;
                Enabled = enabled;
            }

            public string ItemId { get; }
            public string ItemName { get; }
            public float DurationSec { get; }
            public float Range { get; }
            public float BaseDamage { get; }
            public float StunDamage { get; }
            public float Force { get; }
            public float TickIntervalSec { get; }
            public float UseDelaySec { get; }
            public float WarningTimeSec { get; }
            public float ScaleMultiplier { get; }
            public float MaxActiveUseSec { get; }
            public string SfxId { get; }
            public string VfxId { get; }
            public bool Enabled { get; }
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
                    "ItemTable",
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
                error = "ItemTable header is missing.";
                return false;
            }

            var header = PrototypeCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = PrototypeCsvUtility.BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("itemId"))
            {
                error = "itemId column is missing.";
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
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "itemName", itemId),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "durationSec", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "range", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "baseDamage", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "stunDamage", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "force", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "tickIntervalSec", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "useDelaySec", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "warningTimeSec", 0f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "scaleMultiplier", 1f),
                    PrototypeCsvUtility.ReadFloat(cells, headerIndex, "maxActiveUseSec", 0f),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "sfxId", string.Empty),
                    PrototypeCsvUtility.ReadString(cells, headerIndex, "vfxId", string.Empty),
                    PrototypeCsvUtility.ReadBool(cells, headerIndex, "enabled", true));

                if (!row.Enabled)
                {
                    continue;
                }

                rows[itemId] = row;
            }

            if (rows.Count == 0)
            {
                error = "No enabled rows were parsed from ItemTable.";
                return false;
            }

            return true;
        }
    }
}
