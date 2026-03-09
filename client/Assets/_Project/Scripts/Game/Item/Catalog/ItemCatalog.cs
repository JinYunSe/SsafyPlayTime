/*
 * 파일 개요:
 * - ItemCatalog 스크립트가 들어 있는 파일이다.
 * - Catalog 계층에서 CSV 로더 결과를 합쳐 런타임에서 사용할 아이템 카탈로그를 구성한다.
 * - 데이터 구조를 바꾸더라도 실제 사용 흐름과 캐싱 정책은 Runtime 계층과 함께 검토해야 한다.
 */
using System;
using System.Collections.Generic;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed class ItemDefinition
    {
        public ItemDefinition(
            ItemMasterCsvLoader.Row master,
            ItemPresentationTableCsvLoader.Row? presentation)
        {
            Master = master;
            Presentation = presentation;
        }

        public ItemMasterCsvLoader.Row Master { get; }
        public ItemPresentationTableCsvLoader.Row? Presentation { get; }

        public string ResolveUseSfxId()
        {
            if (Presentation.HasValue && !string.IsNullOrWhiteSpace(Presentation.Value.UseSfxId))
            {
                return Presentation.Value.UseSfxId;
            }

            return Master.SfxId;
        }

        public string ResolveStartVfxId()
        {
            if (Presentation.HasValue && !string.IsNullOrWhiteSpace(Presentation.Value.StartVfxId))
            {
                return Presentation.Value.StartVfxId;
            }

            return Master.VfxId;
        }
    }

    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, ItemDefinition> _definitions;
        private readonly IReadOnlyDictionary<string, SoundAssetTableCsvLoader.Row> _soundRows;
        private readonly IReadOnlyDictionary<string, VfxAssetTableCsvLoader.Row> _vfxRows;

        public ItemCatalog(
            Dictionary<string, ItemDefinition> definitions,
            IReadOnlyDictionary<string, SoundAssetTableCsvLoader.Row> soundRows,
            IReadOnlyDictionary<string, VfxAssetTableCsvLoader.Row> vfxRows)
        {
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _soundRows = soundRows ?? throw new ArgumentNullException(nameof(soundRows));
            _vfxRows = vfxRows ?? throw new ArgumentNullException(nameof(vfxRows));
        }

        public IReadOnlyDictionary<string, ItemDefinition> Definitions => _definitions;

        public bool TryGetDefinition(string itemId, out ItemDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                definition = null;
                return false;
            }

            return _definitions.TryGetValue(itemId, out definition);
        }

        public bool TryGetSound(string sfxId, out SoundAssetTableCsvLoader.Row row)
        {
            if (string.IsNullOrWhiteSpace(sfxId))
            {
                row = default;
                return false;
            }

            return _soundRows.TryGetValue(sfxId, out row);
        }

        public bool TryGetVfx(string vfxId, out VfxAssetTableCsvLoader.Row row)
        {
            if (string.IsNullOrWhiteSpace(vfxId))
            {
                row = default;
                return false;
            }

            return _vfxRows.TryGetValue(vfxId, out row);
        }
    }
}

