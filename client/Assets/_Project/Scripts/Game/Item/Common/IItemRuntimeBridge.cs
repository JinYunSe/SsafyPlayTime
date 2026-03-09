/*
 * 파일 개요:
 * - IItemRuntimeBridge 스크립트가 들어 있는 파일이다.
 * - Common 계층에서 아이템 시스템 전반이 공유하는 모델, 상수, 인터페이스를 정의한다.
 * - 이 파일이 바뀌면 Character, World, Runtime 전부에 영향이 갈 수 있으므로 하위 호환성을 우선 확인한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public interface IItemRuntimeBridge
    {
        float Now { get; }

        void OnHeldItemChanged(string heldItemId);
        void OnItemDropped(string itemId, ItemDropReason reason);
        void OnItemConsumed(string itemId);

        void OnPlaySfx(string sfxId, Vector3 worldPosition, bool loop);

        void OnBlackholeRequested(in BlackholeSkillRequest request);
        void OnSatelliteStrikeRequested(in SatelliteStrikeRequest request);
        void OnFlamethrowerStart(string itemId, float endAtSec);
        void OnFlamethrowerTick(in FlamethrowerTickRequest request);
        void OnFlamethrowerStop(string itemId);
        void OnMeleeSwingRequested(in MeleeSwingRequest request);

        void OnBuffStateChanged(ItemBuffMask activeBuffMask, in ItemBuffRuntimeState buffState);
    }
}

