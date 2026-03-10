/*
 * 파일 개요:
 * - ItemBuffSnapshot 스크립트가 들어 있는 파일이다.
 * - Consumable 버프 적용 결과를 캐릭터/네트워크 계층으로 전달하기 위한 공용 스냅샷 모델을 정의한다.
 * - 프로토타입 계산식에 의존하지 않고, 적용된 최종 배율만 명시적으로 전달하는 용도로 사용한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 현재 적용 중인 Consumable 버프의 최종 결과값을 담는다.
    /// </summary>
    public readonly struct ItemBuffSnapshot
    {
        public static readonly ItemBuffSnapshot Default = new(
            ItemBuffMask.None,
            1f,
            1f,
            1f,
            1f,
            1f,
            1f,
            false,
            false);

        public ItemBuffSnapshot(
            ItemBuffMask activeBuffMask,
            float scaleMultiplier,
            float moveSpeedMultiplier,
            float gravityMultiplier,
            float jumpMultiplier,
            float knockbackResistMultiplier,
            float outgoingStunMultiplier,
            bool isSuperArmorActive,
            bool isInvisibilityActive)
        {
            ActiveBuffMask = activeBuffMask;
            ScaleMultiplier = Mathf.Max(0.05f, scaleMultiplier);
            MoveSpeedMultiplier = Mathf.Max(0.05f, moveSpeedMultiplier);
            GravityMultiplier = Mathf.Max(0.05f, gravityMultiplier);
            JumpMultiplier = Mathf.Max(0.05f, jumpMultiplier);
            KnockbackResistMultiplier = Mathf.Max(0.05f, knockbackResistMultiplier);
            OutgoingStunMultiplier = Mathf.Max(0.05f, outgoingStunMultiplier);
            IsSuperArmorActive = isSuperArmorActive;
            IsInvisibilityActive = isInvisibilityActive;
        }

        public ItemBuffMask ActiveBuffMask { get; }
        public float ScaleMultiplier { get; }
        public float MoveSpeedMultiplier { get; }
        public float GravityMultiplier { get; }
        public float JumpMultiplier { get; }
        public float KnockbackResistMultiplier { get; }
        public float OutgoingStunMultiplier { get; }
        public bool IsSuperArmorActive { get; }
        public bool IsInvisibilityActive { get; }

        public bool ApproximatelyEquals(in ItemBuffSnapshot other)
        {
            return ActiveBuffMask == other.ActiveBuffMask &&
                   Mathf.Approximately(ScaleMultiplier, other.ScaleMultiplier) &&
                   Mathf.Approximately(MoveSpeedMultiplier, other.MoveSpeedMultiplier) &&
                   Mathf.Approximately(GravityMultiplier, other.GravityMultiplier) &&
                   Mathf.Approximately(JumpMultiplier, other.JumpMultiplier) &&
                   Mathf.Approximately(KnockbackResistMultiplier, other.KnockbackResistMultiplier) &&
                   Mathf.Approximately(OutgoingStunMultiplier, other.OutgoingStunMultiplier) &&
                   IsSuperArmorActive == other.IsSuperArmorActive &&
                   IsInvisibilityActive == other.IsInvisibilityActive;
        }
    }
}
