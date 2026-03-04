using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SSAFYPlayTime
{
    internal static class PrototypeCsvUtility
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        internal static bool TryReadCsvText(
            string relativeOrAbsolutePath,
            string tableName,
            out string resolvedPath,
            out string csvText,
            out string error)
        {
            resolvedPath = string.Empty;
            csvText = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            {
                error = $"{tableName} path is empty.";
                return false;
            }

            resolvedPath = ResolvePath(relativeOrAbsolutePath);
            if (!File.Exists(resolvedPath))
            {
                error = $"{tableName} not found: {resolvedPath}";
                return false;
            }

            try
            {
                csvText = File.ReadAllText(resolvedPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                error = $"{tableName} read failed: {ex.Message}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(csvText))
            {
                error = $"{tableName} is empty.";
                return false;
            }

            return true;
        }

        internal static string ResolvePath(string relativeOrAbsolutePath)
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

        internal static string ReadNextDataLine(StringReader reader)
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

        internal static Dictionary<string, int> BuildHeaderIndex(IReadOnlyList<string> header)
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

        internal static List<string> ParseCsvLine(string line)
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

        internal static string ReadString(
            IReadOnlyList<string> cells,
            IReadOnlyDictionary<string, int> headerIndex,
            string key,
            string defaultValue = "")
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

        internal static float ReadFloat(
            IReadOnlyList<string> cells,
            IReadOnlyDictionary<string, int> headerIndex,
            string key,
            float defaultValue = 0f)
        {
            var raw = ReadString(cells, headerIndex, key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            return float.TryParse(raw, NumberStyles.Float, Culture, out var value) ? value : defaultValue;
        }

        internal static int ReadInt(
            IReadOnlyList<string> cells,
            IReadOnlyDictionary<string, int> headerIndex,
            string key,
            int defaultValue = 0)
        {
            var raw = ReadString(cells, headerIndex, key, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            return int.TryParse(raw, NumberStyles.Integer, Culture, out var value) ? value : defaultValue;
        }

        internal static bool ReadBool(
            IReadOnlyList<string> cells,
            IReadOnlyDictionary<string, int> headerIndex,
            string key,
            bool defaultValue = false)
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
