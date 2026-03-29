/*
 * 파일 개요:
 * - ShrinkItemUseModule 스크립트가 들어 있는 파일이다.
 * - Runtime/Modules 계층에서 특정 아이템 또는 특정 유형의 사용 규칙을 캡슐화한다.
 * - 아이템별 예외 처리와 수치 적용은 여기서 담당하고, 인벤토리 상태 관리나 외부 시스템 연결은 Controller 계층에 남긴다.
 */
namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 축소 아이템 사용 로직 모듈.
    /// </summary>
    public sealed class ShrinkItemUseModule : ItemUseModule
    {
        public override string ItemId => ItemIds.Shrink;

        public override ItemUseModuleResult TryUse(in ItemUseModuleContext context)
        {
            if (context.Definition == null)
            {
                return ItemUseModuleResult.Failed("Definition missing for shrink item.");
            }

            context.Controller.ActivateShrink(context.Definition);
            return ItemUseModuleResult.SuccessAndConsume();
        }
    }
}


