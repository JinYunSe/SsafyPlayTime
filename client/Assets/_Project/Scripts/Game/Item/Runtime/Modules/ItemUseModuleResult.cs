/*
 * 파일 개요:
 * - ItemUseModuleResult 스크립트가 들어 있는 파일이다.
 * - Runtime/Modules 계층에서 특정 아이템 또는 특정 유형의 사용 규칙을 캡슐화한다.
 * - 아이템별 예외 처리와 수치 적용은 여기서 담당하고, 인벤토리 상태 관리나 외부 시스템 연결은 Controller 계층에 남긴다.
 */
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


