/*
 * 파일 개요:
 * - ItemFieldSystemBootstrap 스크립트가 들어 있는 파일이다.
 * - Dev/Bootstraps 계층에서 ItemScene 전용 자동 결합과 테스트 시작 구성을 담당한다.
 * - 실게임 필수 흐름이 아니라 개발 편의용 계층이므로, 본 게임 로직 의존성을 늘리지 않도록 주의한다.
 */
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

