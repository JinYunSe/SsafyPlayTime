using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemRuntimeHost
    {
        // 런타임 컨트롤러의 콜백을 외부 구독 이벤트로 전달한다.
        void IItemRuntimeBridge.OnHeldItemChanged(string heldItemId)
        {
            HeldItemChanged?.Invoke(heldItemId);
        }

        void IItemRuntimeBridge.OnItemDropped(string itemId, ItemDropReason reason)
        {
            ItemDropped?.Invoke(itemId, reason);
        }

        void IItemRuntimeBridge.OnItemConsumed(string itemId)
        {
            ItemConsumed?.Invoke(itemId);
        }

        void IItemRuntimeBridge.OnPlaySfx(string sfxId, Vector3 worldPosition, bool loop)
        {
            SfxRequested?.Invoke(sfxId, worldPosition, loop);
        }

        void IItemRuntimeBridge.OnBlackholeRequested(in BlackholeSkillRequest request)
        {
            BlackholeRequested?.Invoke(request);
        }

        void IItemRuntimeBridge.OnSatelliteStrikeRequested(in SatelliteStrikeRequest request)
        {
            SatelliteStrikeRequested?.Invoke(request);
        }

        void IItemRuntimeBridge.OnFlamethrowerStart(string itemId, float endAtSec)
        {
            FlamethrowerStarted?.Invoke(itemId, endAtSec);
        }

        void IItemRuntimeBridge.OnFlamethrowerTick(in FlamethrowerTickRequest request)
        {
            FlamethrowerTicked?.Invoke(request);
        }

        void IItemRuntimeBridge.OnFlamethrowerStop(string itemId)
        {
            FlamethrowerStopped?.Invoke(itemId);
        }

        void IItemRuntimeBridge.OnBuffStateChanged(ItemBuffMask activeBuffMask, in ItemBuffRuntimeState buffState)
        {
            BuffStateChanged?.Invoke(activeBuffMask, buffState);
        }
    }
}
