using System;
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime.Gameplay.Items
{
    public static class SoundAssetTableCsvLoader
    {
        public readonly struct Row
        {
            public Row(
                string sfxId,
                string assetKey,
                string mixerGroup,
                float defaultVolume,
                bool loop,
                bool spatial,
                int maxVoices,
                bool enabled,
                string note)
            {
                SfxId = sfxId;
                AssetKey = assetKey;
                MixerGroup = mixerGroup;
                DefaultVolume = defaultVolume;
                Loop = loop;
                Spatial = spatial;
                MaxVoices = maxVoices;
                Enabled = enabled;
                Note = note;
            }

            public string SfxId { get; }
            public string AssetKey { get; }
            public string MixerGroup { get; }
            public float DefaultVolume { get; }
            public bool Loop { get; }
            public bool Spatial { get; }
            public int MaxVoices { get; }
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

            if (!ItemCsvUtility.TryReadCsvText(
                    relativeOrAbsolutePath,
                    "SoundAssetTable",
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
            var headerLine = ItemCsvUtility.ReadNextDataLine(reader);
            if (headerLine == null)
            {
                error = "SoundAssetTable header is missing.";
                return false;
            }

            var header = ItemCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = ItemCsvUtility.BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("sfxId"))
            {
                error = "sfxId column is missing in SoundAssetTable.";
                return false;
            }

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var cells = ItemCsvUtility.ParseCsvLine(line);
                var sfxId = ItemCsvUtility.ReadString(cells, headerIndex, "sfxId", string.Empty);
                if (string.IsNullOrWhiteSpace(sfxId))
                {
                    continue;
                }

                var row = new Row(
                    sfxId,
                    ItemCsvUtility.ReadString(cells, headerIndex, "assetKey", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "mixerGroup", string.Empty),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "defaultVolume", 1f),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "loop", false),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "spatial", false),
                    ItemCsvUtility.ReadInt(cells, headerIndex, "maxVoices", 1),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "enabled", true),
                    ItemCsvUtility.ReadString(cells, headerIndex, "note", string.Empty));

                if (!row.Enabled)
                {
                    continue;
                }

                rows[sfxId] = row;
            }

            if (rows.Count == 0)
            {
                error = "No enabled rows were parsed from SoundAssetTable.";
                return false;
            }

            return true;
        }
    }
}

