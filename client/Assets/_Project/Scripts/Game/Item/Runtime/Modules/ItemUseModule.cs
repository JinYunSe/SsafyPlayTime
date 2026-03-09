namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 사용 동작을 모듈 단위로 분리하기 위한 기본 추상 타입.
    /// </summary>
    public abstract class ItemUseModule
    {
        /// <summary>
        /// 이 모듈이 처리할 아이템 ID.
        /// </summary>
        public abstract string ItemId { get; }

        /// <summary>
        /// 아이템 사용 로직을 실행한다.
        /// </summary>
        public abstract ItemUseModuleResult TryUse(in ItemUseModuleContext context);
    }
}

