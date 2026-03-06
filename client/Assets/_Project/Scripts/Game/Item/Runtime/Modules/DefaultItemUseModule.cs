namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 모듈이 등록되지 않은 아이템에 대한 기본 처리 모듈.
    /// </summary>
    public sealed class DefaultItemUseModule : ItemUseModule
    {
        public override string ItemId => string.Empty;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for default item module.");
            }

            return context.Definition.Master.ConsumeOnUse
                ? ItemUseModuleResult.SuccessAndConsume()
                : ItemUseModuleResult.SuccessWithoutConsume();
        }
    }
}

