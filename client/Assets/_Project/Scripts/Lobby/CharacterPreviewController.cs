using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    public sealed class CharacterPreviewController : MonoBehaviour
    {
        private const int PlayerSlotCount = 4;
        private const int CharacterOptionCount = 4;

        [Header("Character Templates")]
        [FormerlySerializedAs("stattyCharacterRoot")]
        [SerializeField] private GameObject ssatyCharacterRoot;
        [SerializeField] private GameObject alGCharacterRoot;
        [SerializeField] private GameObject fitCharacterRoot;
        [SerializeField] private GameObject wiseCharacterRoot;

        [Header("Selection UI")]
        [SerializeField] private GameObject characterSelectionPanel;
        [FormerlySerializedAs("selectStattyCharacterButton")]
        [SerializeField] private Button selectSsatyCharacterButton;
        [SerializeField] private Button selectAlGCharacterButton;
        [SerializeField] private Button selectFitCharacterButton;
        [SerializeField] private Button selectWiseCharacterButton;

        [Header("Name Slot Layout")]
        [SerializeField] private bool lockPlayerSlotLayoutToViewport = true;
        [SerializeField] private float playerSlotViewportY = 0.36f;
        [SerializeField] private float playerSlotVerticalPixelOffset = 0f;
        [SerializeField] private float playerSlotWidthRatio = 0.22f;
        [SerializeField] private float playerSlotMinWidth = 180f;
        [SerializeField] private float playerSlotMaxWidth = 420f;
        [SerializeField] private float playerSlotHeight = 40f;
        [SerializeField] private float playerSlotExtraViewportY = 0f;
        [SerializeField] private float playerSlotSizeMultiplier = 1.35f;
        [SerializeField] private bool useQuarterWidthNameSlots = true;
        [SerializeField] private float playerSlotQuarterHorizontalMargin = 12f;
        [SerializeField] private float playerSlotQuarterWidthScale = 0.95f;
        [SerializeField] private float nicknameFontSizeMin = 32f;
        [SerializeField] private float nicknameFontSizeMax = 64f;

        [Header("Character Placement")]
        [SerializeField] private float characterVerticalOffset = 20f;
        [SerializeField] private float characterExtraVerticalOffset = 20f;
        [SerializeField] private float algCharacterVerticalOffsetAdjustment = -10f;
        [SerializeField] private float fitCharacterVerticalOffsetAdjustment = 32f;
        [SerializeField] private float fitCharacterWorldYAdjustment = -0.73f;
        [SerializeField] private float wiseCharacterVerticalOffsetAdjustment = 10f;
        [SerializeField] private Transform characterRuntimeRoot;
        [SerializeField] private Camera characterPlacementCamera;
        [SerializeField] private float characterWorldDepth = 8f;
        [SerializeField] private Vector3 characterWorldOffset = Vector3.zero;
        [SerializeField] private float characterScreenPaddingPixels = 24f;
        [SerializeField] private bool keepCharacterScreenSize = true;
        [SerializeField] private float characterTargetScreenHeightPixels = 170f;
        [SerializeField] private float characterScreenHeightMultiplier = 2.5f;
        [SerializeField] private bool useFixedCharacterScale = true;
        [SerializeField] private Vector3 fixedCharacterScale = new Vector3(5f, 5f, 5f);
        [SerializeField] private Vector3[] slotPositionOffsets =
        {
            Vector3.zero,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero
        };
        [SerializeField] private Vector3[] slotRotationOverrides =
        {
            Vector3.zero,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero
        };
        [SerializeField] private Vector3[] slotScaleOverrides =
        {
            new Vector3(5f, 5f, 5f),
            new Vector3(5f, 5f, 5f),
            new Vector3(5f, 5f, 5f),
            new Vector3(5f, 5f, 5f)
        };

        private TMP_Text[] _nameSlots;

        private readonly Transform[,] _slotCharacterRoots = new Transform[PlayerSlotCount, CharacterOptionCount];
        private readonly int[] _selectedCharacterIndexBySlot = { -1, -1, -1, -1 };
        private readonly int[] _playerIdBySlot = { -1, -1, -1, -1 };
        private readonly Dictionary<Transform, Vector3> _characterBaseLocalScales = new();
        private readonly Dictionary<Transform, Quaternion> _characterBaseLocalRotations = new();
        private readonly Dictionary<Transform, Quaternion> _characterVisualChildBaseLocalRotations = new();
        private readonly Dictionary<Transform, float> _characterBaseBoundsHeights = new();
        private readonly Dictionary<Transform, int> _characterOptionIndexByTransform = new();
        private readonly Dictionary<Transform, float> _characterPrePlacedWorldY = new();
        private bool _characterSlotsInitialized;

        /// <summary>Fired when the local player clicks a character select button (passes character index 0-3).</summary>
        public event Action<int> CharacterSelected;

        // ── Public API ──────────────────────────────────────────────────────────────

        public void Initialize(TMP_Text[] nameSlots)
        {
            _nameSlots = nameSlots;
            BindButtonEvents();
            InitializeCharacterSlotsIfNeeded();
        }

        public void UpdateFrame()
        {
            LayoutNameSlotsForCurrentViewport();
            AlignAllCharacterCandidatesToNameSlots();
        }

        /// <summary>Updates the visual state for one player slot.</summary>
        public void UpdateSlot(int slotIndex, int playerId, int characterIndex, bool hasPlayer)
        {
            if (slotIndex < 0 || slotIndex >= PlayerSlotCount)
            {
                return;
            }

            _playerIdBySlot[slotIndex] = hasPlayer ? playerId : -1;
            _selectedCharacterIndexBySlot[slotIndex] = hasPlayer ? SanitizeCharacterIndexOrNone(characterIndex) : -1;
            ApplySelectedCharacterForSlot(slotIndex, hasPlayer);
        }

        public void ClearAllSlots()
        {
            for (var i = 0; i < PlayerSlotCount; i++)
            {
                _playerIdBySlot[i] = -1;
                _selectedCharacterIndexBySlot[i] = -1;
                ApplySelectedCharacterForSlot(i, false);
            }
        }

        /// <summary>Updates the visual slot for the given player when their character selection changes.</summary>
        public void ApplySelectionForPlayer(int playerId, int characterIndex)
        {
            for (var slot = 0; slot < _playerIdBySlot.Length; slot++)
            {
                if (_playerIdBySlot[slot] != playerId)
                {
                    continue;
                }

                _selectedCharacterIndexBySlot[slot] = SanitizeCharacterIndexOrNone(characterIndex);
                ApplySelectedCharacterForSlot(slot, true);
                break;
            }
        }

        /// <summary>Refreshes the character selection button interactable state.</summary>
        /// <param name="takenByOthers">다른 플레이어가 이미 선택한 캐릭터 인덱스 집합. null이면 중복 체크 없음.</param>
        public void RefreshSelectionUiState(bool canSelect, int localPlayerSelectedCharacterIndex,
            IReadOnlyCollection<int> takenByOthers = null)
        {
            if (characterSelectionPanel == null)
            {
                return;
            }

            TrySetGameObjectActive(characterSelectionPanel, canSelect);
            var selected = SanitizeCharacterIndexOrNone(localPlayerSelectedCharacterIndex);

            if (selectSsatyCharacterButton != null)
            {
                TrySetButtonInteractable(selectSsatyCharacterButton,
                    canSelect && selected != 0 && (takenByOthers == null || !takenByOthers.Contains(0)));
            }

            if (selectAlGCharacterButton != null)
            {
                TrySetButtonInteractable(selectAlGCharacterButton,
                    canSelect && selected != 1 && (takenByOthers == null || !takenByOthers.Contains(1)));
            }

            if (selectFitCharacterButton != null)
            {
                TrySetButtonInteractable(selectFitCharacterButton,
                    canSelect && selected != 2 && (takenByOthers == null || !takenByOthers.Contains(2)));
            }

            if (selectWiseCharacterButton != null)
            {
                TrySetButtonInteractable(selectWiseCharacterButton,
                    canSelect && selected != 3 && (takenByOthers == null || !takenByOthers.Contains(3)));
            }
        }

        // ── Character select button callbacks ────────────────────────────────────

        public void OnSelectSsatyCharacter() => CharacterSelected?.Invoke(0);
        public void OnSelectAlGCharacter() => CharacterSelected?.Invoke(1);
        public void OnSelectFitCharacter() => CharacterSelected?.Invoke(2);
        public void OnSelectWiseCharacter() => CharacterSelected?.Invoke(3);

        // ── Private implementation ────────────────────────────────────────────────

        private void BindButtonEvents()
        {
            if (selectSsatyCharacterButton != null)
            {
                selectSsatyCharacterButton.onClick.AddListener(OnSelectSsatyCharacter);
            }

            if (selectAlGCharacterButton != null)
            {
                selectAlGCharacterButton.onClick.AddListener(OnSelectAlGCharacter);
            }

            if (selectFitCharacterButton != null)
            {
                selectFitCharacterButton.onClick.AddListener(OnSelectFitCharacter);
            }

            if (selectWiseCharacterButton != null)
            {
                selectWiseCharacterButton.onClick.AddListener(OnSelectWiseCharacter);
            }
        }

        private void InitializeCharacterSlotsIfNeeded()
        {
            if (_characterSlotsInitialized)
            {
                return;
            }

            var templates = new[] { ssatyCharacterRoot, alGCharacterRoot, fitCharacterRoot, wiseCharacterRoot };
            var nameSlots = _nameSlots;
            if (nameSlots == null || templates.Any(t => t == null) || nameSlots.Any(t => t == null))
            {
                return;
            }

            var runtimeRoot = ResolveCharacterRuntimeRoot();
            for (var slot = 0; slot < PlayerSlotCount; slot++)
            {
                for (var option = 0; option < CharacterOptionCount; option++)
                {
                    var template = templates[option];
                    var prePlacedName = $"{template.name}_Slot{slot + 1}";
                    var prePlaced = runtimeRoot.Find(prePlacedName);

                    Transform cloneTransform;
                    if (prePlaced != null)
                    {
                        cloneTransform = prePlaced;
                        ConfigureCharacterPreviewClone(cloneTransform.gameObject);
                        _characterPrePlacedWorldY[cloneTransform] = cloneTransform.position.y;
                    }
                    else
                    {
                        var clone = Instantiate(template, runtimeRoot, true);
                        clone.name = prePlacedName;
                        ConfigureCharacterPreviewClone(clone);
                        cloneTransform = clone.transform;
                    }

                    cloneTransform.gameObject.SetActive(false);
                    _slotCharacterRoots[slot, option] = cloneTransform;
                    _characterBaseLocalScales[cloneTransform] = cloneTransform.localScale;
                    _characterBaseLocalRotations[cloneTransform] = cloneTransform.localRotation;
                    CacheCharacterVisualChildRotations(cloneTransform);
                    _characterBaseBoundsHeights[cloneTransform] = CalculateCombinedBoundsHeight(cloneTransform);
                    _characterOptionIndexByTransform[cloneTransform] = option;
                }
            }

            for (var i = 0; i < templates.Length; i++)
            {
                if (templates[i] != null)
                {
                    templates[i].SetActive(false);
                }
            }

            _characterSlotsInitialized = true;
            AlignAllCharacterCandidatesToNameSlots();
        }

        private void AlignAllCharacterCandidatesToNameSlots()
        {
            InitializeCharacterSlotsIfNeeded();
            if (!_characterSlotsInitialized)
            {
                return;
            }

            var nameSlots = _nameSlots;
            if (nameSlots == null)
            {
                return;
            }

            for (var slot = 0; slot < PlayerSlotCount; slot++)
            {
                var nameSlot = nameSlots[slot];
                for (var option = 0; option < CharacterOptionCount; option++)
                {
                    AlignCharacterSlot(_slotCharacterRoots[slot, option], nameSlot, slot);
                }
            }
        }

        private void AlignCharacterSlot(Transform characterSlot, TMP_Text nameSlot, int slotIndex = -1)
        {
            if (characterSlot == null || nameSlot == null || nameSlot.transform is not RectTransform nameRect)
            {
                return;
            }

            if (characterSlot is RectTransform charRect)
            {
                if (characterSlot.parent != nameRect.parent)
                {
                    characterSlot.SetParent(nameRect.parent, false);
                }

                var characterSpecificOffset = GetCharacterSpecificVerticalOffset(characterSlot);
                var slotPosOffset = GetSlotPositionOffset(slotIndex);
                charRect.anchorMin = nameRect.anchorMin;
                charRect.anchorMax = nameRect.anchorMax;
                charRect.pivot = nameRect.pivot;
                charRect.anchoredPosition = nameRect.anchoredPosition + new Vector2(
                    slotPosOffset.x,
                    characterVerticalOffset + characterExtraVerticalOffset + characterSpecificOffset + slotPosOffset.y);
                ApplySlotRotationToVisuals(characterSlot, slotIndex);
                if (useFixedCharacterScale)
                {
                    charRect.localScale = GetSlotScale(slotIndex);
                }
                return;
            }

            var worldCam = ResolveCharacterPlacementCamera();
            if (worldCam == null)
            {
                return;
            }

            var uiCam = ResolveUiCamera();
            var uiWorldPoint = GetNameAnchorWorldPoint(nameSlot, nameRect);
            var screenPoint = RectTransformUtility.WorldToScreenPoint(uiCam, uiWorldPoint);
            var worldSlotPosOffset = GetSlotPositionOffset(slotIndex);
            screenPoint.x += worldSlotPosOffset.x;
            screenPoint.y += characterVerticalOffset + characterExtraVerticalOffset
                             + GetCharacterSpecificVerticalOffset(characterSlot)
                             + worldSlotPosOffset.y;
            var padding = Mathf.Max(0f, characterScreenPaddingPixels);
            screenPoint.x = Mathf.Clamp(screenPoint.x, padding, Screen.width - padding);
            screenPoint.y = Mathf.Clamp(screenPoint.y, padding, Screen.height - padding);

            var depth = characterWorldDepth;
            if (depth <= 0f)
            {
                depth = Vector3.Dot(characterSlot.position - worldCam.transform.position,
                    worldCam.transform.forward);
                if (depth <= 0f)
                {
                    depth = 8f;
                }
            }

            var targetWorld = worldCam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
            var finalPos = targetWorld + characterWorldOffset + new Vector3(0f, 0f, worldSlotPosOffset.z);
            if (_characterPrePlacedWorldY.TryGetValue(characterSlot, out var lockedY))
            {
                finalPos.y = lockedY + GetCharacterPrePlacedWorldYAdjustment(characterSlot);
            }

            characterSlot.position = finalPos;
            ApplySlotRotationToVisuals(characterSlot, slotIndex);

            if (useFixedCharacterScale)
            {
                characterSlot.localScale = GetSlotScale(slotIndex);
            }
            else if (keepCharacterScreenSize)
            {
                ApplyCharacterScreenHeightScale(characterSlot, worldCam);
            }
        }

        private void LayoutNameSlotsForCurrentViewport()
        {
            if (!lockPlayerSlotLayoutToViewport || _nameSlots == null)
            {
                return;
            }

            var canvasRect = GetComponentInParent<Canvas>()?.transform as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            var slotX = new[] { 0.125f, 0.375f, 0.625f, 0.875f };
            var y = Mathf.Clamp01(playerSlotViewportY + playerSlotExtraViewportY);
            var sizeMultiplier = Mathf.Max(0.5f, playerSlotSizeMultiplier);
            var width = Mathf.Clamp(
                canvasRect.rect.width * Mathf.Max(0.05f, playerSlotWidthRatio) * sizeMultiplier,
                Mathf.Max(1f, playerSlotMinWidth),
                Mathf.Max(playerSlotMinWidth, playerSlotMaxWidth * sizeMultiplier));

            if (useQuarterWidthNameSlots)
            {
                var quarterWidth = canvasRect.rect.width / PlayerSlotCount;
                var margin = Mathf.Max(0f, playerSlotQuarterHorizontalMargin);
                var quarterScaledWidth = (quarterWidth - margin * 2f) * Mathf.Max(0.5f, playerSlotQuarterWidthScale);
                width = Mathf.Max(1f, quarterScaledWidth);
            }

            var height = Mathf.Max(1f, playerSlotHeight * sizeMultiplier);

            for (var i = 0; i < _nameSlots.Length; i++)
            {
                var text = _nameSlots[i];
                if (text == null || text.transform is not RectTransform rect)
                {
                    continue;
                }

                rect.anchorMin = new Vector2(slotX[i], y);
                rect.anchorMax = new Vector2(slotX[i], y);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, playerSlotVerticalPixelOffset);
                rect.sizeDelta = new Vector2(width, height);

                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.enableAutoSizing = true;
                text.fontSizeMin = Mathf.Max(8f, nicknameFontSizeMin);
                text.fontSizeMax = Mathf.Max(text.fontSizeMin, nicknameFontSizeMax);
            }
        }

        private float GetCharacterSpecificVerticalOffset(Transform characterSlot)
        {
            if (characterSlot == null)
            {
                return 0f;
            }

            if (!_characterOptionIndexByTransform.TryGetValue(characterSlot, out var option))
            {
                return 0f;
            }

            return option switch
            {
                1 => algCharacterVerticalOffsetAdjustment,
                2 => fitCharacterVerticalOffsetAdjustment,
                3 => wiseCharacterVerticalOffsetAdjustment,
                _ => 0f
            };
        }

        private Vector3 GetSlotPositionOffset(int slotIndex)
        {
            if (slotPositionOffsets != null && slotIndex >= 0 && slotIndex < slotPositionOffsets.Length)
            {
                return slotPositionOffsets[slotIndex];
            }
            return Vector3.zero;
        }

        private Vector3 GetSlotRotation(int slotIndex)
        {
            if (slotRotationOverrides != null && slotIndex >= 0 && slotIndex < slotRotationOverrides.Length)
            {
                return slotRotationOverrides[slotIndex];
            }
            return Vector3.zero;
        }

        private Vector3 GetSlotScale(int slotIndex)
        {
            if (slotScaleOverrides != null && slotIndex >= 0 && slotIndex < slotScaleOverrides.Length)
            {
                return slotScaleOverrides[slotIndex];
            }
            return fixedCharacterScale;
        }

        private void CacheCharacterVisualChildRotations(Transform characterSlot)
        {
            if (characterSlot == null)
            {
                return;
            }

            for (var i = 0; i < characterSlot.childCount; i++)
            {
                var child = characterSlot.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                _characterVisualChildBaseLocalRotations[child] = child.localRotation;
            }
        }

        private void ApplySlotRotationToVisuals(Transform characterSlot, int slotIndex)
        {
            if (characterSlot == null)
            {
                return;
            }

            var slotRotation = Quaternion.Euler(GetSlotRotation(slotIndex));
            if (_characterBaseLocalRotations.TryGetValue(characterSlot, out var rootBaseRotation))
            {
                characterSlot.localRotation = rootBaseRotation;
            }

            if (characterSlot.childCount <= 0)
            {
                characterSlot.localRotation *= slotRotation;
                return;
            }

            for (var i = 0; i < characterSlot.childCount; i++)
            {
                var child = characterSlot.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (!_characterVisualChildBaseLocalRotations.TryGetValue(child, out var childBaseRotation))
                {
                    childBaseRotation = child.localRotation;
                    _characterVisualChildBaseLocalRotations[child] = childBaseRotation;
                }

                child.localRotation = childBaseRotation * slotRotation;
            }
        }

        private float GetCharacterPrePlacedWorldYAdjustment(Transform characterSlot)
        {
            if (_characterOptionIndexByTransform.TryGetValue(characterSlot, out var option) && option == 2)
            {
                return fitCharacterWorldYAdjustment;
            }

            return 0f;
        }

        private static void ConfigureCharacterPreviewClone(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            var rigidbodies = clone.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < rigidbodies.Length; i++)
            {
                var rb = rigidbodies[i];
                if (rb == null) continue;
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            var colliders = clone.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }

            var joints = clone.GetComponentsInChildren<ConfigurableJoint>(true);
            for (var i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null) Destroy(joints[i]);
            }

            var syncObjects = clone.GetComponentsInChildren<SyncPhysicsObject>(true);
            for (var i = 0; i < syncObjects.Length; i++)
            {
                if (syncObjects[i] != null) syncObjects[i].enabled = false;
            }
        }

        private void ApplySelectedCharacterForSlot(int slotIndex, bool slotHasPlayer)
        {
            InitializeCharacterSlotsIfNeeded();
            if (!_characterSlotsInitialized || slotIndex < 0 || slotIndex >= PlayerSlotCount)
            {
                return;
            }

            var selectedIndex = _selectedCharacterIndexBySlot[slotIndex];
            var shouldShow = slotHasPlayer;
            for (var option = 0; option < CharacterOptionCount; option++)
            {
                var rect = _slotCharacterRoots[slotIndex, option];
                if (rect == null) continue;
                rect.gameObject.SetActive(shouldShow && selectedIndex >= 0 && option == selectedIndex);
            }
        }

        private void ApplyCharacterScreenHeightScale(Transform characterRoot, Camera worldCam)
        {
            if (characterRoot == null || worldCam == null)
            {
                return;
            }

            if (!_characterBaseLocalScales.TryGetValue(characterRoot, out var baseLocalScale))
            {
                baseLocalScale = characterRoot.localScale;
                _characterBaseLocalScales[characterRoot] = baseLocalScale;
            }

            if (!_characterBaseBoundsHeights.TryGetValue(characterRoot, out var baseBoundsHeight) ||
                baseBoundsHeight <= 0.001f)
            {
                baseBoundsHeight = Mathf.Max(0.001f, CalculateCombinedBoundsHeight(characterRoot));
                _characterBaseBoundsHeights[characterRoot] = baseBoundsHeight;
            }

            var origin = characterRoot.position;
            var pxA = worldCam.WorldToScreenPoint(origin);
            var pxB = worldCam.WorldToScreenPoint(origin + worldCam.transform.up);
            var pixelsPerWorldUnit = Mathf.Abs(pxB.y - pxA.y);
            if (pixelsPerWorldUnit <= 0.0001f)
            {
                return;
            }

            var effectiveTargetScreenHeight = Mathf.Max(1f, characterTargetScreenHeightPixels) *
                                              Mathf.Max(0.5f, characterScreenHeightMultiplier);
            var targetWorldHeight = effectiveTargetScreenHeight / pixelsPerWorldUnit;
            var scaleRatio = targetWorldHeight / baseBoundsHeight;
            characterRoot.localScale = baseLocalScale * scaleRatio;
        }

        private static float CalculateCombinedBoundsHeight(Transform root)
        {
            if (root == null) return 1f;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return 1f;

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null) bounds.Encapsulate(renderers[i].bounds);
            }

            return Mathf.Max(0.001f, bounds.size.y);
        }

        private static Vector3 GetNameAnchorWorldPoint(TMP_Text nameSlot, RectTransform nameRect)
        {
            if (nameSlot == null || nameRect == null) return Vector3.zero;

            nameSlot.ForceMeshUpdate();
            var bounds = nameSlot.textBounds;
            if (bounds.size.sqrMagnitude > 0.0001f)
            {
                return nameRect.TransformPoint(bounds.center);
            }

            return nameRect.TransformPoint(nameRect.rect.center);
        }

        private Transform ResolveCharacterRuntimeRoot()
        {
            if (characterRuntimeRoot != null) return characterRuntimeRoot;

            var root = GameObject.Find("LobbyCharacterRuntimeRoot");
            if (root == null)
            {
                root = new GameObject("LobbyCharacterRuntimeRoot");
            }

            characterRuntimeRoot = root.transform;
            return characterRuntimeRoot;
        }

        private Camera ResolveCharacterPlacementCamera()
        {
            if (characterPlacementCamera != null) return characterPlacementCamera;
            if (Camera.main != null) return Camera.main;
            return FindObjectOfType<Camera>();
        }

        private Camera ResolveUiCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera != null ? canvas.worldCamera : ResolveCharacterPlacementCamera();
        }

        private static int SanitizeCharacterIndexOrNone(int rawIndex)
        {
            return rawIndex >= 0 && rawIndex < CharacterOptionCount ? rawIndex : -1;
        }

        private static void TrySetGameObjectActive(GameObject target, bool active)
        {
            if (target == null) return;
            try { target.SetActive(active); }
            catch (MissingReferenceException) { }
        }

        private static void TrySetButtonInteractable(Button button, bool interactable)
        {
            if (button == null) return;
            try { button.interactable = interactable; }
            catch (MissingReferenceException) { }
        }
    }
}
