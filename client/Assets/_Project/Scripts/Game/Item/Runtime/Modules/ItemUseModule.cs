/*
 * 파일 개요:
 * - ItemUseModule 스크립트가 들어 있는 파일이다.
 * - Runtime/Modules 계층에서 특정 아이템 또는 특정 유형의 사용 규칙을 캡슐화한다.
 * - 아이템별 예외 처리와 수치 적용은 여기서 담당하고, 인벤토리 상태 관리나 외부 시스템 연결은 Controller 계층에 남긴다.
 */
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


