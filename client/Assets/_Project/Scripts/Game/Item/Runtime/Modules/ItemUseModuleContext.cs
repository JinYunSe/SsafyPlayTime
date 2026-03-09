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

