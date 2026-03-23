using System;
using System.Collections;
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
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace SSAFYPlayTime
{
    public sealed partial class LobbyCanvasUIController : MonoBehaviour, INetworkRunnerCallbacks
    {
        [System.Serializable]
        public class SceneRoomSlot
        {
            public GameObject questionMark;
            public GameObject[] characterModels;
        }

        [Header("Scene Room Slots")]
        [SerializeField] private List<SceneRoomSlot> sceneRoomSlots;

        [Header("Scene Direct Slots (Podium Game End)")]
        [SerializeField] private List<SceneRoomSlot> sceneDirectSlots;

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
            public string ClientId;
            public string Nickname;
            public int CharacterIndex;
            public bool IsReady;
        }

        private readonly struct ConnectionTokenInfo
        {
            public ConnectionTokenInfo(string clientId, string nickname)
            {
                ClientId = clientId;
                Nickname = nickname;
            }

            public string ClientId { get; }
            public string Nickname { get; }
        }

        private enum CharacterKind
        {
            Ssaty = 0,
            AlG = 1,
            Fit = 2,
            Wise = 3,
            Random = 4
        }

        private const string PrivateKey = "isPrivate";
        private const string PasswordKey = "password";
        private const string OwnerKey = "owner";
        private const string StartedKey = "started";
        private const string PlayerRosterLabel = "participants";
        private const int MaxPlayers = 4;
        private const string FallbackAppVersion = "ssafy-playtime-v1";
        private const string SharedLobbyName = "ssafy-main-lobby";
        private const char ConnectionTokenSeparator = '|';
        // Fusion ReliableData 채널 식별자. 두 키 모두 "SSAF"·"PLAY" ASCII 코드를 공유하며
        // 세 번째 인자(1 또는 2)로 채널을 구분한다.
        // PlayerRosterReliableKey  : 방장→모든 클라이언트 참가자 목록 동기화
        // CharacterSelectionReliableKey : 클라이언트→방장 캐릭터 선택 전달
        private static readonly ReliableKey PlayerRosterReliableKey =
            ReliableKey.FromInts(unchecked((int)0x53534146), unchecked((int)0x504C4159), 1, 0);
        private static readonly ReliableKey CharacterSelectionReliableKey =
            ReliableKey.FromInts(unchecked((int)0x53534146), unchecked((int)0x504C4159), 2, 0);
        // ReadyStateReliableKey : 클라이언트→방장 준비 상태 전달
        private static readonly ReliableKey ReadyStateReliableKey =
            ReliableKey.FromInts(unchecked((int)0x53534146), unchecked((int)0x504C4159), 3, 0);
        private const int PlayerSlotCount = 4;
        private const int CharacterOptionCount = 5;

        [Header("Debug")]
        [Tooltip("체크 시 1인 방에서도 게임 시작 가능. PlayMode Inspector에서 바로 조작 가능.")]
        [SerializeField] private bool allowSoloStart;
        [Header("Panels")]
        [SerializeField] private GameObject nicknamePanel;
        [SerializeField] private GameObject lobbyPanel;
        [SerializeField] private GameObject roomPanel;  
        [SerializeField] private GameObject createRoomModal;
        [SerializeField] private GameObject passwordModal;
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject gameSettingModal;
        [SerializeField] private GameObject editNicknameModal;

        [Header("Game End Panel")]
        [Tooltip("게임 종료 후 순위를 표시하는 패널 (LauncherScene 캔버스 하위)")]
        [SerializeField] private GameObject gameEndPanel;
        [Tooltip("방장 비정상 종료 시 표시할 안내 모달 (LobbyCanvas/HostExitNoticeModal)")]
        [SerializeField] private GameObject hostExitNoticeModal;
        [Tooltip("HostExitNoticeModal의 확인 버튼")]
        [SerializeField] private Button hostExitNoticeCloseButton;
        [Tooltip("순위 항목 RankingItemUI들이 배치된 부모 Transform")]
        [SerializeField] private Transform rankingContainer;
        [Tooltip("같은 방으로 버튼")]
        [SerializeField] private Button returnToRoomButton;
        [Tooltip("처음으로 버튼")]
        [SerializeField] private Button returnToLobbyButton;

        [Header("Game End 3D Podium")]
        [SerializeField] private Transform[] podiumSlots; // 1, 2, 3, 4등이 설 자리
        private readonly List<GameObject> _spawnedGameEndCharacters = new(); // 소환된 캐릭터 기억해둘 리스트
        [Header("3D Podium")]
        [SerializeField] private GameObject podiumParent;

        [Header("Nickname")]
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button nicknameConfirmButton;
        [SerializeField] private TMP_Text mainPanelNicknameText;
        [SerializeField] private TMP_InputField editNicknameInput;

        [Header("Lobby")]
        [SerializeField] private TMP_Text lobbyHeaderText;
        [SerializeField] private Button createRoomOpenButton;
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
        [SerializeField] private GameObject playerOneReadyBadge;
        [SerializeField] private GameObject playerTwoReadyBadge;
        [SerializeField] private GameObject playerThreeReadyBadge;
        [SerializeField] private GameObject playerFourReadyBadge;
        [FormerlySerializedAs("stattyCharacterRoot")]
        [SerializeField] private GameObject ssatyCharacterRoot;
        [SerializeField] private GameObject alGCharacterRoot;
        [SerializeField] private GameObject fitCharacterRoot;
        [SerializeField] private GameObject wiseCharacterRoot;
        [SerializeField] private GameObject randomCharacterRoot;
        [SerializeField] private GameObject characterSelectionPanel;
        [FormerlySerializedAs("selectStattyCharacterButton")]
        [SerializeField] private Button selectSsatyCharacterButton;
        [SerializeField] private Button selectAlGCharacterButton;
        [SerializeField] private Button selectFitCharacterButton;
        [SerializeField] private Button selectWiseCharacterButton;
        [SerializeField] private Button selectRandomCharacterButton;

        [Header("Character Selection Animators")]
        [SerializeField] private Animator selectSsatyAnimator;
        [SerializeField] private Animator selectAlGAnimator;
        [SerializeField] private Animator selectFitAnimator;
        [SerializeField] private Animator selectWiseAnimator;
        [SerializeField] private Animator selectRandomAnimator;

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
        [Header("Slot Particle Rotation Y Overrides (degrees, Slot1~4)")]
        [SerializeField] private float[] slotParticleRotationYOffsets = { 180f, 180f, 180f, 180f };

        [Header("Character Kind Overrides (Ssaty=0, AlG=1, Fit=2, Wise=3, Random=4)")]
        [SerializeField] private Vector3[] characterKindPositionOffsets =
        {
            Vector3.zero,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero
        };
        [SerializeField] private Vector3[] characterKindRotationOffsets =
        {
            Vector3.zero,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero,
            Vector3.zero
        };
        [SerializeField] private Vector3[] characterKindScaleMultipliers =
        {
            Vector3.one,
            Vector3.one,
            Vector3.one,
            Vector3.one,
            Vector3.one
        };
        [SerializeField] private bool testShowOneCharacterPerSlot = true;
        [SerializeField] private Button leaveRoomButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonText;
        [SerializeField] private string gameplaySceneName = string.Empty;
        [SerializeField] private string launcherSceneName = "LauncherScene";
        [SerializeField] private AudioClip launcherBackgroundMusicClip;
        [Header("In-Game Panel (GameScene)")]
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private Button leaveGameButton;
        [SerializeField] private GameHUD gameHUDPrefab;

        [Header("Character Preview (optional)")]
        [SerializeField] private CharacterPreviewController characterPreview;

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
        private string _localClientId = string.Empty;
        private string _nickname = string.Empty;
        private string _currentRoomName = string.Empty;
        private bool _currentRoomIsPrivate;
        private string _currentRoomPassword = string.Empty;
        private string _currentRoomOwner = "-";
        private int _currentOwnerPlayerId = -1;
        private string _pendingPrivateRoomName = string.Empty;
        private string _pendingPrivateRoomPassword = string.Empty;
        private bool _isNicknameConfirmed;
        private bool _isProcessing;
        private bool _isInLobby;
        private bool _isShuttingDownRunner;
        // 처리 중(_isProcessing)에 수신된 migration token을 보관한다.
        // finally에서 _isProcessing = false 직후 재처리한다.
        private HostMigrationToken _pendingMigrationToken;
        private DateTime _lastSessionListUpdatedAtUtc = DateTime.MinValue;
        // async 메서드 간 NetworkRunner 생성/종료 순서를 보장하는 뮤텍스.
        // SemaphoreSlim(1, 1) : 최대 1개의 코루틴만 동시에 러너를 조작할 수 있도록 제한한다.
        private readonly SemaphoreSlim _runnerLock = new(1, 1);
        private readonly Dictionary<int, ParticipantPresence> _roomParticipantsByPlayerId = new();
        private readonly Transform[,] _slotCharacterRoots = new Transform[PlayerSlotCount, CharacterOptionCount];
        private readonly int[] _selectedCharacterIndexBySlot = { -1, -1, -1, -1 };
        private readonly int[] _playerIdBySlot = { -1, -1, -1, -1 };
        private readonly Dictionary<int, int> _selectedCharacterIndexByPlayerId = new();

        // 로컬 플레이어가 선택한 캐릭터 인덱스.
        // ShutdownRunnerAsync로 초기화되지 않으며 호스트 마이그레이션 후
        // 새 방장에게 캐릭터 선택을 재전송할 때 사용된다. 연쇄 마이그레이션에도 유지된다.
        private int _localSelectedCharacterIndex = -1;
        // 로컬 플레이어의 준비 상태. ShutdownRunnerAsync로 초기화되지 않으며
        // 호스트 마이그레이션 후 새 방장에게 준비 상태를 재전송할 때 사용된다.
        private bool _localIsReady;
        private readonly Dictionary<int, bool> _readyStateByPlayerId = new();
        private readonly Dictionary<Transform, Vector3> _characterBaseLocalScales = new();
        private readonly Dictionary<Transform, Quaternion> _characterBaseLocalRotations = new();
        private readonly Dictionary<Transform, Quaternion> _characterVisualChildBaseLocalRotations = new();
        private readonly Dictionary<Transform, float> _characterBaseBoundsHeights = new();
        private readonly Dictionary<Transform, int> _characterOptionIndexByTransform = new();
        private readonly Dictionary<Transform, float> _characterPrePlacedWorldY = new();
        private bool _characterSlotsInitialized;
        private bool _gameEndReturnTransitionStarted;
        // 게임 종료 후 순위 화면 표시 중 여부. IsActiveSceneNamed("GameEndScene") 대신 사용한다.
        private bool _isShowingGameEndPanel;
        // 게임 종료 씬 전환 직전에 세워두는 플래그. OnSceneLoadDone에서 ShowGameEndPanel 호출 트리거.
        private bool _pendingGameEndPanel;
        // 게임 종료 후 LauncherScene 진입 시 백그라운드에서 실행되는 방 자동 입장 Task.
        // 버튼 클릭 시 이 Task를 await해 완료 여부를 확인한다.
        private Task<bool> _gameEndAutoRoomJoinTask;
        // GameResultData.Clear() 전에 캡처한 로컬 플레이어 순위.
        // StartAutoRoomJoinFromGameEndAsync()에서 순위 기반 딜레이 계산에 사용한다.
        private int _localPlayerRankForAutoJoin;
        private AudioSource _launcherBackgroundMusicSource;
        private GameAudioSource _launcherBackgroundMusicCategory;

        // MonoBehaviour 초기화. 패널·이벤트·캐릭터 슬롯을 순서대로 준비하고 닉네임 입력 화면을 표시한다.
        // 모든 UI 참조는 Inspector에서 SerializeField로 직접 할당해야 한다.
        private void Start()
        {
            // LauncherScene 재로드 시 DontDestroyOnLoad 인스턴스가 이미 존재하면 새 인스턴스의 씬 참조를 넘겨주고 제거한다.
            var existing = FindObjectsByType<LobbyCanvasUIController>(FindObjectsSortMode.None);
            if (existing.Length > 1)
            {
                var persistent = existing.FirstOrDefault(x => x != this);
                if (persistent != null)
                {
                    persistent.podiumParent = this.podiumParent;
                    persistent.podiumSlots = this.podiumSlots;
                    persistent.sceneRoomSlots = this.sceneRoomSlots;
                    persistent.sceneDirectSlots = this.sceneDirectSlots;
                    persistent.characterRuntimeRoot = this.characterRuntimeRoot;
                    persistent.characterPlacementCamera = this.characterPlacementCamera;
                    persistent.ssatyCharacterRoot = this.ssatyCharacterRoot;
                    persistent.alGCharacterRoot = this.alGCharacterRoot;
                    persistent.fitCharacterRoot = this.fitCharacterRoot;
                    persistent.wiseCharacterRoot = this.wiseCharacterRoot;
                    persistent.randomCharacterRoot = this.randomCharacterRoot;
                    persistent.fallbackSpawnPoints = this.fallbackSpawnPoints;
                }
                Destroy(gameObject);
                return;
            }

            AutoBindReadyBadges();
            if (!ValidateInitialReferences())
            {
                enabled = false;
                return;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            EnsurePersistentAcrossScenes();
            EnsureLocalClientId();
            RuntimeLogOverlay.EnsureInstance();
            GameAudioSettingsService.EnsureInstance();
            EnsureCharacterSelectionUi();
            EnsureGameSettingModal();
            EnsureLauncherBackgroundMusic();
            NormalizeCanvasRoot();
            NormalizeRoomListBindings();
            BindEvents();
            InitializeCharacterSlotsIfNeeded();
            if (characterPreview != null)
            {
                characterPreview.Initialize(GetNameSlots());
            }
            
            if (podiumParent != null) podiumParent.SetActive(false);

            SceneManager.sceneLoaded += OnManagedSceneLoaded;
            ShowNicknamePanel();
        }

        private void AutoBindReadyBadges()
        {
            playerOneReadyBadge ??= FindReadyBadge(playerOneText);
            playerTwoReadyBadge ??= FindReadyBadge(playerTwoText);
            playerThreeReadyBadge ??= FindReadyBadge(playerThreeText);
            playerFourReadyBadge ??= FindReadyBadge(playerFourText);
        }

        private void EnsureGameSettingModal()
        {
            if (gameSettingModal == null)
            {
                return;
            }

            var modalController = gameSettingModal.GetComponent<LobbyAudioSettingsModal>();
            if (modalController == null)
            {
                modalController = gameSettingModal.AddComponent<LobbyAudioSettingsModal>();
            }

            modalController.InitializeIfNeeded();
        }

        private void EnsureLauncherBackgroundMusic()
        {
            if (_launcherBackgroundMusicSource == null)
            {
                var sourceRoot = new GameObject("LauncherBackgroundMusic");
                sourceRoot.transform.SetParent(transform, false);

                _launcherBackgroundMusicSource = sourceRoot.AddComponent<AudioSource>();
                _launcherBackgroundMusicSource.playOnAwake = false;
                _launcherBackgroundMusicSource.loop = true;
                _launcherBackgroundMusicSource.spatialBlend = 0f;
                _launcherBackgroundMusicSource.volume = 1f;

                _launcherBackgroundMusicCategory = sourceRoot.AddComponent<GameAudioSource>();
                _launcherBackgroundMusicCategory.SetCategory(GameAudioCategory.BackgroundSound);
                _launcherBackgroundMusicCategory.RefreshBaseVolumeFromCurrentSource();
            }

            _launcherBackgroundMusicSource.clip = launcherBackgroundMusicClip;
            RefreshLauncherBackgroundMusicState();
        }

        private void OnManagedSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RefreshLauncherBackgroundMusicState();
        }

        private void RefreshLauncherBackgroundMusicState()
        {
            if (_launcherBackgroundMusicSource == null)
            {
                return;
            }

            var isLauncherSceneActive = string.Equals(
                SceneManager.GetActiveScene().name,
                launcherSceneName,
                StringComparison.Ordinal);

            if (!isLauncherSceneActive || launcherBackgroundMusicClip == null)
            {
                if (_launcherBackgroundMusicSource.isPlaying)
                {
                    _launcherBackgroundMusicSource.Stop();
                }

                return;
            }

            if (_launcherBackgroundMusicSource.clip != launcherBackgroundMusicClip)
            {
                _launcherBackgroundMusicSource.clip = launcherBackgroundMusicClip;
            }

            if (!_launcherBackgroundMusicSource.isPlaying)
            {
                _launcherBackgroundMusicSource.Play();
            }
        }

        private static GameObject FindReadyBadge(TMP_Text slotText)
        {
            if (slotText == null)
            {
                return null;
            }

            var parent = slotText.transform;
            var child = parent.Find("ReadyBadge");
            return child != null ? child.gameObject : null;
        }

        // 매 프레임 호스트 F5 게임 시작 단축키 처리 및 캐릭터 배치·정렬을 갱신한다.
        private void Update()
        {
            CaptureNetworkInputState();

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
                characterPreview?.UpdateFrame();
            }
        }

        private bool ValidateInitialReferences()
        {
            var references = new Dictionary<string, UnityEngine.Object>
            {
                { nameof(nicknamePanel), nicknamePanel },
                { nameof(lobbyPanel), lobbyPanel },
                { nameof(roomPanel), roomPanel },
                { nameof(createRoomModal), createRoomModal },
                { nameof(passwordModal), passwordModal },
                { nameof(mainPanel), mainPanel },
                { nameof(nicknameInput), nicknameInput },
                { nameof(nicknameConfirmButton), nicknameConfirmButton },
                { nameof(mainPanelNicknameText), mainPanelNicknameText },
                { nameof(lobbyHeaderText), lobbyHeaderText },
                { nameof(createRoomOpenButton), createRoomOpenButton },
                { nameof(roomListContent), roomListContent },
                { nameof(roomItemTemplate), roomItemTemplate },
                { nameof(createRoomNameInput), createRoomNameInput },
                { nameof(createPrivateToggle), createPrivateToggle },
                { nameof(createPasswordInput), createPasswordInput },
                { nameof(createValidationText), createValidationText },
                { nameof(createConfirmButton), createConfirmButton },
                { nameof(createCancelButton), createCancelButton },
                { nameof(joinPasswordInput), joinPasswordInput },
                { nameof(passwordValidationText), passwordValidationText },
                { nameof(passwordJoinButton), passwordJoinButton },
                { nameof(passwordCancelButton), passwordCancelButton },
                { nameof(roomTitleText), roomTitleText },
                { nameof(playerOneText), playerOneText },
                { nameof(playerTwoText), playerTwoText },
                { nameof(playerThreeText), playerThreeText },
                { nameof(playerFourText), playerFourText },
                { nameof(playerOneReadyBadge), playerOneReadyBadge },
                { nameof(playerTwoReadyBadge), playerTwoReadyBadge },
                { nameof(playerThreeReadyBadge), playerThreeReadyBadge },
                { nameof(playerFourReadyBadge), playerFourReadyBadge },
                { nameof(ssatyCharacterRoot), ssatyCharacterRoot },
                { nameof(alGCharacterRoot), alGCharacterRoot },
                { nameof(fitCharacterRoot), fitCharacterRoot },
                { nameof(wiseCharacterRoot), wiseCharacterRoot },
                { nameof(characterSelectionPanel), characterSelectionPanel },
                { nameof(selectSsatyCharacterButton), selectSsatyCharacterButton },
                { nameof(selectAlGCharacterButton), selectAlGCharacterButton },
                { nameof(selectFitCharacterButton), selectFitCharacterButton },
                { nameof(selectWiseCharacterButton), selectWiseCharacterButton },
                { nameof(selectRandomCharacterButton), selectRandomCharacterButton },
                { nameof(characterRuntimeRoot), characterRuntimeRoot },
                { nameof(leaveRoomButton), leaveRoomButton },
            };

            var missing = references
                .Where(kvp => kvp.Value == null)
                .Select(kvp => kvp.Key)
                .ToList();

            if (missing.Any())
            {
                Debug.LogError($"[LobbyCanvasUIController] The following required references are not assigned in the Inspector on GameObject '{gameObject.name}':\n- {string.Join("\n- ", missing)}\nPlease assign them in the Unity Editor and restart play mode.");
                return false;
            }

            return true;
        }


        // 오브젝트 파괴 시 NetworkRunner를 안전하게 종료한다.
        private async void OnDestroy()
        {
            if (_runner != null) _runner.RemoveCallbacks(this);
            SceneManager.sceneLoaded -= OnManagedSceneLoaded;
            await ShutdownRunnerAsync();
        }

        // 버튼·토글·입력 필드에 리스너를 등록한다. Start()에서 한 번만 호출된다.
        private void BindEvents()
        {
            nicknameConfirmButton.onClick.AddListener(OnNicknameConfirmed);
            createRoomOpenButton.onClick.AddListener(OpenCreateRoomModal);

            if (nicknameInput != null)
            {
                nicknameInput.onValueChanged.AddListener(_ => EnforceNameInputLimit(nicknameInput));
            }
            if (editNicknameInput != null)
            {
                editNicknameInput.onValueChanged.AddListener(_ => EnforceNameInputLimit(editNicknameInput));
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
            if (leaveGameButton != null)
            {
                leaveGameButton.onClick.AddListener(OnLeaveRoomClicked);
            }
            if (startGameButton != null)
            {
                // 방장: 게임 시작 / 비방장: 준비 토글 — 같은 버튼을 역할에 따라 재활용
                startGameButton.onClick.AddListener(OnRoomActionButtonClicked);
            }
            if (readyButton != null)
            {
                readyButton.onClick.AddListener(OnReadyButtonClicked);
            }


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

            if (selectRandomCharacterButton != null)
            {
                selectRandomCharacterButton.onClick.AddListener(OnSelectRandomCharacter);
            }

            if (characterPreview != null)
            {
                characterPreview.CharacterSelected += SetLocalPlayerSelectedCharacter;
            }
        }



        // 닉네임 확인 버튼 클릭 처리. 유효성 검사 후 로비에 연결하고 방 목록을 표시한다.
        private async void OnNicknameConfirmed()
        {
            var entered = (nicknameInput.text ?? string.Empty).Trim();
            entered = SanitizeNameToken(entered);
            if (string.IsNullOrEmpty(entered))
            {
                return;
            }

            if (!IsWithinNameLengthLimit(entered))
            {
                return;
            }


            if (!await EnsureLobbyRunnerAsync())
            {
                return;
            }

            _nickname = entered;
            nicknameInput.text = entered;
            _isNicknameConfirmed = true;
            ShowMainPanel();
            RefreshRoomList();
        }

        // 방 만들기 모달을 초기 상태로 리셋하고 표시한다.
        private void OpenCreateRoomModal()
        {
            createRoomNameInput.text = string.Empty;
            createPasswordInput.text = string.Empty;
            createPrivateToggle.isOn = false;
            SetCreateValidation(string.Empty);
            createRoomModal.SetActive(true);
            OnPrivateToggleChanged(false);
        }

        // 방 만들기 확인 버튼 클릭 처리. 유효성 검사 후 Host 모드로 Fusion 세션을 생성한다.
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
            // 로비 탐색용 러너가 살아있으면 먼저 종료한다.
            // Fusion은 하나의 NetworkRunner만 동시에 실행할 수 있으므로
            // Host 모드 세션을 시작하기 전에 기존 Lobby 러너를 반드시 제거해야 한다.
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
                    SceneManager = GetOrAddSceneManager(),
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
            _currentRoomPassword = isPrivate ? password : string.Empty;
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

        // 비공개 토글 상태 변경 시 비밀번호 입력 필드의 표시 여부를 갱신한다.
        private void OnPrivateToggleChanged(bool isPrivate)
        {
            createPasswordInput.gameObject.SetActive(isPrivate);

            ForceRebuildRoomModalLayout();
        }

        // _roomSnapshots 기준으로 방 목록 UI를 재구성한다. 기존 아이템을 제거하고 새로 생성한다.
        private void RefreshRoomList()
        {
            if (!_isNicknameConfirmed)
            {
                return;
            }

            if (roomListContent == null || roomItemTemplate == null)
            {
                return;
            }

            // 역순으로 순회해야 자식 제거 시 인덱스가 흐트러지지 않는다.
            // roomItemTemplate은 재사용하므로 제거 대상에서 제외한다.
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
                return;
            }

            // 인원이 많은 방을 상단에 표시하고, 인원이 같으면 방 이름 오름차순으로 정렬한다.
            foreach (var room in _roomSnapshots.OrderByDescending(r => r.PlayerCount).ThenBy(r => r.Name))
            {
                var row = Instantiate(roomItemTemplate, roomListContent);
                row.SetActive(true);

                var nameText = row.transform.Find("RoomNameText")?.GetComponent<TMP_Text>();
                var countText = row.transform.Find("PlayerCountText")?.GetComponent<TMP_Text>();
                var lockedIcon = row.transform.Find("LockedYN/LockedIcon")?.gameObject;
                var unlockedIcon = row.transform.Find("LockedYN/UnlockedIcon")?.gameObject;

                if(nameText != null)
                {
                    nameText.text = room.Name;
                }

                if (countText != null)
                {
                    countText.text = $"{room.PlayerCount}/{room.MaxPlayers} In Game";
                }

                if (lockedIcon != null)
                {
                    lockedIcon.SetActive(room.IsPrivate); // 비밀방이면 켜짐
                }
                if (unlockedIcon != null)
                {
                    unlockedIcon.SetActive(!room.IsPrivate); // 공개방이면 켜짐
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

        // 방 목록에서 방 클릭 시 처리. 비공개 방이면 비밀번호 모달을 띄우고, 공개 방이면 즉시 입장한다.
        private async void OnRoomSelected(string roomName)
        {
            var selected = _roomSnapshots.FirstOrDefault(r => string.Equals(r.Name, roomName, StringComparison.Ordinal));
            if (selected == null)
            {
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

        // 비밀번호 모달 확인 버튼 클릭 처리. 비밀번호가 일치하면 방에 입장한다.
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

        // Client 모드로 Fusion 세션에 입장한다. 입장 성공 시 방 패널을 표시한다.
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
                Debug.LogWarning($"[Lobby] Room join failed: name={room.Name}, reason={result.ShutdownReason}");
                return;
            }

            _currentRoomName = room.Name;
            _currentRoomIsPrivate = room.IsPrivate;
            _currentRoomPassword = room.IsPrivate ? (room.Password ?? string.Empty) : string.Empty;
            _currentRoomOwner = string.IsNullOrWhiteSpace(room.OwnerNickname) ? "-" : room.OwnerNickname;
            _currentOwnerPlayerId = -1;
            _isInLobby = false;
            RegisterParticipant(_runner.LocalPlayer, _nickname);
            Debug.Log($"[Lobby] Room joined: name={room.Name}, private={room.IsPrivate}");
            ShowRoomPanel();
            UpdateRoomPanel();
        }

        // 게임 시작 버튼(또는 F5) 클릭 처리. 호스트만 허용하며 세션 상태와 인원 수를 검증 후 씬을 로드한다.
        private void OnStartGameClicked()
        {
            if (_runner == null || !_runner.IsRunning)
            {
                return;
            }

            if (!_runner.IsServer)
            {
                return;
            }

            if (!_runner.SessionInfo.IsValid)
            {
                return;
            }

            if (!allowSoloStart && _runner.SessionInfo.PlayerCount <= 1)
            {
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
                Debug.Log("[Lobby] Start requested but gameplaySceneName is empty.");
                return;
            }

            if (!TryResolveGameplaySceneName(gameplaySceneName, out var resolvedSceneName))
            {
                var message = $"게임 씬을 찾을 수 없습니다: {gameplaySceneName}";
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
                Debug.LogWarning($"[Lobby] Start game failed: {e.Message}");
            }
        }

        // 요청한 씬 이름을 Build Settings에서 파일 이름 기준으로 검색해 실제 씬 이름을 반환한다.
        // 파일 경로 또는 씬 이름 부분 일치 순서로 탐색한다.
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

        private static bool IsActiveSceneNamed(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            var activeScene = SceneManager.GetActiveScene();
            return activeScene.IsValid() &&
                   string.Equals(activeScene.name, sceneName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private void CleanupRunnerAfterGameEndHostExit()
        {
            if (!_isShowingGameEndPanel || _gameEndReturnTransitionStarted || _runner == null)
                return;

            Debug.Log("[Lobby] Cleanup stale runner after host exit while staying on game end panel.");

            try
            {
                _runner.RemoveCallbacks(this);
            }
            catch
            {
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
            _currentOwnerPlayerId = -1;
            _lastSessionListUpdatedAtUtc = DateTime.MinValue;
            _roomSnapshots.Clear();
            _roomParticipantsByPlayerId.Clear();
            _selectedCharacterIndexByPlayerId.Clear();
            _readyStateByPlayerId.Clear();
            _spawnedGameplayNetworkCharacters.Clear();
            _spawnedCharacterIndexByPlayerId.Clear();
        }

        // 방 나가기 버튼 클릭 처리. 러너를 종료하고 로비 패널로 복귀해 방 목록을 다시 불러온다.
        private async void OnLeaveRoomClicked()
        {
            _currentRoomName = string.Empty;
            _currentRoomOwner = "-";
            _currentOwnerPlayerId = -1;
            _currentRoomIsPrivate = false;
            _currentRoomPassword = string.Empty;
            // 다음 방 입장 시 ? 기본 선택이 다시 적용되도록 리셋한다.
            _localSelectedCharacterIndex = -1;
            _localIsReady = false;

            await ShutdownRunnerAsync();
            ShowLobbyPanel();
            await EnsureLobbyRunnerAsync();
            RefreshRoomList();
        }

        // 게임 종료 순위 화면에서 플레이어가 "같은 방으로"를 선택한다.
        // 백그라운드 자동 방 입장(_gameEndAutoRoomJoinTask)이 이미 완료돼 있으므로 즉시 반응한다.
        public void ReturnToRoomFromGameEnd()
        {
            if (_gameEndReturnTransitionStarted || _isProcessing)
                return;

            _gameEndReturnTransitionStarted = true;
            _ = ExecuteReturnFromGameEndAsync(goToLobby: false);
        }

        // 게임 종료 순위 화면에서 플레이어가 "처음으로"를 선택한다.
        // 백그라운드에서 이미 방 세션에 입장한 상태이므로 완료 대기 후 퇴장 처리한다.
        public void ReturnToLobbyFromGameEnd()
        {
            if (_gameEndReturnTransitionStarted || _isProcessing)
                return;

            _gameEndReturnTransitionStarted = true;
            _ = ExecuteReturnFromGameEndAsync(goToLobby: true);
        }

        // 두 버튼의 공통 진입점.
        // 백그라운드 자동 방 입장(_gameEndAutoRoomJoinTask) 완료를 대기한 뒤 goToLobby에 따라 분기한다.
        // 순위 화면을 보는 동안(통상 수 초) 자동 입장이 완료되므로 버튼 클릭이 즉시 반응한다.
        private async Task ExecuteReturnFromGameEndAsync(bool goToLobby)
        {
            if (_isProcessing) return;
            _isProcessing = true;

            bool joined = _gameEndAutoRoomJoinTask != null && await _gameEndAutoRoomJoinTask;

            if (!joined)
            {
                // 방 자동 입장 실패 → 로비 메인으로 fallback
                _isProcessing = false;
                ResetGameEndReturnState();
                ShowLobbyPanel();
                return;
            }

            if (goToLobby)
            {
                // 로비 선택: gameEndPanel 닫기 → 방 세션 퇴장 → 로비 패널 표시.
                // 퇴장 시 host migration이 발생하면 다음 순위 플레이어가 방장을 이어받는다.
                _isShowingGameEndPanel = false;

                foreach (var obj in _spawnedGameEndCharacters)
                { if (obj != null) Destroy(obj); }
                _spawnedGameEndCharacters.Clear();

                if (gameEndPanel != null) gameEndPanel.SetActive(false);
                if (podiumParent != null) podiumParent.SetActive(false);
                _currentRoomName = string.Empty;
                _currentRoomOwner = "-";
                _currentOwnerPlayerId = -1;
                _currentRoomIsPrivate = false;
                _currentRoomPassword = string.Empty;
                _localSelectedCharacterIndex = -1;
                _localIsReady = false;
                try
                {
                    await ShutdownRunnerAsync();
                }
                finally
                {
                    _isProcessing = false;
                }
                ShowLobbyPanel();
            }
            else
            {
                // 방 선택: gameEndPanel 닫기 → 방 패널 즉시 표시.
                // LauncherScene에 이미 있으므로 씬 전환 없이 패널 전환만 한다.
                _isProcessing = false;
                _isShowingGameEndPanel = false;

                foreach (var obj in _spawnedGameEndCharacters)
                { if (obj != null) Destroy(obj); }
                _spawnedGameEndCharacters.Clear();

                if (gameEndPanel != null) gameEndPanel.SetActive(false);
                if (podiumParent != null) podiumParent.SetActive(false);
                ShowRoomPanel();
                UpdateRoomPanel();
            }
        }

        // Fusion runner.LoadScene() 호출 전에 세워두는 플래그.
        // Despawned()가 OnSceneLoadDone보다 늦게 실행될 수 있으므로
        // GameResultData.Entries가 비어 있어도 패널을 표시하도록 보장한다.
        public void NotifyGameEndTransition()
        {
            _pendingGameEndPanel = true;
        }

        // 게임 종료 후 LauncherScene 진입 시 OnSceneLoadDone에서 호출.
        // 순위 화면을 표시하고 백그라운드에서 방 세션에 자동 입장한다.
        // 완료 전에 버튼을 눌러도 완료 시점에 즉시 처리되므로 사용자에게 대기 시간이 없다.
        // 로컬 테스트 전용 (네트워크 없음): 씬을 직접 로드한 뒤 게임 종료 패널을 표시한다.
        // DebugGameEndTransition이 씬 전환 시 파괴되므로 DontDestroyOnLoad인 이 컨트롤러에서 코루틴을 실행한다.
        public void LoadSceneAndShowGameEndPanel(string sceneName)
        {
            StartCoroutine(CoLoadSceneAndShowGameEndPanel(sceneName));
        }

        private IEnumerator CoLoadSceneAndShowGameEndPanel(string sceneName)
        {
            SceneManager.LoadScene(sceneName);
            while (SceneManager.GetActiveScene().name != sceneName)
                yield return null;
            ShowGameEndPanel();
        }

        public void ShowGameEndPanel()
        {
            if (gameEndPanel == null)
            {
                Debug.LogWarning("[Lobby] gameEndPanel이 연결되지 않았습니다. Inspector에서 연결하세요.");
                return;
            }

            if (nicknamePanel != null) nicknamePanel.SetActive(false);
            if (lobbyPanel != null) lobbyPanel.SetActive(false);
            if (roomPanel != null) roomPanel.SetActive(false);
            if (createRoomModal != null) createRoomModal.SetActive(false);
            if (passwordModal != null) passwordModal.SetActive(false);
            if (mainPanel != null) mainPanel.SetActive(false);
            if (gamePanel != null) gamePanel.SetActive(false);
            HideAllCharacterSlots();
            gameEndPanel.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _isShowingGameEndPanel = true;
            _gameEndReturnTransitionStarted = false;

            DisplayRankings();
            // Clear() 전에 순위를 캡처해 StartAutoRoomJoinFromGameEndAsync의 딜레이 계산에 사용한다.
            _localPlayerRankForAutoJoin = GameResultData.LocalPlayerRank;
            // 표시 완료 후 데이터를 클리어해 이후 OnSceneLoadDone 재발생 시 패널이 중복 표시되지 않도록 한다.
            GameResultData.Clear();

            if (returnToRoomButton != null)
            {
                returnToRoomButton.onClick.RemoveAllListeners();
                returnToRoomButton.onClick.AddListener(ReturnToRoomFromGameEnd);
            }

            if (returnToLobbyButton != null)
            {
                returnToLobbyButton.onClick.RemoveAllListeners();
                returnToLobbyButton.onClick.AddListener(ReturnToLobbyFromGameEnd);
            }

            _gameEndAutoRoomJoinTask = StartAutoRoomJoinFromGameEndAsync();
        }

        // ─── 방장 비정상 종료 → 로비 복귀 ────────────────────────────────────

        // DebugGameEndTransition.PlayerLeft 및 OnShutdown/OnDisconnectedFromServer/OnHostMigration에서 호출.
        // _isShowingGameEndPanel 플래그를 먼저 세워 중복 진입을 방지하고 코루틴을 시작한다.
        public void TriggerHostExitAndReturnToLobby()
        {
            if (_isShowingGameEndPanel)
            {
                Debug.LogWarning("[HostExit] TriggerHostExitAndReturnToLobby 차단됨: _isShowingGameEndPanel=true");
                return;
            }
            Debug.Log("[HostExit] TriggerHostExitAndReturnToLobby 진입");
            _isShowingGameEndPanel = true;
            _pendingGameEndPanel = false;
            StartCoroutine(CoReturnToLobbyOnHostExit());
        }

        private IEnumerator CoReturnToLobbyOnHostExit()
        {
            Debug.Log($"[HostExit] 코루틴 시작. 현재 씬={SceneManager.GetActiveScene().name}");
            if (!IsActiveSceneNamed(launcherSceneName))
            {
                Debug.Log($"[HostExit] LauncherScene 로드 시작 (launcherSceneName={launcherSceneName})");
                SceneManager.LoadScene(launcherSceneName);
                while (!IsActiveSceneNamed(launcherSceneName))
                    yield return null;
                Debug.Log("[HostExit] LauncherScene 로드 완료");
                // SceneManager.LoadScene은 Fusion 경로가 아니므로 OnSceneLoadDone이 발동하지 않는다.
                // 캐릭터 슬롯 재초기화를 허용하도록 수동으로 리셋한다.
                ResetCharacterSlotState();
            }

            _currentRoomName = string.Empty;
            _currentRoomOwner = "-";
            _currentOwnerPlayerId = -1;
            _currentRoomIsPrivate = false;
            _currentRoomPassword = string.Empty;
            _localSelectedCharacterIndex = -1;
            _localIsReady = false;
            _localPlayerRankForAutoJoin = 0;
            // OnHostMigration이 저장한 준비 상태를 클리어해 재입장 시 Ready 복원을 방지한다.
            _migrationReadyStateByClientId.Clear();
            _isMigrating = false;

            Debug.Log("[HostExit] ShowLobbyPanel 호출");
            ShowLobbyPanel();

            yield return null;
            Debug.Log("[HostExit] ShowHostExitNoticeModal 호출");
            ShowHostExitNoticeModal();
        }

        private void ShowHostExitNoticeModal()
        {
            // Inspector 미연결 시 이름으로 자동 탐색 (빌드 환경 포함)
            if (hostExitNoticeModal == null)
            {
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t.name == "HostExitNoticeModal") { hostExitNoticeModal = t.gameObject; break; }
                }
            }

            if (hostExitNoticeModal == null)
            {
                Debug.LogWarning("[HostExit] HostExitNoticeModal을 찾을 수 없습니다.");
                _isShowingGameEndPanel = false;
                return;
            }

            // 버튼도 미연결 시 자식에서 자동 탐색
            if (hostExitNoticeCloseButton == null)
            {
                foreach (var btn in hostExitNoticeModal.GetComponentsInChildren<Button>(true))
                {
                    if (btn.name == "ConfirmButton") { hostExitNoticeCloseButton = btn; break; }
                }
            }

            Debug.Log($"[HostExit] 모달 표시. modal={hostExitNoticeModal.name}, btn={hostExitNoticeCloseButton?.name}");

            if (hostExitNoticeCloseButton != null)
            {
                hostExitNoticeCloseButton.onClick.RemoveAllListeners();
                hostExitNoticeCloseButton.onClick.AddListener(() =>
                {
                    hostExitNoticeModal.SetActive(false);
                    _isShowingGameEndPanel = false;
                });
            }
            else
            {
                Debug.LogWarning("[HostExit] ConfirmButton을 찾을 수 없습니다.");
                _isShowingGameEndPanel = false;
            }

            hostExitNoticeModal.SetActive(true);
            Debug.Log($"[HostExit] 모달 SetActive(true) 완료.");
        }

        private void DisplayRankings()
        {
            if (rankingContainer == null) return;

            if (podiumParent != null)
            {
                podiumParent.SetActive(true);
            }

            var entries = GameResultData.Entries.OrderBy(e => e.Rank).ToList();
            var rankingItems = rankingContainer.GetComponentsInChildren<RankingItemUI>(true).ToList();

            // 기존 UI(글자) 리스트 업데이트
            for (int i = 0; i < rankingItems.Count; i++)
            {
                bool shouldShow = i < entries.Count;
                rankingItems[i].gameObject.SetActive(shouldShow);
                if (shouldShow)
                {
                    var entry = entries[i];
                    var nickname = !string.IsNullOrWhiteSpace(entry.Nickname) ? entry.Nickname : $"Player{entry.PlayerId}";
                    // 캐릭터 인덱스 파라미터
                    rankingItems[i].SetData(entry.Rank, nickname, entry.CharacterTypeIndex);
                }
            }

            // 3D 캐릭터 단상 소환 (이전에 소환된 애들이 있으면 청소)
            foreach (var obj in _spawnedGameEndCharacters) { if (obj != null) Destroy(obj); }
            _spawnedGameEndCharacters.Clear();

            // 단상 자리를 안 만들어뒀으면 그냥 종료 (에러 방지)
            if (podiumSlots == null || podiumSlots.Length == 0) return;

            for (int i = 0; i < entries.Count; i++)
            {
                if (i >= podiumSlots.Length) break; // 단상 개수보다 사람이 많으면 패스
                
                var entry = entries[i];
                int rankIndex = i; // 0=1등, 1=2등, 2=3등, 3=4등

                // 닉네임 3D Text
                var nickText3D = podiumSlots[rankIndex].GetComponentInChildren<TMPro.TextMeshPro>();
                if (nickText3D != null)
                {
                    var nickname = !string.IsNullOrWhiteSpace(entry.Nickname) ? entry.Nickname : $"Player{entry.PlayerId}";
                    nickText3D.text = nickname;
                }

                // 캐릭터 번호
                int charIndex = entry.CharacterTypeIndex;

                // sceneDirectSlots가 설정되어 있다면 수동 배치된 모델을 활용
                if (sceneDirectSlots != null && rankIndex < sceneDirectSlots.Count)
                {
                    var slot = sceneDirectSlots[rankIndex];
                    if (slot != null && slot.characterModels != null)
                    {
                        if (slot.questionMark != null)
                            slot.questionMark.SetActive(charIndex == (int)CharacterKind.Random);

                        for (int option = 0; option < slot.characterModels.Length; option++)
                        {
                            var model = slot.characterModels[option];
                            if (model != null)
                            {
                                bool isSelectedModel = (option == charIndex && charIndex != (int)CharacterKind.Random);
                                model.SetActive(isSelectedModel);
                                if (isSelectedModel)
                                {
                                    Animator anim = model.GetComponentInChildren<Animator>();
                                    if (anim != null)
                                    {
                                        int actualRank = rankIndex + 1;
                                        anim.SetInteger("Rank", actualRank);
                                    }
                                }
                            }
                        }
                        continue; // 새 로직 탔으면 Instantiate 생략
                    }
                }

                // 구형 로직 (동적 소환 패턴 폴백)
                // 캐릭터 템플릿 가져오기
                GameObject template = GetCharacterTemplateByIndex(charIndex);
                if (template != null && podiumSlots[rankIndex] != null)
                {
                    // 단상 위치(podiumSlots)에 캐릭터 복사해서 소환!
                    GameObject clone = Instantiate(template, podiumSlots[rankIndex].position, podiumSlots[rankIndex].rotation);
                    clone.SetActive(true);
                    _spawnedGameEndCharacters.Add(clone);

                    // 애니메이터 꺼내서 등수별로 춤추게 만들기
                    Animator anim = clone.GetComponentInChildren<Animator>();
                    if (anim != null)
                    {
                        int actualRank = rankIndex + 1; 
                        anim.SetInteger("Rank", actualRank);
                    }
                }
            }
        }

        // LauncherScene 진입 시 OnSceneLoadDone에서 호출.
        // 백그라운드에서 이전 세션을 종료하고 순위 기반 딜레이 후 새 방 세션에 자동 입장한다.
        // 완료 전에 버튼을 눌러도 완료 시점에 즉시 처리되므로 사용자에게 대기 시간이 없다.
        private async Task<bool> StartAutoRoomJoinFromGameEndAsync()
        {
            var roomName = _currentRoomName;
            var isPrivate = _currentRoomIsPrivate;
            var roomPassword = _currentRoomPassword;

            await ShutdownRunnerAsync();
            _localIsReady = false;
            // 게임 종료 후 방으로 돌아올 때 캐릭터 선택을 ?로 초기화한다.
            _localSelectedCharacterIndex = -1;

            if (string.IsNullOrEmpty(roomName))
            {
                Debug.LogWarning("[Lobby] AutoRoomJoin: roomName 없음, 건너뜁니다.");
                return false;
            }

            // ── 순위 기반 딜레이 (백그라운드, 사용자에게 체감 없음) ────────────────────
            // GameEndScene UI를 보는 수 초 사이에 완료되므로 버튼 클릭 시 즉시 반응한다.
            //   1등:   0ms → HOST로 세션 생성
            //   2등: 150ms → CLIENT로 입장
            //   3등: 300ms → CLIENT로 입장
            //   4등: 450ms → CLIENT로 입장
            const int RankDelayMs = 150;
            var localRank = _localPlayerRankForAutoJoin;
            var delaySteps = localRank > 0 ? (localRank - 1) : MaxPlayers;
            if (delaySteps > 0)
                await Task.Delay(delaySteps * RankDelayMs);

            if (!TryCreateRunner(out var runner))
            {
                Debug.LogWarning("[Lobby] AutoRoomJoin: TryCreateRunner 실패.");
                return false;
            }

            _runner = runner;
            StartGameResult result;
            try
            {
                var sessionProperties = new Dictionary<string, SessionProperty>
                {
                    { PrivateKey, isPrivate },
                    { StartedKey, false }
                };
                if (isPrivate && !string.IsNullOrEmpty(roomPassword))
                    sessionProperties[PasswordKey] = roomPassword;

                var appSettings = GetOrCreatePhotonSettings();
                result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.AutoHostOrClient,
                    SessionName = roomName,
                    SceneManager = GetOrAddSceneManager(),
                    PlayerCount = MaxPlayers,
                    SessionProperties = sessionProperties,
                    IsVisible = true,
                    IsOpen = true,
                    ConnectionToken = BuildConnectionToken(_nickname),
                    CustomLobbyName = SharedLobbyName,
                    CustomPhotonAppSettings = appSettings
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lobby] AutoRoomJoin: StartGame 예외. {e.Message}");
                return false;
            }

            if (!result.Ok)
            {
                Debug.LogWarning($"[Lobby] AutoRoomJoin: StartGame 실패. reason={result.ShutdownReason}");
                return false;
            }

            _currentRoomName = roomName;
            _currentRoomIsPrivate = isPrivate;
            _currentRoomPassword = roomPassword;
            _isInLobby = false;

            if (_localSelectedCharacterIndex >= 0)
                _selectedCharacterIndexByPlayerId[_runner.LocalPlayer.PlayerId] = _localSelectedCharacterIndex;

            RegisterParticipant(_runner.LocalPlayer, _nickname);

            if (_runner.IsServer)
            {
                _currentRoomOwner = _nickname;
                _currentOwnerPlayerId = _runner.LocalPlayer.PlayerId;
                UpdateOwnerSessionProperty();
                BroadcastPlayerRoster();
            }
            else if (_localSelectedCharacterIndex >= 0)
            {
                SetLocalPlayerSelectedCharacter(_localSelectedCharacterIndex);
            }

            Debug.Log($"[Lobby] AutoRoomJoin: 완료. isHost={_runner.IsServer}, room={roomName}");
            return true;
        }

        private void ResetGameEndReturnState()
        {
            _gameEndReturnTransitionStarted = false;
            _isShowingGameEndPanel = false;
        }

        // GameScene 전환 시 파괴된 캐릭터 오브젝트 참조를 초기화한다.
        // LauncherScene 복귀 후 InitializeCharacterSlotsIfNeeded()가 재실행되도록 플래그를 리셋한다.
        private void ResetCharacterSlotState()
        {
            _characterSlotsInitialized = false;
            for (var slot = 0; slot < PlayerSlotCount; slot++)
                for (var option = 0; option < CharacterOptionCount; option++)
                    _slotCharacterRoots[slot, option] = null;

            _characterPrePlacedWorldY.Clear();
            _characterBaseBoundsHeights.Clear();
            _characterBaseLocalScales.Clear();
            _characterBaseLocalRotations.Clear();
            _characterOptionIndexByTransform.Clear();

            // CharacterPreviewController도 동일하게 리셋 — GameScene 전환 시 파괴된
            // 슬롯 오브젝트 참조를 제거하고 재초기화를 허용한다.
            characterPreview?.ResetSlots();
        }

        // 현재 방 이름·공개 여부·플레이어 슬롯·게임 시작 버튼 활성 상태를 갱신한다.
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

            RefreshRoomActionButtonState(currentPlayers);

            // 별도 readyButton이 있는 경우 보조적으로 상태 갱신
            RefreshReadyButtonState();
        }

        // 참가자 목록을 PlayerId 오름차순으로 정렬해 플레이어 슬롯 텍스트와 캐릭터 표시를 갱신한다.
        private void UpdatePlayerSlots()
        {
            // GameEndPanel 표시 중에는 캐릭터 슬롯을 갱신하지 않는다.
            if (_isShowingGameEndPanel) return;
            var slotTexts = new[] { playerOneText, playerTwoText, playerThreeText, playerFourText };
            var readyBadges = new[] { playerOneReadyBadge, playerTwoReadyBadge, playerThreeReadyBadge, playerFourReadyBadge };
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
                    var isOwner = participant.PlayerId == _currentOwnerPlayerId;
                    var isReady = !isOwner && _readyStateByPlayerId.TryGetValue(participant.PlayerId, out var ready) && ready;
                    slot.text = participant.Nickname;
                    UpdateReadyBadge(readyBadges[i], isReady);

                    // 변경 감지 로직
                    int lastPlayerId = _playerIdBySlot[i];
                    int newPlayerId = participant.PlayerId;
                    int lastCharIdx = _selectedCharacterIndexBySlot[i];
                    int newCharIdx = SanitizeCharacterIndexOrNone(participant.CharacterIndex);

                    // 애니메이션 재생 조건:
                    // 1. 같은 플레이어가 슬롯에 계속 머물러 있어야 함 (새로 들어온 사람은 기뻐하지 않음)
                    // 2. 캐릭터 인덱스가 실제로 바뀌어야 함
                    // 3. 바뀐 캐릭터가 랜덤(?)이 아니어야 함
                    bool isSamePlayer = (lastPlayerId != -1 && lastPlayerId == newPlayerId);
                    bool charChanged = (lastCharIdx != newCharIdx);
                    bool shouldPlayAnim = isSamePlayer && charChanged && (newCharIdx != (int)CharacterKind.Random) && (newCharIdx != -1);

                    _playerIdBySlot[i] = newPlayerId;
                    _selectedCharacterIndexBySlot[i] = newCharIdx;

                    ApplySelectedCharacterForSlot(i, true, shouldPlayAnim);
                    characterPreview?.UpdateSlot(i, participant.PlayerId, participant.CharacterIndex, true);
                }
                else
                {
                    slot.text = emptyPlayerSlot;
                    UpdateReadyBadge(readyBadges[i], false);
                    _playerIdBySlot[i] = -1;
                    _selectedCharacterIndexBySlot[i] = -1;
                    ApplySelectedCharacterForSlot(i, false, false);
                    characterPreview?.UpdateSlot(i, -1, -1, false);
                }
            }

            LayoutNameSlotsForCurrentViewport();
            AlignAllCharacterCandidatesToNameSlots();
            RefreshCharacterSelectionUiState();
        }

        // 슬롯×캐릭터 조합의 프리뷰 오브젝트를 생성하거나 씬에 미리 배치된 오브젝트를 재사용해 초기화한다.
        // 이미 초기화됐으면 즉시 반환한다.
        private void InitializeCharacterSlotsIfNeeded()
        {
            if (_characterSlotsInitialized)
            {
                return;
            }

            var templates = new[] { ssatyCharacterRoot, alGCharacterRoot, fitCharacterRoot, wiseCharacterRoot, randomCharacterRoot };
            var nameSlots = GetNameSlots();
            if (templates.Take(4).Any(t => t == null) || nameSlots.Any(t => t == null))
            {
                return;
            }

            var runtimeRoot = ResolveCharacterRuntimeRoot();
            // slot(플레이어 슬롯 0~3) × option(캐릭터 종류 0~4) 조합의 2차원 배열을 구성한다.
            for (var slot = 0; slot < PlayerSlotCount; slot++)
            {
                for (var option = 0; option < CharacterOptionCount; option++)
                {
                    var template = templates[option];
                    if (template == null) continue;
                    // 씬에 미리 배치된 오브젝트가 있으면 Instantiate 없이 재사용한다.
                    // 이름 규칙: "{템플릿명}_Slot{슬롯번호}" (예: "SsatyCharacterUI_Slot1")
                    var prePlacedName = $"{template.name}_Slot{slot + 1}";
                    var prePlaced = runtimeRoot.Find(prePlacedName);

                    Transform cloneTransform;
                    if (prePlaced != null)
                    {
                        // 씬에 미리 배치된 오브젝트 재사용 — 월드 Y 좌표를 고정 기준으로 저장
                        cloneTransform = prePlaced;
                        ConfigureCharacterPreviewClone(cloneTransform.gameObject);
                        _characterPrePlacedWorldY[cloneTransform] = cloneTransform.position.y;
                    }
                    else
                    {
                        // 미리 배치된 오브젝트가 없으면 런타임에 템플릿을 복제해 생성
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

        // 모든 슬롯의 캐릭터 오브젝트를 해당 슬롯의 이름 텍스트 위치에 정렬한다.
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
                    AlignCharacterSlot(_slotCharacterRoots[slot, option], nameSlot, slot);
                }
            }
        }

        // 캐릭터 슬롯 하나를 이름 슬롯 텍스트 위치 기준으로 배치한다.
        // UI 공간(RectTransform)과 월드 공간 오브젝트 두 가지 경우를 모두 처리한다.
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
                var kindPositionOffset = GetCharacterKindPositionOffset(characterSlot);
                charRect.anchorMin = nameRect.anchorMin;
                charRect.anchorMax = nameRect.anchorMax;
                charRect.pivot = nameRect.pivot;
                charRect.anchoredPosition = nameRect.anchoredPosition + new Vector2(
                    kindPositionOffset.x,
                    characterVerticalOffset + characterExtraVerticalOffset + characterSpecificOffset + kindPositionOffset.y);
                ApplyRotationToVisuals(characterSlot);
                if (useFixedCharacterScale)
                {
                    charRect.localScale = Vector3.Scale(fixedCharacterScale, GetCharacterKindScaleMultiplier(characterSlot));
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
            var kindPositionOffsetWorld = GetCharacterKindPositionOffset(characterSlot);
            screenPoint.x += kindPositionOffsetWorld.x;
            screenPoint.y += characterVerticalOffset + characterExtraVerticalOffset
                             + GetCharacterSpecificVerticalOffset(characterSlot)
                             + kindPositionOffsetWorld.y;
            var padding = Mathf.Max(0f, characterScreenPaddingPixels);
            screenPoint.x = Mathf.Clamp(screenPoint.x, padding, Screen.width - padding);
            screenPoint.y = Mathf.Clamp(screenPoint.y, padding, Screen.height - padding);

            var depth = characterWorldDepth;
            if (depth <= 0f)
            {
                // Inspector에서 depth가 설정되지 않은 경우 현재 캐릭터 위치의 카메라 기준 깊이를 사용한다.
                // 카메라 뒤쪽(음수)이면 기본값 8f로 폴백한다.
                depth = Vector3.Dot(characterSlot.position - worldCam.transform.position, worldCam.transform.forward);
                if (depth <= 0f)
                {
                    depth = 8f;
                }
            }

            var targetWorld = worldCam.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, depth));
            var finalPos = targetWorld + characterWorldOffset + new Vector3(0f, 0f, kindPositionOffsetWorld.z);
            if (_characterPrePlacedWorldY.TryGetValue(characterSlot, out var lockedY))
            {
                finalPos.y = lockedY + GetCharacterPrePlacedWorldYAdjustment(characterSlot) + kindPositionOffsetWorld.y;
            }
            characterSlot.position = finalPos;
            ApplyRotationToVisuals(characterSlot);
            ApplySlotParticleRotationY(characterSlot, slotIndex);

            if (useFixedCharacterScale)
            {
                characterSlot.localScale = Vector3.Scale(fixedCharacterScale, GetCharacterKindScaleMultiplier(characterSlot));
            }
            else if (keepCharacterScreenSize)
            {
                ApplyCharacterScreenHeightScale(characterSlot, worldCam);
            }
        }

        // 현재 뷰포트 크기를 기준으로 4개 플레이어 이름 슬롯의 위치와 크기를 매 프레임 갱신한다.
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

        // 캐릭터 배치에 사용할 카메라를 반환한다. 인스펙터 지정 → Camera.main → 씬 탐색 순으로 폴백한다.
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

        // UI 좌표 변환에 사용할 카메라를 반환한다. ScreenSpaceOverlay 캔버스는 null을 반환한다.
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

        // 캐릭터 프리뷰 클론의 부모가 될 런타임 루트 Transform을 반환한다.
        // 인스펙터 미설정 시 씬에서 "LobbyCharacterRuntimeRoot"를 찾거나 새로 생성한다.
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

        private void ApplyRotationToVisuals(Transform characterSlot)
        {
            if (characterSlot == null)
            {
                return;
            }

            var combinedRotation = Quaternion.Euler(GetCharacterKindRotationOffset(characterSlot));
            if (_characterBaseLocalRotations.TryGetValue(characterSlot, out var rootBaseRotation))
            {
                characterSlot.localRotation = rootBaseRotation;
            }

            if (characterSlot.childCount <= 0)
            {
                characterSlot.localRotation *= combinedRotation;
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

                child.localRotation = childBaseRotation * combinedRotation;
            }
        }

        // 캐릭터별 추가 수직 오프셋을 반환한다.
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
                (int)CharacterKind.AlG => algCharacterVerticalOffsetAdjustment,
                (int)CharacterKind.Fit => fitCharacterVerticalOffsetAdjustment,
                (int)CharacterKind.Wise => wiseCharacterVerticalOffsetAdjustment,
                _ => 0f
            };
        }

        // 미리 배치된 캐릭터의 월드 Y 보정값을 반환한다. 스티의 경우 별도 조정값을 적용한다.
        private float GetCharacterPrePlacedWorldYAdjustment(Transform characterSlot)
        {
            if (_characterOptionIndexByTransform.TryGetValue(characterSlot, out var option) &&
                option == (int)CharacterKind.Fit)
            {
                return fitCharacterWorldYAdjustment;
            }

            return 0f;
        }

        private void ApplySlotParticleRotationY(Transform characterSlot, int slotIndex)
        {
            if (characterSlot == null || slotIndex < 0 ||
                slotParticleRotationYOffsets == null || slotIndex >= slotParticleRotationYOffsets.Length)
            {
                return;
            }

            var ps = characterSlot.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                return;
            }

            var totalY = slotParticleRotationYOffsets[slotIndex] * Mathf.Deg2Rad;
            var main = ps.main;
            main.startRotationY = new ParticleSystem.MinMaxCurve(totalY);
        }

        private Vector3 GetCharacterKindPositionOffset(Transform characterSlot)
        {
            if (characterKindPositionOffsets != null &&
                _characterOptionIndexByTransform.TryGetValue(characterSlot, out var option) &&
                option >= 0 && option < characterKindPositionOffsets.Length)
            {
                return characterKindPositionOffsets[option];
            }
            return Vector3.zero;
        }

        private Vector3 GetCharacterKindRotationOffset(Transform characterSlot)
        {
            if (characterKindRotationOffsets != null &&
                _characterOptionIndexByTransform.TryGetValue(characterSlot, out var option) &&
                option >= 0 && option < characterKindRotationOffsets.Length)
            {
                return characterKindRotationOffsets[option];
            }
            return Vector3.zero;
        }

        private Vector3 GetCharacterKindScaleMultiplier(Transform characterSlot)
        {
            if (characterKindScaleMultipliers != null &&
                _characterOptionIndexByTransform.TryGetValue(characterSlot, out var option) &&
                option >= 0 && option < characterKindScaleMultipliers.Length)
            {
                return characterKindScaleMultipliers[option];
            }
            return Vector3.one;
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

        // 캐릭터가 화면에서 일정 픽셀 높이를 유지하도록 localScale을 조정한다.
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

        // root 하위의 모든 Renderer 바운딩박스를 합산해 전체 높이(Y)를 반환한다.
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

        // 모든 슬롯의 캐릭터 오브젝트를 비활성화하고 CharacterPreview도 초기화한다.
        private void HideAllCharacterSlots()
        {
            for (var slot = 0; slot < PlayerSlotCount; slot++)
            {
                for (var option = 0; option < CharacterOptionCount; option++)
                {
                    var root = _slotCharacterRoots[slot, option];
                    if (root != null)
                    {
                        root.gameObject.SetActive(false);
                    }
                }
            }

            characterPreview?.ClearAllSlots();
        }

        // 4개 플레이어 이름 슬롯 텍스트를 배열로 반환한다.
        private TMP_Text[] GetNameSlots()
        {
            return new[] { playerOneText, playerTwoText, playerThreeText, playerFourText };
        }

        // 외부에서 슬롯 인덱스와 캐릭터 이름으로 해당 슬롯의 선택 캐릭터를 설정한다.
        public void SetSelectedCharacterForSlot(int slotIndex, string characterName)
        {
            if (slotIndex < 0 || slotIndex >= PlayerSlotCount)
            {
                return;
            }

            _selectedCharacterIndexBySlot[slotIndex] = SanitizeCharacterIndexOrNone(CharacterNameToIndex(characterName));
            ApplySelectedCharacterForSlot(slotIndex, true);
        }

        public void OnSelectSsatyCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Ssaty);
        public void OnSelectAlGCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.AlG);
        public void OnSelectFitCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Fit);
        public void OnSelectWiseCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Wise);
        public void OnSelectRandomCharacter() => SetLocalPlayerSelectedCharacter((int)CharacterKind.Random);
        public void SetLocalSelectedCharacterByName(string characterName) =>
            SetLocalPlayerSelectedCharacter(CharacterNameToIndex(characterName));

        // 로컬 플레이어의 캐릭터 선택을 업데이트하고 서버(방장)에 전파한다.
        // 서버면 BroadcastPlayerRoster, 클라이언트면 SendCharacterSelectionToHost를 호출한다.
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
            _localSelectedCharacterIndex = normalizedIndex;
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

        // 방 내 액션 버튼 클릭 처리. 방장이면 게임 시작, 비방장이면 준비 토글.
        private void OnRoomActionButtonClicked()
        {
            if (_runner != null && _runner.IsRunning && _runner.IsServer)
                OnStartGameClicked();
            else
                ToggleLocalPlayerReady();
        }

        // 준비 버튼 클릭 처리. 로컬 플레이어의 준비 상태를 토글한다.
        private void OnReadyButtonClicked()
        {
            ToggleLocalPlayerReady();
        }


        // 로컬 플레이어의 준비 상태를 토글하고 방장에게 전파한다.
        private void ToggleLocalPlayerReady()
        {
            if (_runner == null || !_runner.IsRunning || _runner.IsServer || !_runner.LocalPlayer.IsRealPlayer)
            {
                return;
            }

            _localIsReady = !_localIsReady;

            var localPlayerId = _runner.LocalPlayer.PlayerId;
            _readyStateByPlayerId[localPlayerId] = _localIsReady;
            if (_roomParticipantsByPlayerId.TryGetValue(localPlayerId, out var presence) && presence != null)
            {
                presence.IsReady = _localIsReady;
            }

            SendReadyStateToHost(_localIsReady);

            RefreshRoomActionButtonState();
            RefreshReadyButtonState();
            RefreshCharacterSelectionUiState();
            UpdatePlayerSlots();
        }

        // 클라이언트가 자신의 준비 상태를 방장에게 ReliableData로 전송한다.
        private void SendReadyStateToHost(bool isReady)
        {
            if (_runner == null || !_runner.IsRunning || _runner.IsServer)
            {
                return;
            }

            if (!TryResolveOwnerPlayerRef(out var ownerPlayer))
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(isReady ? "1" : "0");
            try
            {
                _runner.SendReliableDataToPlayer(ownerPlayer, ReadyStateReliableKey, bytes);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Lobby] Failed to send ready state: {e.Message}");
            }
        }

        // 방장을 제외한 모든 플레이어가 준비 상태인지 확인한다.
        private bool AreAllNonHostPlayersReady()
        {
            if (_runner == null || !_runner.IsRunning)
            {
                return false;
            }

            var nonHostParticipants = _roomParticipantsByPlayerId.Values
                .Where(p => p != null && p.PlayerId != _currentOwnerPlayerId)
                .ToList();

            if (nonHostParticipants.Count == 0)
            {
                return false;
            }

            return nonHostParticipants.All(p =>
                _readyStateByPlayerId.TryGetValue(p.PlayerId, out var ready) && ready);
        }

        // 준비 버튼의 표시 여부와 텍스트를 갱신한다.
        // 방장에게는 숨기고, 비방장에게는 준비 상태에 따라 텍스트를 변경한다.
        private void RefreshReadyButtonState()
        {
            if (readyButton == null)
            {
                return;
            }

            var isHost = _runner != null && _runner.IsRunning && _runner.IsServer;
            var inRoom = roomPanel != null && roomPanel.activeSelf;
            readyButton.gameObject.SetActive(!isHost && inRoom);

            if (readyButtonText != null)
            {
                readyButtonText.text = _localIsReady ? "준비 취소" : "준비";
            }
        }

        // 특정 PlayerId의 캐릭터 선택을 해당 슬롯에 즉시 반영한다.
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
                characterPreview?.ApplySelectionForPlayer(playerId, characterIndex);
                break;
            }
        }

        // 클라이언트가 자신의 캐릭터 선택 인덱스를 호스트(방장)에게 ReliableData로 전송한다.
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

        // 현재 방장의 PlayerRef를 찾아 반환한다. 먼저 저장된 PlayerId로 탐색하고 실패 시 가장 낮은 ID로 폴백한다.
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

        // 캐릭터 인덱스가 유효 범위(0~CharacterOptionCount-1)인지 확인하고, 범위 밖이면 -1을 반환한다.
        private static int SanitizeCharacterIndexOrNone(int rawIndex)
        {
            return rawIndex >= 0 && rawIndex < CharacterOptionCount ? rawIndex : -1;
        }

        // 슬롯의 선택 캐릭터에 해당하는 오브젝트만 활성화하고 나머지는 비활성화한다.
        private void ApplySelectedCharacterForSlot(int slotIndex, bool slotHasPlayer, bool playAnimation = false)
        {
            var selectedIndex = _selectedCharacterIndexBySlot[slotIndex];
            var displayIndex = selectedIndex >= 0 ? selectedIndex : (int)CharacterKind.Random;
            var shouldShow = slotHasPlayer;

            // 1. Scene Room Slots (수동 배치) 로직
            if (sceneRoomSlots != null && slotIndex < sceneRoomSlots.Count)
            {
                var slot = sceneRoomSlots[slotIndex];
                if (slot != null && slot.characterModels != null)
                {
                    if (slot.questionMark != null)
                    {
                        bool showQM = (shouldShow && displayIndex == (int)CharacterKind.Random);
                        // 현재 켜짐/꺼짐 상태가 다를 때만 조작
                        if (slot.questionMark.activeSelf != showQM)
                            slot.questionMark.SetActive(showQM);
                    }

                    for (int option = 0; option < slot.characterModels.Length; option++)
                    {
                        var model = slot.characterModels[option];
                        if (model != null)
                        {
                            bool isSelectedModel = (shouldShow && option == displayIndex && displayIndex != (int)CharacterKind.Random);

                            // 이미 켜져있는 내 캐릭터는 건드리지 않기
                            if (model.activeSelf != isSelectedModel)
                            {
                                model.SetActive(isSelectedModel);
                            }
                        }
                    }
                }
                return; // 새 로직을 탔으면 여기서 종료
            }

            // 2. 구형 로직 (동적 스폰)
            InitializeCharacterSlotsIfNeeded();
            if (!_characterSlotsInitialized || slotIndex < 0 || slotIndex >= PlayerSlotCount)
            {
                return;
            }

            for (var option = 0; option < CharacterOptionCount; option++)
            {
                var rect = _slotCharacterRoots[slotIndex, option];
                if (rect == null)
                {
                    continue;
                }

                bool isSelectedModel = (shouldShow && option == displayIndex);

                // 상태가 바뀌어야 할 때만 SetActive를 불러서 애니메이터 재시작을 막음
                if (rect.gameObject.activeSelf != isSelectedModel)
                {
                    rect.gameObject.SetActive(isSelectedModel);
                }
            }
        }

        // 캐릭터 이름 문자열을 CharacterKind 인덱스로 변환한다. 매칭 실패 시 -1을 반환한다.
        private static int CharacterNameToIndex(string characterName)
        {
            if (string.Equals(characterName, "StattyCharacter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(characterName, "SsatyCharacter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(characterName, "Character_ssaty_humanoid", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.Ssaty;
            }

            if (string.Equals(characterName, "AI.gCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.AlG;
            }

            if (string.Equals(characterName, "FitCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.Fit;
            }

            if (string.Equals(characterName, "WiseCharacter", StringComparison.OrdinalIgnoreCase))
            {
                return (int)CharacterKind.Wise;
            }

            return -1;
        }

        // 인덱스에 해당하는 로비 프리뷰용 캐릭터 템플릿 GameObject를 반환한다.
        private GameObject GetCharacterTemplateByIndex(int characterIndex)
        {
            return SanitizeCharacterIndexOrNone(characterIndex) switch
            {
                (int)CharacterKind.Ssaty => ssatyCharacterRoot,
                (int)CharacterKind.AlG => alGCharacterRoot,
                (int)CharacterKind.Fit => fitCharacterRoot,
                (int)CharacterKind.Wise => wiseCharacterRoot,
                (int)CharacterKind.Random => randomCharacterRoot,
                _ => null
            };
        }

        // 닉네임 입력 패널만 활성화하고 나머지 패널·슬롯을 모두 숨긴다.
        private void ShowNicknamePanel()
        {
            if (gamePanel != null) gamePanel.SetActive(false);
            nicknamePanel.SetActive(true);
            if (mainPanel != null) mainPanel.SetActive(false);
            lobbyPanel.SetActive(false);
            roomPanel.SetActive(false);
            createRoomModal.SetActive(false);
            passwordModal.SetActive(false);
            HideAllCharacterSlots();
            RefreshCharacterSelectionUiState();
        }

        public void ShowMainPanel()
        {
            if (gamePanel != null) gamePanel.SetActive(false);
            nicknamePanel.SetActive(false);
            lobbyPanel.SetActive(false);
            roomPanel.SetActive(false);
            createRoomModal.SetActive(false);
            passwordModal.SetActive(false);
            if (gameSettingModal != null) gameSettingModal.SetActive(false);

            if (mainPanel != null) mainPanel.SetActive(true);

            if (mainPanelNicknameText != null)
            {
                mainPanelNicknameText.text = _nickname;
            }
        }

        public async void ShowLobbyPanel()
        {
            if (gamePanel != null) gamePanel.SetActive(false);
            nicknamePanel.SetActive(false);
            if (mainPanel != null) mainPanel.SetActive(false);
            lobbyPanel.SetActive(true);
            roomPanel.SetActive(false);
            passwordModal.SetActive(false);
            if (hostExitNoticeModal != null) hostExitNoticeModal.SetActive(false);
            lobbyHeaderText.text = string.Format(nicknameHeaderFormat, _nickname);
            HideAllCharacterSlots();
            RefreshCharacterSelectionUiState();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // 모달 미닫힘 등 예외 케이스 방어: 로비 패널 진입 시 플래그 해제
            _isShowingGameEndPanel = false;

            if (_isNicknameConfirmed)
            {
                if (await EnsureLobbyRunnerAsync())
                {
                    RefreshRoomList();
                }
            }
        }

        // 방 대기 패널을 활성화하고 닉네임·로비 패널을 숨긴다.
        private void ShowRoomPanel()
        {
            if (gamePanel != null) gamePanel.SetActive(false);
            nicknamePanel.SetActive(false);
            lobbyPanel.SetActive(false);
            if (mainPanel != null) mainPanel.SetActive(false);
            roomPanel.SetActive(true);
            if (hostExitNoticeModal != null) hostExitNoticeModal.SetActive(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // 모달 미닫힘 등 예외 케이스 방어: 방 패널 진입 시 플래그 해제
            _isShowingGameEndPanel = false;
            RefreshRoomActionButtonState();

            // 방 입장 시 캐릭터 선택이 없으면 ? (Random)을 기본값으로 설정한다.
            // 마이그레이션 복귀 등 이미 선택된 경우(_localSelectedCharacterIndex >= 0)에는 유지한다.
            if (_localSelectedCharacterIndex < 0 &&
                _runner != null && _runner.IsRunning && _runner.LocalPlayer.IsRealPlayer)
            {
                SetLocalPlayerSelectedCharacter((int)CharacterKind.Random);
            }

            RefreshCharacterSelectionUiState();
            RefreshReadyButtonState();
        }

        // 방장/비방장 역할에 따라 공용 액션 버튼(startGameButton)의 문구와 활성 상태를 갱신한다.
        private void RefreshRoomActionButtonState(int? currentPlayersOverride = null)
        {
            if (startGameButton == null)
            {
                return;
            }

            startGameButton.gameObject.SetActive(true);

            var currentPlayers = currentPlayersOverride ??
                (_runner != null && _runner.IsRunning && _runner.SessionInfo.IsValid
                    ? _runner.SessionInfo.PlayerCount
                    : 1);

            var isHost = _runner != null && _runner.IsRunning && _runner.IsServer;
            var btnText = startGameButton.GetComponentInChildren<TMP_Text>();
            if (isHost)
            {
                startGameButton.interactable = allowSoloStart || (currentPlayers > 1 && AreAllNonHostPlayersReady());
                if (btnText != null) btnText.text = "게임 시작";
                return;
            }

            startGameButton.interactable = _runner != null && _runner.IsRunning && _runner.LocalPlayer.IsRealPlayer;
            if (btnText != null) btnText.text = _localIsReady ? "준비 취소" : "준비";
        }

        private void UpdateReadyBadge(GameObject badge, bool visible)
        {
            if (badge == null)
            {
                return;
            }

            TrySetGameObjectActive(badge, visible);
        }

        // 로비 세션 탐색용 러너가 실행 중이 아니면 새로 생성해 SharedLobbyName에 연결한다.
        // 이미 연결된 경우 forceReconnect가 false면 즉시 true를 반환한다.
        private async Task<bool> EnsureLobbyRunnerAsync(bool forceReconnect = false)
        {
            // 여러 async 호출이 동시에 러너를 생성/종료하지 않도록 뮤텍스를 획득한다.
            await _runnerLock.WaitAsync();
            try
            {
                // 이미 로비에 연결된 러너가 있으면 재생성 없이 즉시 반환한다.
                if (!forceReconnect && _runner != null && _runner.IsRunning && _isInLobby)
                {
                    return true;
                }

                await ShutdownRunnerInternalAsync();

                if (!TryCreateRunner(out var runner))
                {
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
                    Debug.Log($"[Lobby] Connected to custom lobby: region={appSettings.FixedRegion}, version={appSettings.AppVersion}, lobby={SharedLobbyName}");
                    return true;
                }
                catch (Exception e)
                {
                    _isInLobby = false;
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

        // _runnerLock을 획득한 뒤 내부 종료 로직을 호출한다. 외부에서 호출하는 퍼블릭 진입점.
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

        // 러너를 실제로 종료하고 관련 상태(스냅샷·참가자·캐릭터 목록 등)를 초기화한다.
        // _runnerLock이 이미 획득된 상태에서만 호출해야 한다.
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
            _readyStateByPlayerId.Clear();
            _spawnedGameplayNetworkCharacters.Clear();
            _spawnedCharacterIndexByPlayerId.Clear();
            _currentOwnerPlayerId = -1;
            // GameEndPanel 표시 중에는 리셋하지 않는다.
            // StartAutoRoomJoinFromGameEndAsync에서 호출될 때 _isShowingGameEndPanel이 true이면
            // 리셋하면 Network.cs의 가드가 뚫려 ShowRoomPanel이 잘못 호출될 수 있다.
            if (!_isShowingGameEndPanel)
                ResetGameEndReturnState();
        }

        // 새 GameObject에 NetworkRunner를 추가하고 콜백을 등록한다. 성공 시 true를 반환한다.
        private bool TryCreateRunner(out NetworkRunner runner)
        {
            _runnerObject = new GameObject("LobbyNetworkRunner");
            runner = _runnerObject.AddComponent<NetworkRunner>();
            if (runner == null)
            {
                Destroy(_runnerObject);
                _runnerObject = null;
                return false;
            }

            // 호스트 마이그레이션 시 씬에 배치된 NetworkObject(물, DeathZone 등)가
            // runner 종료 때 파괴되지 않도록 커스텀 Provider를 등록한다.
            // 기본 NetworkObjectProviderDefault는 DestroySceneObject()에서
            // Destroy(gameObject)를 호출해 씬 오브젝트를 제거한다.
            _runnerObject.AddComponent<MigrationAwareNetworkObjectProvider>();

            runner.ProvideInput = true;
            runner.AddCallbacks(this);
            Debug.Log($"[Lobby] Runner created: object={_runnerObject.name}");
            return true;
        }

        // PhotonAppSettings에서 앱 설정을 복사하고 AppVersion·FixedRegion 기본값을 보장한다.
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

        // 닉네임을 UTF-8 바이트 배열로 인코딩해 Fusion 연결 토큰으로 반환한다.
        private void EnsureLocalClientId()
        {
            if (!string.IsNullOrWhiteSpace(_localClientId))
            {
                return;
            }

            // 같은 PC에서 여러 클라이언트를 동시에 띄우는 테스트를 지원하기 위해
            // PlayerPrefs 고정값이 아니라 실행 인스턴스마다 고유한 clientId를 사용한다.
            _localClientId = Guid.NewGuid().ToString("N");
        }

        private byte[] BuildConnectionToken(string nickname)
        {
            EnsureLocalClientId();
            var safeName = SanitizeNameToken(nickname);
            if (string.IsNullOrEmpty(safeName) || string.IsNullOrWhiteSpace(_localClientId))
            {
                return Array.Empty<byte>();
            }

            return Encoding.UTF8.GetBytes($"{_localClientId}{ConnectionTokenSeparator}{safeName}");
        }

        private static ConnectionTokenInfo ParseConnectionToken(byte[] token)
        {
            if (token == null || token.Length == 0)
            {
                return default;
            }

            try
            {
                var raw = Encoding.UTF8.GetString(token);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return default;
                }

                var separatorIndex = raw.IndexOf(ConnectionTokenSeparator);
                if (separatorIndex <= 0 || separatorIndex >= raw.Length - 1)
                {
                    return new ConnectionTokenInfo(string.Empty, SanitizeNameToken(raw));
                }

                var clientId = raw.Substring(0, separatorIndex).Trim();
                var nickname = SanitizeNameToken(raw.Substring(separatorIndex + 1));
                return new ConnectionTokenInfo(clientId, nickname);
            }
            catch
            {
                return default;
            }
        }

        private static string DecodeConnectionToken(byte[] token) => ParseConnectionToken(token).Nickname;
        private static string DecodeConnectionTokenClientId(byte[] token) => ParseConnectionToken(token).ClientId;

        // 플레이어를 _roomParticipantsByPlayerId에 등록하고 이전에 선택한 캐릭터 인덱스를 유지한다.
        private void RegisterParticipant(PlayerRef player, string nickname, string clientId = null)
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

            EnsureLocalClientId();
            var safeClientId = string.IsNullOrWhiteSpace(clientId)
                ? (player == _runner?.LocalPlayer ? _localClientId : string.Empty)
                : clientId.Trim();
            if (string.IsNullOrWhiteSpace(safeClientId))
            {
                return;
            }

            var selectedCharacter = SanitizeCharacterIndexOrNone(
                _selectedCharacterIndexByPlayerId.TryGetValue(player.PlayerId, out var existing) ? existing : -1);
            _selectedCharacterIndexByPlayerId[player.PlayerId] = selectedCharacter;

            _roomParticipantsByPlayerId[player.PlayerId] = new ParticipantPresence
            {
                PlayerId = player.PlayerId,
                ClientId = safeClientId,
                Nickname = safeName,
                CharacterIndex = selectedCharacter
            };
        }

        // 연결 토큰에서 닉네임을 추출해 플레이어를 등록한다.
        // 토큰이 없는 로컬 플레이어는 _nickname으로 폴백한다.
        private void TryRegisterParticipantFromToken(NetworkRunner runner, PlayerRef player)
        {
            if (runner == null || !runner.IsRunning || !player.IsRealPlayer)
            {
                return;
            }

            var tokenInfo = ParseConnectionToken(runner.GetPlayerConnectionToken(player));
            var nickname = tokenInfo.Nickname;
            var clientId = tokenInfo.ClientId;
            if (string.IsNullOrEmpty(nickname) && player == runner.LocalPlayer)
            {
                nickname = _nickname;
            }

            if (string.IsNullOrWhiteSpace(clientId) && player == runner.LocalPlayer)
            {
                EnsureLocalClientId();
                clientId = _localClientId;
            }

            RegisterParticipant(player, nickname, clientId);
        }

        // 현재 참가자 목록과 방장 PlayerId를 직렬화해 ReliableData 전송용 문자열을 생성한다.
        // 포맷: "{ownerPlayerId};{id}={clientId}^{nickname}^{charIdx}^{isReady}|..."
        private string BuildRosterPayload()
        {
            var entries = _roomParticipantsByPlayerId.Values
                .OrderBy(p => p.PlayerId)
                .Where(p => !string.IsNullOrWhiteSpace(p?.Nickname) && !string.IsNullOrWhiteSpace(p.ClientId))
                .Select(p =>
                {
                    var isReady = _readyStateByPlayerId.TryGetValue(p.PlayerId, out var ready) && ready;
                    return $"{p.PlayerId}={p.ClientId}^{p.Nickname}^{SanitizeCharacterIndexOrNone(p.CharacterIndex)}^{(isReady ? 1 : 0)}";
                });
            return $"{_currentOwnerPlayerId};{string.Join("|", entries)}";
        }

        // BuildRosterPayload가 생성한 문자열을 파싱해 _roomParticipantsByPlayerId와 _selectedCharacterIndexByPlayerId를 갱신한다.
        private void ApplyRosterPayload(string payload)
        {
            _roomParticipantsByPlayerId.Clear();
            _selectedCharacterIndexByPlayerId.Clear();
            _readyStateByPlayerId.Clear();

            if (string.IsNullOrWhiteSpace(payload))
            {
                _currentOwnerPlayerId = -1;
                SyncCurrentRoomOwnerFromRoster();
                return;
            }

            // 포맷 예시: "3;1=clientA^Alice^0|2=clientB^Bob^2|3=clientC^Charlie^1"
            // ';' 앞은 방장 PlayerId, 뒤는 '|' 구분 참가자 목록
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

            // 각 참가자 항목: "{PlayerId}={Nickname}^{CharacterIndex}"
            var entries = payload.Split('|');
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                // '=' 기준으로 PlayerId와 나머지(닉네임^캐릭터인덱스)를 분리
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
                var valueTokens = rawValue.Split('^');
                if (valueTokens.Length < 3)
                {
                    continue;
                }

                var clientId = valueTokens[0].Trim();
                var nickname = SanitizeNameToken(valueTokens[1]);
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrEmpty(nickname))
                {
                    continue;
                }

                var characterIndex = -1;
                if (int.TryParse(valueTokens[2], out var parsed))
                {
                    characterIndex = parsed;
                }

                characterIndex = SanitizeCharacterIndexOrNone(characterIndex);
                _selectedCharacterIndexByPlayerId[playerId] = characterIndex;

                var isReady = false;
                if (valueTokens.Length >= 4 && int.TryParse(valueTokens[3], out var readyVal))
                {
                    isReady = readyVal != 0;
                }

                _readyStateByPlayerId[playerId] = isReady;
                _roomParticipantsByPlayerId[playerId] = new ParticipantPresence
                {
                    PlayerId = playerId,
                    ClientId = clientId,
                    Nickname = nickname,
                    CharacterIndex = characterIndex,
                    IsReady = isReady
                };
            }

            SyncCurrentRoomOwnerFromRoster();

            // 로스터 수신 후 로컬 플레이어의 준비 상태를 동기화한다.
            if (_runner != null && _runner.IsRunning && _runner.LocalPlayer.IsRealPlayer)
            {
                var localId = _runner.LocalPlayer.PlayerId;
                if (_readyStateByPlayerId.TryGetValue(localId, out var localReady))
                {
                    _localIsReady = localReady;
                }
            }

            UpdatePlayerSlots();
            RefreshCharacterSelectionUiState();
        }

        // 서버에서 모든 플레이어에게 최신 참가자 Roster를 ReliableData로 전송한다.
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

            UpdatePlayerSlots();
            RefreshCharacterSelectionUiState();
        }

        // _currentOwnerPlayerId 기준으로 _currentRoomOwner 닉네임을 참가자 목록과 동기화한다.
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

        // 세션 커스텀 프로퍼티에 현재 방장 닉네임을 기록해 로비 세션 목록에 반영한다.
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

        // 방장이 퇴장했을 때 남은 플레이어 중 PlayerId가 가장 낮은 플레이어를 새 방장으로 지정한다.
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



        // 방 만들기 모달의 유효성 검사 메시지를 갱신한다.
        private void SetCreateValidation(string message)
        {
            if (createValidationText != null)
            {
                createValidationText.text = message;
                createValidationText.gameObject.SetActive(!string.IsNullOrEmpty(message));

                ForceRebuildRoomModalLayout();
            }
        }



        // 현재 방 목록의 방장 닉네임과 비교해 중복 여부를 반환한다.
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

        // 현재 방 목록에 동일한 이름의 방이 이미 존재하는지 확인한다.
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

        // 초기 세션 목록이 수신될 때까지 최대 2초간 대기한다. 닉네임 중복 검사 전에 호출한다.
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

        // 캔버스 루트 RectTransform의 스케일과 앵커가 비정상이면 화면 전체를 채우도록 초기화한다.
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

        // roomItemTemplate이 roomListContent의 자식이 아닌 경우 이동해 계층을 정규화한다.
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

        // 캐릭터 선택 UI 참조가 Inspector에서 올바르게 할당됐는지 확인한다.
        // 누락된 참조가 있으면 경고 로그를 출력한다.
        private void EnsureCharacterSelectionUi()
        {
            if (characterSelectionPanel == null ||
                selectSsatyCharacterButton == null ||
                selectAlGCharacterButton == null ||
                selectFitCharacterButton == null ||
                selectWiseCharacterButton == null ||
                selectRandomCharacterButton == null)
            {
                Debug.LogWarning("[Lobby] 캐릭터 선택 UI 참조가 Inspector에 할당되지 않았습니다. CharacterSelectionPanel과 4개의 선택 버튼을 Inspector에서 연결해 주세요.");
            }
        }

        // 캐릭터 선택 버튼의 활성·비활성 상태를 갱신한다.
        // 본인이 선택한 캐릭터와 다른 플레이어가 선택한 캐릭터의 버튼은 비활성화한다.
        private void RefreshCharacterSelectionUiState()
        {
            EnsureCharacterSelectionUi();
            TrySetGameObjectActive(characterSelectionPanel, roomPanel != null && roomPanel.activeSelf);

            var canSelect = roomPanel != null &&
                            roomPanel.activeSelf &&
                            _runner != null &&
                            _runner.IsRunning &&
                            _runner.LocalPlayer.IsRealPlayer &&
                            !_localIsReady; // 준비 상태면 캐릭터 변경 불가
            var localPlayerId = canSelect ? _runner.LocalPlayer.PlayerId : -1;
            var localSelected = canSelect && _selectedCharacterIndexByPlayerId.TryGetValue(localPlayerId, out var current)
                ? SanitizeCharacterIndexOrNone(current)
                : -1;

            // 다른 플레이어가 이미 선택한 캐릭터 인덱스 수집 (스냅샷으로 순회 중 변경 방지)
            var takenByOthers = new System.Collections.Generic.HashSet<int>();
            if (canSelect)
            {
                var snapshot = new System.Collections.Generic.Dictionary<int, int>(_selectedCharacterIndexByPlayerId);
                foreach (var kvp in snapshot)
                {
                    if (kvp.Key == localPlayerId) continue;
                    var idx = SanitizeCharacterIndexOrNone(kvp.Value);
                    if (idx >= 0) takenByOthers.Add(idx);
                }
            }

            if (selectSsatyCharacterButton != null)
            {
                TrySetButtonInteractable(selectSsatyCharacterButton,
                    canSelect && localSelected != (int)CharacterKind.Ssaty && !takenByOthers.Contains((int)CharacterKind.Ssaty));
            }

            if (selectAlGCharacterButton != null)
            {
                TrySetButtonInteractable(selectAlGCharacterButton,
                    canSelect && localSelected != (int)CharacterKind.AlG && !takenByOthers.Contains((int)CharacterKind.AlG));
            }

            if (selectFitCharacterButton != null)
            {
                TrySetButtonInteractable(selectFitCharacterButton,
                    canSelect && localSelected != (int)CharacterKind.Fit && !takenByOthers.Contains((int)CharacterKind.Fit));
            }

            if (selectWiseCharacterButton != null)
            {
                TrySetButtonInteractable(selectWiseCharacterButton,
                    canSelect && localSelected != (int)CharacterKind.Wise && !takenByOthers.Contains((int)CharacterKind.Wise));
            }

            if (selectRandomCharacterButton != null)
            {
                TrySetButtonInteractable(selectRandomCharacterButton,
                    canSelect && localSelected != (int)CharacterKind.Random);
            }

            characterPreview?.RefreshSelectionUiState(canSelect, localSelected, takenByOthers);
        }

        // MissingReferenceException을 무시하고 안전하게 GameObject의 활성 상태를 변경한다.
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

        // MissingReferenceException을 무시하고 안전하게 버튼의 interactable 상태를 변경한다.
        private static void TrySetButtonInteractable(Button target, bool interactable)
        {
            if (target == null)
            {
                return;
            }

            try
            {
                target.interactable = interactable;

                target.enabled = false;
                target.enabled = true;
            }
            catch (MissingReferenceException)
            {
            }
        }

        // 씬 전환에 필요한 NetworkSceneManagerDefault를 가져오거나 없으면 추가한다.
        private NetworkSceneManagerDefault GetOrAddSceneManager()
        {
            return GetComponent<NetworkSceneManagerDefault>() ?? gameObject.AddComponent<NetworkSceneManagerDefault>();
        }

        // Fusion SessionInfo로부터 UI 표시용 RoomSnapshot을 생성한다.
        // started 플래그가 true면 이름을 비워 로비 목록에서 제외한다.
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

        // 세션 프로퍼티에서 bool 값을 읽는다. bool/int/string 타입을 모두 처리한다.
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

        // 세션 프로퍼티에서 string 값을 읽는다. int/bool 타입도 문자열로 변환해 반환한다.
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

        // 세션 MaxPlayers 값을 읽는다. 예외 발생 시 상수 MaxPlayers를 반환한다.
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

        // 세션 IsOpen 값을 읽는다. 예외 발생 시 true(입장 가능)로 폴백한다.
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

        private static string SanitizeNameToken(string value) => LobbyInputValidator.SanitizeNameToken(value);
        private static bool IsWithinNameLengthLimit(string value) => LobbyInputValidator.IsWithinNameLengthLimit(value);
        private static bool ContainsHangul(string value) => LobbyInputValidator.ContainsHangul(value);

        // 입력 필드의 텍스트가 허용 문자 및 길이 제한을 벗어난 경우 정규화된 값으로 교체한다.
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

        // 허용 문자(영문·숫자·공백·., _-)만 남기고 한글 포함 시 8자, 아니면 16자로 자른다.
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

        // TMP_InputField.onValidateInput 콜백. 숫자가 아닌 문자는 '\0'으로 차단한다.
        private static char ValidateNumericPasswordChar(string text, int charIndex, char addedChar)
        {
            return LobbyInputValidator.IsNumericPasswordChar(addedChar) ? addedChar : '\0';
        }

        private static bool IsNumericPassword(string value) => LobbyInputValidator.IsNumericPassword(value);
        private static bool IsNumericPasswordChar(char c) => LobbyInputValidator.IsNumericPasswordChar(c);

        // 비밀번호 입력 필드를 숫자 전용 키패드·단일 라인으로 설정한다.
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

        // 비밀번호 입력 필드에서 숫자 외 문자가 포함된 경우 필터링해 교체한다.
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

        private static string FilterNumericPassword(string value) => LobbyInputValidator.FilterNumericPassword(value);

        public void OpenEditNicknameModal()
        {
            if (editNicknameModal != null)
            {
                editNicknameModal.SetActive(true);
                if (editNicknameInput != null)
                {
                    editNicknameInput.text = _nickname;
                }
            }
        }

        public void OnEditButtonClicked()
        {
            if (editNicknameModal == null) return;

            var newName = (editNicknameInput.text ?? string.Empty).Trim();
            newName = SanitizeNameToken(newName);

            if (!string.IsNullOrEmpty(newName))
            {
                _nickname = newName;

                if(mainPanelNicknameText != null)
                {
                    mainPanelNicknameText.text = _nickname;
                }

                if (editNicknameModal != null) editNicknameModal.SetActive(false);
            }
        }

        private void ForceRebuildRoomModalLayout()
        {
            Canvas.ForceUpdateCanvases();

            if (createRoomModal != null && createRoomModal.transform.childCount > 0)
            {
                var dialogRect = createRoomModal.transform.GetChild(0).GetComponent<RectTransform>();
                if (dialogRect != null)
                {
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRect);
                }
            }
        }

        public void QuitGame()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}

