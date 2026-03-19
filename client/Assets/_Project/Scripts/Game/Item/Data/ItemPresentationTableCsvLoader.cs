/*
 * 파일 개요:
 * - ItemPresentationTableCsvLoader 스크립트가 들어 있는 파일이다.
 * - Data 계층에서 CSV 파싱과 로더 책임을 담당하며, 원본 데이터 형식을 코드 객체로 변환한다.
 * - 스키마 변경이 필요하면 Catalog 계층과 데이터 테이블을 함께 수정해야 한다.
 */
using System;
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime.Gameplay.Items
{
    public static class ItemPresentationTableCsvLoader
    {
        public readonly struct Row
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

            if (!ItemCsvUtility.TryReadCsvText(
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
            var headerLine = ItemCsvUtility.ReadNextDataLine(reader);
            if (headerLine == null)
            {
                error = "ItemPresentationTable header is missing.";
                return false;
            }

            var header = ItemCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = ItemCsvUtility.BuildHeaderIndex(header);
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

                var cells = ItemCsvUtility.ParseCsvLine(line);
                var itemId = ItemCsvUtility.ReadString(cells, headerIndex, "itemId", string.Empty);
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                var row = new Row(
                    itemId,
                    ItemCsvUtility.ReadString(cells, headerIndex, "useSfxId", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "hitSfxId", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "startVfxId", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "impactVfxId", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "endVfxId", string.Empty));

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


