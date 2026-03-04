using System;
using System.Collections.Generic;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// 캐릭터 프리팹/씬 오브젝트에 부착해서 아이템 런타임을 연동하는 진입점이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ItemRuntimeHost : MonoBehaviour, IItemRuntimeBridge
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
        public event Action<string, Vector3> VfxRequested;
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

        public bool Initialize()
        {
            _loadWarnings.Clear();
            _lastError = string.Empty;

            var options = new ItemCatalogLoadOptions(
                itemMasterPath,
                soundAssetPath,
                vfxAssetPath,
                presentationPath);

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

        void IItemRuntimeBridge.OnHeldItemChanged(string heldItemId)
        {
            HeldItemChanged?.Invoke(heldItemId);
        }

        void IItemRuntimeBridge.OnItemDropped(string itemId, ItemDropReason reason)
        {
            ItemDropped?.Invoke(itemId, reason);
        }

        void IItemRuntimeBridge.OnItemConsumed(string itemId)
        {
            ItemConsumed?.Invoke(itemId);
        }

        void IItemRuntimeBridge.OnPlaySfx(string sfxId, Vector3 worldPosition, bool loop)
        {
            SfxRequested?.Invoke(sfxId, worldPosition, loop);
        }

        void IItemRuntimeBridge.OnSpawnVfx(string vfxId, Vector3 worldPosition)
        {
            VfxRequested?.Invoke(vfxId, worldPosition);
        }

        void IItemRuntimeBridge.OnBlackholeRequested(in BlackholeSkillRequest request)
        {
            BlackholeRequested?.Invoke(request);
        }

        void IItemRuntimeBridge.OnSatelliteStrikeRequested(in SatelliteStrikeRequest request)
        {
            SatelliteStrikeRequested?.Invoke(request);
        }

        void IItemRuntimeBridge.OnFlamethrowerStart(string itemId, float endAtSec)
        {
            FlamethrowerStarted?.Invoke(itemId, endAtSec);
        }

        void IItemRuntimeBridge.OnFlamethrowerTick(in FlamethrowerTickRequest request)
        {
            FlamethrowerTicked?.Invoke(request);
        }

        void IItemRuntimeBridge.OnFlamethrowerStop(string itemId)
        {
            FlamethrowerStopped?.Invoke(itemId);
        }

        void IItemRuntimeBridge.OnBuffStateChanged(ItemBuffMask activeBuffMask, in ItemBuffRuntimeState buffState)
        {
            BuffStateChanged?.Invoke(activeBuffMask, buffState);
        }

        private bool EnsureReady(out string reason)
        {
            if (_controller != null)
            {
                reason = string.Empty;
                return true;
            }

            if (Initialize())
            {
                reason = string.Empty;
                return true;
            }

            reason = string.IsNullOrWhiteSpace(_lastError) ? "ItemRuntimeHost is not initialized." : _lastError;
            return false;
        }

        private Vector3 ResolveOwnerPosition()
        {
            return ownerTransform != null ? ownerTransform.position : transform.position;
        }

        private Vector3 ResolveOwnerForward()
        {
            var forward = ownerTransform != null ? ownerTransform.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private void DebugLog(string message)
        {
            if (!enableDebugLog)
            {
                return;
            }

            Debug.Log($"[ItemRuntimeHost] {message}", this);
        }
    }
}
