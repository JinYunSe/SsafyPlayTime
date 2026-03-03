using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SSAFYPlayTime
{
    internal static class ItemTableCsvLoader
    {
        internal readonly struct Row
        {
            public Row(
                string itemId,
                float durationSec,
                float range,
                float baseDamage,
                float force,
                float tickIntervalSec,
                float useDelaySec,
                float warningTimeSec,
                float scaleMultiplier,
                float maxActiveUseSec,
                bool enabled)
            {
                ItemId = itemId;
                DurationSec = durationSec;
                Range = range;
                BaseDamage = baseDamage;
                Force = force;
                TickIntervalSec = tickIntervalSec;
                UseDelaySec = useDelaySec;
                WarningTimeSec = warningTimeSec;
                ScaleMultiplier = scaleMultiplier;
                MaxActiveUseSec = maxActiveUseSec;
                Enabled = enabled;
            }

            public string ItemId { get; }
            public float DurationSec { get; }
            public float Range { get; }
            public float BaseDamage { get; }
            public float Force { get; }
            public float TickIntervalSec { get; }
            public float UseDelaySec { get; }
            public float WarningTimeSec { get; }
            public float ScaleMultiplier { get; }
            public float MaxActiveUseSec { get; }
            public bool Enabled { get; }
        }

        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        internal static bool TryLoadFromDisk(
            string relativeOrAbsolutePath,
            out Dictionary<string, Row> rows,
            out string resolvedPath,
            out string error)
        {
            rows = null;
            resolvedPath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            {
                error = "ItemTable path is empty.";
                return false;
            }

            resolvedPath = ResolvePath(relativeOrAbsolutePath);
            if (!File.Exists(resolvedPath))
            {
                error = $"ItemTable not found: {resolvedPath}";
                return false;
            }

            string csvText;
            try
            {
                csvText = File.ReadAllText(resolvedPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = $"ItemTable read failed: {ex.Message}";
                return false;
            }

            return TryParse(csvText, out rows, out error);
        }

        private static bool TryParse(string csvText, out Dictionary<string, Row> rows, out string error)
        {
            rows = new Dictionary<string, Row>(StringComparer.Ordinal);
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(csvText))
            {
                error = "ItemTable is empty.";
                return false;
            }

            using var reader = new StringReader(csvText);
            var headerLine = ReadNextDataLine(reader);
            if (headerLine == null)
            {
                error = "ItemTable header is missing.";
                return false;
            }

            var header = ParseCsvLine(headerLine);
            var headerIndex = BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("itemId"))
            {
                error = "itemId column is missing.";
                return false;
            }

            var lineNumber = 1;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var cells = ParseCsvLine(line);
                var itemId = ReadString(cells, headerIndex, "itemId", string.Empty);
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                var row = new Row(
                    itemId,
                    ReadFloat(cells, headerIndex, "durationSec", 0f),
                    ReadFloat(cells, headerIndex, "range", 0f),
                    ReadFloat(cells, headerIndex, "baseDamage", 0f),
                    ReadFloat(cells, headerIndex, "force", 0f),
                    ReadFloat(cells, headerIndex, "tickIntervalSec", 0f),
                    ReadFloat(cells, headerIndex, "useDelaySec", 0f),
                    ReadFloat(cells, headerIndex, "warningTimeSec", 0f),
                    ReadFloat(cells, headerIndex, "scaleMultiplier", 1f),
                    ReadFloat(cells, headerIndex, "maxActiveUseSec", 0f),
                    ReadBool(cells, headerIndex, "enabled", true));

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

        private static string ResolvePath(string relativeOrAbsolutePath)
        {
            var normalized = relativeOrAbsolutePath.Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
            {
                return Path.GetFullPath(normalized);
            }

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Assets/".Length);
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, normalized));
        }

        private static string ReadNextDataLine(StringReader reader)
        {
            while (true)
            {
                var line = reader.ReadLine();
                if (line == null)
                {
                    return null;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    return line;
                }
            }
        }

        private static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> header)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                var key = (header[i] ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(key) || map.ContainsKey(key))
                {
                    continue;
                }

                map[key] = i;
            }

            return map;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var cells = new List<string>();
            if (line == null)
            {
                return cells;
            }

            var builder = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (ch == ',' && !inQuotes)
                {
                    cells.Add(builder.ToString().Trim());
                    builder.Clear();
                    continue;
                }

                builder.Append(ch);
            }

            cells.Add(builder.ToString().Trim());
            return cells;
        }

        private static string ReadString(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> headerIndex, string key, string defaultValue)
        {
            if (!headerIndex.TryGetValue(key, out var index))
            {
                return defaultValue;
            }

            if (index < 0 || index >= cells.Count)
            {
                return defaultValue;
            }

            return cells[index];
        }

        private static float ReadFloat(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> headerIndex, string key, float defaultValue)
        {
            var raw = ReadString(cells, headerIndex, key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            return float.TryParse(raw, NumberStyles.Float, Culture, out var value) ? value : defaultValue;
        }

        private static bool ReadBool(IReadOnlyList<string> cells, IReadOnlyDictionary<string, int> headerIndex, string key, bool defaultValue)
        {
            var raw = ReadString(cells, headerIndex, key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            if (bool.TryParse(raw, out var boolValue))
            {
                return boolValue;
            }

            if (int.TryParse(raw, NumberStyles.Integer, Culture, out var intValue))
            {
                return intValue != 0;
            }

            return defaultValue;
        }
    }
}
