/*
 * 파일 개요:
 * - ItemUseModuleContext 스크립트가 들어 있는 파일이다.
 * - Runtime/Modules 계층에서 특정 아이템 또는 특정 유형의 사용 규칙을 캡슐화한다.
 * - 아이템별 예외 처리와 수치 적용은 여기서 담당하고, 인벤토리 상태 관리나 외부 시스템 연결은 Controller 계층에 남긴다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 아이템 사용 모듈이 참조하는 런타임 문맥 값 묶음.
    /// </summary>
    public readonly struct ItemUseModuleContext
    {
        public ItemUseModuleContext(
            ItemRuntimeController controller,
            ItemDefinition definition,
            Vector3 ownerPosition,
            Vector3 ownerForward,
            Vector3 targetPosition)
        {
            Controller = controller;
            Definition = definition;
            OwnerPosition = ownerPosition;
            OwnerForward = ownerForward;
            TargetPosition = targetPosition;
        }

        public ItemRuntimeController Controller { get; }
        public ItemDefinition Definition { get; }
        public Vector3 OwnerPosition { get; }
        public Vector3 OwnerForward { get; }
        public Vector3 TargetPosition { get; }
    }
}


