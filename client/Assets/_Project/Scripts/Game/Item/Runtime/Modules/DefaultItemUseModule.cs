/*
 * 파일 개요:
 * - DefaultItemUseModule 스크립트가 들어 있는 파일이다.
 * - Runtime/Modules 계층에서 특정 아이템 또는 특정 유형의 사용 규칙을 캡슐화한다.
 * - 아이템별 예외 처리와 수치 적용은 여기서 담당하고, 인벤토리 상태 관리나 외부 시스템 연결은 Controller 계층에 남긴다.
 */
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


