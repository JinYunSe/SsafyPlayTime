/*
 * 파일 개요:
 * - ItemGameplayRunner.Lifecycle 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Core 계층에서 ItemGameplayRunner의 생명주기, 입력, 이벤트 연결 같은 중심 흐름을 관리한다.
 * - 테스트용 실행 경로를 정리하는 파일이므로, 실게임 로직과 분리된 검증 허브라는 성격을 유지해야 한다.
 */
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    public sealed partial class ItemGameplayRunner
    {
        private void Awake()
        {
            if (!ShouldRunInCurrentScene())
            {
                enabled = false;
                return;
            }

            ResolveReferences();
            itemRuntimeHost?.SetOwnerTransform(targetRoot);
            EnsureCharacterItemRuntimeBindings();
        }

        private void OnEnable()
        {
            if (!ShouldRunInCurrentScene())
            {
                enabled = false;
                return;
            }

            ResolveReferences();
            EnsureCharacterItemRuntimeBindings();
            BindRuntimeEvents();

            if (itemRuntimeHost != null && !itemRuntimeHost.IsReady && !itemRuntimeHost.Initialize())
            {
                LogStatus($"ItemRuntimeHost init failed: {itemRuntimeHost.LastError}");
            }

            ApplyRuntimeAudioOutputPolicy();
            EnsureAudioListenerGuard();
            LoadPresentationTablesIfNeeded();
        }

        private void OnDisable()
        {
            UnbindRuntimeEvents();

            StopAllBlackholeRoutines();
            StopAllSatelliteStrikeRoutines();

            StopFlamethrowerParticle();
            StopAllLoopingSfx();
            RestoreRuntimeAudioOutputPolicy();
            CleanupFallbackAudioListener();
            ReleaseAllBlackholeTargetOutlines();
            ReleaseBlackholeOuterLayerFallbackMaterial();
            ReleaseBlackholeTargetOutlineMaterial();
        }

        private void ResolveReferences()
        {
            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = GetComponent<ItemRuntimeHost>();
            }

            if (itemRuntimeHost == null)
            {
                itemRuntimeHost = FindObjectOfType<ItemRuntimeHost>();
            }

            if (targetRoot != null)
            {
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                targetRoot = player.transform;
                EnsureCharacterItemRuntimeBindings();
                return;
            }

            targetRoot = itemRuntimeHost != null ? itemRuntimeHost.transform : transform;
            EnsureCharacterItemRuntimeBindings();
        }

        // 입력 처리 전 런타임이 준비되어 있는지 확인한다.
        public void SetRuntimeHost(ItemRuntimeHost runtimeHost)
        {
            if (!ShouldRunInCurrentScene())
            {
                enabled = false;
                return;
            }

            if (runtimeHost == null || itemRuntimeHost == runtimeHost)
            {
                return;
            }

            // 호스트 교체 시 중복 이벤트 구독을 방지한다.
            UnbindRuntimeEvents();
            itemRuntimeHost = runtimeHost;

            // OwnerTransform이 유효하면 항상 갱신한다.
            // 멀티플레이에서 로컬 플레이어가 스폰된 뒤 SetRuntimeHost가 호출될 때
            // targetRoot가 이미 자기 자신(ItemGameplayRunner)으로 설정되어 있어도
            // 올바른 플레이어 트랜스폼으로 덮어쓴다.
            if (itemRuntimeHost.OwnerTransform != null)
            {
                targetRoot = itemRuntimeHost.OwnerTransform;
            }
            else if (targetRoot == null)
            {
                targetRoot = itemRuntimeHost.transform;
            }

            itemRuntimeHost.SetOwnerTransform(targetRoot);
            EnsureCharacterItemRuntimeBindings();
            BindRuntimeEvents();

            if (!itemRuntimeHost.IsReady && !itemRuntimeHost.Initialize())
            {
                LogStatus($"ItemRuntimeHost init failed: {itemRuntimeHost.LastError}");
            }
        }

        private bool EnsureRuntimeReady()
        {
            if (itemRuntimeHost == null)
            {
                LogStatus("ItemRuntimeHost missing.");
                return false;
            }

            if (itemRuntimeHost.IsReady)
            {
                return true;
            }

            if (itemRuntimeHost.Initialize())
            {
                return true;
            }

            LogStatus($"ItemRuntimeHost init failed: {itemRuntimeHost.LastError}");
            return false;
        }

        private void EnsureCharacterItemRuntimeBindings()
        {
            if (targetRoot == null || itemRuntimeHost == null)
            {
                return;
            }

            itemRuntimeHost.SetOwnerTransform(targetRoot);
            EnsurePickupInteractorOnTarget();
            var useInteractor = EnsureUseInteractorOnTarget();
            var heldItemPresenter = EnsureHeldItemPresenterOnTarget();
            EnsureMeleeSwingHandlerOnTarget(heldItemPresenter);
            EnsureBuffApplierOnTarget();
            EnsureHandGrabHandlerRuntimeHosts();

            var interactionService = targetRoot.GetComponent<ItemFieldInteractionService>();
            if (interactionService != null)
            {
                interactionService.SetRuntimeHost(itemRuntimeHost);
                interactionService.SetOwnerTransform(targetRoot);
            }

            if (useInteractor != null)
            {
                useInteractor.SetAimingCamera(Camera.main);
            }
        }

        private void EnsurePickupInteractorOnTarget()
        {
            if (targetRoot == null || itemRuntimeHost == null)
            {
                return;
            }

            var pickupInteractor = targetRoot.GetComponent<ItemFieldPickupInteractor>();
            if (pickupInteractor == null)
            {
                // 한국어: ItemScene에서는 플레이어 손 프리팹과 무관하게 우클릭 습득 경로를 보장한다.
                pickupInteractor = targetRoot.gameObject.AddComponent<ItemFieldPickupInteractor>();
            }

            pickupInteractor.SetRuntimeHost(itemRuntimeHost);
            pickupInteractor.SetInteractorRoot(targetRoot);
            pickupInteractor.SetUseLegacyInput(enableTemporaryRightClickPickup);
            pickupInteractor.SetPickupKey(enableTemporaryRightClickPickup ? temporaryPickupKey : KeyCode.None);
        }

        private ItemCharacterUseInteractor EnsureUseInteractorOnTarget()
        {
            if (targetRoot == null || itemRuntimeHost == null)
            {
                return null;
            }

            var useInteractor = targetRoot.GetComponent<ItemCharacterUseInteractor>();
            if (useInteractor == null)
            {
                // 한국어: 캐릭터에 사용 인터랙터가 없으면 런타임에 보강한다.
                useInteractor = targetRoot.gameObject.AddComponent<ItemCharacterUseInteractor>();
            }

            useInteractor.SetRuntimeHost(itemRuntimeHost);
            useInteractor.SetOwnerRoot(targetRoot);
            useInteractor.SetUseItemKey(KeyCode.Mouse0);
            useInteractor.SetUseLegacyInput(false);
            return useInteractor;
        }

        private ItemCharacterHeldItemPresenter EnsureHeldItemPresenterOnTarget()
        {
            if (targetRoot == null || itemRuntimeHost == null)
            {
                return null;
            }

            var heldItemPresenter = targetRoot.GetComponent<ItemCharacterHeldItemPresenter>();
            if (heldItemPresenter == null)
            {
                // 한국어: 손에 드는 시각화가 빠진 경우 런타임에만 보강한다.
                heldItemPresenter = targetRoot.gameObject.AddComponent<ItemCharacterHeldItemPresenter>();
            }

            heldItemPresenter.SetRuntimeHost(itemRuntimeHost);
            heldItemPresenter.SetCharacterRoot(targetRoot);
            return heldItemPresenter;
        }

        private void EnsureMeleeSwingHandlerOnTarget(ItemCharacterHeldItemPresenter heldItemPresenter)
        {
            if (targetRoot == null || itemRuntimeHost == null)
            {
                return;
            }

            var meleeSwingHandler = targetRoot.GetComponent<ItemCharacterMeleeSwingHandler>();
            if (meleeSwingHandler == null)
            {
                // 한국어: 근접 무기 사용 이벤트를 받기 위해 핸들러를 맞춰 둔다.
                meleeSwingHandler = targetRoot.gameObject.AddComponent<ItemCharacterMeleeSwingHandler>();
            }

            meleeSwingHandler.SetRuntimeHost(itemRuntimeHost);
            meleeSwingHandler.SetHeldItemPresenter(heldItemPresenter);
            meleeSwingHandler.SetOwnerRoot(targetRoot);
        }

        private void EnsureBuffApplierOnTarget()
        {
            if (targetRoot == null || itemRuntimeHost == null)
            {
                return;
            }

            var buffApplier = targetRoot.GetComponent<ItemCharacterBuffApplier>();
            if (buffApplier == null)
            {
                // 한국어: 성장/축소/투명화 같은 버프 이벤트를 같은 호스트로 묶는다.
                buffApplier = targetRoot.gameObject.AddComponent<ItemCharacterBuffApplier>();
            }

            buffApplier.SetRuntimeHost(itemRuntimeHost);
            buffApplier.SetCharacterRoot(targetRoot);
        }

        private void EnsureHandGrabHandlerRuntimeHosts()
        {
            if (targetRoot == null || itemRuntimeHost == null)
            {
                return;
            }

            var handGrabHandlers = targetRoot.GetComponentsInChildren<HandGrabHandler>(true);
            for (var i = 0; i < handGrabHandlers.Length; i++)
            {
                var handGrabHandler = handGrabHandlers[i];
                if (handGrabHandler == null)
                {
                    continue;
                }

                handGrabHandler.SetItemRuntimeHost(itemRuntimeHost);
            }
        }
    }
}

