/*
 * 파일 개요:
 * - ItemRuntimeController.Buffs 스크립트가 들어 있는 파일이다.
 * - Runtime/Controller 계층에서 아이템 상태 전이, 사용 요청 처리, 공용 브리지 호출을 조합한다.
 * - 아이템 공통 흐름을 수정할 때 진입점으로 삼는 파일이며, 개별 아이템 예외는 Modules 계층으로 분리하는 것을 우선한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 버프 활성화/해제 타이머를 관리한다.
    /// </summary>
    public sealed partial class ItemRuntimeController
    {
        internal void ActivateGrowth(ItemDefinition def)
        {
            // 성장/축소 버프는 동시에 유지하지 않는다.
            _activeBuffMask |= ItemBuffMask.Growth;
            _activeBuffMask &= ~ItemBuffMask.Shrink;
            _growthEndAt = _bridge.Now + Mathf.Max(0f, def.Master.DurationSec);
            _shrinkEndAt = 0f;
            NotifyBuffStateChanged();
        }

        internal void ActivateShrink(ItemDefinition def)
        {
            // 성장/축소 버프는 동시에 유지하지 않는다.
            _activeBuffMask |= ItemBuffMask.Shrink;
            _activeBuffMask &= ~ItemBuffMask.Growth;
            _shrinkEndAt = _bridge.Now + Mathf.Max(0f, def.Master.DurationSec);
            _growthEndAt = 0f;
            NotifyBuffStateChanged();
        }

        internal void ActivateSuperArmor(ItemDefinition def)
        {
            _activeBuffMask |= ItemBuffMask.SuperArmor;
            _superArmorEndAt = _bridge.Now + Mathf.Max(0f, def.Master.DurationSec);
            NotifyBuffStateChanged();
        }

        internal void ActivateInvisibility(ItemDefinition def)
        {
            _activeBuffMask |= ItemBuffMask.Invisibility;
            _invisibilityEndAt = _bridge.Now + Mathf.Max(0f, def.Master.DurationSec);
            NotifyBuffStateChanged();
        }

        private void TickBuffDurations(float now)
        {
            var changed = false;
            if ((_activeBuffMask & ItemBuffMask.Growth) != 0 && now >= _growthEndAt)
            {
                _activeBuffMask &= ~ItemBuffMask.Growth;
                _growthEndAt = 0f;
                changed = true;
            }

            if ((_activeBuffMask & ItemBuffMask.Shrink) != 0 && now >= _shrinkEndAt)
            {
                _activeBuffMask &= ~ItemBuffMask.Shrink;
                _shrinkEndAt = 0f;
                changed = true;
            }

            if ((_activeBuffMask & ItemBuffMask.SuperArmor) != 0 && now >= _superArmorEndAt)
            {
                _activeBuffMask &= ~ItemBuffMask.SuperArmor;
                _superArmorEndAt = 0f;
                changed = true;
            }

            if ((_activeBuffMask & ItemBuffMask.Invisibility) != 0 && now >= _invisibilityEndAt)
            {
                _activeBuffMask &= ~ItemBuffMask.Invisibility;
                _invisibilityEndAt = 0f;
                changed = true;
            }

            if (changed)
            {
                NotifyBuffStateChanged();
            }
        }

        private void NotifyBuffStateChanged()
        {
            var now = _bridge.Now;
            var state = new ItemBuffRuntimeState(
                Mathf.Max(0f, _growthEndAt - now),
                Mathf.Max(0f, _shrinkEndAt - now),
                Mathf.Max(0f, _superArmorEndAt - now),
                Mathf.Max(0f, _invisibilityEndAt - now));
            _bridge.OnBuffStateChanged(_activeBuffMask, state);
        }
    }
}

