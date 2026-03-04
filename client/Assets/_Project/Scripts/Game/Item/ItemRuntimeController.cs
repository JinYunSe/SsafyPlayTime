using System;
using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed class ItemRuntimeController
    {
        private const float DefaultFlamethrowerRadius = 1.2f;
        private const float DefaultFlamethrowerTickInterval = 0.2f;

        private readonly ItemCatalog _catalog;
        private readonly IItemRuntimeBridge _bridge;
        private readonly Dictionary<string, float> _cooldownEndTimes = new(StringComparer.Ordinal);

        private string _heldItemId = string.Empty;
        private bool _isFlamethrowerActive;
        private float _equipmentEndAt;
        private float _nextFlamethrowerTickAt;

        private ItemBuffMask _activeBuffMask = ItemBuffMask.None;
        private float _growthEndAt;
        private float _shrinkEndAt;
        private float _superArmorEndAt;
        private float _invisibilityEndAt;

        public ItemRuntimeController(ItemCatalog catalog, IItemRuntimeBridge bridge)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public string HeldItemId => _heldItemId;
        public bool IsFlamethrowerActive => _isFlamethrowerActive;
        public ItemBuffMask ActiveBuffMask => _activeBuffMask;
        public float EquipmentEndAtSec => _equipmentEndAt;

        public bool TryPickup(string itemId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                reason = "itemId is empty.";
                return false;
            }

            if (!_catalog.TryGetDefinition(itemId, out _))
            {
                reason = $"Unknown itemId: {itemId}";
                return false;
            }

            if (!string.IsNullOrEmpty(_heldItemId))
            {
                reason = $"Already holding an item: {_heldItemId}";
                return false;
            }

            SetHeldItem(itemId);
            return true;
        }

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
                    // 사무용 도구는 실제 타격/투척 로직을 외부 전투 시스템에서 처리하도록 둔다.
                    return true;

                default:
                    if (def.Master.ConsumeOnUse)
                    {
                        ConsumeHeldItem(def);
                    }

                    return true;
            }
        }

        public void Tick(Vector3 ownerPosition, Vector3 ownerForward)
        {
            var now = _bridge.Now;
            TickBuffDurations(now);
            TickFlamethrower(now, ownerPosition, ownerForward);
        }

        public void NotifyStunned()
        {
            if (string.IsNullOrWhiteSpace(_heldItemId))
            {
                return;
            }

            if (!_catalog.TryGetDefinition(_heldItemId, out var def))
            {
                return;
            }

            if (!def.Master.DropOnStun && !def.Master.StunDropEnabled)
            {
                return;
            }

            StopFlamethrowerIfNeeded();
            var dropped = _heldItemId;
            SetHeldItem(string.Empty);
            _bridge.OnItemDropped(dropped, ItemDropReason.Stunned);
        }

        public void ResetRuntimeState()
        {
            StopFlamethrowerIfNeeded();
            _cooldownEndTimes.Clear();
            _activeBuffMask = ItemBuffMask.None;
            _growthEndAt = 0f;
            _shrinkEndAt = 0f;
            _superArmorEndAt = 0f;
            _invisibilityEndAt = 0f;
            SetHeldItem(string.Empty);
            NotifyBuffStateChanged();
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

        private void PlayUsePresentation(ItemDefinition def, Vector3 position)
        {
            var sfxId = def.ResolveUseSfxId();
            if (!string.IsNullOrWhiteSpace(sfxId))
            {
                var loop = _catalog.TryGetSound(sfxId, out var soundRow) && soundRow.Loop;
                _bridge.OnPlaySfx(sfxId, position, loop);
            }

            var vfxId = def.ResolveStartVfxId();
            if (!string.IsNullOrWhiteSpace(vfxId))
            {
                _bridge.OnSpawnVfx(vfxId, position);
            }
        }

        private void ConsumeHeldItem(ItemDefinition def)
        {
            SetCooldown(def.Master.ItemId, def.Master.CooldownSec);
            var consumedId = def.Master.ItemId;
            SetHeldItem(string.Empty);
            _bridge.OnItemConsumed(consumedId);
        }

        private void SetCooldown(string itemId, float cooldownSec)
        {
            if (cooldownSec <= 0f)
            {
                return;
            }

            _cooldownEndTimes[itemId] = _bridge.Now + cooldownSec;
        }

        private bool IsCoolingDown(string itemId, float now, out float remain)
        {
            remain = 0f;
            if (!_cooldownEndTimes.TryGetValue(itemId, out var endAt))
            {
                return false;
            }

            remain = endAt - now;
            if (remain <= 0f)
            {
                _cooldownEndTimes.Remove(itemId);
                remain = 0f;
                return false;
            }

            return true;
        }

        private void SetHeldItem(string itemId)
        {
            if (itemId == _heldItemId)
            {
                return;
            }

            _heldItemId = itemId ?? string.Empty;
            _bridge.OnHeldItemChanged(_heldItemId);
        }

        private static Vector3 NormalizeForward(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }
    }
}
