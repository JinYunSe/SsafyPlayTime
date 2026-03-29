/*
 * 파일 개요:
 * - ItemMeleeDamageHitbox 스크립트가 들어 있는 파일이다.
 * - Character 계층에서 캐릭터와 아이템 시스템의 결합 지점을 담당한다.
 * - 입력, 손 장착, 근접 판정, 버프 반영 같은 캐릭터 쪽 연결만 여기서 다루고, 실제 상태 전이는 Runtime 계층에서 유지한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 근접 무기 히트 콜라이더 이벤트를 상위 핸들러에 전달한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemMeleeDamageHitbox : MonoBehaviour
    {
        private ItemCharacterMeleeSwingHandler _owner;

        public void SetOwner(ItemCharacterMeleeSwingHandler owner)
        {
            _owner = owner;
        }

        private void OnTriggerEnter(Collider other)
        {
            _owner?.HandleHitCollider(other);
        }

        private void OnTriggerStay(Collider other)
        {
            _owner?.HandleHitCollider(other);
        }
    }
}

