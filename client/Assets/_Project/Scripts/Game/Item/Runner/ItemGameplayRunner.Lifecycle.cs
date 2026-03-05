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
        }

        private void OnEnable()
        {
            if (!ShouldRunInCurrentScene())
            {
                enabled = false;
                return;
            }

            ResolveReferences();
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

            StopFlamethrowerParticle();
            StopAllLoopingSfx();
            RestoreRuntimeAudioOutputPolicy();
            CleanupFallbackAudioListener();
            ReleaseBlackholeOuterLayerFallbackMaterial();
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
                return;
            }

            targetRoot = itemRuntimeHost != null ? itemRuntimeHost.transform : transform;
        }

        // 입력 처리 전 런타임이 준비되어 있는지 확인한다.
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
    }
}
