using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    public sealed class LobbyCanvasUIController : MonoBehaviour, INetworkRunnerCallbacks
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
        [SerializeField] private TMP_Text roomMembersText;
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
        [SerializeField] private string validationEnterRoomName = "방 이름을 입력해주세요.";
        [SerializeField] private string validationRoomNameInUse = "이미 사용 중인 방 이름입니다.";
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
        [SerializeField] private string roomMembersFormat = "방장: {0}\n\n인원: {1}/{2}";
        [SerializeField] private string roomParticipantsFormat = "참여자: {0}";
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

        private void Start()
        {
            RuntimeLogOverlay.EnsureInstance();
            AutoBindLobbyRefsIfMissing();
            NormalizeCanvasRoot();
            NormalizeRoomListBindings();
            BindEvents();
            ShowNicknamePanel();
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

            createPrivateToggle.onValueChanged.AddListener(OnPrivateToggleChanged);
            createConfirmButton.onClick.AddListener(OnCreateRoomConfirmed);
            createCancelButton.onClick.AddListener(() => createRoomModal.SetActive(false));

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

            try
            {
                _runner.LoadScene(gameplaySceneName, LoadSceneMode.Single, LocalPhysicsMode.None, true);
                Debug.Log($"[Lobby] Starting game scene: {gameplaySceneName}");
            }
            catch (Exception e)
            {
                SetLobbyStatus(string.Format(statusStartGameFailedFormat, e.Message));
                Debug.LogWarning($"[Lobby] Start game failed: {e.Message}");
            }
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
            var baseText = string.Format(roomMembersFormat, _currentRoomOwner, currentPlayers, MaxPlayers);
            var participants = BuildParticipantDisplayText();
            roomMembersText.text = string.IsNullOrEmpty(participants)
                ? baseText
                : $"{baseText}\n\n{string.Format(roomParticipantsFormat, participants)}";
            if (startGameButton != null)
            {
                var canStart = _runner != null && _runner.IsRunning && _runner.IsServer && currentPlayers > 1;
                startGameButton.gameObject.SetActive(true);
                startGameButton.interactable = canStart;
            }
        }

        private void ShowNicknamePanel()
        {
            nicknamePanel.SetActive(true);
            lobbyPanel.SetActive(false);
            roomPanel.SetActive(false);
            createRoomModal.SetActive(false);
            passwordModal.SetActive(false);
            SetNicknameValidation(string.Empty);
        }

        private void ShowLobbyPanel()
        {
            nicknamePanel.SetActive(false);
            lobbyPanel.SetActive(true);
            roomPanel.SetActive(false);
            passwordModal.SetActive(false);
            lobbyHeaderText.text = string.Format(nicknameHeaderFormat, _nickname);
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

        private string BuildParticipantDisplayText()
        {
            if (_roomParticipantsByPlayerId.Count == 0)
            {
                return string.Empty;
            }

            var names = _roomParticipantsByPlayerId.Values
                .Where(v => !string.IsNullOrWhiteSpace(v?.Nickname))
                .OrderBy(v => v.PlayerId)
                .Select(v => v.Nickname)
                .ToList();
            return names.Count == 0 ? string.Empty : string.Join(", ", names);
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

            _roomParticipantsByPlayerId[player.PlayerId] = new ParticipantPresence
            {
                PlayerId = player.PlayerId,
                Nickname = safeName
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
                .Select(p => $"{p.PlayerId}={p.Nickname}");
            return $"{_currentOwnerPlayerId};{string.Join("|", entries)}";
        }

        private void ApplyRosterPayload(string payload)
        {
            _roomParticipantsByPlayerId.Clear();

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

                var nickname = SanitizeNameToken(entry.Substring(idSeparator + 1));
                if (string.IsNullOrEmpty(nickname))
                {
                    continue;
                }

                _roomParticipantsByPlayerId[playerId] = new ParticipantPresence
                {
                    PlayerId = playerId,
                    Nickname = nickname
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
            if (lobbyPanel == null) lobbyPanel = FindChildByNames("로비패널", "LobbyPanel");
            if (roomPanel == null) roomPanel = FindChildByNames("방패널", "RoomPanel");
            if (nicknamePanel == null) nicknamePanel = FindChildByNames("닉네임패널", "NicknamePanel");
            if (createRoomModal == null) createRoomModal = FindChildByNames("방생성모달", "CreateRoomModal");
            if (passwordModal == null) passwordModal = FindChildByNames("비밀번호모달", "PasswordModal");

            if (roomListContent == null)
            {
                var listContainer = FindChildByNames("방목록컨테이너", "RoomListContainer");
                if (listContainer != null)
                {
                    roomListContent = listContainer.transform;
                }
            }

            if (roomItemTemplate == null)
            {
                roomItemTemplate = FindChildByNames("방항목템플릿", "RoomItemTemplate");
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

            if (player.IsRealPlayer)
            {
                _roomParticipantsByPlayerId.Remove(player.PlayerId);
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
            if (runner != _runner || key != PlayerRosterReliableKey)
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

            ApplyRosterPayload(payload);

            if (roomPanel.activeSelf)
            {
                UpdateRoomPanel();
            }
        }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner runner) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    }
}

