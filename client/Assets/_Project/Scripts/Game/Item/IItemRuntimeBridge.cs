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
        void OnSpawnVfx(string vfxId, Vector3 worldPosition);

        void OnBlackholeRequested(in BlackholeSkillRequest request);
        void OnSatelliteStrikeRequested(in SatelliteStrikeRequest request);
        void OnFlamethrowerStart(string itemId, float endAtSec);
        void OnFlamethrowerTick(in FlamethrowerTickRequest request);
        void OnFlamethrowerStop(string itemId);

        void OnBuffStateChanged(ItemBuffMask activeBuffMask, in ItemBuffRuntimeState buffState);
    }
}
