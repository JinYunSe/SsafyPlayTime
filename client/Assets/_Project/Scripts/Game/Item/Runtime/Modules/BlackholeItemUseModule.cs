namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 블랙홀 폭탄 사용 로직 모듈.
    /// </summary>
    public sealed class BlackholeItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.BlackholeBomb;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for blackhole item.");
            }

            context.Controller.UseBlackhole(context.Definition, context.OwnerPosition, context.OwnerForward);
            return ItemUseModuleResult.SuccessAndConsume();
        }
    }
}

