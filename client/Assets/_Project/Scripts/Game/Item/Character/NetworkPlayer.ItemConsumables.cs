using Fusion;
using SSAFYPlayTime.Gameplay.Items;

public sealed partial class NetworkPlayer
{
    private bool _itemBuffNetworkReady;
    private ItemBuffSnapshot _localItemBuffSnapshot = ItemBuffSnapshot.Default;

    [Networked] private int NetworkedItemBuffMask { get; set; }
    [Networked] private float NetworkedItemScaleMultiplier { get; set; }
    [Networked] private float NetworkedItemMoveSpeedMultiplier { get; set; }
    [Networked] private float NetworkedItemGravityMultiplier { get; set; }
    [Networked] private float NetworkedItemJumpMultiplier { get; set; }
    [Networked] private float NetworkedItemKnockbackResistMultiplier { get; set; }
    [Networked] private float NetworkedItemOutgoingStunMultiplier { get; set; }
    [Networked] private NetworkBool NetworkedItemSuperArmorActive { get; set; }
    [Networked] private NetworkBool NetworkedItemInvisibilityActive { get; set; }

    public bool CanWriteItemBuffSnapshot()
    {
        if (!_itemBuffNetworkReady)
        {
            return false;
        }

        if (Object == null || !Object.IsValid)
        {
            return false;
        }

        return HasStateAuthority;
    }

    public void SetItemBuffSnapshot(ItemBuffSnapshot snapshot)
    {
        _localItemBuffSnapshot = snapshot;

        if (!CanWriteItemBuffSnapshot())
        {
            return;
        }

        NetworkedItemBuffMask = (int)snapshot.ActiveBuffMask;
        NetworkedItemScaleMultiplier = snapshot.ScaleMultiplier;
        NetworkedItemMoveSpeedMultiplier = snapshot.MoveSpeedMultiplier;
        NetworkedItemGravityMultiplier = snapshot.GravityMultiplier;
        NetworkedItemJumpMultiplier = snapshot.JumpMultiplier;
        NetworkedItemKnockbackResistMultiplier = snapshot.KnockbackResistMultiplier;
        NetworkedItemOutgoingStunMultiplier = snapshot.OutgoingStunMultiplier;
        NetworkedItemSuperArmorActive = snapshot.IsSuperArmorActive;
        NetworkedItemInvisibilityActive = snapshot.IsInvisibilityActive;
    }

    public ItemBuffSnapshot GetItemBuffSnapshot()
    {
        if (!CanWriteItemBuffSnapshot())
        {
            return _localItemBuffSnapshot;
        }

        return new ItemBuffSnapshot(
            (ItemBuffMask)NetworkedItemBuffMask,
            NetworkedItemScaleMultiplier <= 0f ? 1f : NetworkedItemScaleMultiplier,
            NetworkedItemMoveSpeedMultiplier <= 0f ? 1f : NetworkedItemMoveSpeedMultiplier,
            NetworkedItemGravityMultiplier <= 0f ? 1f : NetworkedItemGravityMultiplier,
            NetworkedItemJumpMultiplier <= 0f ? 1f : NetworkedItemJumpMultiplier,
            NetworkedItemKnockbackResistMultiplier <= 0f ? 1f : NetworkedItemKnockbackResistMultiplier,
            NetworkedItemOutgoingStunMultiplier <= 0f ? 1f : NetworkedItemOutgoingStunMultiplier,
            NetworkedItemSuperArmorActive,
            NetworkedItemInvisibilityActive);
    }

    private ItemCharacterBuffApplier ResolveItemBuffApplier()
    {
        return GetComponent<ItemCharacterBuffApplier>();
    }

    private void MarkItemBuffNetworkReady()
    {
        _itemBuffNetworkReady = true;
        SetItemBuffSnapshot(_localItemBuffSnapshot);
    }
}
