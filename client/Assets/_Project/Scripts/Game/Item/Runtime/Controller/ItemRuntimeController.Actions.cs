using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 사용 액션(스킬 발동/장비 토글/소모)을 담당한다.
    /// </summary>
    public sealed partial class ItemRuntimeController
    {
        public bool TryUseHeldItem(Vector3 ownerPosition, Vector3 ownerForward, Vector3 targetPosition, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(_heldItemId))
            {
                reason = "No held item.";
                return false;
            }

            if (!_catalog.TryGetDefinition(_heldItemId, out var def))
            {
                reason = $"Held item is missing in catalog: {_heldItemId}";
                return false;
            }

            var now = _bridge.Now;
            if (IsCoolingDown(_heldItemId, now, out var remain))
            {
                reason = $"Cooldown remains: {remain:0.00}s";
                return false;
            }

            ownerForward = NormalizeForward(ownerForward);
            if (targetPosition == Vector3.zero)
            {
                targetPosition = ownerPosition + ownerForward * 6f;
            }

            PlayUsePresentation(def, ownerPosition);

            switch (def.Master.ItemId)
            {
                case ItemIds.BlackholeBomb:
                    UseBlackhole(def, ownerPosition, ownerForward);
                    ConsumeHeldItem(def);
                    return true;

                case ItemIds.Growth:
                    ActivateGrowth(def);
                    ConsumeHeldItem(def);
                    return true;

                case ItemIds.Shrink:
                    ActivateShrink(def);
                    ConsumeHeldItem(def);
                    return true;

                case ItemIds.Americano:
                    ActivateSuperArmor(def);
                    ConsumeHeldItem(def);
                    return true;

                case ItemIds.Invisibility:
                    ActivateInvisibility(def);
                    ConsumeHeldItem(def);
                    return true;

                case ItemIds.SatelliteStrike:
                    UseSatelliteStrike(def, targetPosition);
                    ConsumeHeldItem(def);
                    return true;

                case ItemIds.Flamethrower:
                    ToggleFlamethrower(def);
                    return true;

                case ItemIds.OfficeTool:
                    // 사무용품 계열은 전투 상세 로직을 별도 시스템에서 처리한다.
                    return true;

                default:
                    if (def.Master.ConsumeOnUse)
                    {
                        ConsumeHeldItem(def);
                    }

                    return true;
            }
        }

        private void UseBlackhole(ItemDefinition def, Vector3 ownerPosition, Vector3 ownerForward)
        {
            var request = new BlackholeSkillRequest(
                ownerPosition + ownerForward * 6f,
                Mathf.Max(0f, def.Master.UseDelaySec),
                Mathf.Max(0f, def.Master.DurationSec),
                Mathf.Max(0f, def.Master.Range),
                Mathf.Max(0f, def.Master.Force));
            _bridge.OnBlackholeRequested(request);
        }

        private void UseSatelliteStrike(ItemDefinition def, Vector3 targetPosition)
        {
            var request = new SatelliteStrikeRequest(
                targetPosition,
                Mathf.Max(0f, def.Master.WarningTimeSec),
                Mathf.Max(0f, def.Master.Range),
                Mathf.Max(0f, def.Master.Force),
                Mathf.Max(0f, def.Master.BaseDamage));
            _bridge.OnSatelliteStrikeRequested(request);
        }

        private void ToggleFlamethrower(ItemDefinition def)
        {
            if (_isFlamethrowerActive)
            {
                StopFlamethrowerIfNeeded();
                return;
            }

            var now = _bridge.Now;
            var maxUseSec = def.Master.MaxActiveUseSec > 0f ? def.Master.MaxActiveUseSec : 5f;
            _equipmentEndAt = now + maxUseSec;
            _nextFlamethrowerTickAt = now;
            _isFlamethrowerActive = true;
            _bridge.OnFlamethrowerStart(def.Master.ItemId, _equipmentEndAt);
        }

        private void TickFlamethrower(float now, Vector3 ownerPosition, Vector3 ownerForward)
        {
            if (!_isFlamethrowerActive)
            {
                return;
            }

            if (now >= _equipmentEndAt)
            {
                StopFlamethrowerIfNeeded();
                return;
            }

            if (!_catalog.TryGetDefinition(ItemIds.Flamethrower, out var flameDef))
            {
                StopFlamethrowerIfNeeded();
                return;
            }

            if (now < _nextFlamethrowerTickAt)
            {
                return;
            }

            var interval = flameDef.Master.TickIntervalSec > 0f ? flameDef.Master.TickIntervalSec : DefaultFlamethrowerTickInterval;
            _nextFlamethrowerTickAt = now + interval;

            ownerForward = NormalizeForward(ownerForward);
            var request = new FlamethrowerTickRequest(
                ownerPosition + Vector3.up * 1.2f + ownerForward * 0.7f,
                ownerForward,
                Mathf.Max(0f, flameDef.Master.Range),
                DefaultFlamethrowerRadius,
                Mathf.Max(0f, flameDef.Master.Force),
                Mathf.Max(0f, flameDef.Master.BaseDamage),
                Mathf.Max(0f, flameDef.Master.StunDamage));
            _bridge.OnFlamethrowerTick(request);
        }

        private void StopFlamethrowerIfNeeded()
        {
            if (!_isFlamethrowerActive)
            {
                return;
            }

            if (_catalog.TryGetDefinition(ItemIds.Flamethrower, out var flameDef))
            {
                SetCooldown(flameDef.Master.ItemId, flameDef.Master.OverheatCooldownSec);
            }

            _isFlamethrowerActive = false;
            _equipmentEndAt = 0f;
            _nextFlamethrowerTickAt = 0f;
            _bridge.OnFlamethrowerStop(ItemIds.Flamethrower);
        }

        private void PlayUsePresentation(ItemDefinition def, Vector3 position)
        {
            var sfxId = def.ResolveUseSfxId();
            if (!string.IsNullOrWhiteSpace(sfxId))
            {
                var loop = _catalog.TryGetSound(sfxId, out var soundRow) && soundRow.Loop;
                _bridge.OnPlaySfx(sfxId, position, loop);
            }
            // 테이블 기반 시작 VFX는 현재 전부 비활성화한다.
            // (투사체형 프리팹 자동 생성 방지 목적)
        }

        private void ConsumeHeldItem(ItemDefinition def)
        {
            SetCooldown(def.Master.ItemId, def.Master.CooldownSec);
            var consumedId = def.Master.ItemId;
            SetHeldItem(string.Empty);
            _bridge.OnItemConsumed(consumedId);
        }
    }
}
