using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed partial class ItemPrototypeHotkeyRunner : MonoBehaviour
    {
        private const float UiPanelWidth = 360f;
        private const float UiLineHeight = 22f;
        private const float RunnerLookupIntervalSec = 1f;

        private const string DefaultItemTableRelativePath = "_Project/Data/ItemTable.csv";
        private const string DefaultSoundAssetTableRelativePath = "_Project/Data/SoundAssetTable.csv";
        private const string DefaultVfxAssetTableRelativePath = "_Project/Data/VfxAssetTable.csv";
        private const string DefaultItemPresentationTableRelativePath = "_Project/Data/ItemPresentationTable.csv";
        private const string ItemIdBlackholeBomb = "ITEM_BLACKHOLE_BOMB";
        private const string ItemIdGrowth = "ITEM_GROWTH";
        private const string ItemIdShrink = "ITEM_SHRINK";
        private const string ItemIdAmericano = "ITEM_AMERICANO";
        private const string ItemIdFlamethrower = "ITEM_FLAMETHROWER";
        private const string ItemIdInvisibility = "ITEM_INVISIBILITY";
        private const string ItemIdSatelliteStrike = "ITEM_SATELLITE_STRIKE";
        private const string PrototypeBlackholeName = "Prototype_Blackhole";
        private const string PrototypeSatelliteWarningName = "Prototype_SatelliteWarning";
        private const string PrototypeSatelliteExplosionName = "Prototype_SatelliteExplosion";
        private const string PrototypeHitDummyName = "PrototypeHitDummy";

        private static ItemPrototypeHotkeyRunner _instance;

        [Header("Authority")]
        [SerializeField] private bool hostAuthorityOnlyWhenRunnerExists = true;

        [Header("Data Source")]
        [SerializeField] private bool loadValuesFromItemTable = true;
        [SerializeField] private string itemTableRelativePath = DefaultItemTableRelativePath;
        [SerializeField] private bool loadAssetMetadataTables = true;
        [SerializeField] private string soundAssetTableRelativePath = DefaultSoundAssetTableRelativePath;
        [SerializeField] private string vfxAssetTableRelativePath = DefaultVfxAssetTableRelativePath;
        [SerializeField] private string itemPresentationTableRelativePath = DefaultItemPresentationTableRelativePath;
        [SerializeField] private bool enablePresentationFromTables = true;
        [SerializeField] private float defaultPrototypeSfxVolume = 1f;

        [Header("Target")]
        [SerializeField] private Transform targetRoot;
        [SerializeField] private bool autoCreateTargetIfMissing = true;
        [SerializeField] private bool autoCreatePrototypeGroundIfMissing = true;
        [SerializeField] private float prototypeGroundY = -0.5f;
        [SerializeField] private float prototypeGroundSize = 40f;
        [SerializeField] private bool autoAttachPrototypeControllerOnDummy = true;
        [SerializeField] private bool autoCreateHitDummyIfMissing = true;
        [SerializeField] private float hitDummyInitialHp = 300f;

        [Header("Blackhole Bomb")]
        [SerializeField] private float blackholeDelaySec = 3f;
        [SerializeField] private float blackholeDurationSec = 6f;
        [SerializeField] private float blackholeRadius = 9f;
        [SerializeField] private float blackholeForce = 8f;
        [SerializeField] private float blackholePullStrengthMultiplier = 2.25f;
        [SerializeField] private float blackholeFlamethrowerRangeBoostMultiplier = 3f;

        [Header("Growth/Shrink")]
        [SerializeField] private float growthDurationSec = 8f;
        [SerializeField] private float growthScaleMultiplier = 1.35f;
        [SerializeField] private float shrinkDurationSec = 8f;
        [SerializeField] private float shrinkScaleMultiplier = 0.7f;

        [Header("Americano")]
        [SerializeField] private float superArmorDurationSec = 3f;

        [Header("Flamethrower")]
        [SerializeField] private float flamethrowerMaxUseSec = 5f;
        [SerializeField] private float flamethrowerRange = 8f;
        [SerializeField] private float flamethrowerRadius = 1.2f;
        [SerializeField] private float flamethrowerPushForce = 2f;
        [SerializeField] private float flamethrowerTickIntervalSec = 0.2f;
        [SerializeField] private float flamethrowerDamagePerTick = 4f;

        [Header("Invisibility")]
        [SerializeField] private float invisibilityDurationSec = 8f;
        [SerializeField] private float invisibilityAlpha = 0.25f;

        [Header("Satellite Strike")]
        [SerializeField] private float satelliteWarningSec = 1.5f;
        [SerializeField] private float satelliteRadius = 5f;
        [SerializeField] private float satelliteForce = 14f;

        [Header("Physics")]
        [SerializeField] private LayerMask physicsMask = ~0;

        private readonly Dictionary<Renderer, Color[]> _originalRendererColors = new();

        private Coroutine _scaleRoutine;
        private Coroutine _invisibilityRoutine;
        private bool _hasBaseScale;
        private Vector3 _baseScale = Vector3.one;
        private bool _isScaleBuffActive;
        private float _scaleBuffEndTime;
        private string _scaleBuffLabel = string.Empty;
        private bool _isInvisibilityActive;
        private float _invisibilityEndTime;

        private bool _isSuperArmorActive;
        private float _superArmorEndTime;
        private TickTimer _superArmorTickTimer = TickTimer.None;

        private bool _isFlamethrowerActive;
        private float _flamethrowerEndTime;
        private float _nextFlamethrowerTickTime;
        private TickTimer _flamethrowerEndTickTimer = TickTimer.None;
        private TickTimer _flamethrowerTickTimer = TickTimer.None;
        private ParticleSystem _flamethrowerParticle;
        private float _baseFlamethrowerRange = -1f;
        private bool _isFlamethrowerRangeBoostedByBlackhole;
        private readonly Collider[] _flamethrowerOverlapBuffer = new Collider[128];
        private readonly HashSet<int> _flamethrowerUniqueTargetIds = new();
        private int _lastFlamethrowerTickHitCount;
        private float _lastFlamethrowerTickDamage;

        private PrototypeDamageDummy _hitDummy;

        private NetworkRunner _runnerCache;
        private float _nextRunnerLookupTime;
        private bool _itemTableApplied;
        private bool _assetTablesApplied;
        private Dictionary<string, ItemTableCsvLoader.Row> _itemTableRows;
        private ItemPrototypeDataCatalog _dataCatalog;
        private readonly Dictionary<string, AudioClip> _audioClipCache = new(System.StringComparer.Ordinal);
        private readonly Dictionary<string, AudioSource> _loopingSfxSources = new(System.StringComparer.Ordinal);

        private string _statusLine = "Ready";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null || FindObjectOfType<ItemPrototypeHotkeyRunner>() != null)
            {
                return;
            }

            var go = new GameObject("ItemPrototypeHotkeyRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<ItemPrototypeHotkeyRunner>();
        }

        public static void NotifyStunnedFromGameplay()
        {
            if (_instance == null)
            {
                return;
            }

            _instance.ApplyStunDrop();
        }

        public void NotifyStunnedFromSystem()
        {
            ApplyStunDrop();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadValuesFromItemTableIfNeeded();
            LoadAssetMetadataTablesIfNeeded();
            CaptureBaseFlamethrowerRangeIfNeeded();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            StopAllLoopingSfx();
        }

        private void OnDestroy()
        {
            StopAllLoopingSfx();
        }

        private void Start()
        {
            ResolveTarget();
        }

        private void Update()
        {
            HandleHotkeys();
            TickTimedStates();
            TickFlamethrower();
        }

        private void OnGUI()
        {
            DrawHotkeyGuide();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ResolveTarget();
        }

        private void HandleHotkeys()
        {
            var alpha1 = Input.GetKeyDown(KeyCode.Alpha1);
            var alpha2 = Input.GetKeyDown(KeyCode.Alpha2);
            var alpha3 = Input.GetKeyDown(KeyCode.Alpha3);
            var alpha4 = Input.GetKeyDown(KeyCode.Alpha4);
            var alpha5 = Input.GetKeyDown(KeyCode.Alpha5);
            var alpha6 = Input.GetKeyDown(KeyCode.Alpha6);
            var alpha7 = Input.GetKeyDown(KeyCode.Alpha7);
            var alpha0 = Input.GetKeyDown(KeyCode.Alpha0);

            var hasInput = alpha1 || alpha2 || alpha3 || alpha4 || alpha5 || alpha6 || alpha7 || alpha0;
            if (!hasInput)
            {
                return;
            }

            if (!CanRunPrototypeInput())
            {
                SetStatus("Host authority: client input ignored");
                return;
            }

            if (alpha1) TriggerBlackholeBomb();
            if (alpha2) TriggerGrowth();
            if (alpha3) TriggerShrink();
            if (alpha4) TriggerAmericano();
            if (alpha5) TriggerFlamethrower();
            if (alpha6) TriggerInvisibility();
            if (alpha7) TriggerSatelliteStrike();

            // 기절 드랍 규칙을 빠르게 확인하기 위한 프로토타입 전용 키
            if (alpha0) SimulateStunDrop();
        }

        private bool CanRunPrototypeInput()
        {
            if (!hostAuthorityOnlyWhenRunnerExists)
            {
                return true;
            }

            if (!TryGetRunner(out var runner))
            {
                return true;
            }

            return runner.IsServer;
        }
    }
}

