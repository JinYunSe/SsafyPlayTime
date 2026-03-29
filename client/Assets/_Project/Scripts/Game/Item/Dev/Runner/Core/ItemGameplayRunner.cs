/*
 * 파일 개요:
 * - ItemGameplayRunner 스크립트가 들어 있는 파일이다.
 * - Dev/Runner/Core 계층에서 ItemGameplayRunner의 생명주기, 입력, 이벤트 연결 같은 중심 흐름을 관리한다.
 * - 테스트용 실행 경로를 정리하는 파일이므로, 실게임 로직과 분리된 검증 허브라는 성격을 유지해야 한다.
 */
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SSAFYPlayTime.Gameplay.Items
{
    /// <summary>
    /// ItemScene에서 아이템 런타임을 실행하고 테스트 입력(단축키)을 전달하는 러너.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class ItemGameplayRunner : MonoBehaviour
    {
        private const string RuntimeSceneName = "ItemScene";
        private const string BlackholeVisualName = "Item_Blackhole";
        private const float AudioListenerCheckIntervalSec = 1f;

        [Header("참조")]
        [SerializeField] private ItemRuntimeHost itemRuntimeHost;
        [SerializeField] private Transform targetRoot;
        [SerializeField] private bool runOnlyInItemScene = true;

        [Header("로컬 테스트 컨트롤")]
        [SerializeField] private bool enableLocalDebugController = true;
        [SerializeField] private bool allowLocalDebugControllerForNetworkPlayer = false;
        [SerializeField] private bool enableTemporaryRightClickPickup = true;
        [SerializeField] private KeyCode temporaryPickupKey = KeyCode.Mouse1;
        [SerializeField] private bool resetRuntimeStateOnSpace = true;
        [SerializeField] private KeyCode localResetKey = KeyCode.R;
        [SerializeField] private float localMoveSpeed = 6f;
        [SerializeField] private float localTurnLerp = 12f;
        [SerializeField] private string localDebugDummyName = "PrototypeHitDummy";
        [SerializeField] private float localDebugDummyForwardOffset = 7f;
        [SerializeField] private float localDebugDummyHeight = 1f;
        [SerializeField] private float localCameraPivotHeight = 1.4f;
        [SerializeField] private float localCameraMouseSensitivity = 3f;
        [SerializeField] private float localCameraZoomSpeed = 2f;
        [SerializeField] private float localCameraMinPitch = -20f;
        [SerializeField] private float localCameraMaxPitch = 70f;
        [SerializeField] private float localCameraDefaultPitch = 20f;
        [SerializeField] private float localCameraMinDistance = 2f;
        [SerializeField] private float localCameraMaxDistance = 10f;
        [SerializeField] private float localCameraDefaultDistance = 5f;

        [Header("프리젠테이션")]
        [SerializeField] private bool enableRuntimePresentation = true;
        [SerializeField] private bool disableRuntimeAudioOutput = false;
        [SerializeField] private string soundAssetTableRelativePath = ItemCatalogLoader.DefaultSoundAssetPath;
        [SerializeField] private string vfxAssetTableRelativePath = ItemCatalogLoader.DefaultVfxAssetPath;
        [SerializeField] private float defaultSfxVolume = 1f;

        [Header("블랙홀")]
        [SerializeField] private float blackholeThrowSpeed = 8f;
        [SerializeField] private float blackholeThrowArc = 0.35f;
        [SerializeField] private float blackholePullStrengthMultiplier = 2.25f;
        [SerializeField] private float blackholeExpandSpeedMultiplier = 1.5f;
        [SerializeField] private float blackholePlayerPullMultiplier = 1.5f;
        [SerializeField] private float blackholeItemPullMultiplier = 1.5f;
        [SerializeField] private float blackholePlayerEscapeDamping = 0.2f;
        [SerializeField] private string blackholeEffectPrefabAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple.prefab";
        [SerializeField] private float blackholeEffectScale = 0.7f;
        [SerializeField] private Color blackholeOuterLayerColor = new Color(0.08f, 0.02f, 0.14f, 0.28f);
        [SerializeField] private Color blackholeOuterLayerRimColor = new Color(0.52f, 0.3f, 0.82f, 0.45f);
        [SerializeField] private bool enableBlackholeTargetOutline = true;
        [SerializeField] private Color blackholeTargetOutlineColor = new Color(0.72f, 0.56f, 1f, 0.9f);
        [SerializeField] private float blackholeTargetOutlineScaleMultiplier = 1.045f;

        [Header("화염방사기")]
        [SerializeField] private LayerMask physicsMask = ~0;
        [SerializeField] private bool applyFlamethrowerDamageToDummy = true;
        [SerializeField] private bool applyFlamethrowerPushForce = true;
        [SerializeField] private float flamethrowerDamageRangeMultiplier = 0.5f;
        [SerializeField] private string flamethrowerEffectPrefabAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Misc/FlamethrowerBlocky.prefab";
        [SerializeField] private float flamethrowerEffectScale = 1f;

        [Header("위성 폭격")]
        [SerializeField] private string satelliteProjectilePrefabAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Combat/Missiles/Sci-Fi/Antimatter/AntimatterMissileBlue.prefab";
        [SerializeField] private string satelliteBeamChargeupPrefabAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Chargeup/BeamupChargeupBlue.prefab";
        [SerializeField] private string satelliteBeamCloudPrefabAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Cloud/BeamupCloudBlue.prefab";
        [SerializeField] private string satelliteBeamCylinderPrefabAssetPath =
            "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Cylinder/BeamupCylinderBlue.prefab";
        [SerializeField] private float satelliteProjectileVisualScale = 1f;
        [SerializeField] private float satelliteBeamChargeupScale = 1f;
        [SerializeField] private float satelliteBeamCloudScale = 1f;
        [SerializeField] private float satelliteBeamCylinderScale = 1f;

        [Header("로그")]
        [SerializeField] private bool enableStatusLog = true;

        private readonly Collider[] _flamethrowerOverlapBuffer = new Collider[128];
        private readonly Collider[] _blackholeOverlapBuffer = new Collider[256];
        private readonly HashSet<int> _flamethrowerUniqueTargetIds = new();
        private readonly Dictionary<string, SoundAssetTableCsvLoader.Row> _soundRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, VfxAssetTableCsvLoader.Row> _vfxRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AudioClip> _audioClipCache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AudioSource> _loopingSfxSources = new(StringComparer.Ordinal);
        private readonly Dictionary<Type, MethodInfo> _damageApplyMethodCache = new();

        private bool _eventsBound;
        private float _flamethrowerRangeMultiplier = 1f;
        private readonly HashSet<Coroutine> _activeBlackholeRoutines = new();
        private ParticleSystem _flamethrowerParticle;
        private ParticleSystem[] _flamethrowerParticles = Array.Empty<ParticleSystem>();
        private GameObject _flamethrowerFxRoot;
        private GameObject _flamethrowerEffectPrefabCache;
        private GameObject _blackholeEffectPrefabCache;
        private GameObject _satelliteProjectilePrefabCache;
        private GameObject _satelliteBeamChargeupPrefabCache;
        private GameObject _satelliteBeamCloudPrefabCache;
        private GameObject _satelliteBeamCylinderPrefabCache;
        private Material _blackholeOuterLayerFallbackMaterial;
        private Material _blackholeTargetOutlineMaterial;
        private readonly Dictionary<Transform, BlackholeOutlineState> _blackholeOutlineStates = new();
        private bool _presentationLoaded;
        private AudioListener _fallbackAudioListener;
        private float _nextAudioListenerCheckTime;
        private bool _audioPolicyCaptured;
        private bool _previousAudioListenerPause;
        private float _previousAudioListenerVolume;
        private Transform _debugControlTarget;
        private Rigidbody _debugControlBody;
        private Vector3 _debugInitialPosition;
        private Quaternion _debugInitialRotation;
        private bool _debugControlInitialized;
        private Camera _debugRuntimeCamera;
        private float _debugDefaultYaw;
        private float _debugYaw;
        private float _debugPitch;
        private float _debugDistance;
        private RigidbodyConstraints _debugOriginalConstraints;
        private bool _debugConstraintsCaptured;
        private bool _debugControllerSkipLogged;
        private Transform _debugDummyTarget;
        private Rigidbody _debugDummyBody;
        private bool _flamethrowerVisualActive;
        private float _flamethrowerVisualRange;
        private float _flamethrowerVisualRadius;

        private sealed class BlackholeOutlineState
        {
            public int RefCount;
            public readonly List<GameObject> ProxyObjects = new();
        }

        private Vector3 GetTargetPosition()
        {
            return targetRoot != null ? targetRoot.position : transform.position;
        }

        private Vector3 GetTargetForward()
        {
            var forward = targetRoot != null ? targetRoot.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return forward.normalized;
        }

        private void LogStatus(string message)
        {
            if (!enableStatusLog)
            {
                return;
            }

            Debug.Log($"[ItemGameplayRunner] {message}", this);
        }

        public void SetRunOnlyInItemScene(bool enabled)
        {
            runOnlyInItemScene = enabled;
        }

        public void SetTemporaryPickupInput(bool enabled, KeyCode pickupKey)
        {
            enableTemporaryRightClickPickup = enabled;
            temporaryPickupKey = pickupKey;
        }

        public void SetLocalDebugControllerEnabled(bool enabled)
        {
            enableLocalDebugController = enabled;
        }
    }
}

