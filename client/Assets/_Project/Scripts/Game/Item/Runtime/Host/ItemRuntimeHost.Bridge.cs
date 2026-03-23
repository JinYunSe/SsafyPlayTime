/*
 * 파일 개요:
 * - ItemRuntimeHost.Bridge 스크립트가 들어 있는 파일이다.
 * - Runtime/Host 계층에서 MonoBehaviour 기반 진입점과 외부 시스템 이벤트 연결을 담당한다.
 * - 씬, 캐릭터, Dev 러너가 런타임 컨트롤러를 사용할 때 거치는 허브 역할이므로 참조 안정성을 우선 유지한다.
 */
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

        void IItemRuntimeBridge.OnMeleeSwingRequested(in MeleeSwingRequest request)
        {
            MeleeSwingRequested?.Invoke(request);
        }

        void IItemRuntimeBridge.OnBuffStateChanged(ItemBuffMask activeBuffMask, in ItemBuffRuntimeState buffState)
        {
            BuffStateChanged?.Invoke(activeBuffMask, buffState);
        }
    }
}

