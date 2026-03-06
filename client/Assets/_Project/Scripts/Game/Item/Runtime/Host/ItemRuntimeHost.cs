using System;
using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 씬 오브젝트에 부착되어 ItemRuntimeController를 구동하는 런타임 진입점.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ItemRuntimeHost : MonoBehaviour, IItemRuntimeBridge
    {
        [Header("데이터 경로")]
        [SerializeField] private string itemMasterPath = ItemCatalogLoader.DefaultItemMasterPath;
        [SerializeField] private string soundAssetPath = ItemCatalogLoader.DefaultSoundAssetPath;
        [SerializeField] private string vfxAssetPath = ItemCatalogLoader.DefaultVfxAssetPath;
        [SerializeField] private string presentationPath = ItemCatalogLoader.DefaultPresentationPath;

        [Header("실행 옵션")]
        [SerializeField] private bool autoInitializeOnAwake = true;
        [SerializeField] private bool autoTickInUpdate = true;
        [SerializeField] private bool enableDebugLog;
        [SerializeField] private Transform ownerTransform;

        private readonly List<string> _loadWarnings = new();
        private ItemRuntimeController _controller;
        private string _lastError = string.Empty;

        public event Action<string> HeldItemChanged;
        public event Action<string, ItemDropReason> ItemDropped;
        public event Action<string> ItemConsumed;
        public event Action<string, Vector3, bool> SfxRequested;
        public event Action<BlackholeSkillRequest> BlackholeRequested;
        public event Action<SatelliteStrikeRequest> SatelliteStrikeRequested;
        public event Action<string, float> FlamethrowerStarted;
        public event Action<FlamethrowerTickRequest> FlamethrowerTicked;
        public event Action<string> FlamethrowerStopped;
        public event Action<ItemBuffMask, ItemBuffRuntimeState> BuffStateChanged;

        public bool IsReady => _controller != null;
        public string HeldItemId => _controller?.HeldItemId ?? string.Empty;
        public bool IsFlamethrowerActive => _controller != null && _controller.IsFlamethrowerActive;
        public IReadOnlyList<string> LoadWarnings => _loadWarnings;
        public string LastError => _lastError;
        public Transform OwnerTransform => ownerTransform;
        public float Now => Time.time;
        public ItemCatalogLoadOptions CatalogLoadOptions =>
            new ItemCatalogLoadOptions(itemMasterPath, soundAssetPath, vfxAssetPath, presentationPath);

        private void Awake()
        {
            if (ownerTransform == null)
            {
                ownerTransform = transform;
            }

            if (autoInitializeOnAwake)
            {
                Initialize();
            }
        }

        private void Update()
        {
            if (!autoTickInUpdate || _controller == null)
            {
                return;
            }

            TickRuntime();
        }

        /// <summary>
        /// CSV 테이블을 로드하고 런타임 컨트롤러를 생성한다.
        /// </summary>
        public bool Initialize()
        {
            _loadWarnings.Clear();
            _lastError = string.Empty;

            var options = CatalogLoadOptions;

            if (!ItemCatalogLoader.TryLoadFromDisk(options, out var catalog, out var warnings, out var error))
            {
                _controller = null;
                _lastError = error ?? "Unknown load error.";
                DebugLog($"Initialize failed: {_lastError}");
                return false;
            }

            if (warnings != null)
            {
                _loadWarnings.AddRange(warnings);
            }

            _controller = new ItemRuntimeController(catalog, this);
            DebugLog($"Initialize success (warnings: {_loadWarnings.Count}).");
            return true;
        }

        public bool TryPickup(string itemId, out string reason)
        {
            if (!EnsureReady(out reason))
            {
                return false;
            }

            return _controller.TryPickup(itemId, out reason);
        }

        public bool TryUseHeldItem(Vector3 targetPosition, out string reason)
        {
            if (!EnsureReady(out reason))
            {
                return false;
            }

            var ownerPos = ResolveOwnerPosition();
            var ownerForward = ResolveOwnerForward();
            return _controller.TryUseHeldItem(ownerPos, ownerForward, targetPosition, out reason);
        }

        public bool TryDropHeldItem(out string reason)
        {
            if (!EnsureReady(out reason))
            {
                return false;
            }

            return _controller.TryDropHeldItem(ItemDropReason.Manual, out reason);
        }

        public void TickRuntime()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.Tick(ResolveOwnerPosition(), ResolveOwnerForward());
        }

        public void NotifyStunned()
        {
            _controller?.NotifyStunned();
        }

        public void ResetRuntimeState()
        {
            _controller?.ResetRuntimeState();
        }

        public void SetOwnerTransform(Transform owner)
        {
            ownerTransform = owner != null ? owner : transform;
        }
    }
}
