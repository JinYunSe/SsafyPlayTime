/*
 * 파일 개요:
 * - ItemRuntimeController.Buffs 스크립트가 들어 있는 파일이다.
 * - Consumable 버프의 활성화, 상호 배타 규칙, 지속시간 만료를 관리한다.
 * - 프로토타입식 분기 나열 대신, 버프 마스크와 종료 시각을 명시적으로 다루는 구조로 새로 정리했다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// Consumable 버프 활성화/만료를 관리한다.
    /// </summary>
    public sealed partial class ItemRuntimeController
    {
        internal void ActivateGrowth(ItemDefinition def)
        {
            ActivateTimedBuff(ItemBuffMask.Growth, def, ItemBuffMask.Shrink);
        }

        internal void ActivateShrink(ItemDefinition def)
        {
            ActivateTimedBuff(ItemBuffMask.Shrink, def, ItemBuffMask.Growth);
        }

        internal void ActivateSuperArmor(ItemDefinition def)
        {
            ActivateTimedBuff(ItemBuffMask.SuperArmor, def, ItemBuffMask.None);
        }

        internal void ActivateInvisibility(ItemDefinition def)
        {
            ActivateTimedBuff(ItemBuffMask.Invisibility, def, ItemBuffMask.None);
        }

        private void ActivateTimedBuff(ItemBuffMask buffMask, ItemDefinition def, ItemBuffMask exclusiveMask)
        {
            if (def == null)
            {
                ItemRuntimeLog.Warn(buffMask.ToString(), "버프 활성화 실패: definition is null");
                return;
            }

            if (exclusiveMask != ItemBuffMask.None)
            {
                ClearBuff(exclusiveMask);
            }

            _activeBuffMask |= buffMask;
            SetBuffEndTime(buffMask, _bridge.Now + Mathf.Max(0f, def.Master.DurationSec));
            ItemRuntimeLog.Info(def.Master.ItemId, $"버프 활성화: mask={buffMask}, duration={Mathf.Max(0f, def.Master.DurationSec):0.00}, activeMask={_activeBuffMask}");
            NotifyBuffStateChanged();
        }

        private void TickBuffDurations(float now)
        {
            var changed = false;
            changed |= TryExpireBuff(ItemBuffMask.Growth, now);
            changed |= TryExpireBuff(ItemBuffMask.Shrink, now);
            changed |= TryExpireBuff(ItemBuffMask.SuperArmor, now);
            changed |= TryExpireBuff(ItemBuffMask.Invisibility, now);

            if (changed)
            {
                NotifyBuffStateChanged();
            }
        }

        private bool TryExpireBuff(ItemBuffMask buffMask, float now)
        {
            if ((_activeBuffMask & buffMask) == 0)
            {
                return false;
            }

            var endTime = GetBuffEndTime(buffMask);
            if (endTime <= 0f || now < endTime)
            {
                return false;
            }

            ClearBuff(buffMask);
            ItemRuntimeLog.Info(buffMask.ToString(), $"버프 만료: now={now:0.00}, activeMask={_activeBuffMask}");
            return true;
        }

        private void ClearBuff(ItemBuffMask buffMask)
        {
            _activeBuffMask &= ~buffMask;
            SetBuffEndTime(buffMask, 0f);
        }

        private float GetBuffEndTime(ItemBuffMask buffMask)
        {
            if (buffMask == ItemBuffMask.Growth)
            {
                return _growthEndAt;
            }

            if (buffMask == ItemBuffMask.Shrink)
            {
                return _shrinkEndAt;
            }

            if (buffMask == ItemBuffMask.SuperArmor)
            {
                return _superArmorEndAt;
            }

            if (buffMask == ItemBuffMask.Invisibility)
            {
                return _invisibilityEndAt;
            }

            return 0f;
        }

        private void SetBuffEndTime(ItemBuffMask buffMask, float endTime)
        {
            if (buffMask == ItemBuffMask.Growth)
            {
                _growthEndAt = endTime;
                return;
            }

            if (buffMask == ItemBuffMask.Shrink)
            {
                _shrinkEndAt = endTime;
                return;
            }

            if (buffMask == ItemBuffMask.SuperArmor)
            {
                _superArmorEndAt = endTime;
                return;
            }

            if (buffMask == ItemBuffMask.Invisibility)
            {
                _invisibilityEndAt = endTime;
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
            ItemRuntimeLog.Info("BuffState", $"버프 상태 전파: mask={_activeBuffMask}, growth={state.GrowthRemainSec:0.0}, shrink={state.ShrinkRemainSec:0.0}, superArmor={state.SuperArmorRemainSec:0.0}, invis={state.InvisibilityRemainSec:0.0}");
            _bridge.OnBuffStateChanged(_activeBuffMask, state);
        }
    }
}
