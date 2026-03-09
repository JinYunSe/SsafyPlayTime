/*
 * 파일 개요:
 * - ItemMasterCsvLoader 스크립트가 들어 있는 파일이다.
 * - Data 계층에서 CSV 파싱과 로더 책임을 담당하며, 원본 데이터 형식을 코드 객체로 변환한다.
 * - 스키마 변경이 필요하면 Catalog 계층과 데이터 테이블을 함께 수정해야 한다.
 */
using System;
using System.Collections.Generic;
using System.IO;

namespace SSAFYPlayTime.Gameplay.Items
{
    public static class ItemMasterCsvLoader
    {
        public readonly struct Row
        {
            public Row(
                string itemId,
                string itemName,
                ItemType itemType,
                ItemUseType useType,
                bool requiresHandEquip,
                int holdSlotCount,
                bool consumeOnUse,
                bool dropOnStun,
                float cooldownSec,
                float durationSec,
                float range,
                float baseDamage,
                float stunDamage,
                float force,
                float tickIntervalSec,
                float useDelaySec,
                float warningTimeSec,
                float scaleMultiplier,
                float moveSpeedMultiplier,
                float baseDamageMultiplier,
                float knockbackResistMultiplier,
                float gravityMultiplier,
                float jumpMultiplier,
                int superArmorLevel,
                bool revealOnAttack,
                bool canMelee,
                bool canThrow,
                int durability,
                bool stunDropEnabled,
                float maxActiveUseSec,
                float overheatCooldownSec,
                string prefabPath,
                string iconPath,
                string sfxId,
                string vfxId,
                bool enabled)
            {
                ItemId = itemId;
                ItemName = itemName;
                ItemType = itemType;
                UseType = useType;
                RequiresHandEquip = requiresHandEquip;
                HoldSlotCount = holdSlotCount;
                ConsumeOnUse = consumeOnUse;
                DropOnStun = dropOnStun;
                CooldownSec = cooldownSec;
                DurationSec = durationSec;
                Range = range;
                BaseDamage = baseDamage;
                StunDamage = stunDamage;
                Force = force;
                TickIntervalSec = tickIntervalSec;
                UseDelaySec = useDelaySec;
                WarningTimeSec = warningTimeSec;
                ScaleMultiplier = scaleMultiplier;
                MoveSpeedMultiplier = moveSpeedMultiplier;
                BaseDamageMultiplier = baseDamageMultiplier;
                KnockbackResistMultiplier = knockbackResistMultiplier;
                GravityMultiplier = gravityMultiplier;
                JumpMultiplier = jumpMultiplier;
                SuperArmorLevel = superArmorLevel;
                RevealOnAttack = revealOnAttack;
                CanMelee = canMelee;
                CanThrow = canThrow;
                Durability = durability;
                StunDropEnabled = stunDropEnabled;
                MaxActiveUseSec = maxActiveUseSec;
                OverheatCooldownSec = overheatCooldownSec;
                PrefabPath = prefabPath;
                IconPath = iconPath;
                SfxId = sfxId;
                VfxId = vfxId;
                Enabled = enabled;
            }

            public string ItemId { get; }
            public string ItemName { get; }
            public ItemType ItemType { get; }
            public ItemUseType UseType { get; }
            public bool RequiresHandEquip { get; }
            public int HoldSlotCount { get; }
            public bool ConsumeOnUse { get; }
            public bool DropOnStun { get; }
            public float CooldownSec { get; }
            public float DurationSec { get; }
            public float Range { get; }
            public float BaseDamage { get; }
            public float StunDamage { get; }
            public float Force { get; }
            public float TickIntervalSec { get; }
            public float UseDelaySec { get; }
            public float WarningTimeSec { get; }
            public float ScaleMultiplier { get; }
            public float MoveSpeedMultiplier { get; }
            public float BaseDamageMultiplier { get; }
            public float KnockbackResistMultiplier { get; }
            public float GravityMultiplier { get; }
            public float JumpMultiplier { get; }
            public int SuperArmorLevel { get; }
            public bool RevealOnAttack { get; }
            public bool CanMelee { get; }
            public bool CanThrow { get; }
            public int Durability { get; }
            public bool StunDropEnabled { get; }
            public float MaxActiveUseSec { get; }
            public float OverheatCooldownSec { get; }
            public string PrefabPath { get; }
            public string IconPath { get; }
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

            if (!ItemCsvUtility.TryReadCsvText(
                    relativeOrAbsolutePath,
                    "ItemMaster",
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
                error = "ItemMaster header is missing.";
                return false;
            }

            var header = ItemCsvUtility.ParseCsvLine(headerLine);
            var headerIndex = ItemCsvUtility.BuildHeaderIndex(header);
            if (!headerIndex.ContainsKey("itemId"))
            {
                error = "itemId column is missing in ItemMaster.";
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

                var itemTypeText = ItemCsvUtility.ReadString(cells, headerIndex, "itemType", "Consumable");
                var useTypeText = ItemCsvUtility.ReadString(cells, headerIndex, "useType", "Instant");
                var itemType = ParseItemType(itemTypeText);
                var useType = ParseUseType(useTypeText);

                var row = new Row(
                    itemId,
                    ItemCsvUtility.ReadString(cells, headerIndex, "itemName", itemId),
                    itemType,
                    useType,
                    ItemCsvUtility.ReadBool(cells, headerIndex, "requiresHandEquip", true),
                    ItemCsvUtility.ReadInt(cells, headerIndex, "holdSlotCount", 1),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "consumeOnUse", itemType == ItemType.Consumable),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "dropOnStun", true),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "cooldownSec", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "durationSec", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "range", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "baseDamage", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "stunDamage", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "force", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "tickIntervalSec", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "useDelaySec", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "warningTimeSec", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "scaleMultiplier", 1f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "moveSpeedMultiplier", 1f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "baseDamageMultiplier", 1f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "knockbackResistMultiplier", 1f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "gravityMultiplier", 1f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "jumpMultiplier", 1f),
                    ItemCsvUtility.ReadInt(cells, headerIndex, "superArmorLevel", 0),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "revealOnAttack", false),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "canMelee", false),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "canThrow", false),
                    ItemCsvUtility.ReadInt(cells, headerIndex, "durability", 0),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "stunDropEnabled", true),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "maxActiveUseSec", 0f),
                    ItemCsvUtility.ReadFloat(cells, headerIndex, "overheatCooldownSec", 0f),
                    ItemCsvUtility.ReadString(cells, headerIndex, "prefabPath", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "iconPath", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "sfxId", string.Empty),
                    ItemCsvUtility.ReadString(cells, headerIndex, "vfxId", string.Empty),
                    ItemCsvUtility.ReadBool(cells, headerIndex, "enabled", true));

                if (!row.Enabled)
                {
                    continue;
                }

                rows[itemId] = row;
            }

            if (rows.Count == 0)
            {
                error = "No enabled rows were parsed from ItemMaster.";
                return false;
            }

            return true;
        }

        private static ItemType ParseItemType(string raw)
        {
            return raw != null && raw.Equals("Equipment", StringComparison.OrdinalIgnoreCase)
                ? ItemType.Equipment
                : ItemType.Consumable;
        }

        private static ItemUseType ParseUseType(string raw)
        {
            return raw != null && raw.Equals("Hold", StringComparison.OrdinalIgnoreCase)
                ? ItemUseType.Hold
                : ItemUseType.Instant;
        }
    }
}


