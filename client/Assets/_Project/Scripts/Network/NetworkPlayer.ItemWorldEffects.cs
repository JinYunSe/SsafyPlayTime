using System.Collections;
using Fusion;
using SSAFYPlayTime.Gameplay.Items;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private const string ReplicatedBlackholeVisualName = "Item_Blackhole_Replicated";
    private const string ReplicatedBlackholeFxName = "Item_BlackholeFx";
    private const string ReplicatedSatelliteVisualName = "Item_SatelliteStrike_Replicated";
    private const string ReplicatedSatelliteChargeName = "Item_SatelliteStrike_Charge";
    private const string ReplicatedSatelliteBeamName = "Item_SatelliteStrike_Beam";
    private const string BlackholeEffectResourcePath = "Effect_02_BlackHole";

    [Header("Item World Effects")]
    [SerializeField] private LayerMask itemWorldEffectMask = ~0;
    [SerializeField] private float blackholeLaunchForwardOffset = 0.7f;
    [SerializeField] private float blackholeLaunchHeightOffset = 1.2f;
    [SerializeField] private float blackholeLaunchVisualDuration = 0.35f;
    [SerializeField] private float blackholeVisualScale = 0.7f;
    [SerializeField] private float blackholePullStrengthMultiplier = 2.25f;
    [SerializeField] private float blackholeExpandSpeedMultiplier = 1.5f;
    [SerializeField] private float blackholePlayerPullMultiplier = 1.5f;
    [SerializeField] private float blackholeItemPullMultiplier = 1.5f;
    [SerializeField] private float blackholePlayerEscapeDamping = 0.2f;
    [SerializeField] private float satelliteProjectileTravelSec = 0.35f;
    [SerializeField] private float satelliteBeamHeight = 24f;
    [SerializeField] private bool enableItemWorldEffectLog;

    private readonly Collider[] _replicatedBlackholeOverlapBuffer = new Collider[256];
    private readonly Collider[] _replicatedSatelliteOverlapBuffer = new Collider[256];
    private readonly DefaultItemFieldPrefabResolver _replicatedEffectPrefabResolver = new();

    private bool _itemWorldEffectNetworkReady;
    private bool _itemWorldEffectEventsBound;
    private ItemRuntimeHost _itemWorldEffectBoundHost;
    private int _lastAppliedBlackholeSeq;
    private int _lastAppliedSatelliteStrikeSeq;
    private Coroutine _activeReplicatedBlackholeRoutine;
    private Coroutine _activeReplicatedSatelliteRoutine;
    private GameObject _blackholeEffectPrefabCache;

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

    private void OnDisable()
    {
        UnbindItemWorldEffectEvents();
        StopActiveItemWorldEffectCoroutines();
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
            return;
        }

        NetworkedBlackholeCenter = request.Center;
        NetworkedBlackholeDelaySec = request.DelaySec;
        NetworkedBlackholeDurationSec = request.DurationSec;
        NetworkedBlackholeRadius = request.Radius;
        NetworkedBlackholeForce = request.Force;
        NetworkedBlackholeSeq++;

        StartReplicatedBlackhole(request, applyGameplay: true);
    }

    private void HandleSatelliteStrikeRequested(SatelliteStrikeRequest request)
    {
        if (!CanWriteItemWorldEffectState())
        {
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

        StartReplicatedSatelliteStrike(request, applyGameplay: true);
    }

    private void ApplyReplicatedWorldItemEffects()
    {
        if (Object == null || !Object.IsValid || HasStateAuthority)
        {
            return;
        }

        if (NetworkedBlackholeSeq > 0 && _lastAppliedBlackholeSeq != NetworkedBlackholeSeq)
        {
            _lastAppliedBlackholeSeq = NetworkedBlackholeSeq;
            StartReplicatedBlackhole(
                new BlackholeSkillRequest(
                    NetworkedBlackholeCenter,
                    NetworkedBlackholeDelaySec,
                    NetworkedBlackholeDurationSec,
                    NetworkedBlackholeRadius,
                    NetworkedBlackholeForce),
                applyGameplay: false);
        }

        if (NetworkedSatelliteStrikeSeq > 0 && _lastAppliedSatelliteStrikeSeq != NetworkedSatelliteStrikeSeq)
        {
            _lastAppliedSatelliteStrikeSeq = NetworkedSatelliteStrikeSeq;
            StartReplicatedSatelliteStrike(
                new SatelliteStrikeRequest(
                    NetworkedSatelliteStrikeCenter,
                    transform.position,
                    transform.forward,
                    NetworkedSatelliteStrikeWarningSec,
                    NetworkedSatelliteStrikeDurationSec,
                    NetworkedSatelliteStrikeRadius,
                    NetworkedSatelliteStrikeForce,
                    NetworkedSatelliteStrikeBaseDamage,
                    NetworkedSatelliteStrikeStunDamage),
                applyGameplay: false);
        }
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
    }

    private IEnumerator CoReplicatedBlackhole(BlackholeSkillRequest request, bool applyGameplay)
    {
        var startPosition = transform.position + Vector3.up * blackholeLaunchHeightOffset + transform.forward * blackholeLaunchForwardOffset;
        var center = request.Center;
        var launchDuration = Mathf.Min(Mathf.Max(0.05f, blackholeLaunchVisualDuration), Mathf.Max(0.05f, request.DelaySec));
        var visualRoot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visualRoot.name = ReplicatedBlackholeVisualName;
        visualRoot.transform.position = startPosition;
        visualRoot.transform.localScale = Vector3.one * 0.45f;

        var rootCollider = visualRoot.GetComponent<Collider>();
        if (rootCollider != null)
        {
            rootCollider.enabled = false;
        }

        ApplyTransparentSphereVisual(visualRoot, new Color(0.07f, 0.07f, 0.08f, 0.14f));
        TryAttachReplicatedBlackholeFx(visualRoot.transform);

        var elapsedLaunch = 0f;
        while (elapsedLaunch < launchDuration)
        {
            elapsedLaunch += Time.deltaTime;
            var t = Mathf.Clamp01(elapsedLaunch / launchDuration);
            visualRoot.transform.position = Vector3.Lerp(startPosition, center, t);
            yield return null;
        }

        if (request.DelaySec > launchDuration)
        {
            yield return new WaitForSeconds(request.DelaySec - launchDuration);
        }

        visualRoot.transform.position = center;
        var duration = Mathf.Max(0.1f, request.DurationSec);
        var radius = Mathf.Max(0.1f, request.Radius);
        var force = Mathf.Max(0f, request.Force);
        var expandDuration = Mathf.Max(0.05f, duration / Mathf.Max(0.1f, blackholeExpandSpeedMultiplier));
        var activeElapsed = 0f;

        while (activeElapsed < duration)
        {
            activeElapsed += Time.deltaTime;
            var ramp = Mathf.Clamp01(activeElapsed / expandDuration);
            visualRoot.transform.localScale = Vector3.one * Mathf.Lerp(0.45f, radius * 2f, ramp);
            visualRoot.transform.Rotate(Vector3.up, 220f * Time.deltaTime, Space.World);

            if (applyGameplay)
            {
                ApplyBlackholeGameplay(center, radius, force, ramp);
            }

            yield return null;
        }

        Destroy(visualRoot);
        _activeReplicatedBlackholeRoutine = null;
    }

    private IEnumerator CoReplicatedSatelliteStrike(SatelliteStrikeRequest request, bool applyGameplay)
    {
        var center = ResolveSatelliteGroundCenter(request.Center);
        var launchOrigin = transform.position + Vector3.up * blackholeLaunchHeightOffset + transform.forward * blackholeLaunchForwardOffset;
        var projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projectile.name = ReplicatedSatelliteVisualName;
        projectile.transform.position = launchOrigin;
        projectile.transform.localScale = Vector3.one * 0.3f;
        ApplyTransparentSphereVisual(projectile, new Color(0.95f, 0.3f, 0.3f, 0.7f));
        DisableCollider(projectile);

        var travelSec = Mathf.Max(0.05f, satelliteProjectileTravelSec);
        var travelElapsed = 0f;
        while (travelElapsed < travelSec)
        {
            travelElapsed += Time.deltaTime;
            var t = Mathf.Clamp01(travelElapsed / travelSec);
            projectile.transform.position = Vector3.Lerp(launchOrigin, center + Vector3.up * 0.2f, t);
            yield return null;
        }

        Destroy(projectile);

        var charge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        charge.name = ReplicatedSatelliteChargeName;
        charge.transform.position = center + Vector3.up * 0.05f;
        charge.transform.localScale = new Vector3(
            Mathf.Max(0.35f, request.Radius * 0.2f),
            0.05f,
            Mathf.Max(0.35f, request.Radius * 0.2f));
        ApplyTransparentSphereVisual(charge, new Color(0.35f, 0.7f, 1f, 0.35f));
        DisableCollider(charge);

        if (request.WarningSec > 0f)
        {
            yield return new WaitForSeconds(request.WarningSec);
        }

        Destroy(charge);

        var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = ReplicatedSatelliteBeamName;
        beam.transform.position = center + Vector3.up * (satelliteBeamHeight * 0.5f);
        beam.transform.localScale = new Vector3(
            Mathf.Max(0.35f, request.Radius * 0.28f),
            satelliteBeamHeight * 0.5f,
            Mathf.Max(0.35f, request.Radius * 0.28f));
        ApplyTransparentSphereVisual(beam, new Color(0.35f, 0.7f, 1f, 0.45f));
        DisableCollider(beam);

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

        Destroy(beam);
        _activeReplicatedSatelliteRoutine = null;
    }

    private void ApplyBlackholeGameplay(Vector3 center, float radius, float force, float ramp)
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

            var distance = Mathf.Max(toCenter.magnitude, 0.35f);
            var pullMultiplier = root != null && root.CompareTag("Player")
                ? Mathf.Max(0.05f, blackholePlayerPullMultiplier)
                : Mathf.Max(0.05f, blackholeItemPullMultiplier);
            var pullStrength = (force * blackholePullStrengthMultiplier * (0.4f + ramp * 1.6f)) /
                               Mathf.Sqrt(distance) * pullMultiplier;

            if (root != null && root.CompareTag("Player"))
            {
                ApplyBlackholeEscapeDamping(body, toCenter.normalized);
            }

            body.AddForce(toCenter.normalized * pullStrength, ForceMode.Acceleration);
        }
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
                if (damagePerTick > 0f)
                {
                    var playerStats = targetPlayer.GetComponentInParent<PlayerStats>();
                    if (playerStats != null)
                    {
                        playerStats.TakeDamage(Mathf.Max(0, Mathf.RoundToInt(damagePerTick)));
                    }
                }

                if (stunPerTick > 0f)
                {
                    targetPlayer.ApplyStunDamage(stunPerTick, 1f, 0f, explosionForce);
                }
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

    private void TryAttachReplicatedBlackholeFx(Transform parent)
    {
        if (parent == null || parent.Find(ReplicatedBlackholeFxName) != null)
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
            return;
        }

        var instance = Instantiate(_blackholeEffectPrefabCache, parent);
        instance.name = ReplicatedBlackholeFxName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * Mathf.Max(0.001f, blackholeVisualScale);
        ItemVisualCompatibilityUtility.ApplyUrpMaterialFallback(instance);

        var colliders = instance.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
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
