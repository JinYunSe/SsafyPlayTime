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
