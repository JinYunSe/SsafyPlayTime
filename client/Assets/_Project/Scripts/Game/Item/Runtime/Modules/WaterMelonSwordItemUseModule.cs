namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 수박검 사용 로직 모듈.
    /// </summary>
    public sealed class WaterMelonSwordItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.WaterMelonSword;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for watermelon sword item.");
            }

            context.Controller.UseWatermelonSword(context.Definition);
            return ItemUseModuleResult.SuccessWithoutConsume();
        }
    }
}

