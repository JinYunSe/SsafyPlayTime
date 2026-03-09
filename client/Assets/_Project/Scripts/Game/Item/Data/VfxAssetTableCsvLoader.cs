/*
 * 파일 개요:
 * - VfxAssetTableCsvLoader 스크립트가 들어 있는 파일이다.
 * - Data 계층에서 CSV 파싱과 로더 책임을 담당하며, 원본 데이터 형식을 코드 객체로 변환한다.
 * - 스키마 변경이 필요하면 Catalog 계층과 데이터 테이블을 함께 수정해야 한다.
 */
using System;
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime.Gameplay.Items
{
    public static class VfxAssetTableCsvLoader
    {
        public readonly struct Row
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

            if (!ItemCsvUtility.TryReadCsvText(
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
            var headerLine = ItemCsvUtility.ReadNextDataLine(reader);
            if (headerLine == null)
            {
                error = "VfxAssetTable header is missing.";
                return false;
            }

            var header = ItemCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = ItemCsvUtility.BuildHeaderIndex(header);
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

                var cells = ItemCsvUtility.ParseCsvLine(line);
                var vfxId = ItemCsvUtility.ReadString(cells, headerIndex, "vfxId", string.Empty);
                if (string.IsNullOrWhiteSpace(vfxId))
                {
                    continue;
                }

                var row = new Row(
                    vfxId,
                    ItemCsvUtility.ReadString(cells, headerIndex, "assetKey", string.Empty),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "pooling", true),
                    ItemCsvUtility.ReadInt(cells, headerIndex, "prewarmCount", 0),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "lifetimeSec", 0f),
                    ItemCsvUtility.ReadString(cells, headerIndex, "attachType", string.Empty),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "scale", 1f),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "enabled", true),
                    ItemCsvUtility.ReadString(cells, headerIndex, "note", string.Empty));

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


