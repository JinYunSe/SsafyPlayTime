using System.Collections;
using System.Collections.Generic;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class NetworkPlayer
{
    private const string ReplicatedBlackholeVisualName = "Item_Blackhole_Replicated";
    private const string ReplicatedBlackholeFxName = "Item_BlackholeFx";
    private const string ReplicatedBlackholeShellName = "Item_BlackholeShell";
    private const string ReplicatedSatelliteVisualName = "Item_SatelliteStrike_Replicated";
    private const string ReplicatedSatelliteChargeName = "Item_SatelliteStrike_Charge";
    private const string ReplicatedSatelliteBeamName = "Item_SatelliteStrike_Beam";
    private const string ReplicatedFlamethrowerFxName = "Item_FlamethrowerFx_Replicated";
    private const string NetworkedItemEffectProxyAssetPath = "Assets/_Project/Prefabs/Effects/NetworkedItemEffectProxy.prefab";
    private const string NetworkedItemEffectProxyResourcePath = "_Project/Prefabs/Effects/NetworkedItemEffectProxy";
    private const string BlackholeVisualAssetPath = "Assets/_Project/Prefabs/Items/BlackholeBomb.prefab";
    private const string BlackholeVisualResourcePath = "_Project/Prefabs/Items/BlackholeBomb";
    private const string BlackholeEffectResourcePath = "Polygon Arsenal/Prefabs/Interactive/BlackHole/Mega/MegaBlackHolePurple";
    private const string FlamethrowerEffectAssetPath = "Assets/Polygon Arsenal/Prefabs/Misc/FlamethrowerBlocky.prefab";
    private const string FlamethrowerEffectResourcePath = "Polygon Arsenal/Prefabs/Misc/FlamethrowerBlocky";
    private const string SatelliteProjectileAssetPath =
        "Assets/Polygon Arsenal/Prefabs/Combat/Missiles/Sci-Fi/Antimatter/AntimatterMissileBlue.prefab";
    private const string SatelliteProjectileResourcePath =
        "Polygon Arsenal/Prefabs/Combat/Missiles/Sci-Fi/Antimatter/AntimatterMissileBlue";
    private const string SatelliteChargeupAssetPath =
        "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Chargeup/BeamupChargeupBlue.prefab";
    private const string SatelliteChargeupResourcePath =
        "Polygon Arsenal/Prefabs/Interactive/BeamUp/Chargeup/BeamupChargeupBlue";
    private const string SatelliteCloudAssetPath =
        "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Cloud/BeamupCloudBlue.prefab";
    private const string SatelliteCloudResourcePath =
        "Polygon Arsenal/Prefabs/Interactive/BeamUp/Cloud/BeamupCloudBlue";
    private const string SatelliteCylinderAssetPath =
        "Assets/Polygon Arsenal/Prefabs/Interactive/BeamUp/Cylinder/BeamupCylinderBlue.prefab";
    private const string SatelliteCylinderResourcePath =
        "Polygon Arsenal/Prefabs/Interactive/BeamUp/Cylinder/BeamupCylinderBlue";

    [Header("Item World Effects")]
    [SerializeField] private LayerMask itemWorldEffectMask = ~0;
    [SerializeField] private float blackholeLaunchForwardOffset = 0.7f;
    [SerializeField] private float blackholeLaunchHeightOffset = 1.2f;
    [SerializeField] private float blackholeLaunchVisualDuration = 0.35f;
    [SerializeField] private float blackholeVisualScale = 0.7f;
    [SerializeField] private float blackholeActivationScaleMultiplier = 3f;
    [SerializeField] private float blackholeVisualGroundOffset = 0.35f;
    [SerializeField] private float blackholeThrowSpeed = 8f;
    [SerializeField] private float blackholeThrowArc = 0.35f;
    [SerializeField] private bool enableReplicatedBlackholeSecondaryFx;
    [SerializeField] private float blackholePullStrengthMultiplier = 3.25f;
    [SerializeField] private float blackholeExpandSpeedMultiplier = 1.5f;
    [SerializeField] private float blackholePlayerPullMultiplier = 2.5f;
    [SerializeField] private float blackholeItemPullMultiplier = 1.5f;
    [SerializeField] private float blackholePlayerEscapeDamping = 0.45f;
    [SerializeField] private bool enableReplicatedBlackholeTargetOutline = true;
    [SerializeField] private Color replicatedBlackholeTargetOutlineColor = new(0.72f, 0.56f, 1f, 0.9f);
    [SerializeField] private float replicatedBlackholeTargetOutlineScaleMultiplier = 1.045f;
    [SerializeField] private float satelliteProjectileTravelSec = 0.35f;
    [SerializeField] private float satelliteBeamHeight = 24f;
    [SerializeField] private float flamethrowerVisualForwardOffset = 0.7f;
    [SerializeField] private float flamethrowerVisualHeightOffset = 1.2f;
    [SerializeField] private float flamethrowerVisualScale = 2f;
    [SerializeField] private Vector3 flamethrowerMuzzleLocalOffset = new(0f, 0f, 0.5f);
    [SerializeField] private Vector3 flamethrowerMuzzleLocalEulerOffset = Vector3.zero;
    [SerializeField] private bool enableItemWorldEffectLog;

    private readonly Collider[] _replicatedBlackholeOverlapBuffer = new Collider[256];
    private readonly Collider[] _replicatedSatelliteOverlapBuffer = new Collider[256];
    private readonly DefaultItemFieldPrefabResolver _replicatedEffectPrefabResolver = new();

    private bool _itemWorldEffectNetworkReady;
    private bool _itemWorldEffectEventsBound;
    private ItemRuntimeHost _itemWorldEffectBoundHost;
    private int _lastAppliedBlackholeSeq;
    private int _lastAppliedSatelliteStrikeSeq;
    private int _lastAppliedFlamethrowerTickSeq;
    private int _lastAppliedFlamethrowerStopSeq;
    private int _lastAppliedMeleeSwingSeq;
    private Coroutine _activeReplicatedBlackholeRoutine;
    private Coroutine _activeReplicatedSatelliteRoutine;
    private GameObject _blackholeVisualPrefabCache;
    private GameObject _blackholeEffectPrefabCache;
    private GameObject _flamethrowerEffectPrefabCache;
    private GameObject _satelliteProjectilePrefabCache;
    private GameObject _satelliteChargeupPrefabCache;
    private GameObject _satelliteCloudPrefabCache;
    private GameObject _satelliteCylinderPrefabCache;
    private GameObject _networkedItemEffectProxyPrefabCache;
    private GameObject _replicatedFlamethrowerFxRoot;
    private ParticleSystem[] _replicatedFlamethrowerParticles = System.Array.Empty<ParticleSystem>();
    private float _nextItemGameplayRunnerLookupTime;
    private bool _cachedHasItemGameplayRunner;
    private Material _replicatedBlackholeTargetOutlineMaterial;
    private readonly Dictionary<Transform, ReplicatedBlackholeOutlineState> _replicatedBlackholeOutlineStates = new();
    private NetworkObject _activeBlackholeEffectProxy;
    private NetworkObject _activeSatelliteProjectileEffectProxy;
    private NetworkObject _activeSatelliteChargeEffectProxy;
    private NetworkObject _activeSatelliteBeamEffectProxy;
    private NetworkObject _activeFlamethrowerEffectProxy;

    [Networked] private int NetworkedBlackholeSeq { get; set; }
    [Networked] private Vector3 NetworkedBlackholeCenter { get; set; }
    [Networked] private float NetworkedBlackholeDelaySec { get; set; }
    [Networked] private float NetworkedBlackholeDurationSec { get; set; }
    [Networked] private float NetworkedBlackholeRadius { get; set; }
    [Networked] private float NetworkedBlackholeForce { get; set; }
    [Networked] private int NetworkedSatelliteStrikeSeq { get; set; }
    [Networked] private Vector3 NetworkedSatelliteStrikeCenter { get; set; }
    [Networked] private float NetworkedSatelliteStrikeWarningSec { get; set; }
    [Networked] private float NetworkedSatelliteStrikeDurationSec { get; set; }
    [Networked] private float NetworkedSatelliteStrikeRadius { get; set; }
    [Networked] private float NetworkedSatelliteStrikeForce { get; set; }
    [Networked] private float NetworkedSatelliteStrikeBaseDamage { get; set; }
    [Networked] private float NetworkedSatelliteStrikeStunDamage { get; set; }
    [Networked] private NetworkBool NetworkedFlamethrowerActive { get; set; }
    [Networked] private int NetworkedFlamethrowerTickSeq { get; set; }
    [Networked] private int NetworkedFlamethrowerStopSeq { get; set; }
    [Networked] private Vector3 NetworkedFlamethrowerOrigin { get; set; }
    [Networked] private Vector3 NetworkedFlamethrowerForward { get; set; }
    [Networked] private float NetworkedFlamethrowerRange { get; set; }
    [Networked] private float NetworkedFlamethrowerRadius { get; set; }
    [Networked] private int NetworkedMeleeSwingSeq { get; set; }
    [Networked] private float NetworkedMeleeSwingDuration { get; set; }

    private sealed class ReplicatedBlackholeOutlineState
    {
        public int RefCount;
        public readonly List<GameObject> ProxyObjects = new();
    }

    private void OnDisable()
    {
        UnbindItemWorldEffectEvents();
        StopActiveItemWorldEffectCoroutines();
        ReleaseAllReplicatedBlackholeTargetOutlines();
        StopReplicatedFlamethrowerVisual();
    }

    private void MarkItemWorldEffectNetworkReady()
    {
        _itemWorldEffectNetworkReady = true;
        EnsureItemWorldEffectBindings();
    }

    private void EnsureItemWorldEffectBindings()
    {
        if (!_itemWorldEffectNetworkReady || _itemRuntimeHost == null)
        {
            return;
        }

        if (_itemWorldEffectBoundHost == _itemRuntimeHost && _itemWorldEffectEventsBound)
        {
            return;
        }

        UnbindItemWorldEffectEvents();
        _itemWorldEffectBoundHost = _itemRuntimeHost;
        _itemWorldEffectBoundHost.BlackholeRequested += HandleBlackholeRequested;
        _itemWorldEffectBoundHost.SatelliteStrikeRequested += HandleSatelliteStrikeRequested;
        _itemWorldEffectBoundHost.FlamethrowerStarted += HandleFlamethrowerStarted;
        _itemWorldEffectBoundHost.FlamethrowerTicked += HandleFlamethrowerTicked;
        _itemWorldEffectBoundHost.FlamethrowerStopped += HandleFlamethrowerStopped;
        _itemWorldEffectBoundHost.ItemConsumed += HandleItemConsumed;
        _itemWorldEffectBoundHost.ItemDropped += HandleRuntimeItemDropped;
        _itemWorldEffectBoundHost.MeleeSwingRequested += HandleMeleeSwingRequested;
        _itemWorldEffectEventsBound = true;
    }

    private void UnbindItemWorldEffectEvents()
    {
        if (!_itemWorldEffectEventsBound || _itemWorldEffectBoundHost == null)
        {
            _itemWorldEffectBoundHost = null;
            _itemWorldEffectEventsBound = false;
            return;
        }

        _itemWorldEffectBoundHost.BlackholeRequested -= HandleBlackholeRequested;
        _itemWorldEffectBoundHost.SatelliteStrikeRequested -= HandleSatelliteStrikeRequested;
        _itemWorldEffectBoundHost.FlamethrowerStarted -= HandleFlamethrowerStarted;
        _itemWorldEffectBoundHost.FlamethrowerTicked -= HandleFlamethrowerTicked;
        _itemWorldEffectBoundHost.FlamethrowerStopped -= HandleFlamethrowerStopped;
        _itemWorldEffectBoundHost.ItemConsumed -= HandleItemConsumed;
        _itemWorldEffectBoundHost.ItemDropped -= HandleRuntimeItemDropped;
        _itemWorldEffectBoundHost.MeleeSwingRequested -= HandleMeleeSwingRequested;
        _itemWorldEffectBoundHost = null;
        _itemWorldEffectEventsBound = false;
    }

    private bool CanWriteItemWorldEffectState()
    {
        return _itemWorldEffectNetworkReady &&
               Object != null &&
               Object.IsValid &&
               HasStateAuthority;
    }

    private void HandleBlackholeRequested(BlackholeSkillRequest request)
    {
        if (!CanWriteItemWorldEffectState())
        {
            ItemRuntimeLog.Warn(ItemIds.BlackholeBomb, "Blackhole request ignored because this peer does not have StateAuthority.", this);
            return;
        }

        NetworkedBlackholeCenter = request.Center;
        NetworkedBlackholeDelaySec = request.DelaySec;
        NetworkedBlackholeDurationSec = request.DurationSec;
        NetworkedBlackholeRadius = request.Radius;
        NetworkedBlackholeForce = request.Force;
        NetworkedBlackholeSeq++;
        ItemRuntimeLog.Info(ItemIds.BlackholeBomb, $"Blackhole request replicated: seq={NetworkedBlackholeSeq}, center={request.Center}, radius={request.Radius:0.00}", this);

        StartReplicatedBlackhole(request, applyGameplay: true);
    }

    private void HandleSatelliteStrikeRequested(SatelliteStrikeRequest request)
    {
        if (!CanWriteItemWorldEffectState())
        {
            ItemRuntimeLog.Warn(ItemIds.SatelliteStrike, "Satellite strike request ignored because this peer does not have StateAuthority.", this);
            return;
        }

        NetworkedSatelliteStrikeCenter = request.Center;
        NetworkedSatelliteStrikeWarningSec = request.WarningSec;
        NetworkedSatelliteStrikeDurationSec = request.DurationSec;
        NetworkedSatelliteStrikeRadius = request.Radius;
        NetworkedSatelliteStrikeForce = request.Force;
        NetworkedSatelliteStrikeBaseDamage = request.BaseDamage;
        NetworkedSatelliteStrikeStunDamage = request.StunDamage;
        NetworkedSatelliteStrikeSeq++;
        ItemRuntimeLog.Info(ItemIds.SatelliteStrike, $"Satellite strike request replicated: seq={NetworkedSatelliteStrikeSeq}, center={request.Center}, radius={request.Radius:0.00}", this);

        StartReplicatedSatelliteStrike(request, applyGameplay: true);
    }

    private void HandleFlamethrowerStarted(string itemId, float endAtSec)
    {
        if (!CanWriteItemWorldEffectState())
        {
            ItemRuntimeLog.Warn(itemId, "Flamethrower start ignored because this peer does not have StateAuthority.", this);
            return;
        }

        NetworkedFlamethrowerActive = true;
        ItemRuntimeLog.Info(itemId, $"Flamethrower start replicated: endAt={endAtSec:0.00}", this);
        var forward = transform.forward.sqrMagnitude > 0.0001f ? transform.forward.normalized : Vector3.forward;
        var origin = ResolveFlamethrowerEffectOrigin(forward);
        EnsureOrUpdateFlamethrowerEffectProxy(
            origin,
            forward,
            Mathf.Max(1f, NetworkedFlamethrowerRange),
            Mathf.Max(0.25f, NetworkedFlamethrowerRadius));
    }

    private void HandleFlamethrowerTicked(FlamethrowerTickRequest request)
    {
        if (!CanWriteItemWorldEffectState())
        {
            ItemRuntimeLog.Warn(ItemIds.Flamethrower, "Flamethrower tick ignored because this peer does not have StateAuthority.", this);
            return;
        }

        NetworkedFlamethrowerActive = true;
        NetworkedFlamethrowerOrigin = request.Origin;
        NetworkedFlamethrowerForward = request.Forward;
        NetworkedFlamethrowerRange = request.Range;
        NetworkedFlamethrowerRadius = request.Radius;
        NetworkedFlamethrowerTickSeq++;
        ItemRuntimeLog.Info(ItemIds.Flamethrower, $"Flamethrower tick replicated: seq={NetworkedFlamethrowerTickSeq}, origin={request.Origin}, range={request.Range:0.00}, radius={request.Radius:0.00}", this);

        var safeForward = request.Forward.sqrMagnitude > 0.0001f ? request.Forward.normalized : transform.forward;
        EnsureOrUpdateFlamethrowerEffectProxy(request.Origin, safeForward, request.Range, request.Radius);
    }

    private void HandleFlamethrowerStopped(string itemId)
    {
        if (!CanWriteItemWorldEffectState())
        {
            ItemRuntimeLog.Warn(itemId, "Flamethrower stop ignored because this peer does not have StateAuthority.", this);
            return;
        }

        NetworkedFlamethrowerActive = false;
        NetworkedFlamethrowerStopSeq++;
        ItemRuntimeLog.Info(itemId, $"Flamethrower stop replicated: seq={NetworkedFlamethrowerStopSeq}", this);
        StopReplicatedFlamethrowerVisual();
    }

    private void HandleItemConsumed(string itemId)
    {
        if (!CanWriteItemWorldEffectState())
        {
            ItemRuntimeLog.Warn(itemId, "Item consume broadcast ignored because this peer does not have StateAuthority.", this);
            return;
        }

        ItemRuntimeLog.Info(itemId, "Item consume broadcast requested.", this);
        BroadcastItemConsumed(itemId);
    }

    private void HandleMeleeSwingRequested(MeleeSwingRequest request)
    {
        if (!CanWriteItemWorldEffectState())
        {
            ItemRuntimeLog.Warn(request.ItemId, "Melee swing replication ignored because this peer does not have StateAuthority.", this);
            return;
        }

        NetworkedMeleeSwingDuration = request.ActiveDurationSec;
        NetworkedMeleeSwingSeq++;
        ItemRuntimeLog.Info(request.ItemId, $"Melee swing replicated: seq={NetworkedMeleeSwingSeq}, duration={request.ActiveDurationSec:0.00}", this);
    }

    private void ApplyReplicatedWorldItemEffects()
    {
        if (Object == null || !Object.IsValid || HasStateAuthority)
        {
            return;
        }

        if (NetworkedMeleeSwingSeq > 0 && _lastAppliedMeleeSwingSeq != NetworkedMeleeSwingSeq)
        {
            _lastAppliedMeleeSwingSeq = NetworkedMeleeSwingSeq;
            ItemRuntimeLog.Info(ItemIds.WaterMelonSword, $"Replicated melee swing applied: seq={NetworkedMeleeSwingSeq}, duration={NetworkedMeleeSwingDuration:0.00}", this);
            TriggerReplicatedMeleeSwing();
        }
    }

    private void TriggerReplicatedMeleeSwing()
    {
        var handler = GetComponentInChildren<ItemCharacterMeleeSwingHandler>(true);
        if (handler == null)
        {
            ItemRuntimeLog.Warn(ItemIds.WaterMelonSword, "Replicated melee swing skipped because ItemCharacterMeleeSwingHandler is missing.", this);
            return;
        }

        handler.TriggerReplicatedSwing(NetworkedMeleeSwingDuration);
    }

    private void StartReplicatedBlackhole(BlackholeSkillRequest request, bool applyGameplay)
    {
        if (_activeReplicatedBlackholeRoutine != null)
        {
            StopCoroutine(_activeReplicatedBlackholeRoutine);
        }

        _activeReplicatedBlackholeRoutine = StartCoroutine(CoReplicatedBlackhole(request, applyGameplay));
    }

    private void StartReplicatedSatelliteStrike(SatelliteStrikeRequest request, bool applyGameplay)
    {
        if (_activeReplicatedSatelliteRoutine != null)
        {
            StopCoroutine(_activeReplicatedSatelliteRoutine);
        }

        _activeReplicatedSatelliteRoutine = StartCoroutine(CoReplicatedSatelliteStrike(request, applyGameplay));
    }

    private void StopActiveItemWorldEffectCoroutines()
    {
        if (_activeReplicatedBlackholeRoutine != null)
        {
            StopCoroutine(_activeReplicatedBlackholeRoutine);
            _activeReplicatedBlackholeRoutine = null;
        }

        if (_activeReplicatedSatelliteRoutine != null)
        {
            StopCoroutine(_activeReplicatedSatelliteRoutine);
            _activeReplicatedSatelliteRoutine = null;
        }

        StopReplicatedFlamethrowerVisual();
        DespawnNetworkedItemEffectProxy(ref _activeBlackholeEffectProxy);
        DespawnNetworkedItemEffectProxy(ref _activeSatelliteProjectileEffectProxy);
        DespawnNetworkedItemEffectProxy(ref _activeSatelliteChargeEffectProxy);
        DespawnNetworkedItemEffectProxy(ref _activeSatelliteBeamEffectProxy);
        ReleaseAllReplicatedBlackholeTargetOutlines();
    }

    private IEnumerator CoReplicatedBlackhole(BlackholeSkillRequest request, bool applyGameplay)
    {
        if (!HasStateAuthority || Runner == null)
        {
            _activeReplicatedBlackholeRoutine = null;
            yield break;
        }

        var startPosition = transform.position + Vector3.up * blackholeLaunchHeightOffset + transform.forward * blackholeLaunchForwardOffset;
        var center = ResolveSatelliteGroundCenter(request.Center);
        var throwForward = ResolveReplicatedThrowForward(center);
        var startRotation = throwForward.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(throwForward, Vector3.up)
            : Quaternion.identity;
        var outlinedTargets = new HashSet<Transform>();
        var outlinedTargetsThisFrame = new HashSet<Transform>();

        DespawnNetworkedItemEffectProxy(ref _activeBlackholeEffectProxy);
        _activeBlackholeEffectProxy = SpawnNetworkedItemEffectProxy(
            startPosition,
            startRotation,
            proxy => proxy.InitializeBlackhole(request.Radius, throwForward));

        var visualRoot = _activeBlackholeEffectProxy != null ? _activeBlackholeEffectProxy.gameObject : null;
        var bombBody = visualRoot != null ? visualRoot.GetComponent<Rigidbody>() : null;
        var bombProxy = visualRoot != null ? visualRoot.GetComponent<NetworkedItemEffectProxy>() : null;
        if (visualRoot == null || bombBody == null || bombProxy == null)
        {
            _activeReplicatedBlackholeRoutine = null;
            yield break;
        }

        if (bombBody != null)
        {
            bombBody.isKinematic = true;
            bombBody.useGravity = false;
        }

        var delaySec = Mathf.Max(0f, request.DelaySec);
        if (delaySec > 0f)
        {
            var travelElapsed = 0f;
            var previousPosition = startPosition;
            var arcHeight = Mathf.Max(0.75f, Vector3.Distance(startPosition, center) * Mathf.Max(0.1f, blackholeThrowArc));

            while (travelElapsed < delaySec)
            {
                var step = Mathf.Max(0.001f, Time.deltaTime);
                travelElapsed = Mathf.Min(delaySec, travelElapsed + step);
                var t = Mathf.Clamp01(travelElapsed / delaySec);
                var linear = Vector3.Lerp(startPosition, center, t);
                var arcOffset = 4f * arcHeight * t * (1f - t);
                var nextPosition = linear + Vector3.up * arcOffset;
                var lookDirection = nextPosition - previousPosition;
                var rotation = lookDirection.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                    : visualRoot.transform.rotation;

                bombProxy.SyncNetworkPose(nextPosition, rotation);
                previousPosition = nextPosition;
                yield return null;
            }
        }

        center = ResolveSatelliteGroundCenter(request.Center);
        bombBody.velocity = Vector3.zero;
        bombBody.angularVelocity = Vector3.zero;
        bombBody.isKinematic = true;
        bombBody.useGravity = false;
        bombProxy.Radius = request.Radius;
        bombProxy.SetActivated(true);
        bombProxy.SyncNetworkPose(center, Quaternion.identity, zeroVelocity: true);

        var duration = Mathf.Max(0.1f, request.DurationSec);
        var radius = Mathf.Max(0.1f, request.Radius);
        var force = Mathf.Max(0f, request.Force);
        var expandDuration = Mathf.Max(0.05f, duration / Mathf.Max(0.1f, blackholeExpandSpeedMultiplier));
        var blackholeStartTime = Time.time;

        while (Time.time - blackholeStartTime < duration)
        {
            var activeElapsed = Time.time - blackholeStartTime;
            var ramp = Mathf.Clamp01(activeElapsed / expandDuration);
            visualRoot.transform.position = center;
            visualRoot.transform.rotation = Quaternion.identity;
            bombProxy.SyncNetworkPose(center, Quaternion.identity);

            if (applyGameplay)
            {
                ApplyBlackholeGameplay(center, radius, force, ramp, outlinedTargets, outlinedTargetsThisFrame);

                if (outlinedTargets.Count > 0)
                {
                    var releaseBuffer = new List<Transform>();
                    foreach (var target in outlinedTargets)
                    {
                        if (target == null || !outlinedTargetsThisFrame.Contains(target))
                        {
                            releaseBuffer.Add(target);
                        }
                    }

                    for (var i = 0; i < releaseBuffer.Count; i++)
                    {
                        var target = releaseBuffer[i];
                        ReleaseReplicatedBlackholeOutlineForTarget(target);
                        outlinedTargets.Remove(target);
                    }
                }

                outlinedTargetsThisFrame.Clear();
            }

            yield return null;
        }

        foreach (var target in outlinedTargets)
        {
            ReleaseReplicatedBlackholeOutlineForTarget(target);
        }

        DespawnNetworkedItemEffectProxy(ref _activeBlackholeEffectProxy);
        _activeReplicatedBlackholeRoutine = null;
    }

    private IEnumerator CoReplicatedSatelliteStrike(SatelliteStrikeRequest request, bool applyGameplay)
    {
        if (!HasStateAuthority || Runner == null)
        {
            _activeReplicatedSatelliteRoutine = null;
            yield break;
        }

        var center = ResolveSatelliteGroundCenter(request.Center);
        var launchOrigin = transform.position + Vector3.up * blackholeLaunchHeightOffset + transform.forward * blackholeLaunchForwardOffset;
        var throwForward = ResolveReplicatedThrowForward(request.Center);
        var throwDirection = (throwForward + Vector3.up * blackholeThrowArc).normalized;
        var velocity = throwDirection * Mathf.Max(0.1f, blackholeThrowSpeed);
        var gravity = Physics.gravity;
        var travelStartTime = Time.time;
        var current = launchOrigin;

        DespawnNetworkedItemEffectProxy(ref _activeSatelliteProjectileEffectProxy);
        _activeSatelliteProjectileEffectProxy = SpawnNetworkedItemEffectProxy(
            launchOrigin,
            Quaternion.identity,
            proxy => proxy.InitializeSatelliteProjectile(request.Radius, throwForward));

        var projectile = _activeSatelliteProjectileEffectProxy != null ? _activeSatelliteProjectileEffectProxy.gameObject : null;
        var projectileBody = projectile != null ? projectile.GetComponent<Rigidbody>() : null;
        var projectileProxy = projectile != null ? projectile.GetComponent<NetworkedItemEffectProxy>() : null;
        if (projectileBody != null)
        {
            projectileBody.mass = 1.5f;
            projectileBody.drag = 0.15f;
            projectileBody.angularDrag = 0.05f;
            projectileBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            projectileBody.interpolation = RigidbodyInterpolation.Interpolate;
            projectileBody.isKinematic = false;
            projectileBody.useGravity = true;
            projectileBody.velocity = velocity;
        }

        while (Time.time - travelStartTime < 3f)
        {
            var step = Mathf.Max(0.001f, Time.deltaTime);
            var nextVelocity = velocity + gravity * step;
            var next = current + velocity * step;
            var move = next - current;
            var distance = move.magnitude;

            if (distance > 0.0001f &&
                Physics.SphereCast(
                    current,
                    0.18f,
                    move.normalized,
                    out var hit,
                    distance,
                    itemWorldEffectMask,
                    QueryTriggerInteraction.Ignore))
            {
                center = hit.point + hit.normal.normalized * 0.02f;
                if (projectile != null)
                {
                    projectile.transform.position = center;
                    projectileProxy?.SyncNetworkPose(center, projectile.transform.rotation, zeroVelocity: true);
                }
                break;
            }

            if (projectile != null)
            {
                projectile.transform.position = next;
                var lookDirection = nextVelocity.sqrMagnitude > 0.0001f ? nextVelocity.normalized : velocity.normalized;
                if (lookDirection.sqrMagnitude > 0.0001f)
                {
                    projectile.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
                }

                projectileProxy?.SyncNetworkPose(projectile.transform.position, projectile.transform.rotation);
            }

            current = next;
            velocity = nextVelocity;
            center = ResolveSatelliteGroundCenter(current);
            yield return null;
        }

        DespawnNetworkedItemEffectProxy(ref _activeSatelliteProjectileEffectProxy);

        DespawnNetworkedItemEffectProxy(ref _activeSatelliteChargeEffectProxy);
        _activeSatelliteChargeEffectProxy = SpawnNetworkedItemEffectProxy(
            center + Vector3.up * 0.05f,
            Quaternion.identity,
            proxy => proxy.InitializeSatelliteCharge(request.Radius));

        if (request.WarningSec > 0f)
        {
            yield return new WaitForSeconds(request.WarningSec);
        }

        DespawnNetworkedItemEffectProxy(ref _activeSatelliteChargeEffectProxy);

        DespawnNetworkedItemEffectProxy(ref _activeSatelliteBeamEffectProxy);
        _activeSatelliteBeamEffectProxy = SpawnNetworkedItemEffectProxy(
            center,
            Quaternion.identity,
            proxy => proxy.InitializeSatelliteBeam(request.Radius));

        var duration = Mathf.Max(0.1f, request.DurationSec);
        var tickInterval = 0.25f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            if (applyGameplay)
            {
                ApplySatelliteStrikeGameplay(
                    center,
                    Mathf.Max(0.1f, request.Radius),
                    request.BaseDamage,
                    request.StunDamage,
                    request.Force,
                    tickInterval,
                    duration);
            }

            var waitSec = Mathf.Min(tickInterval, duration - elapsed);
            elapsed += waitSec;
            if (waitSec > 0f)
            {
                yield return new WaitForSeconds(waitSec);
            }
            else
            {
                yield return null;
            }
        }

        DespawnNetworkedItemEffectProxy(ref _activeSatelliteBeamEffectProxy);
        _activeReplicatedSatelliteRoutine = null;
    }

    private void ApplyBlackholeGameplay(
        Vector3 center,
        float radius,
        float force,
        float ramp,
        HashSet<Transform> outlinedTargets,
        HashSet<Transform> outlinedTargetsThisFrame)
    {
        var overlapCount = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            _replicatedBlackholeOverlapBuffer,
            itemWorldEffectMask,
            QueryTriggerInteraction.Ignore);

        for (var i = 0; i < overlapCount; i++)
        {
            var hitCollider = _replicatedBlackholeOverlapBuffer[i];
            if (hitCollider == null)
            {
                continue;
            }

            var body = hitCollider.attachedRigidbody;
            if (body == null || body.isKinematic)
            {
                continue;
            }

            var root = body.transform.root;
            var toCenter = center - body.worldCenterOfMass;
            if (toCenter.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            if (outlinedTargetsThisFrame != null && root != null && outlinedTargetsThisFrame.Add(root))
            {
                if (outlinedTargets != null && !outlinedTargets.Contains(root))
                {
                    ApplyReplicatedBlackholeOutlineForTarget(root);
                    outlinedTargets.Add(root);
                }
            }

            var distance = Mathf.Max(toCenter.magnitude, 0.35f);
            var pullMultiplier = root != null && root.CompareTag("Player")
                ? Mathf.Max(0.05f, blackholePlayerPullMultiplier)
                : Mathf.Max(0.05f, blackholeItemPullMultiplier);
            var pullStrength = (force * blackholePullStrengthMultiplier * (0.4f + ramp * 1.6f)) /
                               Mathf.Sqrt(distance) * pullMultiplier;

            if (root != null && root.CompareTag("Player"))
            {
                ApplyBlackholeEscapeDamping(body, toCenter.normalized);
                var inwardVelocityBoost = toCenter.normalized *
                                          ((force * 0.06f) + (ramp * 0.45f)) /
                                          Mathf.Max(0.75f, Mathf.Sqrt(distance));
                body.AddForce(inwardVelocityBoost, ForceMode.VelocityChange);
            }

            body.AddForce(toCenter.normalized * pullStrength, ForceMode.Acceleration);
        }
    }

    private void ApplyReplicatedBlackholeOutlineForTarget(Transform root)
    {
        if (!enableReplicatedBlackholeTargetOutline || root == null)
        {
            return;
        }

        if (_replicatedBlackholeOutlineStates.TryGetValue(root, out var existingState))
        {
            existingState.RefCount++;
            return;
        }

        var state = new ReplicatedBlackholeOutlineState
        {
            RefCount = 1
        };

        CreateReplicatedBlackholeOutlineProxies(root, state);
        if (state.ProxyObjects.Count == 0)
        {
            return;
        }

        _replicatedBlackholeOutlineStates[root] = state;
    }

    private void ReleaseReplicatedBlackholeOutlineForTarget(Transform root)
    {
        if (root == null)
        {
            return;
        }

        if (!_replicatedBlackholeOutlineStates.TryGetValue(root, out var state))
        {
            return;
        }

        state.RefCount--;
        if (state.RefCount > 0)
        {
            return;
        }

        DestroyReplicatedBlackholeOutlineState(state);
        _replicatedBlackholeOutlineStates.Remove(root);
    }

    private void ReleaseAllReplicatedBlackholeTargetOutlines()
    {
        if (_replicatedBlackholeOutlineStates.Count == 0)
        {
            return;
        }

        foreach (var pair in _replicatedBlackholeOutlineStates)
        {
            DestroyReplicatedBlackholeOutlineState(pair.Value);
        }

        _replicatedBlackholeOutlineStates.Clear();
    }

    private void CreateReplicatedBlackholeOutlineProxies(Transform root, ReplicatedBlackholeOutlineState state)
    {
        if (root == null || state == null)
        {
            return;
        }

        var outlineMaterial = GetOrCreateReplicatedBlackholeTargetOutlineMaterial();
        if (outlineMaterial == null)
        {
            return;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (!ShouldCreateReplicatedBlackholeOutlineProxy(renderer))
            {
                continue;
            }

            GameObject proxyObject = null;
            if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
            {
                proxyObject = CreateReplicatedBlackholeOutlineProxyForSkinnedRenderer(skinnedMeshRenderer, outlineMaterial);
            }
            else if (renderer is MeshRenderer meshRenderer)
            {
                proxyObject = CreateReplicatedBlackholeOutlineProxyForMeshRenderer(meshRenderer, outlineMaterial);
            }

            if (proxyObject != null)
            {
                state.ProxyObjects.Add(proxyObject);
            }
        }
    }

    private GameObject CreateReplicatedBlackholeOutlineProxyForMeshRenderer(MeshRenderer renderer, Material outlineMaterial)
    {
        if (renderer == null || outlineMaterial == null)
        {
            return null;
        }

        if (!renderer.TryGetComponent<MeshFilter>(out var meshFilter) || meshFilter.sharedMesh == null)
        {
            return null;
        }

        var proxy = new GameObject($"{renderer.gameObject.name}_BlackholeOutline");
        proxy.transform.SetParent(renderer.transform, false);
        proxy.transform.localPosition = Vector3.zero;
        proxy.transform.localRotation = Quaternion.identity;
        proxy.transform.localScale = Vector3.one * Mathf.Max(1f, replicatedBlackholeTargetOutlineScaleMultiplier);
        proxy.layer = renderer.gameObject.layer;

        var proxyFilter = proxy.AddComponent<MeshFilter>();
        proxyFilter.sharedMesh = meshFilter.sharedMesh;

        var proxyRenderer = proxy.AddComponent<MeshRenderer>();
        proxyRenderer.sharedMaterials = BuildReplicatedBlackholeOutlineMaterialArray(meshFilter.sharedMesh.subMeshCount, outlineMaterial);
        ConfigureReplicatedBlackholeOutlineRenderer(proxyRenderer);
        return proxy;
    }

    private GameObject CreateReplicatedBlackholeOutlineProxyForSkinnedRenderer(SkinnedMeshRenderer renderer, Material outlineMaterial)
    {
        if (renderer == null || outlineMaterial == null || renderer.sharedMesh == null)
        {
            return null;
        }

        var proxy = new GameObject($"{renderer.gameObject.name}_BlackholeOutline");
        proxy.transform.SetParent(renderer.transform, false);
        proxy.transform.localPosition = Vector3.zero;
        proxy.transform.localRotation = Quaternion.identity;
        proxy.transform.localScale = Vector3.one * Mathf.Max(1f, replicatedBlackholeTargetOutlineScaleMultiplier);
        proxy.layer = renderer.gameObject.layer;

        var proxyFilter = proxy.AddComponent<MeshFilter>();
        var proxyRenderer = proxy.AddComponent<MeshRenderer>();
        proxyRenderer.sharedMaterials = BuildReplicatedBlackholeOutlineMaterialArray(renderer.sharedMesh.subMeshCount, outlineMaterial);
        ConfigureReplicatedBlackholeOutlineRenderer(proxyRenderer);

        var proxyUpdater = proxy.AddComponent<BlackholeSkinnedOutlineProxy>();
        proxyUpdater.Initialize(renderer, proxyFilter);
        return proxy;
    }

    private static Material[] BuildReplicatedBlackholeOutlineMaterialArray(int subMeshCount, Material outlineMaterial)
    {
        var count = Mathf.Max(1, subMeshCount);
        var materials = new Material[count];
        for (var i = 0; i < count; i++)
        {
            materials[i] = outlineMaterial;
        }

        return materials;
    }

    private static void ConfigureReplicatedBlackholeOutlineRenderer(Renderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;
    }

    private bool ShouldCreateReplicatedBlackholeOutlineProxy(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled)
        {
            return false;
        }

        if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
        {
            return false;
        }

        var owner = renderer.gameObject;
        if (owner == null)
        {
            return false;
        }

        var objectName = owner.name ?? string.Empty;
        if (objectName.IndexOf("BlackholeOutline", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("Item_BlackholeFx", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            objectName.IndexOf("OuterLayer", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return owner.GetComponentInParent<ParticleSystem>() == null &&
               owner.GetComponentInParent<LineRenderer>() == null &&
               owner.GetComponentInParent<TrailRenderer>() == null;
    }

    private Material GetOrCreateReplicatedBlackholeTargetOutlineMaterial()
    {
        if (_replicatedBlackholeTargetOutlineMaterial != null)
        {
            return _replicatedBlackholeTargetOutlineMaterial;
        }

        var shader =
            Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
        {
            return null;
        }

        _replicatedBlackholeTargetOutlineMaterial = new Material(shader)
        {
            name = "Item_Blackhole_TargetOutline_Replicated",
            hideFlags = HideFlags.DontSave
        };

        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_BaseColor"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetColor("_BaseColor", replicatedBlackholeTargetOutlineColor);
        }
        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_Color"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetColor("_Color", replicatedBlackholeTargetOutlineColor);
        }
        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_Surface"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetFloat("_Surface", 1f);
        }
        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_Blend"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetFloat("_Blend", 0f);
        }
        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_SrcBlend"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }
        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_DstBlend"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }
        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_ZWrite"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetFloat("_ZWrite", 0f);
        }
        if (_replicatedBlackholeTargetOutlineMaterial.HasProperty("_Cull"))
        {
            _replicatedBlackholeTargetOutlineMaterial.SetFloat("_Cull", (float)CullMode.Front);
        }

        _replicatedBlackholeTargetOutlineMaterial.SetOverrideTag("RenderType", "Transparent");
        _replicatedBlackholeTargetOutlineMaterial.renderQueue = (int)RenderQueue.Transparent + 40;
        _replicatedBlackholeTargetOutlineMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _replicatedBlackholeTargetOutlineMaterial.EnableKeyword("_ALPHABLEND_ON");
        _replicatedBlackholeTargetOutlineMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        return _replicatedBlackholeTargetOutlineMaterial;
    }

    private static void DestroyReplicatedBlackholeOutlineState(ReplicatedBlackholeOutlineState state)
    {
        if (state == null)
        {
            return;
        }

        for (var i = 0; i < state.ProxyObjects.Count; i++)
        {
            var proxy = state.ProxyObjects[i];
            if (proxy != null)
            {
                Destroy(proxy);
            }
        }

        state.ProxyObjects.Clear();
    }

    private void ApplyBlackholeEscapeDamping(Rigidbody body, Vector3 toCenterDir)
    {
        var damping = Mathf.Clamp01(blackholePlayerEscapeDamping);
        if (body == null || damping <= 0f)
        {
            return;
        }

        var awayDir = -toCenterDir;
        var awaySpeed = Vector3.Dot(body.velocity, awayDir);
        if (awaySpeed <= 0f)
        {
            return;
        }

        body.velocity += toCenterDir * (awaySpeed * damping);
    }

    private void ApplySatelliteStrikeGameplay(
        Vector3 center,
        float radius,
        float totalHealthDamage,
        float totalStunDamage,
        float explosionForce,
        float tickInterval,
        float duration)
    {
        var tickCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.1f, duration) / Mathf.Max(0.01f, tickInterval)));
        var damagePerTick = Mathf.Max(0f, totalHealthDamage) / tickCount;
        var stunPerTick = Mathf.Max(0f, totalStunDamage) / tickCount;
        var capsuleOffset = Mathf.Max(0f, (satelliteBeamHeight * 0.5f) - radius);
        var bottom = center + Vector3.down * capsuleOffset;
        var top = center + Vector3.up * capsuleOffset;

        var overlapCount = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            radius,
            _replicatedSatelliteOverlapBuffer,
            itemWorldEffectMask,
            QueryTriggerInteraction.Ignore);

        for (var i = 0; i < overlapCount; i++)
        {
            var hitCollider = _replicatedSatelliteOverlapBuffer[i];
            if (hitCollider == null)
            {
                continue;
            }

            var targetPlayer = hitCollider.GetComponentInParent<NetworkPlayer>();
            if (targetPlayer != null && targetPlayer != this)
            {
                targetPlayer.ApplyCombinedDamage(
                    damagePerTick,
                    stunPerTick,
                    "SatelliteStrike",
                    0f,
                    explosionForce);
            }

            var body = hitCollider.attachedRigidbody;
            if (body != null && !body.isKinematic && explosionForce > 0f)
            {
                var offset = body.worldCenterOfMass - center;
                var planarOffset = new Vector3(offset.x, 0f, offset.z);
                var distance = Mathf.Max(0.1f, planarOffset.magnitude);
                var falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.1f, radius));
                var direction = planarOffset.sqrMagnitude > 0.0001f
                    ? planarOffset.normalized
                    : Random.insideUnitSphere.normalized;
                direction.y = Mathf.Max(0.15f, 0.35f);
                direction.Normalize();
                body.AddForce(direction * (explosionForce * Mathf.Max(0.15f, falloff)), ForceMode.VelocityChange);
            }
        }
    }

    private Vector3 ResolveSatelliteGroundCenter(Vector3 requestedCenter)
    {
        var rayOrigin = requestedCenter + Vector3.up * 30f;
        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out var hit,
                80f,
                itemWorldEffectMask,
                QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * 0.02f;
        }

        return requestedCenter + Vector3.up * 0.02f;
    }

    private Vector3 ResolveReplicatedThrowForward(Vector3 targetPosition)
    {
        var forward = targetPosition - transform.position;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude <= 0.0001f)
        {
            return Vector3.forward;
        }

        return forward.normalized;
    }

    private GameObject CreateReplicatedBlackholeVisual(Vector3 startPosition)
    {
        var prefab = TryLoadReplicatedBlackholeVisualPrefab();
        if (prefab != null)
        {
            var instance = Instantiate(prefab, startPosition, Quaternion.identity);
            instance.name = ReplicatedBlackholeVisualName;
            PrepareReplicatedVisualInstance(instance, false);
            RefreshReplicatedPrefabVisual(instance);
            PlayAllParticles(instance);
            ItemRuntimeLog.Info(ItemIds.BlackholeBomb, $"Replicated blackhole visual prefab used: {prefab.name}", this);
            return instance;
        }

        var fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fallback.name = ReplicatedBlackholeVisualName;
        fallback.transform.position = startPosition;
        fallback.transform.localScale = Vector3.one * 0.28f;
        DisableCollider(fallback);
        ApplyTransparentSphereVisual(fallback, new Color(0.07f, 0.07f, 0.08f, 0.14f));
        TryAttachReplicatedBlackholeFx(fallback.transform);
        EnsureVisibleBlackholeShell(fallback.transform);
        ItemRuntimeLog.Warn(ItemIds.BlackholeBomb, "Replicated blackhole visual prefab missing. Using primitive fallback.", this);
        return fallback;
    }

    private void EnsureVisibleBlackholeShell(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        var shell = parent.Find(ReplicatedBlackholeShellName);
        if (shell == null)
        {
            var shellObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shellObject.name = ReplicatedBlackholeShellName;
            shellObject.transform.SetParent(parent, false);
            shellObject.transform.localPosition = Vector3.zero;
            shellObject.transform.localRotation = Quaternion.identity;
            shellObject.transform.localScale = Vector3.one * 0.28f;
            DisableCollider(shellObject);
            ApplyTransparentSphereVisual(shellObject, new Color(0.08f, 0.08f, 0.1f, 0.32f));
            ItemRuntimeLog.InfoOnce("BlackholeShellCreated", ItemIds.BlackholeBomb, "Replicated blackhole shell created.", this);
            return;
        }

        shell.localPosition = Vector3.zero;
        shell.localRotation = Quaternion.identity;
        shell.localScale = Vector3.one * 0.28f;
        ApplyTransparentSphereVisual(shell.gameObject, new Color(0.08f, 0.08f, 0.1f, 0.32f));
    }

    private GameObject CreateReplicatedSatelliteProjectile(Vector3 startPosition)
    {
        var prefab = TryLoadReplicatedSatelliteProjectilePrefab();
        if (prefab != null)
        {
            var instance = Instantiate(prefab, startPosition, prefab.transform.rotation);
            instance.name = ReplicatedSatelliteVisualName;
            PrepareReplicatedVisualInstance(instance, false);
            RefreshReplicatedPrefabVisual(instance);
            PlayAllParticles(instance);
            ItemRuntimeLog.Info(ItemIds.SatelliteStrike, $"Replicated satellite projectile prefab used: {prefab.name}", this);
            return instance;
        }

        var fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        fallback.name = ReplicatedSatelliteVisualName;
        fallback.transform.position = startPosition;
        fallback.transform.localScale = Vector3.one * 0.3f;
        DisableCollider(fallback);
        ApplyTransparentSphereVisual(fallback, new Color(0.95f, 0.3f, 0.3f, 0.7f));
        ItemRuntimeLog.Warn(ItemIds.SatelliteStrike, "Replicated satellite projectile prefab missing. Using primitive fallback.", this);
        return fallback;
    }

    private GameObject CreateReplicatedSatelliteCharge(Vector3 center, float radius)
    {
        var root = new GameObject(ReplicatedSatelliteChargeName);
        root.transform.position = center + Vector3.up * 0.05f;

        var attached =
            TryAttachReplicatedEffectChild(root.transform, TryLoadReplicatedSatelliteChargeupPrefab(), "Chargeup", Vector3.one * Mathf.Max(1f, radius * 0.35f)) |
            TryAttachReplicatedEffectChild(root.transform, TryLoadReplicatedSatelliteCloudPrefab(), "Cloud", Vector3.one * Mathf.Max(1f, radius * 0.3f));
        if (attached)
        {
            ItemRuntimeLog.Info(ItemIds.SatelliteStrike, "Replicated satellite charge effect prefab used.", this);
            return root;
        }

        var fallback = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fallback.name = ReplicatedSatelliteChargeName;
        fallback.transform.SetParent(root.transform, false);
        fallback.transform.localPosition = Vector3.zero;
        fallback.transform.localScale = new Vector3(
            Mathf.Max(0.35f, radius * 0.2f),
            0.05f,
            Mathf.Max(0.35f, radius * 0.2f));
        DisableCollider(fallback);
        ApplyTransparentSphereVisual(fallback, new Color(0.35f, 0.7f, 1f, 0.35f));
        ItemRuntimeLog.Warn(ItemIds.SatelliteStrike, "Replicated satellite charge effect missing. Using cylinder fallback.", this);
        return root;
    }

    private GameObject CreateReplicatedSatelliteBeam(Vector3 center, float radius)
    {
        var root = new GameObject(ReplicatedSatelliteBeamName);
        root.transform.position = center;

        var attachedCloud = TryAttachReplicatedEffectChild(
            root.transform,
            TryLoadReplicatedSatelliteCloudPrefab(),
            "Cloud",
            Vector3.one * Mathf.Max(1f, radius * 0.3f));
        var attachedBeam = TryAttachReplicatedEffectChild(
            root.transform,
            TryLoadReplicatedSatelliteCylinderPrefab(),
            "Beam",
            Vector3.one * Mathf.Max(1f, radius * 0.3f));
        if (attachedCloud || attachedBeam)
        {
            ItemRuntimeLog.Info(ItemIds.SatelliteStrike, "Replicated satellite beam effect prefab used.", this);
            return root;
        }

        var fallback = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        fallback.name = ReplicatedSatelliteBeamName;
        fallback.transform.SetParent(root.transform, false);
        fallback.transform.position = center + Vector3.up * (satelliteBeamHeight * 0.5f);
        fallback.transform.localScale = new Vector3(
            Mathf.Max(0.35f, radius * 0.28f),
            satelliteBeamHeight * 0.5f,
            Mathf.Max(0.35f, radius * 0.28f));
        DisableCollider(fallback);
        ApplyTransparentSphereVisual(fallback, new Color(0.35f, 0.7f, 1f, 0.45f));
        ItemRuntimeLog.Warn(ItemIds.SatelliteStrike, "Replicated satellite beam effect missing. Using cylinder fallback.", this);
        return root;
    }

    private void PrepareReplicatedVisualInstance(GameObject instance, bool attachBlackholeFx)
    {
        if (instance == null)
        {
            return;
        }

        DisableColliders(instance);
        DisableBehaviours(instance);
        ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(instance);
        if (attachBlackholeFx)
        {
            TryAttachReplicatedBlackholeFx(instance.transform);
        }
    }

    private static void RefreshReplicatedPrefabVisual(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        var blackholeAuthoring = instance.GetComponent<ItemBlackholeVisualAuthoring>();
        if (blackholeAuthoring != null)
        {
            blackholeAuthoring.RefreshVisual();
        }
    }

    private bool TryAttachReplicatedEffectChild(Transform parent, GameObject prefab, string childName, Vector3 localScale)
    {
        if (parent == null || prefab == null)
        {
            return false;
        }

        var instance = Instantiate(prefab, parent);
        instance.name = childName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = prefab.transform.rotation;
        instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, localScale);
        PrepareReplicatedVisualInstance(instance, false);
        RefreshReplicatedPrefabVisual(instance);
        PlayAllParticles(instance);
        return true;
    }

    private void UpdateReplicatedFlamethrowerVisualFollow()
    {
        if (!HasStateAuthority || !NetworkedFlamethrowerActive)
        {
            return;
        }

        var forward = NetworkedFlamethrowerForward.sqrMagnitude > 0.0001f
            ? NetworkedFlamethrowerForward.normalized
            : transform.forward;
        var origin = ResolveFlamethrowerEffectOrigin(forward);
        EnsureOrUpdateFlamethrowerEffectProxy(origin, forward, NetworkedFlamethrowerRange, NetworkedFlamethrowerRadius);
    }

    private void ApplyReplicatedFlamethrowerTick(Vector3 origin, Vector3 forward, float range, float radius)
    {
        if (!ShouldDriveFlamethrowerVisualLocally())
        {
            return;
        }

        EnsureReplicatedFlamethrowerVisual();
        if (_replicatedFlamethrowerFxRoot == null)
        {
            return;
        }

        var safeForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
        AttachReplicatedFlamethrowerVisualToAnchor(origin, safeForward);
        TuneReplicatedFlamethrowerParticles(range, radius);
        PlayReplicatedFlamethrowerParticles();
    }

    private void EnsureReplicatedFlamethrowerVisual()
    {
        if (_replicatedFlamethrowerFxRoot != null)
        {
            return;
        }

        var prefab = TryLoadReplicatedFlamethrowerEffectPrefab();
        if (prefab != null)
        {
            _replicatedFlamethrowerFxRoot = Instantiate(prefab, ResolveFlamethrowerEffectAnchor());
            _replicatedFlamethrowerFxRoot.name = ReplicatedFlamethrowerFxName;
            _replicatedFlamethrowerFxRoot.transform.localPosition = Vector3.zero;
            _replicatedFlamethrowerFxRoot.transform.localRotation = Quaternion.identity;
            _replicatedFlamethrowerFxRoot.transform.localScale = Vector3.one * Mathf.Max(0.01f, flamethrowerVisualScale);
            DisableColliders(_replicatedFlamethrowerFxRoot);
            DisableBehaviours(_replicatedFlamethrowerFxRoot);
            ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(_replicatedFlamethrowerFxRoot);
            _replicatedFlamethrowerParticles = _replicatedFlamethrowerFxRoot.GetComponentsInChildren<ParticleSystem>(true);
            ConfigureReplicatedFlamethrowerParticles();
            ItemRuntimeLog.Info(ItemIds.Flamethrower, $"Replicated flamethrower effect prefab used: {prefab.name}", this);
            return;
        }

        var fx = new GameObject(ReplicatedFlamethrowerFxName);
        fx.transform.SetParent(ResolveFlamethrowerEffectAnchor(), false);
        var particle = fx.AddComponent<ParticleSystem>();
        _replicatedFlamethrowerFxRoot = fx;
        _replicatedFlamethrowerParticles = new[] { particle };

        var main = particle.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.9f, 0.4f, 0.9f),
            new Color(1f, 0.35f, 0.1f, 0.55f));

        var emission = particle.emission;
        emission.rateOverTime = 200f;

        var shape = particle.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 24f;
        shape.radius = 0.12f;
        shape.length = 0.6f;
        shape.randomDirectionAmount = 0.2f;
            ItemRuntimeLog.Warn(ItemIds.Flamethrower, "Replicated flamethrower effect missing. Using particle fallback.", this);
    }

    private GameObject TryLoadReplicatedFlamethrowerEffectPrefab()
    {
        if (_flamethrowerEffectPrefabCache != null)
        {
            return _flamethrowerEffectPrefabCache;
        }

        _flamethrowerEffectPrefabCache = _replicatedEffectPrefabResolver.Resolve(FlamethrowerEffectAssetPath);
        if (_flamethrowerEffectPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("FlamethrowerEffectAsset", ItemIds.Flamethrower, $"Flamethrower effect prefab loaded from asset path: {FlamethrowerEffectAssetPath}", this);
            return _flamethrowerEffectPrefabCache;
        }

        _flamethrowerEffectPrefabCache = Resources.Load<GameObject>(FlamethrowerEffectResourcePath);
        if (_flamethrowerEffectPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("FlamethrowerEffectResource", ItemIds.Flamethrower, $"Flamethrower effect prefab loaded from Resources: {FlamethrowerEffectResourcePath}", this);
        }
        else
        {
            ItemRuntimeLog.WarnOnce("FlamethrowerEffectMissing", ItemIds.Flamethrower, $"Flamethrower effect prefab load failed: asset={FlamethrowerEffectAssetPath}, resource={FlamethrowerEffectResourcePath}", this);
        }
        return _flamethrowerEffectPrefabCache;
    }

    private Transform ResolveFlamethrowerEffectAnchor()
    {
        if (_heldItemPresenter != null && _heldItemPresenter.CurrentHeldVisualRoot != null)
        {
            return _heldItemPresenter.CurrentHeldVisualRoot;
        }

        return transform;
    }

    private Vector3 ResolveFlamethrowerEffectOrigin(Vector3 forward)
    {
        var anchor = ResolveFlamethrowerEffectAnchor();
        if (_heldItemPresenter != null && _heldItemPresenter.CurrentHeldVisualRoot != null && anchor == _heldItemPresenter.CurrentHeldVisualRoot)
        {
            return anchor.TransformPoint(flamethrowerMuzzleLocalOffset);
        }

        return transform.position + Vector3.up * flamethrowerVisualHeightOffset + forward * flamethrowerVisualForwardOffset;
    }

    private void EnsureOrUpdateFlamethrowerEffectProxy(Vector3 origin, Vector3 forward, float range, float radius)
    {
        if (!HasStateAuthority || Runner == null)
        {
            return;
        }

        var safeForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
        var rotation = Quaternion.LookRotation(safeForward, Vector3.up);
        var safeRange = Mathf.Max(0.5f, range);
        var safeRadius = Mathf.Max(0.1f, radius);

        if (_activeFlamethrowerEffectProxy == null || !_activeFlamethrowerEffectProxy || !_activeFlamethrowerEffectProxy.gameObject.activeInHierarchy)
        {
            _activeFlamethrowerEffectProxy = SpawnNetworkedItemEffectProxy(
                origin,
                rotation,
                proxy => proxy.InitializeFlamethrower(safeRange, safeRadius, safeForward));
            return;
        }

        _activeFlamethrowerEffectProxy.transform.SetPositionAndRotation(origin, rotation);
        var proxyBehaviour = _activeFlamethrowerEffectProxy.GetComponent<NetworkedItemEffectProxy>();
        if (proxyBehaviour != null)
        {
            proxyBehaviour.InitializeFlamethrower(safeRange, safeRadius, safeForward);
            proxyBehaviour.SyncNetworkPose(origin, rotation, zeroVelocity: true);
        }
    }

    private void AttachReplicatedFlamethrowerVisualToAnchor(Vector3 origin, Vector3 forward)
    {
        if (_replicatedFlamethrowerFxRoot == null)
        {
            return;
        }

        var anchor = ResolveFlamethrowerEffectAnchor();
        if (_replicatedFlamethrowerFxRoot.transform.parent != anchor)
        {
            _replicatedFlamethrowerFxRoot.transform.SetParent(anchor, false);
        }

        var hasHeldFlamethrower =
            _heldItemPresenter != null &&
            _heldItemPresenter.CurrentHeldVisualRoot != null &&
            anchor == _heldItemPresenter.CurrentHeldVisualRoot;

        if (hasHeldFlamethrower)
        {
            // 손에 든 화염방사기 시각 효과는 총구 오프셋을 그대로 따라가게 한다.
            _replicatedFlamethrowerFxRoot.transform.localPosition = flamethrowerMuzzleLocalOffset;
            _replicatedFlamethrowerFxRoot.transform.localRotation = Quaternion.Euler(flamethrowerMuzzleLocalEulerOffset);
            _replicatedFlamethrowerFxRoot.transform.localScale = Vector3.one * Mathf.Max(0.01f, flamethrowerVisualScale);
            return;
        }

        _replicatedFlamethrowerFxRoot.transform.position = origin;
        _replicatedFlamethrowerFxRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        _replicatedFlamethrowerFxRoot.transform.localScale = Vector3.one * Mathf.Max(0.01f, flamethrowerVisualScale);
    }

    private GameObject TryLoadNetworkedItemEffectProxyPrefab()
    {
        if (_networkedItemEffectProxyPrefabCache != null)
        {
            return _networkedItemEffectProxyPrefabCache;
        }

        _networkedItemEffectProxyPrefabCache = _replicatedEffectPrefabResolver.Resolve(NetworkedItemEffectProxyAssetPath);
        if (_networkedItemEffectProxyPrefabCache != null)
        {
            return _networkedItemEffectProxyPrefabCache;
        }

        _networkedItemEffectProxyPrefabCache = Resources.Load<GameObject>(NetworkedItemEffectProxyResourcePath);
        if (_networkedItemEffectProxyPrefabCache == null)
        {
            ItemRuntimeLog.WarnOnce("NetworkedItemEffectProxyMissing", "ItemWorldEffect", $"Networked item effect proxy load failed: asset={NetworkedItemEffectProxyAssetPath}, resource={NetworkedItemEffectProxyResourcePath}", this);
        }

        return _networkedItemEffectProxyPrefabCache;
    }

    private NetworkObject SpawnNetworkedItemEffectProxy(Vector3 position, Quaternion rotation, System.Action<NetworkedItemEffectProxy> initialize)
    {
        if (!HasStateAuthority || Runner == null)
        {
            return null;
        }

        var prefab = TryLoadNetworkedItemEffectProxyPrefab();
        if (prefab == null)
        {
            return null;
        }

        return Runner.Spawn(
            prefab,
            position,
            rotation,
            onBeforeSpawned: (_, obj) =>
            {
                var proxy = obj.GetComponent<NetworkedItemEffectProxy>();
                initialize?.Invoke(proxy);
            });
    }

    private void DespawnNetworkedItemEffectProxy(ref NetworkObject proxy)
    {
        if (proxy != null && Runner != null && Runner.IsRunning && HasStateAuthority)
        {
            Runner.Despawn(proxy);
        }

        proxy = null;
    }

    private GameObject TryLoadReplicatedBlackholeVisualPrefab()
    {
        if (_blackholeVisualPrefabCache != null)
        {
            return _blackholeVisualPrefabCache;
        }

        _blackholeVisualPrefabCache = _replicatedEffectPrefabResolver.Resolve(BlackholeVisualAssetPath);
        if (_blackholeVisualPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("BlackholeVisualAsset", ItemIds.BlackholeBomb, $"Blackhole visual prefab loaded from asset path: {BlackholeVisualAssetPath}", this);
            return _blackholeVisualPrefabCache;
        }

        _blackholeVisualPrefabCache = Resources.Load<GameObject>(BlackholeVisualResourcePath);
        if (_blackholeVisualPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("BlackholeVisualResource", ItemIds.BlackholeBomb, $"Blackhole visual prefab loaded from Resources: {BlackholeVisualResourcePath}", this);
        }
        else
        {
            ItemRuntimeLog.WarnOnce("BlackholeVisualMissing", ItemIds.BlackholeBomb, $"Blackhole visual prefab load failed: asset={BlackholeVisualAssetPath}, resource={BlackholeVisualResourcePath}", this);
        }
        return _blackholeVisualPrefabCache;
    }

    private GameObject TryLoadReplicatedSatelliteProjectilePrefab()
    {
        if (_satelliteProjectilePrefabCache != null)
        {
            return _satelliteProjectilePrefabCache;
        }

        _satelliteProjectilePrefabCache = _replicatedEffectPrefabResolver.Resolve(SatelliteProjectileAssetPath);
        if (_satelliteProjectilePrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteProjectileAsset", ItemIds.SatelliteStrike, $"Satellite projectile prefab loaded from asset path: {SatelliteProjectileAssetPath}", this);
            return _satelliteProjectilePrefabCache;
        }

        _satelliteProjectilePrefabCache = Resources.Load<GameObject>(SatelliteProjectileResourcePath);
        if (_satelliteProjectilePrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteProjectileResource", ItemIds.SatelliteStrike, $"Satellite projectile prefab loaded from Resources: {SatelliteProjectileResourcePath}", this);
        }
        else
        {
            ItemRuntimeLog.WarnOnce("SatelliteProjectileMissing", ItemIds.SatelliteStrike, $"Satellite projectile prefab load failed: asset={SatelliteProjectileAssetPath}, resource={SatelliteProjectileResourcePath}", this);
        }
        return _satelliteProjectilePrefabCache;
    }

    private GameObject TryLoadReplicatedSatelliteChargeupPrefab()
    {
        if (_satelliteChargeupPrefabCache != null)
        {
            return _satelliteChargeupPrefabCache;
        }

        _satelliteChargeupPrefabCache = _replicatedEffectPrefabResolver.Resolve(SatelliteChargeupAssetPath);
        if (_satelliteChargeupPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteChargeAsset", ItemIds.SatelliteStrike, $"Satellite charge prefab loaded from asset path: {SatelliteChargeupAssetPath}", this);
            return _satelliteChargeupPrefabCache;
        }

        _satelliteChargeupPrefabCache = Resources.Load<GameObject>(SatelliteChargeupResourcePath);
        if (_satelliteChargeupPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteChargeResource", ItemIds.SatelliteStrike, $"Satellite charge prefab loaded from Resources: {SatelliteChargeupResourcePath}", this);
        }
        else
        {
            ItemRuntimeLog.WarnOnce("SatelliteChargeMissing", ItemIds.SatelliteStrike, $"Satellite charge prefab load failed: asset={SatelliteChargeupAssetPath}, resource={SatelliteChargeupResourcePath}", this);
        }
        return _satelliteChargeupPrefabCache;
    }

    private GameObject TryLoadReplicatedSatelliteCloudPrefab()
    {
        if (_satelliteCloudPrefabCache != null)
        {
            return _satelliteCloudPrefabCache;
        }

        _satelliteCloudPrefabCache = _replicatedEffectPrefabResolver.Resolve(SatelliteCloudAssetPath);
        if (_satelliteCloudPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteCloudAsset", ItemIds.SatelliteStrike, $"Satellite cloud prefab loaded from asset path: {SatelliteCloudAssetPath}", this);
            return _satelliteCloudPrefabCache;
        }

        _satelliteCloudPrefabCache = Resources.Load<GameObject>(SatelliteCloudResourcePath);
        if (_satelliteCloudPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteCloudResource", ItemIds.SatelliteStrike, $"Satellite cloud prefab loaded from Resources: {SatelliteCloudResourcePath}", this);
        }
        else
        {
            ItemRuntimeLog.WarnOnce("SatelliteCloudMissing", ItemIds.SatelliteStrike, $"Satellite cloud prefab load failed: asset={SatelliteCloudAssetPath}, resource={SatelliteCloudResourcePath}", this);
        }
        return _satelliteCloudPrefabCache;
    }

    private GameObject TryLoadReplicatedSatelliteCylinderPrefab()
    {
        if (_satelliteCylinderPrefabCache != null)
        {
            return _satelliteCylinderPrefabCache;
        }

        _satelliteCylinderPrefabCache = _replicatedEffectPrefabResolver.Resolve(SatelliteCylinderAssetPath);
        if (_satelliteCylinderPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteCylinderAsset", ItemIds.SatelliteStrike, $"Satellite beam prefab loaded from asset path: {SatelliteCylinderAssetPath}", this);
            return _satelliteCylinderPrefabCache;
        }

        _satelliteCylinderPrefabCache = Resources.Load<GameObject>(SatelliteCylinderResourcePath);
        if (_satelliteCylinderPrefabCache != null)
        {
            ItemRuntimeLog.InfoOnce("SatelliteCylinderResource", ItemIds.SatelliteStrike, $"Satellite beam prefab loaded from Resources: {SatelliteCylinderResourcePath}", this);
        }
        else
        {
            ItemRuntimeLog.WarnOnce("SatelliteCylinderMissing", ItemIds.SatelliteStrike, $"Satellite beam prefab load failed: asset={SatelliteCylinderAssetPath}, resource={SatelliteCylinderResourcePath}", this);
        }
        return _satelliteCylinderPrefabCache;
    }

    private bool ShouldDriveFlamethrowerVisualLocally()
    {
        return Object != null && Object.IsValid;
    }

    private bool HasActiveItemGameplayRunnerInScene()
    {
        if (Time.unscaledTime < _nextItemGameplayRunnerLookupTime)
        {
            return _cachedHasItemGameplayRunner;
        }

        _nextItemGameplayRunnerLookupTime = Time.unscaledTime + 1f;
        var runners = FindObjectsOfType<ItemGameplayRunner>(true);
        for (var i = 0; i < runners.Length; i++)
        {
            var runner = runners[i];
            if (runner == null || !runner.isActiveAndEnabled)
            {
                continue;
            }

            _cachedHasItemGameplayRunner = true;
            return true;
        }

        _cachedHasItemGameplayRunner = false;
        return false;
    }

    private void ConfigureReplicatedFlamethrowerParticles()
    {
        if (_replicatedFlamethrowerParticles == null || _replicatedFlamethrowerParticles.Length == 0)
        {
            return;
        }

        for (var i = 0; i < _replicatedFlamethrowerParticles.Length; i++)
        {
            var particle = _replicatedFlamethrowerParticles[i];
            if (particle == null)
            {
                continue;
            }

            var main = particle.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }
    }

    private void TuneReplicatedFlamethrowerParticles(float range, float radius)
    {
        if (_replicatedFlamethrowerParticles == null || _replicatedFlamethrowerParticles.Length == 0)
        {
            return;
        }

        var safeRange = Mathf.Max(0.5f, range);
        var safeRadius = Mathf.Max(0.1f, radius);
        var speed = Mathf.Max(4f, safeRange * 2.2f);
        var lifetime = Mathf.Max(0.08f, safeRange / Mathf.Max(0.01f, speed));
        var minSize = Mathf.Max(0.08f, safeRadius * 0.35f);
        var maxSize = Mathf.Max(0.16f, safeRadius * 0.65f);

        for (var i = 0; i < _replicatedFlamethrowerParticles.Length; i++)
        {
            var particle = _replicatedFlamethrowerParticles[i];
            if (particle == null)
            {
                continue;
            }

            var main = particle.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.75f, speed);
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime * 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);

            var shape = particle.shape;
            if (shape.enabled && shape.shapeType == ParticleSystemShapeType.Cone)
            {
                shape.length = Mathf.Max(0.1f, safeRange * 0.2f);
            }
        }
    }

    private void PlayReplicatedFlamethrowerParticles()
    {
        if (_replicatedFlamethrowerParticles == null)
        {
            return;
        }

        for (var i = 0; i < _replicatedFlamethrowerParticles.Length; i++)
        {
            var particle = _replicatedFlamethrowerParticles[i];
            if (particle != null && !particle.isPlaying)
            {
                particle.Play(true);
            }
        }
    }

    private void StopReplicatedFlamethrowerVisual()
    {
        DespawnNetworkedItemEffectProxy(ref _activeFlamethrowerEffectProxy);

        if (_replicatedFlamethrowerParticles != null)
        {
            for (var i = 0; i < _replicatedFlamethrowerParticles.Length; i++)
            {
                var particle = _replicatedFlamethrowerParticles[i];
                if (particle != null)
                {
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }
    }

    private void TryAttachReplicatedBlackholeFx(Transform parent)
    {
        if (parent == null || parent.Find(ReplicatedBlackholeFxName) != null)
        {
            return;
        }

        if (!enableReplicatedBlackholeSecondaryFx)
        {
            return;
        }

        if (_blackholeEffectPrefabCache == null)
        {
            _blackholeEffectPrefabCache = _replicatedEffectPrefabResolver.Resolve($"Assets/Resources/{BlackholeEffectResourcePath}.prefab");
            if (_blackholeEffectPrefabCache == null)
            {
                _blackholeEffectPrefabCache = Resources.Load<GameObject>(BlackholeEffectResourcePath);
            }
        }

        if (_blackholeEffectPrefabCache == null)
        {
            ItemRuntimeLog.WarnOnce("BlackholeFxMissing", ItemIds.BlackholeBomb, $"Blackhole FX resource load failed: resource={BlackholeEffectResourcePath}", this);
            return;
        }

        var instance = Instantiate(_blackholeEffectPrefabCache, parent);
        instance.name = ReplicatedBlackholeFxName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * Mathf.Max(0.001f, blackholeVisualScale);
        ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(instance);
        PlayAllParticles(instance);
        ItemRuntimeLog.InfoOnce("BlackholeFxLoaded", ItemIds.BlackholeBomb, $"Blackhole FX resource loaded: {instance.name}", this);

        var colliders = instance.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private static void DisableColliders(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var colliders = root.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private static void PlayAllParticles(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var particles = root.GetComponentsInChildren<ParticleSystem>(true);
        for (var i = 0; i < particles.Length; i++)
        {
            var particle = particles[i];
            if (particle == null)
            {
                continue;
            }

            particle.gameObject.SetActive(true);
            particle.Play(true);
        }
    }

    private static void DisableBehaviours(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        var behaviours = root.GetComponents<MonoBehaviour>();
        for (var i = 0; i < behaviours.Length; i++)
        {
            var behaviour = behaviours[i];
            if (behaviour != null)
            {
                behaviour.enabled = false;
            }
        }
    }

    private static void DisableCollider(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        var collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
        }
    }

    private static void ApplyTransparentSphereVisual(GameObject target, Color color)
    {
        if (target == null)
        {
            return;
        }

        var renderer = target.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        var sharedMaterial = renderer.sharedMaterial;
        if (sharedMaterial == null ||
            sharedMaterial.shader == null ||
            !sharedMaterial.shader.isSupported)
        {
            ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(target, true);
        }

        var material = renderer.material;
        if (material == null)
        {
            renderer.enabled = false;
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }
        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }
        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }
        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }
        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
