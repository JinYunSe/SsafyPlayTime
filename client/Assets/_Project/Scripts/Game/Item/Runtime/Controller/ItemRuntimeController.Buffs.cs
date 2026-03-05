using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 버프 활성화/해제 타이머를 관리한다.
    /// </summary>
    public sealed partial class ItemRuntimeController
    {
        private void ActivateGrowth(ItemDefinition def)
        {
            // 성장/축소 버프는 동시에 유지하지 않는다.
            _activeBuffMask |= ItemBuffMask.Growth;
            _activeBuffMask &= ~ItemBuffMask.Shrink;
            _growthEndAt = _bridge.Now + Mathf.Max(0f, def.Master.DurationSec);
            _shrinkEndAt = 0f;
            NotifyBuffStateChanged();
        }

        private void ActivateShrink(ItemDefinition def)
        {
            // 성장/축소 버프는 동시에 유지하지 않는다.
            _activeBuffMask |= ItemBuffMask.Shrink;
            _activeBuffMask &= ~ItemBuffMask.Growth;
            _shrinkEndAt = _bridge.Now + Mathf.Max(0f, def.Master.DurationSec);
            _growthEndAt = 0f;
            NotifyBuffStateChanged();
        }

        private void ActivateSuperArmor(ItemDefinition def)
        {
            _activeBuffMask |= ItemBuffMask.SuperArmor;
            _superArmorEndAt = _bridge.Now + Mathf.Max(0f, def.Master.DurationSec);
            NotifyBuffStateChanged();
        }

        private void ActivateInvisibility(ItemDefinition def)
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
