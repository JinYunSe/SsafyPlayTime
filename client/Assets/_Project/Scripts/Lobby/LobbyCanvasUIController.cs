using SSAFYPlayTime.Lobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    public sealed class LobbyCanvasUIController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject nicknamePanel;
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private GameObject roomPanel;
        [SerializeField] private GameObject createRoomModal;
        [SerializeField] private GameObject passwordModal;

        [Header("Nickname")]
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button nicknameConfirmButton;

        [Header("Lobby")]
        [SerializeField] private TMP_Text lobbyHeaderText;
        [SerializeField] private TMP_Text lobbyStatusText;
        [SerializeField] private Button createRoomOpenButton;
        [SerializeField] private Button refreshRoomsButton;
        [SerializeField] private Transform roomListContent;
        [SerializeField] private GameObject roomItemTemplate;

        [Header("Create Room Modal")]
        [SerializeField] private TMP_InputField createRoomNameInput;
        [SerializeField] private Toggle createPrivateToggle;
        [SerializeField] private TMP_InputField createPasswordInput;
        [SerializeField] private TMP_Text createValidationText;
        [SerializeField] private Button createConfirmButton;
        [SerializeField] private Button createCancelButton;

        [Header("Password Modal")]
        [SerializeField] private TMP_InputField joinPasswordInput;
        [SerializeField] private TMP_Text passwordValidationText;
        [SerializeField] private Button passwordJoinButton;
        [SerializeField] private Button passwordCancelButton;

        [Header("Room View")]
        [SerializeField] private TMP_Text roomTitleText;
        [SerializeField] private TMP_Text roomMembersText;
        [SerializeField] private Button leaveRoomButton;

        private readonly InMemoryLobbyService _lobbyService = new();

        private string _nickname = string.Empty;
        private LobbyRoom _currentRoom;
        private LobbyRoom _pendingPrivateRoom;

        private void Start()
        {
            BindEvents();
            ShowNicknamePanel();
        }

        private void BindEvents()
        {
            nicknameConfirmButton.onClick.AddListener(OnNicknameConfirmed);
            createRoomOpenButton.onClick.AddListener(OpenCreateRoomModal);
            refreshRoomsButton.onClick.AddListener(RefreshRoomList);

            createPrivateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);
            createConfirmButton.onClick.AddListener(OnCreateRoomConfirmed);
            createCancelButton.onClick.AddListener(() => createRoomModal.SetActive(false));

            passwordJoinButton.onClick.AddListener(OnPasswordConfirmed);
            passwordCancelButton.onClick.AddListener(() => passwordModal.SetActive(false));

            leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        }

        private void OnNicknameConfirmed()
        {
            var entered = (nicknameInput.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(entered))
            {
                return;
            }

            _nickname = entered;
            ShowLobbyPanel();
            RefreshRoomList();
        }

        private void OpenCreateRoomModal()
        {
            createRoomNameInput.text = string.Empty;
            createPasswordInput.text = string.Empty;
            createPrivateToggle.isOn = false;
            createValidationText.text = string.Empty;
            createRoomModal.SetActive(true);
            OnPrivateToggleChanged(false);
        }

        private void OnCreateRoomConfirmed()
        {
            var roomName = (createRoomNameInput.text ?? string.Empty).Trim();
            var isPrivate = createPrivateToggle.isOn;
            var password = createPasswordInput.text ?? string.Empty;

            if (string.IsNullOrEmpty(roomName))
            {
                createValidationText.text = "Please enter room name.";
                return;
            }

            if (isPrivate && string.IsNullOrWhiteSpace(password))
            {
                createValidationText.text = "Password required for private room.";
                return;
            }

            createValidationText.text = string.Empty;
            var room = _lobbyService.CreateRoom(roomName, isPrivate, password, _nickname);
            createRoomModal.SetActive(false);
            EnterRoom(room);
        }

        private void OnPrivateToggleChanged(bool isPrivate)
        {
            createPasswordInput.gameObject.SetActive(isPrivate);
        }

        private void RefreshRoomList()
        {
            for (var i = roomListContent.childCount - 1; i >= 0; i--)
            {
                var child = roomListContent.GetChild(i).gameObject;
                if (child != roomItemTemplate)
                {
                    Destroy(child);
                }
            }

            var rooms = _lobbyService.GetRooms();
            if (rooms.Count == 0)
            {
                lobbyStatusText.text = "No rooms available.";
                return;
            }

            lobbyStatusText.text = "Select a room to join.";
            foreach (var room in rooms)
            {
                var row = Instantiate(roomItemTemplate, roomListContent);
                row.SetActive(true);

                var text = row.GetComponentInChildren<TMP_Text>(true);
                if (text != null)
                {
                    var lockState = room.IsPrivate ? "LOCK" : "OPEN";
                    text.text = $"{lockState}  {room.Name}  ({room.MemberCount})";
                }

                var button = row.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => OnRoomSelected(room));
                }
            }
        }

        private void OnRoomSelected(LobbyRoom room)
        {
            if (room.IsPrivate)
            {
                _pendingPrivateRoom = room;
                joinPasswordInput.text = string.Empty;
                passwordValidationText.text = string.Empty;
                passwordModal.SetActive(true);
                return;
            }

            if (_lobbyService.TryJoinRoom(room, _nickname, string.Empty, out var error))
            {
                EnterRoom(room);
            }
            else
            {
                lobbyStatusText.text = error;
            }
        }

        private void OnPasswordConfirmed()
        {
            if (_pendingPrivateRoom == null)
            {
                passwordModal.SetActive(false);
                return;
            }

            if (_lobbyService.TryJoinRoom(_pendingPrivateRoom, _nickname, joinPasswordInput.text, out var error))
            {
                passwordModal.SetActive(false);
                EnterRoom(_pendingPrivateRoom);
                _pendingPrivateRoom = null;
            }
            else
            {
                passwordValidationText.text = error;
            }
        }

        private void EnterRoom(LobbyRoom room)
        {
            _currentRoom = room;
            ShowRoomPanel();
            UpdateRoomPanel();
        }

        private void OnLeaveRoomClicked()
        {
            _lobbyService.LeaveRoom(_currentRoom, _nickname);
            _currentRoom = null;
            ShowLobbyPanel();
            RefreshRoomList();
        }

        private void UpdateRoomPanel()
        {
            if (_currentRoom == null)
            {
                return;
            }

            var state = _currentRoom.IsPrivate ? "[PRIVATE]" : "[PUBLIC]";
            roomTitleText.text = $"{state} {_currentRoom.Name}";
            roomMembersText.text = $"Owner: {_currentRoom.OwnerNickname}\n\nMembers ({_currentRoom.MemberCount})\n- {string.Join("\n- ", _currentRoom.Members)}";
        }

        private void ShowNicknamePanel()
        {
            nicknamePanel.SetActive(true);
            lobbyPanel.SetActive(false);
            roomPanel.SetActive(false);
            createRoomModal.SetActive(false);
            passwordModal.SetActive(false);
        }

        private void ShowLobbyPanel()
        {
            nicknamePanel.SetActive(false);
            lobbyPanel.SetActive(true);
            roomPanel.SetActive(false);
            passwordModal.SetActive(false);
            lobbyHeaderText.text = $"Nickname: {_nickname}";
        }

        private void ShowRoomPanel()
        {
            nicknamePanel.SetActive(false);
            lobbyPanel.SetActive(false);
            roomPanel.SetActive(true);
        }
    }
}
