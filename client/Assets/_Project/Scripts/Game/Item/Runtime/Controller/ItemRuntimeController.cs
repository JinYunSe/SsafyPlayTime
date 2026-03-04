using System;
using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 보유/사용/쿨다운/버프 상태를 순수 로직으로 관리한다.
    /// </summary>
    public sealed partial class ItemRuntimeController
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
