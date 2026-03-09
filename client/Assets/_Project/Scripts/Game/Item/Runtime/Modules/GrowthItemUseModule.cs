namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 성장 아이템 사용 로직 모듈.
    /// </summary>
    public sealed class GrowthItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.Growth;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for growth item.");
            }

            context.Controller.ActivateGrowth(context.Definition);
            return ItemUseModuleResult.SuccessAndConsume();
        }
    }
}

