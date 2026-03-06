namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 사용 모듈 실행 결과를 표현한다.
    /// </summary>
    public readonly struct ItemUseModuleResult
    {
        public ItemUseModuleResult(bool success, bool consumeHeldItem, string reason)
        {
            Success = success;
            ConsumeHeldItem = consumeHeldItem;
            Reason = reason ?? string.Empty;
        }

        public bool Success { get; }
        public bool ConsumeHeldItem { get; }
        public string Reason { get; }

        public static ItemUseModuleResult SuccessAndConsume()
        {
            return new ItemUseModuleResult(true, true, string.Empty);
        }

        public static ItemUseModuleResult SuccessWithoutConsume()
        {
            return new ItemUseModuleResult(true, false, string.Empty);
        }

        public static ItemUseModuleResult Failed(string reason)
        {
            return new ItemUseModuleResult(false, false, reason);
        }
    }
}

