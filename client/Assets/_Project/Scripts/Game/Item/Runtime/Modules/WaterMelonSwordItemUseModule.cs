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
            // 수박검 전투 상세 로직은 별도 전투 시스템에서 처리한다.
            return ItemUseModuleResult.SuccessWithoutConsume();
        }
    }
}

