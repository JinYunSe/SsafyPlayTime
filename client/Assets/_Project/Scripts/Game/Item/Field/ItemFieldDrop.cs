using System;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 필드에 떨어진 아이템 오브젝트의 식별 정보와 획득 상태를 관리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldDrop : MonoBehaviour
    {
        [SerializeField] private string itemId = string.Empty;
        [SerializeField] private bool destroyOnPickup = true;
        [SerializeField] private bool disableCollidersOnPickup = true;

        private bool _pickedUp;

        public string ItemId => itemId;
        public bool IsPickedUp => _pickedUp;
        public event Action<ItemFieldDrop> PickedUp;

        public void SetItemId(string value)
        {
            itemId = value ?? string.Empty;
        }

        public bool CanBePickedUp()
        {
            return !_pickedUp && !string.IsNullOrWhiteSpace(itemId);
        }

        public void MarkPickedUp()
        {
            if (_pickedUp)
            {
                return;
            }

            _pickedUp = true;
            PickedUp?.Invoke(this);
            if (disableCollidersOnPickup)
            {
                var colliders = GetComponentsInChildren<Collider>(true);
                for (var i = 0; i < colliders.Length; i++)
                {
                    colliders[i].enabled = false;
                }
            }

            if (destroyOnPickup)
            {
                Destroy(gameObject);
                return;
            }

            gameObject.SetActive(false);
        }
    }
}
