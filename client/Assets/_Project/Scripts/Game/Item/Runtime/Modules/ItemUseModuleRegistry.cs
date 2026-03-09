/*
 * 파일 개요:
 * - ItemUseModuleRegistry 스크립트가 들어 있는 파일이다.
 * - Runtime/Modules 계층에서 특정 아이템 또는 특정 유형의 사용 규칙을 캡슐화한다.
 * - 아이템별 예외 처리와 수치 적용은 여기서 담당하고, 인벤토리 상태 관리나 외부 시스템 연결은 Controller 계층에 남긴다.
 */
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


