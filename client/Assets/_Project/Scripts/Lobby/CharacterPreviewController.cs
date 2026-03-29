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
        private readonly int[] _selectedCharacterIndexBySlot = { -1, -1, -1, -1 };
        private readonly int[] _playerIdBySlot = { -1, -1, -1, -1 };

        /// <summary>Fired when the local player clicks a character select button (passes character index 0-3).</summary>
        public event Action<int> CharacterSelected;

        // ── Public API ──────────────────────────────────────────────────────────────

        public void Initialize(TMP_Text[] nameSlots)
        {
            _nameSlots = nameSlots;
            BindButtonEvents();
        }

        public void UpdateFrame()
        {
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

        public void ResetSlots()
        {
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
            // sceneRoomSlots 방식으로 전환 후 CharacterPreviewController는 슬롯 추적만 담당
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
