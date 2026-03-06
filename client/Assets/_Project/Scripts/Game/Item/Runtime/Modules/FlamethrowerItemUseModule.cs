namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 화염방사기 토글 사용 로직 모듈.
    /// </summary>
    public sealed class FlamethrowerItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.Flamethrower;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for flamethrower item.");
            }

            context.Controller.ToggleFlamethrower(context.Definition);
            return ItemUseModuleResult.SuccessWithoutConsume();
        }
    }
}

