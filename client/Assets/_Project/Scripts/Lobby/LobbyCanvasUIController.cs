using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    public sealed partial class LobbyCanvasUIController : MonoBehaviour, INetworkRunnerCallbacks
    {
        private sealed class RoomSnapshot
        {
            public string Name;
            public bool IsPrivate;
            public string Password;
            public string OwnerNickname;
            public int PlayerCount;
            public int MaxPlayers;
            public bool IsOpen;
        }

        private sealed class ParticipantPresence
        {
            public int PlayerId;
            public string Nickname;
            public int CharacterIndex;
        }

        private enum CharacterKind
        {
            Ghost = 0,
            Glasses = 1,
            Green = 2,
            Ssaty = 3
        }

        private const string PrivateKey = "isPrivate";
        private const string PasswordKey = "password";
        private const string OwnerKey = "owner";
        private const string StartedKey = "started";
        private const string PlayerRosterLabel = "participants";
        private const int MaxPlayers = 4;
        private const string FallbackAppVersion = "ssafy-playtime-v1";
        private const string SharedLobbyName = "ssafy-main-lobby";
        private static readonly ReliableKey PlayerRosterReliableKey =
            ReliableKey.FromInts(unchecked((int)0x53534146), unchecked((int)0x504C4159), 1, 0);
        private static readonly ReliableKey CharacterSelectionReliableKey =
            ReliableKey.FromInts(unchecked((int)0x53534146), unchecked((int)0x504C4159), 2, 0);
        private const int PlayerSlotCount = 4;
        private const int CharacterOptionCount = 4;

        [Header("Panels")]
        [SerializeField] private GameObject nicknamePanel;
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private GameObject roomPanel;
        [SerializeField] private GameObject createRoomModal;
        [SerializeField] private GameObject passwordModal;

        [Header("Nickname")]
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button nicknameConfirmButton;
        [SerializeField] private TMP_Text nicknameValidationText;

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
        [SerializeField] private TMP_Text playerOneText;
        [SerializeField] private TMP_Text playerTwoText;
        [SerializeField] private TMP_Text playerThreeText;
        [SerializeField] private TMP_Text playerFourText;
        [SerializeField] private GameObject ghostCharacterRoot;
        [SerializeField] private GameObject glassesCharacterRoot;
        [SerializeField] private GameObject greenCharacterRoot;
        [SerializeField] private GameObject ssatyCharacterRoot;
        [SerializeField] private GameObject characterSelectionPanel;
        [SerializeField] private Button selectGhostCharacterButton;
        [SerializeField] private Button selectGlassesCharacterButton;
        [SerializeField] private Button selectGreenCharacterButton;
        [SerializeField] private Button selectSsatyCharacterButton;
        [SerializeField] private bool lockPlayerSlotLayoutToViewport = true;
        [SerializeField] private float playerSlotViewportY = 0.3f;
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
        [SerializeField] private float characterVerticalOffset = 20f;
        [SerializeField] private float characterExtraVerticalOffset = 20f;
        [SerializeField] private float ghostCharacterVerticalOffsetAdjustment = -18f;
        [SerializeField] private Transform characterRuntimeRoot;
        [SerializeField] private Camera characterPlacementCamera;
        [SerializeField] private float characterWorldDepth = 8f;
        [SerializeField] private Vector3 characterWorldOffset = Vector3.zero;
        [SerializeField] private float characterScreenPaddingPixels = 24f;
        [SerializeField] private bool keepCharacterScreenSize = true;
        [SerializeField] private float characterTargetScreenHeightPixels = 170f;
        [SerializeField] private float characterScreenHeightMultiplier = 2.5f;
        [SerializeField] private bool testShowOneCharacterPerSlot = true;
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private string gameplaySceneName = string.Empty;

        [Header("UI Text (Inspector)")]
        [SerializeField] private string statusRefreshingRooms = "방 목록을 새로고침 중입니다...";
        [SerializeField] private string statusNetworkLobbyFailed = "네트워크 로비 연결에 실패했습니다.";
        [SerializeField] private string statusRoomListUiNotBound = "방 목록 UI 참조가 연결되지 않았습니다.";
        [SerializeField] private string statusNoRooms = "현재 참여 가능한 방이 없습니다.";
        [SerializeField] private string statusSelectRoom = "입장할 방을 선택하세요.";
        [SerializeField] private string statusRoomNotFound = "방을 찾을 수 없습니다. 목록을 새로고침하세요.";
        [SerializeField] private string statusRunnerInitFailed = "네트워크 러너 초기화에 실패했습니다.";
        [SerializeField] private string statusRunnerNotReady = "네트워크 러너가 준비되지 않았습니다.";
        [SerializeField] private string statusOnlyHostCanStart = "호스트만 게임을 시작할 수 있습니다.";
        [SerializeField] private string statusSessionNotReady = "세션이 아직 준비되지 않았습니다.";
        [SerializeField] private string statusNeedTwoPlayers = "게임 시작에는 최소 2명이 필요합니다.";
        [SerializeField] private string statusStartRequestedNoScene = "게임 시작 요청됨 (게임 씬이 설정되지 않음)";
        [SerializeField] private string statusHostCanStartWithF5 = "호스트는 F5로 게임을 시작할 수 있습니다.";
        [SerializeField] private string statusConnectedFormat = "연결됨 ({0}/{1}/{2}) - 입장할 방을 선택하세요.";
        [SerializeField] private string statusLobbyConnectFailedFormat = "로비 연결 실패: {0}";
        [SerializeField] private string statusRoomJoinFailedFormat = "방 입장 실패: {0}";
        [SerializeField] private string statusRoomCreateFailedFormat = "방 생성 실패: {0}";
        [SerializeField] private string statusStartGameFailedFormat = "게임 시작 실패: {0}";
        [SerializeField] private string statusRoomsUpdatedFormat = "방 목록 업데이트: {0}개";
        [SerializeField] private string statusDisconnectedFormat = "연결 종료: {0}";
        [SerializeField] private string statusHostMigrationInitFailed = "호스트 이관 실패: 러너 초기화 실패";
        [SerializeField] private string statusHostMigrationFailedFormat = "호스트 이관 실패: {0}";
        [SerializeField] private string validationEnterNickname = "닉네임을 입력해주세요.";
        [SerializeField] private string validationNicknameInUse = "이미 사용 중인 닉네임입니다.";
        [SerializeField] private string validationNicknameLengthExceeded = "닉네임은 영문/숫자 최대 16자, 한글 포함 시 최대 8자입니다.";
        [SerializeField] private string validationEnterRoomName = "방 이름을 입력해주세요.";
        [SerializeField] private string validationRoomNameInUse = "이미 사용 중인 방 이름입니다.";
        [SerializeField] private string validationRoomNameLengthExceeded = "방 이름은 영문/숫자 최대 16자, 한글 포함 시 최대 8자입니다.";
        [SerializeField] private string validationPrivatePasswordRequired = "비공개 방은 비밀번호가 필요합니다.";
        [SerializeField] private string validationPasswordNumericOnly = "비밀번호는 숫자만 사용할 수 있습니다.";
        [SerializeField] private string validationInvalidPassword = "비밀번호가 올바르지 않습니다.";
        [SerializeField] private string validationRoomUnavailable = "이미 종료된 방입니다.";
        [SerializeField] private string accessPrivate = "비공개";
        [SerializeField] private string accessPublic = "공개";
        [SerializeField] private string roomStateJoinable = "입장가능";
        [SerializeField] private string roomStateFull = "가득참";
        [SerializeField] private string roomTagPrivate = "[비공개]";
        [SerializeField] private string roomTagPublic = "[공개]";
        [SerializeField] private string emptyPlayerSlot = "-";
        [SerializeField] private string nicknameHeaderFormat = "닉네임: {0}";

        private readonly List<RoomSnapshot> _roomSnapshots = new();

        private NetworkRunner _runner;
        private GameObject _runnerObject;
        private string _nickname = string.Empty;
        private string _currentRoomName = string.Empty;
        private bool _currentRoomIsPrivate;
        private string _currentRoomOwner = "-";
        private int _currentOwnerPlayerId = -1;
        private string _pendingPrivateRoomName = string.Empty;
        private string _pendingPrivateRoomPassword = string.Empty;
        private bool _isNicknameConfirmed;
        private bool _isProcessing;
        private bool _isInLobby;
        private bool _isShuttingDownRunner;
        private DateTime _lastSessionListUpdatedAtUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _runnerLock = new(1, 1);
        private readonly Dictionary<int, ParticipantPresence> _roomParticipantsByPlayerId = new();
        private readonly Transform[,] _slotCharacterRoots = new Transform[PlayerSlotCount, CharacterOptionCount];
        private readonly int[] _selectedCharacterIndexBySlot = { -1, -1, -1, -1 };
        private readonly int[] _playerIdBySlot = { -1, -1, -1, -1 };
        private readonly Dictionary<int, int> _selectedCharacterIndexByPlayerId = new();
        private readonly Dictionary<Transform, Vector3> _characterBaseLocalScales = new();
        private readonly Dictionary<Transform, float> _characterBaseBoundsHeights = new();
        private readonly Dictionary<Transform, int> _characterOptionIndexByTransform = new();
        private bool _characterSlotsInitialized;

        private void Start()
        {
            EnsurePersistentAcrossScenes();
            RuntimeLogOverlay.EnsureInstance();
            ApplyRuntimeLayoutOverrides();
            AutoBindLobbyRefsIfMissing();
            EnsureCharacterSelectionUi();
            NormalizeCanvasRoot();
            NormalizeRoomListBindings();
            BindEvents();
            InitializeCharacterSlotsIfNeeded();
            ShowNicknamePanel();
        }

        private void ApplyRuntimeLayoutOverrides()
        {
            // Scene-serialized inspector values can override code defaults.
            // Force requested lobby preview layout values at runtime so build output matches.
            playerSlotViewportY = 0.36f;
            playerSlotExtraViewportY = 0f;
            playerSlotSizeMultiplier = 1.35f;
            useQuarterWidthNameSlots = true;
            playerSlotQuarterHorizontalMargin = 12f;
            playerSlotQuarterWidthScale = 0.95f;
            nicknameFontSizeMin = 32f;
            nicknameFontSizeMax = 64f;
            characterVerticalOffset = 20f;
            characterExtraVerticalOffset = 20f;
            ghostCharacterVerticalOffsetAdjustment = -18f;
            characterScreenHeightMultiplier = 2.5f;
        }

        private void Update()
        {
            if (roomPanel != null && roomPanel.activeSelf && _runner != null && _runner.IsRunning && _runner.IsServer)
            {
                if (Input.GetKeyDown(KeyCode.F5))
                {
                    OnStartGameClicked();
                }
            }

            if (roomPanel != null && roomPanel.activeSelf)
            {
                LayoutNameSlotsForCurrentViewport();
                AlignAllCharacterCandidatesToNameSlots();
            }
        }


        private async void OnDestroy()
        {
            await ShutdownRunnerAsync();
        }

        private void BindEvents()
        {
            nicknameConfirmButton.onClick.AddListener(OnNicknameConfirmed);
            createRoomOpenButton.onClick.AddListener(OpenCreateRoomModal);
            refreshRoomsButton.onClick.AddListener(OnRefreshRoomsClicked);

            if (nicknameInput != null)
            {
                nicknameInput.onValueChanged.AddListener(_ => EnforceNameInputLimit(nicknameInput));
            }

            createPrivateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);
            createConfirmButton.onClick.AddListener(OnCreateRoomConfirmed);
            createCancelButton.onClick.AddListener(() => createRoomModal.SetActive(false));

            if (createRoomNameInput != null)
            {
                createRoomNameInput.onValueChanged.AddListener(_ => EnforceNameInputLimit(createRoomNameInput));
            }

            passwordJoinButton.onClick.AddListener(OnPasswordConfirmed);
            passwordCancelButton.onClick.AddListener(() => passwordModal.SetActive(false));

            if (createPasswordInput != null)
            {
                ConfigureNumericPasswordInput(createPasswordInput);
                createPasswordInput.onValidateInput += ValidateNumericPasswordChar;
                createPasswordInput.onValueChanged.AddListener(_ => EnforceNumericPasswordInput(createPasswordInput));
            }

            if (joinPasswordInput != null)
            {
                ConfigureNumericPasswordInput(joinPasswordInput);
                joinPasswordInput.onValidateInput += ValidateNumericPasswordChar;
                joinPasswordInput.onValueChanged.AddListener(_ => EnforceNumericPasswordInput(joinPasswordInput));
            }

            leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
            if (startGameButton != null)
            {
                startGameButton.onClick.AddListener(OnStartGameClicked);
            }

            if (selectGhostCharacterButton != null)
            {
                selectGhostCharacterButton.onClick.AddListener(OnSelectGhostCharacter);
            }

            if (selectGlassesCharacterButton != null)
            {
                selectGlassesCharacterButton.onClick.AddListener(OnSelectGlassesCharacter);
            }

            if (selectGreenCharacterButton != null)
            {
                selectGreenCharacterButton.onClick.AddListener(OnSelectGreenCharacter);
            }

            if (selectSsatyCharacterButton != null)
            {
                selectSsatyCharacterButton.onClick.AddListener(OnSelectSsatyCharacter);
            }
        }

        private async void OnRefreshRoomsClicked()
        {
            if (!_isNicknameConfirmed)
            {
                return;
            }

            SetLobbyStatus(statusRefreshingRooms);
            if (!await EnsureLobbyRunnerAsync())
            {
                SetLobbyStatus(statusNetworkLobbyFailed);
                return;
            }

            RefreshRoomList();
            Debug.Log("[Lobby] Room list refresh completed via lobby reconnect.");
        }

        private async void OnNicknameConfirmed()
        {
            var entered = (nicknameInput.text ?? string.Empty).Trim();
            entered = SanitizeNameToken(entered);
            if (string.IsNullOrEmpty(entered))
            {
                SetNicknameValidation(validationEnterNickname);
                return;
            }

            if (!IsWithinNameLengthLimit(entered))
            {
                SetNicknameValidation(validationNicknameLengthExceeded);
                return;
            }

            SetNicknameValidation(string.Empty);

            if (!await EnsureLobbyRunnerAsync())
            {
                SetNicknameValidation(statusNetworkLobbyFailed);
                return;
            }

            await WaitForInitialSessionListAsync();

            if (IsNicknameAlreadyUsed(entered))
            {
                SetNicknameValidation(validationNicknameInUse);
                return;
            }

            _nickname = entered;
            nicknameInput.text = entered;
            _isNicknameConfirmed = true;
            ShowLobbyPanel();
            RefreshRoomList();
        }

        private void OpenCreateRoomModal()
        {
            createRoomNameInput.text = string.Empty;
            createPasswordInput.text = string.Empty;
            createPrivateToggle.isOn = false;
            SetCreateValidation(string.Empty);
            createRoomModal.SetActive(true);
            OnPrivateToggleChanged(false);
        }

        private async void OnCreateRoomConfirmed()
        {
            if (_isProcessing)
            {
                return;
            }

            var roomName = (createRoomNameInput.text ?? string.Empty).Trim();
            roomName = SanitizeNameToken(roomName);
            var isPrivate = createPrivateToggle.isOn;
            var password = createPasswordInput.text ?? string.Empty;

            if (string.IsNullOrEmpty(roomName))
            {
                SetCreateValidation(validationEnterRoomName);
                return;
            }

            if (!IsWithinNameLengthLimit(roomName))
            {
                SetCreateValidation(validationRoomNameLengthExceeded);
                return;
            }

            if (IsRoomNameAlreadyUsed(roomName))
            {
                SetCreateValidation(validationRoomNameInUse);
                return;
            }

            if (isPrivate && string.IsNullOrWhiteSpace(password))
            {
                SetCreateValidation(validationPrivatePasswordRequired);
                return;
            }

            if (isPrivate && !IsNumericPassword(password))
            {
                SetCreateValidation(validationPasswordNumericOnly);
                return;
            }

            SetCreateValidation(string.Empty);

            var sessionProperties = new Dictionary<string, SessionProperty>
            {
                { PrivateKey, isPrivate },
                { OwnerKey, _nickname }
            };
            if (isPrivate)
            {
                sessionProperties[PasswordKey] = password;
            }
            sessionProperties[StartedKey] = false;

            _isProcessing = true;
            await ShutdownRunnerAsync();

            if (!TryCreateRunner(out var runner))
            {
                _isProcessing = false;
                SetCreateValidation(statusRunnerInitFailed);
                return;
            }

            _runner = runner;
            StartGameResult result;
            try
            {
                var appSettings = GetOrCreatePhotonSettings();
                result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Host,
                    SessionName = roomName,
                    PlayerCount = MaxPlayers,
                    SessionProperties = sessionProperties,
                    IsVisible = true,
                    IsOpen = true,
                    CustomLobbyName = SharedLobbyName,
                    CustomPhotonAppSettings = appSettings
                });
            }
            finally
            {
                _isProcessing = false;
            }

            if (!result.Ok)
            {
                SetCreateValidation(string.Format(statusRoomCreateFailedFormat, result.ShutdownReason));
                Debug.LogWarning($"[Lobby] Room create failed: name={roomName}, reason={result.ShutdownReason}");
                return;
            }

            _currentRoomName = roomName;
            _currentRoomIsPrivate = isPrivate;
            _currentRoomOwner = _nickname;
            _currentOwnerPlayerId = _runner.LocalPlayer.PlayerId;
            _isInLobby = false;
            RegisterParticipant(_runner.LocalPlayer, _nickname);
            UpdateOwnerSessionProperty();
            BroadcastPlayerRoster();
            Debug.Log($"[Lobby] Room created: name={roomName}, private={isPrivate}, owner={_nickname}, runnerActive={_runner != null && _runner.IsRunning}");
            createRoomModal.SetActive(false);
            ShowRoomPanel();
            UpdateRoomPanel();
        }

        private void OnPrivateToggleChanged(bool isPrivate)
        {
            createPasswordInput.gameObject.SetActive(isPrivate);
        }

        private void RefreshRoomList()
        {
            if (!_isNicknameConfirmed)
            {
                return;
            }

            if (roomListContent == null || roomItemTemplate == null)
            {
                SetLobbyStatus(statusRoomListUiNotBound);
                return;
            }

            for (var i = roomListContent.childCount - 1; i >= 0; i--)
            {
                var child = roomListContent.GetChild(i).gameObject;
                if (child != roomItemTemplate)
                {
                    Destroy(child);
                }
            }

            if (_roomSnapshots.Count == 0)
            {
                SetLobbyStatus(statusNoRooms);
                return;
            }

            SetLobbyStatus(statusSelectRoom);
            foreach (var room in _roomSnapshots.OrderByDescending(r => r.PlayerCount).ThenBy(r => r.Name))
            {
                var row = Instantiate(roomItemTemplate, roomListContent);
                row.SetActive(true);

                var text = row.GetComponentInChildren<TMP_Text>(true);
                if (text != null)
                {
                    var joinable = room.IsOpen && room.PlayerCount < room.MaxPlayers;
                    var accessState = room.IsPrivate ? accessPrivate : accessPublic;
                    var roomState = joinable ? roomStateJoinable : roomStateFull;
                    text.text = $"{accessState}/{roomState}  {room.Name}  ({room.PlayerCount}/{room.MaxPlayers})";
                }

                var button = row.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    var joinable = room.IsOpen && room.PlayerCount < room.MaxPlayers;
                    button.interactable = joinable;
                    if (joinable)
                    {
                        button.onClick.AddListener(() => OnRoomSelected(room.Name));
                    }
                }
            }
        }

        private async void OnRoomSelected(string roomName)
        {
            var selected = _roomSnapshots.FirstOrDefault(r => string.Equals(r.Name, roomName, StringComparison.Ordinal));
            if (selected == null)
            {
                SetLobbyStatus(statusRoomNotFound);
                return;
            }

            if (selected.IsPrivate)
            {
                _pendingPrivateRoomName = selected.Name;
                _pendingPrivateRoomPassword = selected.Password ?? string.Empty;
                joinPasswordInput.text = string.Empty;
                passwordValidationText.text = string.Empty;
                passwordModal.SetActive(true);
                return;
            }

            await JoinRoomAsync(selected);
        }

        private async void OnPasswordConfirmed()
        {
            if (string.IsNullOrEmpty(_pendingPrivateRoomName))
            {
                passwordModal.SetActive(false);
                return;
            }

            if (!string.Equals(joinPasswordInput.text ?? string.Empty, _pendingPrivateRoomPassword, StringComparison.Ordinal))
            {
                passwordValidationText.text = validationInvalidPassword;
                return;
            }

            var selected = _roomSnapshots.FirstOrDefault(r => string.Equals(r.Name, _pendingPrivateRoomName, StringComparison.Ordinal));
            if (selected == null)
            {
                passwordValidationText.text = validationRoomUnavailable;
                return;
            }

            passwordModal.SetActive(false);
            _pendingPrivateRoomName = string.Empty;
            _pendingPrivateRoomPassword = string.Empty;
            await JoinRoomAsync(selected);
        }

        private async Task JoinRoomAsync(RoomSnapshot room)
        {
            if (_isProcessing)
            {
                return;
            }

            _isProcessing = true;
            await ShutdownRunnerAsync();

            if (!TryCreateRunner(out var runner))
            {
                _isProcessing = false;
                SetLobbyStatus(statusRunnerInitFailed);
                return;
            }

            _runner = runner;
            StartGameResult result;
            try
            {
                var appSettings = GetOrCreatePhotonSettings();
                result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Client,
                    SessionName = room.Name,
                    SceneManager = GetOrAddSceneManager(),
                    ConnectionToken = BuildConnectionToken(_nickname),
                    CustomLobbyName = SharedLobbyName,
                    CustomPhotonAppSettings = appSettings
                });
            }
            finally
            {
                _isProcessing = false;
            }

            if (!result.Ok)
            {
                SetLobbyStatus(string.Format(statusRoomJoinFailedFormat, result.ShutdownReason));
                Debug.LogWarning($"[Lobby] Room join failed: name={room.Name}, reason={result.ShutdownReason}");
                return;
            }

            _currentRoomName = room.Name;
            _currentRoomIsPrivate = room.IsPrivate;
            _currentRoomOwner = string.IsNullOrWhiteSpace(room.OwnerNickname) ? "-" : room.OwnerNickname;
            _currentOwnerPlayerId = -1;
            _isInLobby = false;
            RegisterParticipant(_runner.LocalPlayer, _nickname);
            Debug.Log($"[Lobby] Room joined: name={room.Name}, private={room.IsPrivate}");
            ShowRoomPanel();
            UpdateRoomPanel();
        }

        private void OnStartGameClicked()
        {
            if (_runner == null || !_runner.IsRunning)
            {
                SetLobbyStatus(statusRunnerNotReady);
                return;
            }

            if (!_runner.IsServer)
            {
                SetLobbyStatus(statusOnlyHostCanStart);
                return;
            }

            if (!_runner.SessionInfo.IsValid)
            {
                SetLobbyStatus(statusSessionNotReady);
                return;
            }

            if (_runner.SessionInfo.PlayerCount <= 1)
            {
                SetLobbyStatus(statusNeedTwoPlayers);
                return;
            }

            try
            {
                _runner.SessionInfo.UpdateCustomProperties(new Dictionary<string, SessionProperty>
                {
                    { StartedKey, true }
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lobby] Failed to update start flag: {e.Message}");
            }

            if (string.IsNullOrWhiteSpace(gameplaySceneName))
            {
                SetLobbyStatus(statusStartRequestedNoScene);
                Debug.Log("[Lobby] Start requested but gameplaySceneName is empty.");
                return;
            }

            if (!TryResolveGameplaySceneName(gameplaySceneName, out var resolvedSceneName))
            {
                var message = $"게임 씬을 찾을 수 없습니다: {gameplaySceneName}";
                SetLobbyStatus(message);
                Debug.LogWarning($"[Lobby] {message}. Build Settings에 씬이 포함되어 있는지 확인하세요.");
                return;
            }

            try
            {
                _runner.LoadScene(resolvedSceneName, LoadSceneMode.Single, LocalPhysicsMode.None, true);
                Debug.Log($"[Lobby] Starting game scene: {resolvedSceneName}");
            }
            catch (Exception e)
            {
                SetLobbyStatus(string.Format(statusStartGameFailedFormat, e.Message));
                Debug.LogWarning($"[Lobby] Start game failed: {e.Message}");
            }
        }

        private static bool TryResolveGameplaySceneName(string requestedSceneName, out string resolvedSceneName)
        {
            resolvedSceneName = string.Empty;
            if (string.IsNullOrWhiteSpace(requestedSceneName))
            {
                return false;
            }

            var trimmed = requestedSceneName.Trim();
            var requestedBaseName = System.IO.Path.GetFileNameWithoutExtension(trimmed);

            // 1) Exact scene name match in Build Settings
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var baseName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.Equals(baseName, requestedBaseName, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedSceneName = baseName;
                    return true;
                }
            }

            // 2) Exact full path match in Build Settings
            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (string.Equals(path, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedSceneName = System.IO.Path.GetFileNameWithoutExtension(path);
                    return true;
                }
            }

            return false;
        }

        private async void OnLeaveRoomClicked()
        {
            _currentRoomName = string.Empty;
            _currentRoomOwner = "-";
            _currentOwnerPlayerId = -1;
            _currentRoomIsPrivate = false;

            await ShutdownRunnerAsync();
            ShowLobbyPanel();
            await EnsureLobbyRunnerAsync();
            RefreshRoomList();
        }

        private void UpdateRoomPanel()
        {
            if (string.IsNullOrEmpty(_currentRoomName))
            {
                return;
            }

            var currentPlayers = _runner != null && _runner.IsRunning && _runner.SessionInfo.IsValid
                ? _runner.SessionInfo.PlayerCount
                : 1;

            var state = _currentRoomIsPrivate ? roomTagPrivate : roomTagPublic;
            SyncCurrentRoomOwnerFromRoster();
            roomTitleText.text = $"{state} {_currentRoomName}";
            UpdatePlayerSlots();
            if (startGameButton != null)
            {
                var canStart = _runner != null && _runner.IsRunning && _runner.IsServer && currentPlayers > 1;
                startGameButton.gameObject.SetActive(true);
                startGameButton.interactable = canStart;
            }
        }

        private void UpdatePlayerSlots()
        {
            var slotTexts = new[] { playerOneText, playerTwoText, playerThreeText, playerFourText };
            var orderedParticipants = _roomParticipantsByPlayerId.Values
                .Where(v => !string.IsNullOrWhiteSpace(v?.Nickname))
                .OrderBy(v => v.PlayerId)
                .Take(slotTexts.Length)
                .ToList();

            for (var i = 0; i < slotTexts.Length; i++)
            {
                var slot = slotTexts[i];
                if (slot == null)
                {
                    continue;
                }

                if (i < orderedParticipants.Count)
                {
                    var participant = orderedParticipants[i];
                    slot.text = participant.Nickname;
                    _playerIdBySlot[i] = participant.PlayerId;
                    _selectedCharacterIndexBySlot[i] = SanitizeCharacterIndexOrNone(participant.CharacterIndex);
                    ApplySelectedCharacterForSlot(i, true);
                }
                else
                {
                    slot.text = emptyPlayerSlot;
                    _playerIdBySlot[i] = -1;
                    _selectedCharacterIndexBySlot[i] = -1;
                    ApplySelectedCharacterForSlot(i, false);
                }
            }

            LayoutNameSlotsForCurrentViewport();
            AlignAllCharacterCandidatesToNameSlots();
            RefreshCharacterSelectionUiState();
        }

        private void InitializeCharacterSlotsIfNeeded()
        {
            if (_characterSlotsInitialized)
            {
                return;
            }

            var templates = new[] { ghostCharacterRoot, glassesCharacterRoot, greenCharacterRoot, ssatyCharacterRoot };
            var nameSlots = GetNameSlots();
            if (templates.Any(t => t == null) || nameSlots.Any(t => t == null))
            {
                return;
            }

            for (var slot = 0; slot < PlayerSlotCount; slot++)
            {
                for (var option = 0; option < CharacterOptionCount; option++)
                {
                    var template = templates[option];
                    var clone = Instantiate(template, ResolveCharacterRuntimeRoot(), true);
                    clone.name = $"{template.name}_Slot{slot + 1}";
                    ConfigureCharacterPreviewClone(clone);
                    clone.SetActive(false);
                    var cloneTransform = clone.transform;
                    _slotCharacterRoots[slot, option] = cloneTransform;
                    _characterBaseLocalScales[cloneTransform] = cloneTransform.localScale;
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

            var nameSlots = GetNameSlots();
            for (var slot = 0; slot < PlayerSlotCount; slot++)
            {
                var nameSlot = nameSlots[slot];
                for (var option = 0; option < CharacterOptionCount; option++)
                {
                    AlignCharacterSlot(_slotCharacterRoots[slot, option], nameSlot);
                }
            }
        }

        private void AlignCharacterSlot(Transform characterSlot, TMP_Text nameSlot)
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
                charRect.anchorMin = nameRect.anchorMin;
                charRect.anchorMax = nameRect.anchorMax;
                charRect.pivot = nameRect.pivot;
                charRect.anchoredPosition = nameRect.anchoredPosition + new Vector2(
                    0f,
                    characterVerticalOffset + characterExtraVerticalOffset + characterSpecificOffset);
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
            screenPoint.y += characterVerticalOffset + characterExtraVerticalOffset + GetCharacterSpecificVerticalOffset(characterSlot);
            var padding = Mathf.Max(0f, characterScreenPaddingPixels);
            screenPoint.x = Mathf.Clamp(screenPoint.x, padding, Screen.width - padding);
            screenPoint.y = Mathf.Clamp(screenPoint.y, padding, Screen.height - padding);

            var depth = characterWorldDepth;
            if (depth <= 0f)
            {
                depth = Vector3.Dot(characterSlot.position - worldCam.transform.position, worldCam.transform.forward);
                if (depth <= 0f)
                {
                    depth = 8f;
                }
            }

            var targetWorld = worldCam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
            characterSlot.position = targetWorld + characterWorldOffset;

            if (keepCharacterScreenSize)
            {
                ApplyCharacterScreenHeightScale(characterSlot, worldCam);
            }
        }

        private void LayoutNameSlotsForCurrentViewport()
        {
            if (!lockPlayerSlotLayoutToViewport)
            {
                return;
            }

            var canvasRect = GetComponentInParent<Canvas>()?.transform as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            var slotTexts = GetNameSlots();
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

            for (var i = 0; i < slotTexts.Length; i++)
            {
                var text = slotTexts[i];
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

        private static Vector3 GetNameAnchorWorldPoint(TMP_Text nameSlot, RectTransform nameRect)
        {
            if (nameSlot == null || nameRect == null)
            {
                return Vector3.zero;
            }

            // Use rendered glyph bounds so world characters track the visible nickname
            // instead of the full stretched text rect.
            nameSlot.ForceMeshUpdate();
            var bounds = nameSlot.textBounds;
            if (bounds.size.sqrMagnitude > 0.0001f)
            {
                return nameRect.TransformPoint(bounds.center);
            }

            return nameRect.TransformPoint(nameRect.rect.center);
        }

        private Camera ResolveCharacterPlacementCamera()
        {
            if (characterPlacementCamera != null)
            {
                return characterPlacementCamera;
            }

            if (Camera.main != null)
            {
                return Camera.main;
            }

            return FindObjectOfType<Camera>();
        }

        private Camera ResolveUiCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera != null ? canvas.worldCamera : ResolveCharacterPlacementCamera();
        }

        private Transform ResolveCharacterRuntimeRoot()
        {
            if (characterRuntimeRoot != null)
            {
                return characterRuntimeRoot;
            }

            var root = GameObject.Find("LobbyCharacterRuntimeRoot");
            if (root == null)
            {
                root = new GameObject("LobbyCharacterRuntimeRoot");
            }

            characterRuntimeRoot = root.transform;
            return characterRuntimeRoot;
        }

        private float GetCharacterSpecificVerticalOffset(Transform characterSlot)
        {
            if (characterSlot == null)
            {
                return 0f;
            }

            if (_characterOptionIndexByTransform.TryGetValue(characterSlot, out var option) &&
                option == (int)CharacterKind.Ghost)
            {
                return ghostCharacterVerticalOffsetAdjustment;
            }

            return 0f;
        }

        private static void ConfigureCharacterPreviewClone(GameObject clone)
        {
            if (clone == null)
            {
                return;
            }

            // Lobby preview characters must stay animation-driven.
            // If ragdoll rigidbodies remain dynamic, bones drift by gravity and stretch the skinned mesh.
            var rigidbodies = clone.GetComponentsInChildren<Rigidbody>(true);
            for (var i = 0; i < rigidbodies.Length; i++)
            {
                var rb = rigidbodies[i];
                if (rb == null)
                {
                    continue;
                }

                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            var colliders = clone.GetComponentsInChildren<Collider>(true);
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                {
                    continue;
                }

                colliders[i].enabled = false;
            }

            // Ragdoll joints can lock bone transforms and prevent visible animation updates in UI preview.
            var joints = clone.GetComponentsInChildren<ConfigurableJoint>(true);
            for (var i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null)
                {
                    continue;
                }

                Destroy(joints[i]);
            }

            var syncObjects = clone.GetComponentsInChildren<SyncPhysicsObject>(true);
            for (var i = 0; i < syncObjects.Length; i++)
            {
                if (syncObjects[i] == null)
                {
                    continue;
                }

                syncObjects[i].enabled = false;
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

            if (!_characterBaseBoundsHeights.TryGetValue(characterRoot, out var baseBoundsHeight) || baseBoundsHeight <= 0.001f)
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
            if (root == null)
            {
                return 1f;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return 1f;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                bounds.Encapsulate(renderers[i].bounds);
            }

            return Mathf.Max(0.001f, bounds.size.y);
        }

        private TMP_Text[] GetNameSlots()
        {
            return new[] { playerOneText, playerTwoText, playerThreeText, playerFourText };
        }

        public void SetSelectedCharacterForSlot(int slotIndex, string characterName)
        {
            if (slotIndex < 0 || slotIndex >= PlayerSlotCount)
            {
                return;
            }

            _selectedCharacterIndexBySlot[slotIndex] = SanitizeCharacterIndexOrNone(CharacterNameToIndex(characterName));
            ApplySelectedCharacterForSlot(slotIndex, true);
        }

        public void OnSelectGhostCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Ghost);
        public void OnSelectGlassesCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Glasses);
        public void OnSelectGreenCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Green);
        public void OnSelectSsatyCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Ssaty);
        public void SetLocalSelectedCharacterByName(string characterName) =>
            SetLocalPlayerSelectedCharacter(CharacterNameToIndex(characterName));

        private void SetLocalPlayerSelectedCharacter(int characterIndex)
        {
            if (_runner == null || !_runner.IsRunning || !_runner.LocalPlayer.IsRealPlayer)
            {
                return;
            }

            var normalizedIndex = SanitizeCharacterIndexOrNone(characterIndex);
            if (normalizedIndex < 0)
            {
                return;
            }
            var localPlayerId = _runner.LocalPlayer.PlayerId;
            _selectedCharacterIndexByPlayerId[localPlayerId] = normalizedIndex;

            if (_roomParticipantsByPlayerId.TryGetValue(localPlayerId, out var localPresence) && localPresence != null)
            {
                localPresence.CharacterIndex = normalizedIndex;
            }

            ApplyCharacterSelectionToVisibleSlot(localPlayerId, normalizedIndex);
            RefreshCharacterSelectionUiState();

            if (_runner.IsServer)
            {
                BroadcastPlayerRoster();
            }
            else
            {
                SendCharacterSelectionToHost(normalizedIndex);
            }
        }

        private void ApplyCharacterSelectionToVisibleSlot(int playerId, int characterIndex)
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

        private void SendCharacterSelectionToHost(int characterIndex)
        {
            if (_runner == null || !_runner.IsRunning || _runner.IsServer)
            {
                return;
            }

            if (!TryResolveOwnerPlayerRef(out var ownerPlayer))
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(characterIndex.ToString());
            try
            {
                _runner.SendReliableDataToPlayer(ownerPlayer, CharacterSelectionReliableKey, bytes);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lobby] Failed to send character selection: {e.Message}");
            }
        }

        private bool TryResolveOwnerPlayerRef(out PlayerRef ownerPlayer)
        {
            ownerPlayer = PlayerRef.None;
            if (_runner == null || !_runner.IsRunning)
            {
                return false;
            }

            if (_currentOwnerPlayerId > 0)
            {
                foreach (var active in _runner.ActivePlayers)
                {
                    if (active.IsRealPlayer && active.PlayerId == _currentOwnerPlayerId)
                    {
                        ownerPlayer = active;
                        return true;
                    }
                }
            }

            foreach (var active in _runner.ActivePlayers.OrderBy(p => p.PlayerId))
            {
                if (!active.IsRealPlayer)
                {
                    continue;
                }

                if (active == _runner.LocalPlayer)
                {
                    continue;
                }

                ownerPlayer = active;
                return true;
            }

            return false;
        }

        private static int SanitizeCharacterIndexOrNone(int rawIndex)
        {
            return rawIndex >= 0 && rawIndex < CharacterOptionCount ? rawIndex : -1;
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
                if (rect == null)
                {
                    continue;
                }

                rect.gameObject.SetActive(shouldShow && selectedIndex >= 0 && option == selectedIndex);
            }
        }

        private static int CharacterNameToIndex(string characterName)
        {
            if (string.Equals(characterName, "GhostCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.Ghost;
            }

            if (string.Equals(characterName, "GlassesCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.Glasses;
            }

            if (string.Equals(characterName, "GreenCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.Green;
            }

            if (string.Equals(characterName, "SsatyCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.Ssaty;
            }

            return -1;
        }

        private GameObject GetCharacterTemplateByIndex(int characterIndex)
        {
            return SanitizeCharacterIndexOrNone(characterIndex) switch
            {
                (int)CharacterKind.Ghost => ghostCharacterRoot,
                (int)CharacterKind.Glasses => glassesCharacterRoot,
                (int)CharacterKind.Green => greenCharacterRoot,
                (int)CharacterKind.Ssaty => ssatyCharacterRoot,
                _ => null
            };
        }

        private void ShowNicknamePanel()
        {
            nicknamePanel.SetActive(true);
            lobbyPanel.SetActive(false);
            roomPanel.SetActive(false);
            createRoomModal.SetActive(false);
            passwordModal.SetActive(false);
            SetNicknameValidation(string.Empty);
            RefreshCharacterSelectionUiState();
        }

        private void ShowLobbyPanel()
        {
            nicknamePanel.SetActive(false);
            lobbyPanel.SetActive(true);
            roomPanel.SetActive(false);
            passwordModal.SetActive(false);
            lobbyHeaderText.text = string.Format(nicknameHeaderFormat, _nickname);
            RefreshCharacterSelectionUiState();
        }

        private void ShowRoomPanel()
        {
            nicknamePanel.SetActive(false);
            lobbyPanel.SetActive(false);
            roomPanel.SetActive(true);
            if (startGameButton == null && _runner != null && _runner.IsServer)
            {
                SetLobbyStatus(statusHostCanStartWithF5);
            }
            RefreshCharacterSelectionUiState();
        }

        private async Task<bool> EnsureLobbyRunnerAsync(bool forceReconnect = false)
        {
            await _runnerLock.WaitAsync();
            try
            {
                if (!forceReconnect && _runner != null && _runner.IsRunning && _isInLobby)
                {
                    return true;
                }

                await ShutdownRunnerInternalAsync();

                if (!TryCreateRunner(out var runner))
                {
                    SetLobbyStatus(statusRunnerInitFailed);
                    return false;
                }

                _runner = runner;

                try
                {
                    var appSettings = GetOrCreatePhotonSettings();
                    await _runner.JoinSessionLobby(
                        SessionLobby.Custom,
                        SharedLobbyName,
                        customAppSettings: appSettings);
                    _isInLobby = true;
                    SetLobbyStatus(string.Format(
                        statusConnectedFormat,
                        appSettings.FixedRegion,
                        appSettings.AppVersion,
                        SharedLobbyName));
                    Debug.Log($"[Lobby] Connected to custom lobby: region={appSettings.FixedRegion}, version={appSettings.AppVersion}, lobby={SharedLobbyName}");
                    return true;
                }
                catch (Exception e)
                {
                    _isInLobby = false;
                    SetLobbyStatus(string.Format(statusLobbyConnectFailedFormat, e.Message));
                    Debug.LogWarning($"[Lobby] Lobby connect failed: {e.Message}");
                    await ShutdownRunnerInternalAsync();
                    return false;
                }
            }
            finally
            {
                _runnerLock.Release();
            }
        }

        private async Task ShutdownRunnerAsync()
        {
            await _runnerLock.WaitAsync();
            try
            {
                await ShutdownRunnerInternalAsync();
            }
            finally
            {
                _runnerLock.Release();
            }
        }

        private async Task ShutdownRunnerInternalAsync()
        {
            if (_runner == null)
            {
                return;
            }

            _runner.RemoveCallbacks(this);

            try
            {
                if (_runner.IsRunning)
                {
                    _isShuttingDownRunner = true;
                    await _runner.Shutdown();
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                _isShuttingDownRunner = false;
            }

            if (_runnerObject != null)
            {
                Destroy(_runnerObject);
            }
            else
            {
                Destroy(_runner);
            }
            _runner = null;
            _runnerObject = null;
            _isInLobby = false;
            _lastSessionListUpdatedAtUtc = DateTime.MinValue;
            _roomSnapshots.Clear();
            _roomParticipantsByPlayerId.Clear();
            _selectedCharacterIndexByPlayerId.Clear();
            _spawnedGameplayNetworkCharacters.Clear();
            _currentOwnerPlayerId = -1;
        }

        private bool TryCreateRunner(out NetworkRunner runner)
        {
            _runnerObject = new GameObject("LobbyNetworkRunner");
            _runnerObject.transform.SetParent(transform, false);
            runner = _runnerObject.AddComponent<NetworkRunner>();
            if (runner == null)
            {
                Destroy(_runnerObject);
                _runnerObject = null;
                return false;
            }

            runner.ProvideInput = true;
            runner.AddCallbacks(this);
            Debug.Log($"[Lobby] Runner created: object={_runnerObject.name}");
            return true;
        }

        private FusionAppSettings GetOrCreatePhotonSettings()
        {
            FusionAppSettings settings;
            if (PhotonAppSettings.TryGetGlobal(out var global) && global != null && global.AppSettings != null)
            {
                settings = global.AppSettings.GetCopy();
            }
            else
            {
                settings = new FusionAppSettings();
            }

            if (string.IsNullOrWhiteSpace(settings.AppVersion))
            {
                settings.AppVersion = FallbackAppVersion;
            }

            if (string.IsNullOrWhiteSpace(settings.FixedRegion))
            {
                settings.FixedRegion = "kr";
            }

            return settings;
        }

        private static byte[] BuildConnectionToken(string nickname)
        {
            var safeName = SanitizeNameToken(nickname);
            if (string.IsNullOrEmpty(safeName))
            {
                return Array.Empty<byte>();
            }

            return Encoding.UTF8.GetBytes(safeName);
        }

        private static string DecodeConnectionToken(byte[] token)
        {
            if (token == null || token.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return SanitizeNameToken(Encoding.UTF8.GetString(token));
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RegisterParticipant(PlayerRef player, string nickname)
        {
            if (!player.IsRealPlayer)
            {
                return;
            }

            var safeName = SanitizeNameToken(nickname);
            if (string.IsNullOrEmpty(safeName))
            {
                return;
            }

            var selectedCharacter = SanitizeCharacterIndexOrNone(
                _selectedCharacterIndexByPlayerId.TryGetValue(player.PlayerId, out var existing) ? existing : -1);
            _selectedCharacterIndexByPlayerId[player.PlayerId] = selectedCharacter;

            _roomParticipantsByPlayerId[player.PlayerId] = new ParticipantPresence
            {
                PlayerId = player.PlayerId,
                Nickname = safeName,
                CharacterIndex = selectedCharacter
            };
        }

        private void TryRegisterParticipantFromToken(NetworkRunner runner, PlayerRef player)
        {
            if (runner == null || !runner.IsRunning || !player.IsRealPlayer)
            {
                return;
            }

            var nickname = DecodeConnectionToken(runner.GetPlayerConnectionToken(player));
            if (string.IsNullOrEmpty(nickname) && player == runner.LocalPlayer)
            {
                nickname = _nickname;
            }

            RegisterParticipant(player, nickname);
        }

        private string BuildRosterPayload()
        {
            var entries = _roomParticipantsByPlayerId.Values
                .OrderBy(p => p.PlayerId)
                .Where(p => !string.IsNullOrWhiteSpace(p?.Nickname))
                .Select(p => $"{p.PlayerId}={p.Nickname}^{SanitizeCharacterIndexOrNone(p.CharacterIndex)}");
            return $"{_currentOwnerPlayerId};{string.Join("|", entries)}";
        }

        private void ApplyRosterPayload(string payload)
        {
            _roomParticipantsByPlayerId.Clear();
            _selectedCharacterIndexByPlayerId.Clear();

            if (string.IsNullOrWhiteSpace(payload))
            {
                _currentOwnerPlayerId = -1;
                SyncCurrentRoomOwnerFromRoster();
                return;
            }

            var separatorIndex = payload.IndexOf(';');
            if (separatorIndex >= 0)
            {
                var ownerToken = payload.Substring(0, separatorIndex);
                if (!int.TryParse(ownerToken, out _currentOwnerPlayerId))
                {
                    _currentOwnerPlayerId = -1;
                }
                payload = payload.Substring(separatorIndex + 1);
            }
            else
            {
                _currentOwnerPlayerId = -1;
            }

            var entries = payload.Split('|');
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                var idSeparator = entry.IndexOf('=');
                if (idSeparator <= 0 || idSeparator >= entry.Length - 1)
                {
                    continue;
                }

                if (!int.TryParse(entry.Substring(0, idSeparator), out var playerId))
                {
                    continue;
                }

                var rawValue = entry.Substring(idSeparator + 1);
                var characterIndex = -1;
                var nicknameToken = rawValue;
                var charSeparator = rawValue.IndexOf('^');
                if (charSeparator >= 0)
                {
                    nicknameToken = rawValue.Substring(0, charSeparator);
                    var charToken = rawValue.Substring(charSeparator + 1);
                    if (int.TryParse(charToken, out var parsed))
                    {
                        characterIndex = parsed;
                    }
                }

                var nickname = SanitizeNameToken(nicknameToken);
                if (string.IsNullOrEmpty(nickname))
                {
                    continue;
                }

                characterIndex = SanitizeCharacterIndexOrNone(characterIndex);
                _selectedCharacterIndexByPlayerId[playerId] = characterIndex;

                _roomParticipantsByPlayerId[playerId] = new ParticipantPresence
                {
                    PlayerId = playerId,
                    Nickname = nickname,
                    CharacterIndex = characterIndex
                };
            }

            SyncCurrentRoomOwnerFromRoster();
        }

        private void BroadcastPlayerRoster()
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer)
            {
                return;
            }

            var payload = BuildRosterPayload();
            var bytes = Encoding.UTF8.GetBytes(payload);

            foreach (var player in _runner.ActivePlayers)
            {
                try
                {
                    _runner.SendReliableDataToPlayer(player, PlayerRosterReliableKey, bytes);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Lobby] Failed to send roster to {player}: {e.Message}");
                }
            }
        }

        private void SyncCurrentRoomOwnerFromRoster()
        {
            if (_currentOwnerPlayerId > 0 &&
                _roomParticipantsByPlayerId.TryGetValue(_currentOwnerPlayerId, out var ownerPresence) &&
                !string.IsNullOrWhiteSpace(ownerPresence?.Nickname))
            {
                _currentRoomOwner = ownerPresence.Nickname;
                return;
            }

            if (_runner != null && _runner.IsRunning && _runner.IsServer && _runner.LocalPlayer.IsRealPlayer)
            {
                _currentOwnerPlayerId = _runner.LocalPlayer.PlayerId;
                _currentRoomOwner = string.IsNullOrWhiteSpace(_nickname) ? "-" : _nickname;
            }
            else if (string.IsNullOrWhiteSpace(_currentRoomOwner))
            {
                _currentRoomOwner = "-";
            }
        }

        private void UpdateOwnerSessionProperty()
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer || !_runner.SessionInfo.IsValid)
            {
                return;
            }

            try
            {
                _runner.SessionInfo.UpdateCustomProperties(new Dictionary<string, SessionProperty>
                {
                    { OwnerKey, _currentRoomOwner }
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lobby] Failed to update owner property: {e.Message}");
            }
        }

        private void RecalculateOwnerAfterLeave()
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsServer)
            {
                return;
            }

            if (_currentOwnerPlayerId > 0 && _roomParticipantsByPlayerId.ContainsKey(_currentOwnerPlayerId))
            {
                SyncCurrentRoomOwnerFromRoster();
                return;
            }

            var nextOwner = _runner.ActivePlayers
                .Where(p => p.IsRealPlayer)
                .OrderBy(p => p.PlayerId)
                .FirstOrDefault();

            if (nextOwner.IsRealPlayer)
            {
                _currentOwnerPlayerId = nextOwner.PlayerId;
                if (_currentOwnerPlayerId == _runner.LocalPlayer.PlayerId)
                {
                    _currentRoomOwner = _nickname;
                    RegisterParticipant(nextOwner, _nickname);
                }
                else
                {
                    SyncCurrentRoomOwnerFromRoster();
                }
            }
            else
            {
                _currentOwnerPlayerId = -1;
                _currentRoomOwner = "-";
            }

            UpdateOwnerSessionProperty();
        }

        private void SetLobbyStatus(string message)
        {
            if (lobbyStatusText != null)
            {
                lobbyStatusText.text = message;
            }
        }

        private void SetCreateValidation(string message)
        {
            if (createValidationText != null)
            {
                createValidationText.text = message;
            }
        }

        private void SetNicknameValidation(string message)
        {
            if (nicknameValidationText != null)
            {
                nicknameValidationText.text = message;
            }
        }

        private bool IsNicknameAlreadyUsed(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
            {
                return false;
            }

            return _roomSnapshots.Any(r =>
                !string.IsNullOrWhiteSpace(r.OwnerNickname) &&
                string.Equals(r.OwnerNickname, nickname, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsRoomNameAlreadyUsed(string roomName)
        {
            if (string.IsNullOrWhiteSpace(roomName))
            {
                return false;
            }

            return _roomSnapshots.Any(r =>
                !string.IsNullOrWhiteSpace(r.Name) &&
                string.Equals(r.Name, roomName, StringComparison.OrdinalIgnoreCase));
        }

        private async Task WaitForInitialSessionListAsync()
        {
            if (_lastSessionListUpdatedAtUtc != DateTime.MinValue)
            {
                return;
            }

            var timeoutAt = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < timeoutAt)
            {
                if (_lastSessionListUpdatedAtUtc != DateTime.MinValue)
                {
                    break;
                }

                await Task.Delay(50);
            }
        }

        private void NormalizeCanvasRoot()
        {
            if (transform is not RectTransform root)
            {
                return;
            }

            if (root.localScale == Vector3.zero)
            {
                root.localScale = Vector3.one;
            }

            if (root.anchorMin == Vector2.zero && root.anchorMax == Vector2.zero && root.sizeDelta == Vector2.zero)
            {
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
                root.pivot = new Vector2(0.5f, 0.5f);
            }
        }

        private void NormalizeRoomListBindings()
        {
            if (roomListContent == null || roomItemTemplate == null)
            {
                return;
            }

            if (roomItemTemplate.transform.parent != roomListContent)
            {
                roomItemTemplate.transform.SetParent(roomListContent, false);
            }
        }

        private void AutoBindLobbyRefsIfMissing()
        {
            if (lobbyPanel == null) lobbyPanel = FindChildByNames("LobbyPanel");
            if (roomPanel == null) roomPanel = FindChildByNames("RoomPanel");
            if (nicknamePanel == null) nicknamePanel = FindChildByNames("NicknamePanel");
            if (createRoomModal == null) createRoomModal = FindChildByNames("CreateRoomModal");
            if (passwordModal == null) passwordModal = FindChildByNames("PasswordModal");

            if (roomListContent == null)
            {
                var listContainer = FindChildByNames("RoomListContainer");
                if (listContainer != null)
                {
                    roomListContent = listContainer.transform;
                }
            }

            if (roomItemTemplate == null)
            {
                roomItemTemplate = FindChildByNames("RoomItemTemplate");
            }

            if (ghostCharacterRoot == null) ghostCharacterRoot = FindChildByNames("GhostCharacter");
            if (glassesCharacterRoot == null) glassesCharacterRoot = FindChildByNames("GlassesCharacter");
            if (greenCharacterRoot == null) greenCharacterRoot = FindChildByNames("GreenCharacter");
            if (ssatyCharacterRoot == null) ssatyCharacterRoot = FindChildByNames("SsatyCharacter");
            if (characterRuntimeRoot == null) characterRuntimeRoot = FindChildByNames("LobbyCharacterRuntimeRoot")?.transform;
            if (characterSelectionPanel == null) characterSelectionPanel = FindChildByNames("CharacterSelectionPanel");
            if (selectGhostCharacterButton == null) selectGhostCharacterButton = FindChildByNames("GhostCharacterUIButton", "GhostCharacterButton", "SelectGhostButton", "GhostSelectButton")?.GetComponent<Button>();
            if (selectGlassesCharacterButton == null) selectGlassesCharacterButton = FindChildByNames("GlassesCharacterUIButton", "GlassesCharacterButton", "SelectGlassesButton", "GlassesSelectButton")?.GetComponent<Button>();
            if (selectGreenCharacterButton == null) selectGreenCharacterButton = FindChildByNames("GreenCharacterUIButton", "GreenCharacterButton", "SelectGreenButton", "GreenSelectButton")?.GetComponent<Button>();
            if (selectSsatyCharacterButton == null) selectSsatyCharacterButton = FindChildByNames("SsatyCharacterUIButton", "SsatyCharacterButton", "SelectSsatyButton", "SsatySelectButton")?.GetComponent<Button>();
        }

        private void EnsureCharacterSelectionUi()
        {
            if (IsMissingReference(characterSelectionPanel)) characterSelectionPanel = null;
            if (IsMissingReference(selectGhostCharacterButton)) selectGhostCharacterButton = null;
            if (IsMissingReference(selectGlassesCharacterButton)) selectGlassesCharacterButton = null;
            if (IsMissingReference(selectGreenCharacterButton)) selectGreenCharacterButton = null;
            if (IsMissingReference(selectSsatyCharacterButton)) selectSsatyCharacterButton = null;

            if (characterSelectionPanel == null) characterSelectionPanel = FindChildByNames("CharacterSelectionPanel");
            if (selectGhostCharacterButton == null) selectGhostCharacterButton = FindChildByNames("GhostCharacterUIButton", "GhostCharacterButton", "SelectGhostButton", "GhostSelectButton")?.GetComponent<Button>();
            if (selectGlassesCharacterButton == null) selectGlassesCharacterButton = FindChildByNames("GlassesCharacterUIButton", "GlassesCharacterButton", "SelectGlassesButton", "GlassesSelectButton")?.GetComponent<Button>();
            if (selectGreenCharacterButton == null) selectGreenCharacterButton = FindChildByNames("GreenCharacterUIButton", "GreenCharacterButton", "SelectGreenButton", "GreenSelectButton")?.GetComponent<Button>();
            if (selectSsatyCharacterButton == null) selectSsatyCharacterButton = FindChildByNames("SsatyCharacterUIButton", "SsatyCharacterButton", "SelectSsatyButton", "SsatySelectButton")?.GetComponent<Button>();

            if (characterSelectionPanel == null ||
                selectGhostCharacterButton == null ||
                selectGlassesCharacterButton == null ||
                selectGreenCharacterButton == null ||
                selectSsatyCharacterButton == null)
            {
                Debug.LogWarning("[Lobby] Character selection UI is missing in hierarchy. Please add CharacterSelectionPanel with 4 select buttons.");
            }
        }

        private void RefreshCharacterSelectionUiState()
        {
            EnsureCharacterSelectionUi();
            TrySetGameObjectActive(characterSelectionPanel, roomPanel != null && roomPanel.activeSelf);

            var canSelect = roomPanel != null &&
                            roomPanel.activeSelf &&
                            _runner != null &&
                            _runner.IsRunning &&
                            _runner.LocalPlayer.IsRealPlayer;
            var localPlayerId = canSelect ? _runner.LocalPlayer.PlayerId : -1;
            var selected = canSelect && _selectedCharacterIndexByPlayerId.TryGetValue(localPlayerId, out var current)
                ? SanitizeCharacterIndexOrNone(current)
                : -1;

            if (selectGhostCharacterButton != null)
            {
                TrySetButtonInteractable(selectGhostCharacterButton, canSelect && selected != (int)CharacterKind.Ghost);
            }

            if (selectGlassesCharacterButton != null)
            {
                TrySetButtonInteractable(selectGlassesCharacterButton, canSelect && selected != (int)CharacterKind.Glasses);
            }

            if (selectGreenCharacterButton != null)
            {
                TrySetButtonInteractable(selectGreenCharacterButton, canSelect && selected != (int)CharacterKind.Green);
            }

            if (selectSsatyCharacterButton != null)
            {
                TrySetButtonInteractable(selectSsatyCharacterButton, canSelect && selected != (int)CharacterKind.Ssaty);
            }
        }

        private static bool IsMissingReference(UnityEngine.Object target)
        {
            return target == null;
        }

        private static void TrySetGameObjectActive(GameObject target, bool active)
        {
            if (target == null)
            {
                return;
            }

            try
            {
                target.SetActive(active);
            }
            catch (MissingReferenceException)
            {
            }
        }

        private static void TrySetButtonInteractable(Button target, bool interactable)
        {
            if (target == null)
            {
                return;
            }

            try
            {
                target.interactable = interactable;
            }
            catch (MissingReferenceException)
            {
            }
        }

        private GameObject FindChildByNames(params string[] names)
        {
            if (names == null || names.Length == 0)
            {
                return null;
            }

            var transforms = GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t == null)
                {
                    continue;
                }

                for (var i = 0; i < names.Length; i++)
                {
                    var name = names[i];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    if (string.Equals(t.name, name, StringComparison.Ordinal))
                    {
                        return t.gameObject;
                    }
                }
            }

            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var global = GameObject.Find(name);
                if (global != null)
                {
                    return global;
                }
            }

            return null;
        }

        private NetworkSceneManagerDefault GetOrAddSceneManager()
        {
            return GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        private static RoomSnapshot BuildSnapshot(SessionInfo session)
        {
            var started = ReadBool(session, StartedKey);
            var maxPlayers = ReadMaxPlayers(session);
            var isOpen = ReadIsOpen(session);
            var snapshot = new RoomSnapshot
            {
                Name = session.Name,
                PlayerCount = session.PlayerCount,
                IsPrivate = ReadBool(session, PrivateKey),
                Password = ReadString(session, PasswordKey),
                OwnerNickname = ReadString(session, OwnerKey),
                MaxPlayers = maxPlayers <= 0 ? MaxPlayers : maxPlayers,
                IsOpen = isOpen
            };

            if (started)
            {
                snapshot.Name = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(snapshot.OwnerNickname))
            {
                snapshot.OwnerNickname = "-";
            }

            return snapshot;
        }

        private static bool ReadBool(SessionInfo session, string key)
        {
            if (session.Properties == null || !session.Properties.TryGetValue(key, out var value))
            {
                return false;
            }

            try
            {
                if (value.Isbool)
                {
                    return value;
                }

                if (value.IsInt)
                {
                    return (int)value != 0;
                }

                if (value.IsString && bool.TryParse((string)value, out var parsed))
                {
                    return parsed;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadString(SessionInfo session, string key)
        {
            if (session.Properties == null || !session.Properties.TryGetValue(key, out var value))
            {
                return string.Empty;
            }

            try
            {
                if (value.IsString)
                {
                    return value;
                }

                if (value.IsInt)
                {
                    return ((int)value).ToString();
                }

                if (value.Isbool)
                {
                    return ((bool)value).ToString();
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int ReadMaxPlayers(SessionInfo session)
        {
            try
            {
                return session.MaxPlayers;
            }
            catch
            {
                return MaxPlayers;
            }
        }

        private static bool ReadIsOpen(SessionInfo session)
        {
            try
            {
                return Convert.ToBoolean(session.IsOpen);
            }
            catch
            {
                return true;
            }
        }

        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
        {
            _isInLobby = true;
            _lastSessionListUpdatedAtUtc = DateTime.UtcNow;
            _roomSnapshots.Clear();
            foreach (var session in sessionList)
            {
                if (!session.IsValid || !session.IsVisible)
                {
                    continue;
                }

                var snapshot = BuildSnapshot(session);
                if (string.IsNullOrWhiteSpace(snapshot.Name))
                {
                    continue;
                }

                _roomSnapshots.Add(snapshot);
            }

            if (_isNicknameConfirmed && lobbyPanel.activeSelf)
            {
                RefreshRoomList();
            }

            SetLobbyStatus(string.Format(statusRoomsUpdatedFormat, _roomSnapshots.Count));
            LogRoomSnapshotSummary();
        }

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner != _runner)
            {
                return;
            }

            if (runner.IsServer)
            {
                TryRegisterParticipantFromToken(runner, player);

                if (player == runner.LocalPlayer && !_roomParticipantsByPlayerId.ContainsKey(player.PlayerId))
                {
                    RegisterParticipant(player, _nickname);
                }

                if (_currentOwnerPlayerId <= 0 && runner.LocalPlayer.IsRealPlayer)
                {
                    _currentOwnerPlayerId = runner.LocalPlayer.PlayerId;
                    _currentRoomOwner = _nickname;
                    UpdateOwnerSessionProperty();
                }

                BroadcastPlayerRoster();
            }
            else if (player == runner.LocalPlayer)
            {
                RegisterParticipant(player, _nickname);
            }

            if (runner.IsServer && IsActiveGameplayScene())
            {
                TrySpawnGameplayNetworkCharacter(player);
            }

            if (roomPanel.activeSelf)
            {
                UpdateRoomPanel();
            }
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (runner != _runner)
            {
                return;
            }

            if (runner.IsServer && _spawnedGameplayNetworkCharacters.TryGetValue(player.PlayerId, out var spawned) && spawned != null)
            {
                try
                {
                    runner.Despawn(spawned);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Lobby] Failed to despawn player character. player={player.PlayerId}, error={e.Message}");
                }
            }
            _spawnedGameplayNetworkCharacters.Remove(player.PlayerId);

            if (player.IsRealPlayer)
            {
                _roomParticipantsByPlayerId.Remove(player.PlayerId);
                _selectedCharacterIndexByPlayerId.Remove(player.PlayerId);
            }

            if (runner.IsServer)
            {
                RecalculateOwnerAfterLeave();
                BroadcastPlayerRoster();
            }

            if (roomPanel.activeSelf)
            {
                UpdateRoomPanel();
            }
        }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (runner != _runner)
            {
                Debug.Log("[Lobby] Ignored shutdown callback from stale runner.");
                return;
            }

            _isInLobby = false;
            if (lobbyPanel.activeSelf)
            {
                SetLobbyStatus(string.Format(statusDisconnectedFormat, shutdownReason));
            }
            Debug.Log($"[Lobby] Runner shutdown: reason={shutdownReason}");

            if (_isShuttingDownRunner || _isProcessing)
            {
                Debug.Log("[Lobby] Shutdown in progress, skip recovery.");
                return;
            }

        }

        private void LogRoomSnapshotSummary()
        {
            var summary = _roomSnapshots.Count == 0
                ? "-"
                : string.Join(", ", _roomSnapshots.Select(r => $"{r.Name}({r.PlayerCount}/{MaxPlayers})"));
            Debug.Log($"[Lobby] Session list updated: count={_roomSnapshots.Count}, rooms={summary}, updatedAtUtc={_lastSessionListUpdatedAtUtc:O}");
        }

        private static string SanitizeNameToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var chars = value
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '.' || c == '_' || c == '-')
                .ToArray();
            return new string(chars).Trim();
        }

        private static bool IsWithinNameLengthLimit(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var maxLength = ContainsHangul(value) ? 8 : 16;
            return value.Length <= maxLength;
        }

        private static bool ContainsHangul(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if ((c >= '\u1100' && c <= '\u11FF') ||
                    (c >= '\u3130' && c <= '\u318F') ||
                    (c >= '\uAC00' && c <= '\uD7AF'))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnforceNameInputLimit(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            var current = input.text ?? string.Empty;
            var normalized = NormalizeNameInput(current);
            if (string.Equals(current, normalized, StringComparison.Ordinal))
            {
                return;
            }

            input.SetTextWithoutNotify(normalized);
            input.caretPosition = normalized.Length;
            input.selectionAnchorPosition = normalized.Length;
            input.selectionFocusPosition = normalized.Length;
        }

        private static string NormalizeNameInput(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var filteredChars = value
                .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '.' || c == '_' || c == '-')
                .ToArray();
            var filtered = new string(filteredChars);

            var maxLength = ContainsHangul(filtered) ? 8 : 16;
            if (filtered.Length <= maxLength)
            {
                return filtered;
            }

            return filtered.Substring(0, maxLength);
        }

        private static char ValidateNumericPasswordChar(string text, int charIndex, char addedChar)
        {
            return IsNumericPasswordChar(addedChar) ? addedChar : '\0';
        }

        private static bool IsNumericPassword(string value)
        {
            if (value == null)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                if (!IsNumericPasswordChar(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNumericPasswordChar(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static void ConfigureNumericPasswordInput(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            input.keyboardType = TouchScreenKeyboardType.NumberPad;
            input.characterValidation = TMP_InputField.CharacterValidation.Integer;
            input.lineType = TMP_InputField.LineType.SingleLine;
        }

        private static void EnforceNumericPasswordInput(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            var current = input.text ?? string.Empty;
            var filtered = FilterNumericPassword(current);
            if (string.Equals(current, filtered, StringComparison.Ordinal))
            {
                return;
            }

            input.SetTextWithoutNotify(filtered);
            input.caretPosition = filtered.Length;
            input.selectionAnchorPosition = filtered.Length;
            input.selectionFocusPosition = filtered.Length;
        }

        private static string FilterNumericPassword(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var chars = value.Where(IsNumericPasswordChar).ToArray();
            return new string(chars);
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            if (runner != _runner)
            {
                Debug.Log("[Lobby] Ignored disconnect callback from stale runner.");
                return;
            }

            _isInLobby = false;

            if (_isShuttingDownRunner || _isProcessing)
            {
                Debug.Log("[Lobby] Shutdown in progress, skip disconnect recovery.");
                return;
            }

        }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (runner != _runner || !runner.IsServer)
            {
                return;
            }

            var nickname = DecodeConnectionToken(token);
            if (!string.IsNullOrEmpty(nickname))
            {
                Debug.Log($"[Lobby] Connect request from {request.RemoteAddress}: {PlayerRosterLabel}={nickname}");
            }
        }
        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        void INetworkRunnerCallbacks.OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        async void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
        {
            if (runner != _runner)
            {
                return;
            }

            if (_isProcessing)
            {
                return;
            }

            _isProcessing = true;
            try
            {
                Debug.Log("[Lobby] Host migration started.");

                await ShutdownRunnerAsync();

                if (!TryCreateRunner(out var newRunner))
                {
                    SetLobbyStatus(statusHostMigrationInitFailed);
                    return;
                }

                _runner = newRunner;
                var appSettings = GetOrCreatePhotonSettings();
                var result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = hostMigrationToken.GameMode,
                    SessionName = _currentRoomName,
                    SceneManager = GetOrAddSceneManager(),
                    CustomLobbyName = SharedLobbyName,
                    CustomPhotonAppSettings = appSettings,
                    HostMigrationToken = hostMigrationToken
                });

                if (!result.Ok)
                {
                    SetLobbyStatus(string.Format(statusHostMigrationFailedFormat, result.ShutdownReason));
                    Debug.LogWarning($"[Lobby] Host migration failed: {result.ShutdownReason}");
                    return;
                }

                if (_runner.IsServer && _runner.LocalPlayer.IsRealPlayer)
                {
                    RegisterParticipant(_runner.LocalPlayer, _nickname);
                    _currentOwnerPlayerId = _runner.LocalPlayer.PlayerId;
                    _currentRoomOwner = _nickname;
                    UpdateOwnerSessionProperty();
                    BroadcastPlayerRoster();
                }

                Debug.Log("[Lobby] Host migration completed.");
                ShowRoomPanel();
                UpdateRoomPanel();
            }
            finally
            {
                _isProcessing = false;
            }
        }
        void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
        {
            if (runner != _runner)
            {
                return;
            }

            string payload;
            try
            {
                payload = data.Array == null
                    ? string.Empty
                    : Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
            }
            catch
            {
                payload = string.Empty;
            }

            if (key == PlayerRosterReliableKey)
            {
                ApplyRosterPayload(payload);

                if (roomPanel.activeSelf)
                {
                    UpdateRoomPanel();
                }
                return;
            }

            if (key == CharacterSelectionReliableKey && _runner != null && _runner.IsRunning && _runner.IsServer)
            {
                if (player.IsRealPlayer && int.TryParse(payload, out var selected))
                {
                    var normalized = SanitizeCharacterIndexOrNone(selected);
                    if (normalized >= 0)
                    {
                        _selectedCharacterIndexByPlayerId[player.PlayerId] = normalized;
                        if (_roomParticipantsByPlayerId.TryGetValue(player.PlayerId, out var presence) && presence != null)
                        {
                            presence.CharacterIndex = normalized;
                        }

                        BroadcastPlayerRoster();
                    }
                }

                if (roomPanel.activeSelf)
                {
                    UpdateRoomPanel();
                }
            }
        }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner)
        {
            if (runner != _runner)
            {
                return;
            }

            _spawnedGameplayNetworkCharacters.Clear();
        }

        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner)
        {
            if (runner != _runner)
            {
                return;
            }

            if (IsActiveGameplayScene())
            {
                if (nicknamePanel != null) nicknamePanel.SetActive(false);
                if (lobbyPanel != null) lobbyPanel.SetActive(false);
                if (roomPanel != null) roomPanel.SetActive(false);
                if (createRoomModal != null) createRoomModal.SetActive(false);
                if (passwordModal != null) passwordModal.SetActive(false);

                if (runner.IsServer)
                {
                    TrySpawnGameplayNetworkCharactersForAllPlayers();
                }
            }
        }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    }
}



