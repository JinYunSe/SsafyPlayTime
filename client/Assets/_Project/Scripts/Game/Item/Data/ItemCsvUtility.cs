/*
 * 파일 개요:
 * - ItemCsvUtility 스크립트가 들어 있는 파일이다.
 * - Data 계층에서 CSV 파싱과 로더 책임을 담당하며, 원본 데이터 형식을 코드 객체로 변환한다.
 * - 스키마 변경이 필요하면 Catalog 계층과 데이터 테이블을 함께 수정해야 한다.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    internal static class ItemCsvUtility
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

            if (!TryResolveExistingPath(relativeOrAbsolutePath, out resolvedPath))
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

        private static bool TryResolveExistingPath(string relativeOrAbsolutePath, out string resolvedPath)
        {
            var normalized = (relativeOrAbsolutePath ?? string.Empty).Replace('\\', '/');
            if (Path.IsPathRooted(normalized))
            {
                resolvedPath = Path.GetFullPath(normalized);
                return File.Exists(resolvedPath);
            }

            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring("Assets/".Length);
            }

            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(Application.dataPath, normalized)),
                Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, normalized)),
                Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Assets", normalized))
            };

            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (!File.Exists(candidate))
                {
                    continue;
                }

                resolvedPath = candidate;
                return true;
            }

            resolvedPath = candidates[0];
            return false;
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


