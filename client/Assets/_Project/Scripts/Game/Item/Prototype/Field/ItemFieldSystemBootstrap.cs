using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 필드 드랍/획득 컴포넌트 연결을 한 곳에서 관리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemFieldSystemBootstrap : MonoBehaviour
    {
        [SerializeField] private bool ensureDropSpawner = true;
        [SerializeField] private bool ensurePickupInteractor = true;

        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private ItemFieldDropSpawner dropSpawner;
        [SerializeField] private ItemFieldPickupInteractor pickupInteractor;

        private void Awake()
        {
            ResolveReferences();
            WireDependencies();
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (ensureDropSpawner && dropSpawner == null)
            {
                dropSpawner = GetComponent<ItemFieldDropSpawner>();
                if (dropSpawner == null)
                {
                    dropSpawner = gameObject.AddComponent<ItemFieldDropSpawner>();
                }
            }

            if (ensurePickupInteractor && pickupInteractor == null)
            {
                pickupInteractor = GetComponent<ItemFieldPickupInteractor>();
                if (pickupInteractor == null)
                {
                    pickupInteractor = gameObject.AddComponent<ItemFieldPickupInteractor>();
                }
            }
        }

        private void WireDependencies()
        {
            if (itemRuntimeHost == null)
            {
                return;
            }

            if (dropSpawner != null)
            {
                dropSpawner.SetRuntimeHost(itemRuntimeHost);
            }

            if (pickupInteractor != null)
            {
                pickupInteractor.SetRuntimeHost(itemRuntimeHost);
            }
        }
    }
}
