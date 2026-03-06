using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// CombatParamsTable.csv에서 기절 시스템 파라미터를 읽어오는 로더.
    /// key-value 형식 (paramId → value)
    /// </summary>
    public static class CombatParamsCsvLoader
    {
        internal static bool TryLoadFromDisk(
            string relativeOrAbsolutePath,
            out Dictionary<string, float> paramDict,
            out string resolvedPath,
            out string error)
        {
            paramDict = null;
            resolvedPath = string.Empty;
            error = string.Empty;

            if (!ItemCsvUtility.TryReadCsvText(
                    relativeOrAbsolutePath,
                    "CombatParamsTable",
                    out resolvedPath,
                    out var csvText,
                    out error))
            {
                return false;
            }

            return TryParse(csvText, out paramDict, out error);
        }

        private static bool TryParse(string csvText, out Dictionary<string, float> paramDict, out string error)
        {
            paramDict = new Dictionary<string, float>(System.StringComparer.Ordinal);
            error = string.Empty;

            using var reader = new StringReader(csvText);
            var headerLine = ItemCsvUtility.ReadNextDataLine(reader);
            if (headerLine == null)
            {
                error = "CombatParamsTable header is missing.";
                return false;
            }

            var header = ItemCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = ItemCsvUtility.BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("paramId"))
            {
                error = "paramId column is missing in CombatParamsTable.";
                return false;
            }

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cells = ItemCsvUtility.ParseCsvLine(line);
                var id = ItemCsvUtility.ReadString(cells, headerIndex, "paramId", string.Empty);
                if (string.IsNullOrWhiteSpace(id)) continue;

                var value = ItemCsvUtility.ReadFloat(cells, headerIndex, "value", 0f);
                paramDict[id] = value;
            }

            if (paramDict.Count == 0)
            {
                error = "No rows were parsed from CombatParamsTable.";
                return false;
            }

            return true;
        }
    }
}
