namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 축소 아이템 사용 로직 모듈.
    /// </summary>
    public sealed class ShrinkItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.Shrink;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for shrink item.");
            }

            context.Controller.ActivateShrink(context.Definition);
            return ItemUseModuleResult.SuccessAndConsume();
        }
    }
}

