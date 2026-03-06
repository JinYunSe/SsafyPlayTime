namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 슈퍼아머(아메리카노) 사용 로직 모듈.
    /// </summary>
    public sealed class SuperArmorItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.Americano;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for super armor item.");
            }

            context.Controller.ActivateSuperArmor(context.Definition);
            return ItemUseModuleResult.SuccessAndConsume();
        }
    }
}

