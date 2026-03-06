namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 투명화 아이템 사용 로직 모듈.
    /// </summary>
    public sealed class InvisibilityItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.Invisibility;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for invisibility item.");
            }

            context.Controller.ActivateInvisibility(context.Definition);
            return ItemUseModuleResult.SuccessAndConsume();
        }
    }
}

