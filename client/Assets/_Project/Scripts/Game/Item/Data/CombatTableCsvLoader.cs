/*
 * 파일 개요:
 * - CombatTableCsvLoader 스크립트가 들어 있는 파일이다.
 * - Data 계층에서 CSV 파싱과 로더 책임을 담당하며, 원본 데이터 형식을 코드 객체로 변환한다.
 * - 스키마 변경이 필요하면 Catalog 계층과 데이터 테이블을 함께 수정해야 한다.
 */
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// CombatTable.csv에서 공격별 전투 수치를 읽어오는 로더.
    /// </summary>
    public static class CombatTableCsvLoader
    {
        public readonly struct Row
        {
            public Row(
                string combatStatId,
                string combatStatName,
                float baseDamage,
                float stunDamage,
                float knockbackForce,
                float cooldownSec,
                float stunDurationBase,
                float stunDurationRandMin,
                float stunDurationRandMax,
                float selfStunDuration,
                float selfStunChance,
                int hitCountToStun,
                float groggyVulnerabilityMultiplier,
                float airborneVulnerabilityMultiplier,
                float velocityDamageMultiplier,
                string animationClip,
                string inputDescription,
                bool enabled)
            {
                CombatStatId = combatStatId;
                CombatStatName = combatStatName;
                BaseDamage = baseDamage;
                StunDamage = stunDamage;
                KnockbackForce = knockbackForce;
                CooldownSec = cooldownSec;
                StunDurationBase = stunDurationBase;
                StunDurationRandMin = stunDurationRandMin;
                StunDurationRandMax = stunDurationRandMax;
                SelfStunDuration = selfStunDuration;
                SelfStunChance = selfStunChance;
                HitCountToStun = hitCountToStun;
                GroggyVulnerabilityMultiplier = groggyVulnerabilityMultiplier;
                AirborneVulnerabilityMultiplier = airborneVulnerabilityMultiplier;
                VelocityDamageMultiplier = velocityDamageMultiplier;
                AnimationClip = animationClip;
                InputDescription = inputDescription;
                Enabled = enabled;
            }

            public string CombatStatId { get; }
            public string CombatStatName { get; }
            public float BaseDamage { get; }
            public float StunDamage { get; }
            public float KnockbackForce { get; }
            public float CooldownSec { get; }
            public float StunDurationBase { get; }
            public float StunDurationRandMin { get; }
            public float StunDurationRandMax { get; }
            public float SelfStunDuration { get; }
            public float SelfStunChance { get; }
            public int HitCountToStun { get; }
            public float GroggyVulnerabilityMultiplier { get; }
            public float AirborneVulnerabilityMultiplier { get; }
            public float VelocityDamageMultiplier { get; }
            public string AnimationClip { get; }
            public string InputDescription { get; }
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

            if (!ItemCsvUtility.TryReadCsvText(
                    relativeOrAbsolutePath,
                    "CombatTable",
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
            rows = new Dictionary<string, Row>(System.StringComparer.Ordinal);
            error = string.Empty;

            using var reader = new StringReader(csvText);
            var headerLine = ItemCsvUtility.ReadNextDataLine(reader);
            if (headerLine == null)
            {
                error = "CombatTable header is missing.";
                return false;
            }

            var header = ItemCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = ItemCsvUtility.BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("combatStatId"))
            {
                error = "combatStatId column is missing in CombatTable.";
                return false;
            }

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cells = ItemCsvUtility.ParseCsvLine(line);
                var id = ItemCsvUtility.ReadString(cells, headerIndex, "combatStatId", string.Empty);
                if (string.IsNullOrWhiteSpace(id)) continue;

                var row = new Row(
                    id,
                    ItemCsvUtility.ReadString(cells, headerIndex, "combatStatName", id),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "baseDamage", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "stunDamage", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "knockbackForce", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "cooldownSec", 0.5f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "stunDurationBase", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "stunDurationRandMin", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "stunDurationRandMax", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "selfStunDuration", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "selfStunChance", 0f),
                    ItemCsvUtility.ReadInt(cells, headerIndex, "hitCountToStun", 1),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "groggyVulnerabilityMultiplier", 1f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "airborneVulnerabilityMultiplier", 1f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "velocityDamageMultiplier", 0f),
                    ItemCsvUtility.ReadString(cells, headerIndex, "animationClip", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "inputDescription", string.Empty),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "enabled", true));

                if (!row.Enabled) continue;
                rows[id] = row;
            }

            if (rows.Count == 0)
            {
                error = "No enabled rows were parsed from CombatTable.";
                return false;
            }

            return true;
        }
    }
}

