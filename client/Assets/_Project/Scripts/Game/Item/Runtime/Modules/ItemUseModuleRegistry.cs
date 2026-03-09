using System;
using System.Collections.Generic;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 ID와 사용 모듈을 매핑하는 레지스트리.
    /// </summary>
    public sealed class ItemUseModuleRegistry
    {
        private readonly Dictionary<string, ItemUseModule> _moduleByItemId;
        private readonly ItemUseModule _fallbackModule;

        public ItemUseModuleRegistry(IEnumerable<ItemUseModule> modules, ItemUseModule fallbackModule = null)
        {
            _moduleByItemId = new Dictionary<string, ItemUseModule>(StringComparer.Ordinal);
            _fallbackModule = fallbackModule ?? new DefaultItemUseModule();

            if (modules == null)
            {
                return;
            }

            foreach (var module in modules)
            {
                if (module == null || string.IsNullOrWhiteSpace(module.ItemId))
                {
                    continue;
                }

                _moduleByItemId[module.ItemId] = module;
            }
        }

        public ItemUseModuleResult TryUse(string itemId, in ItemUseModuleContext context)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return ItemUseModuleResult.Failed("itemId is empty.");
            }

            if (_moduleByItemId.TryGetValue(itemId, out var module))
            {
                return module.TryUse(context);
            }

            return _fallbackModule.TryUse(context);
        }

        public static ItemUseModuleRegistry CreateDefault()
        {
            return new ItemUseModuleRegistry(
                new ItemUseModule[]
                {
                    new BlackholeItemUseModule(),
                    new GrowthItemUseModule(),
                    new ShrinkItemUseModule(),
                    new SuperArmorItemUseModule(),
                    new InvisibilityItemUseModule(),
                    new SatelliteStrikeItemUseModule(),
                    new FlamethrowerItemUseModule(),
                    new WaterMelonSwordItemUseModule()
                },
                new DefaultItemUseModule());
        }
    }
}

