namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 위성 폭격 사용 로직 모듈.
    /// </summary>
    public sealed class SatelliteStrikeItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.SatelliteStrike;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for satellite strike item.");
            }

            context.Controller.UseSatelliteStrike(
                context.Definition,
                context.OwnerPosition,
                context.OwnerForward,
                context.TargetPosition);
            return ItemUseModuleResult.SuccessAndConsume();
        }
    }
}

