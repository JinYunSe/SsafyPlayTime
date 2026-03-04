using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void Update()
        {
            TickAudioListenerGuard();
            UpdateFlamethrowerVisualFollow();
            TickLocalDebugController();

            if (!enableHotkeys || itemRuntimeHost == null)
            {
                return;
            }

            var alpha1 = Input.GetKeyDown(KeyCode.Alpha1);
            if (!alpha1)
            {
                return;
            }

            if (!CanRunItemInput())
            {
                LogStatus("Host authority: client input ignored");
                return;
            }

            TriggerItemByHotkey(ItemIds.BlackholeBomb);
        }

        // 단축키로 아이템을 강제 장착/사용한다.
        private void TriggerItemByHotkey(string itemId)
        {
            if (!EnsureRuntimeReady())
            {
                return;
            }

            if (!string.Equals(itemRuntimeHost.HeldItemId, itemId, System.StringComparison.Ordinal))
            {
                if (!itemRuntimeHost.TryPickup(itemId, out var pickupReason))
                {
                    if (!forceReplaceHeldItemOnHotkey)
                    {
                        LogStatus($"Pickup failed: {pickupReason}");
                        return;
                    }

                    itemRuntimeHost.ResetRuntimeState();
                    if (!itemRuntimeHost.TryPickup(itemId, out pickupReason))
                    {
                        LogStatus($"Forced pickup failed: {pickupReason}");
                        return;
                    }
                }
            }

            if (!itemRuntimeHost.TryUseHeldItem(Vector3.zero, out var useReason))
            {
                LogStatus($"Use failed: {useReason}");
            }
        }
    }
}
