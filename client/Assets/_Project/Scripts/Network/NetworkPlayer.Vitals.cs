using System.Collections;
using System.Linq;
using Fusion;
using SSAFYPlayTime.Game.GhostThrow;
using UnityEngine;

public sealed partial class NetworkPlayer
{
    private const int DefaultHiddenMaxHealth = 260;
    private const float DefaultHealthHitImmunity = 0.12f;
    private const float DefaultRecentHealthDamageWindow = 3.5f;
    private const float DefaultRecentHealthDamageCap = 85f;
    private const float DefaultLowHealthStunBonus = 0.35f;
    private const float DefaultDeathFreezeDuration = 1.25f;
    private const float LocalDeathCameraHoldDuration = 0.8f;
    private const float DeadMotionDamping = 0.94f;

    [Networked] private int NetworkedMaxHealth { get; set; }
    [Networked] private int NetworkedCurrentHealth { get; set; }
    [Networked] private NetworkBool NetworkedIsDead { get; set; }
    [Networked] private int NetworkedDeathSequence { get; set; }
    [Networked] private float NetworkedRecentHealthDamage { get; set; }
    [Networked] private float NetworkedRecentHealthWindowRemaining { get; set; }
    [Networked] private float NetworkedHealthHitImmunityRemaining { get; set; }
    [Networked] private float NetworkedDeathFreezeRemaining { get; set; }

    private int _localMaxHealth = DefaultHiddenMaxHealth;
    private int _localCurrentHealth = DefaultHiddenMaxHealth;
    private bool _localIsDead;
    private int _localDeathSequence;
    private float _localRecentHealthDamage;
    private float _localRecentHealthWindowRemaining;
    private float _localHealthHitImmunityRemaining;
    private float _localDeathFreezeRemaining;
    private bool _localVitalsInitialized;
    private Coroutine _localDeathTransitionCoroutine;
    private int _lastPresentedDeathSequence = -1;
    private GameObject _localGhostRoot;
    private Camera _localGhostCamera;
    private GhostSpectatorCamera _localGhostSpectatorCamera;
    private GhostThrowManager _localGhostThrowManager;

    public bool IsDeadState => GetIsDeadState();
    public int CurrentHealth => GetCurrentHealth();
    public int MaxHealth => GetMaxHealth();
    public bool UsesLocalGhostDeathFlow => HasLocalPresentationAuthority();

    private void EnsureVitalStateInitialized()
    {
        var configuredMaxHealth = ResolveConfiguredMaxHealth();

        if (!IsNetworkReady)
        {
            if (!_localVitalsInitialized)
            {
                _localMaxHealth = configuredMaxHealth;
                _localCurrentHealth = configuredMaxHealth;
                _localIsDead = false;
                _localDeathSequence = 0;
                _localRecentHealthDamage = 0f;
                _localRecentHealthWindowRemaining = 0f;
                _localHealthHitImmunityRemaining = 0f;
                _localDeathFreezeRemaining = 0f;
                _localVitalsInitialized = true;
            }

            _localMaxHealth = Mathf.Max(1, _localMaxHealth);
            _localCurrentHealth = Mathf.Clamp(_localCurrentHealth, 0, _localMaxHealth);
            return;
        }

        if (!HasStateAuthority)
            return;

        if (NetworkedMaxHealth <= 0)
        {
            NetworkedMaxHealth = configuredMaxHealth;
            NetworkedCurrentHealth = configuredMaxHealth;
            NetworkedIsDead = false;
            NetworkedDeathSequence = 0;
            NetworkedRecentHealthDamage = 0f;
            NetworkedRecentHealthWindowRemaining = 0f;
            NetworkedHealthHitImmunityRemaining = 0f;
            NetworkedDeathFreezeRemaining = 0f;
        }
    }

    private void TickVitalState(float dt)
    {
        if (dt <= 0f)
            return;

        if (IsNetworkReady)
        {
            if (!HasStateAuthority)
                return;

            if (NetworkedRecentHealthWindowRemaining > 0f)
            {
                NetworkedRecentHealthWindowRemaining = Mathf.Max(0f, NetworkedRecentHealthWindowRemaining - dt);
                if (NetworkedRecentHealthWindowRemaining <= 0f)
                    NetworkedRecentHealthDamage = 0f;
            }

            if (NetworkedHealthHitImmunityRemaining > 0f)
                NetworkedHealthHitImmunityRemaining = Mathf.Max(0f, NetworkedHealthHitImmunityRemaining - dt);

            if (NetworkedDeathFreezeRemaining > 0f)
                NetworkedDeathFreezeRemaining = Mathf.Max(0f, NetworkedDeathFreezeRemaining - dt);

            return;
        }

        if (_localRecentHealthWindowRemaining > 0f)
        {
            _localRecentHealthWindowRemaining = Mathf.Max(0f, _localRecentHealthWindowRemaining - dt);
            if (_localRecentHealthWindowRemaining <= 0f)
                _localRecentHealthDamage = 0f;
        }

        if (_localHealthHitImmunityRemaining > 0f)
            _localHealthHitImmunityRemaining = Mathf.Max(0f, _localHealthHitImmunityRemaining - dt);

        if (_localDeathFreezeRemaining > 0f)
            _localDeathFreezeRemaining = Mathf.Max(0f, _localDeathFreezeRemaining - dt);
    }

    public bool ApplyCombinedDamage(float healthDamage, float stunDamage, string source)
    {
        return ApplyCombinedDamage(healthDamage, stunDamage, source, 0f, 0f, 1f, false);
    }

    public bool ApplyCombinedDamage(
        float healthDamage,
        float stunDamage,
        string source,
        float attackerVelocity,
        float impulseMagnitude,
        float bodyPartMultiplier = 1f,
        bool deferStunEntryDamping = false)
    {
        var applied = false;
        if (healthDamage > 0f)
            applied |= ApplyHealthDamage(healthDamage, source);

        if (stunDamage > 0f)
        {
            ApplyStunDamage(stunDamage, bodyPartMultiplier, attackerVelocity, impulseMagnitude, deferStunEntryDamping);
            applied = true;
        }

        return applied;
    }

    public bool ApplyHealthDamage(float damage, string source = "")
    {
        EnsureVitalStateInitialized();

        if (damage <= 0f || GetIsDeadState())
            return false;

        if (IsNetworkReady && !HasStateAuthority)
            return false;

        if (GetHealthHitImmunityRemaining() > 0f)
            return false;

        var pendingDamage = Mathf.Max(0f, damage);
        var recentDamageCap = ResolveConfiguredRecentHealthDamageCap();
        var recentDamage = GetRecentHealthDamage();
        if (recentDamageCap > 0f)
        {
            var allowedDamage = Mathf.Max(0f, recentDamageCap - recentDamage);
            pendingDamage = Mathf.Min(pendingDamage, allowedDamage);
        }

        if (pendingDamage <= 0f)
            return false;

        var currentHealth = GetCurrentHealth();
        var appliedDamage = Mathf.Max(1, Mathf.RoundToInt(pendingDamage));
        var nextHealth = Mathf.Max(0, currentHealth - appliedDamage);
        SetCurrentHealth(nextHealth);
        SetRecentHealthDamage(recentDamage + appliedDamage);
        SetRecentHealthWindowRemaining(ResolveConfiguredRecentHealthDamageWindow());
        SetHealthHitImmunityRemaining(ResolveConfiguredHealthHitImmunity());

        if (nextHealth <= 0)
            BeginDeath(source);

        return true;
    }

    public bool KillImmediately(string source = "ImmediateKill")
    {
        EnsureVitalStateInitialized();

        if (GetIsDeadState())
            return false;

        if (IsNetworkReady && !HasStateAuthority)
            return false;

        BeginDeath(source);
        return true;
    }

    private void BeginDeath(string source)
    {
        if (GetIsDeadState())
            return;

        SetCurrentHealth(0);
        SetIsDeadState(true);
        SetDeathSequence(GetDeathSequence() + 1);
        SetRecentHealthDamage(0f);
        SetRecentHealthWindowRemaining(0f);
        SetHealthHitImmunityRemaining(0f);
        SetDeathFreezeRemaining(DefaultDeathFreezeDuration);

        TryForceDropHeldContentOnDeath();

        if (_isActiveRagdoll)
        {
            TriggerStun(ResolveConfiguredDeathCollapseDuration(), applyEntryDamping: true);
        }
        else
        {
            ClearPunchHitDetectionWindow();
            _isLeftGrabActive = false;
            _isRightGrabActive = false;
            _isGrabActive = false;
            SetStunTimeRemaining(0f);
            SetAccumulatedStun(0f);
            CaptureCollapseAnchorPose(transform.position, transform.rotation);
            SetLocalPhysicalPhase(PhysicalPhase.Stunned, 1f, false);
            SynchronizeStunPresentationPhase();
            SetStunVisualMode(true);
            FlagPhysicsPresentationReset();
        }

        _localMoveSpeed = 0f;
        _localPresentationLocomotionState = PresentationLocomotionState.Idle;
        if (IsNetworkReady)
        {
            NetworkedMoveSpeed = 0f;
            NetworkedLocomotionState = (byte)PresentationLocomotionState.Idle;
            NetworkedIsSprinting = false;
        }

        Debug.Log($"[NetworkPlayer] {name} died. source={source}", this);
    }

    private void TryForceDropHeldContentOnDeath()
    {
        if (_handGrabHandlers != null)
        {
            for (var i = 0; i < _handGrabHandlers.Length; i++)
            {
                var handler = _handGrabHandlers[i];
                if (handler != null && handler.IsHolding)
                    handler.Drop();
            }
        }

        if (!TryPrepareItemInteractionService(out var runtimeHost) || _itemFieldInteractionService == null)
            return;

        if (!_itemFieldInteractionService.TryDropHeldItem(out var droppedItemId, out var dropSpawnPosition, out _))
            return;

        TrySpawnNetworkedFieldDrop(droppedItemId, dropSpawnPosition, runtimeHost, out _);
    }

    private bool TryTickDeadState(float dt)
    {
        if (!GetIsDeadState())
            return false;

        SetLocalPhysicalPhase(PhysicalPhase.Stunned, 1f, false);
        SetStunTimeRemaining(0f);
        ClampStunnedMotion();
        SyncRootToPhysicsBody();

        if (GetDeathFreezeRemaining() > 0f)
            return true;

        DampenDeadMotion();
        return true;
    }

    private void DampenDeadMotion()
    {
        if (rigidbody3D != null && !rigidbody3D.isKinematic)
        {
            rigidbody3D.velocity *= DeadMotionDamping;
            rigidbody3D.angularVelocity *= DeadMotionDamping;
        }

        if (_puppetMaster == null || _puppetMaster.muscles == null)
            return;

        for (var i = 0; i < _puppetMaster.muscles.Length; i++)
        {
            var joint = _puppetMaster.muscles[i].joint;
            if (joint == null)
                continue;

            var muscleBody = joint.GetComponent<Rigidbody>();
            if (muscleBody == null || muscleBody.isKinematic)
                continue;

            muscleBody.velocity *= DeadMotionDamping;
            muscleBody.angularVelocity *= DeadMotionDamping;
        }
    }

    private float ResolveLowHealthStunMultiplier()
    {
        if (GetIsDeadState())
            return 1f;

        var normalizedHealth = Mathf.Clamp01(GetHealthNormalized());
        var missingHealthRatio = 1f - normalizedHealth;
        return 1f + missingHealthRatio * ResolveConfiguredLowHealthStunBonus();
    }

    private void UpdateLocalDeathPresentation()
    {
        if (!HasLocalPresentationAuthority() || !GetIsDeadState())
            return;

        var deathSequence = GetDeathSequence();
        if (_lastPresentedDeathSequence == deathSequence)
            return;

        _lastPresentedDeathSequence = deathSequence;
        if (_localDeathTransitionCoroutine != null)
            StopCoroutine(_localDeathTransitionCoroutine);

        _localDeathTransitionCoroutine = StartCoroutine(CoRunLocalDeathTransition());
    }

    private IEnumerator CoRunLocalDeathTransition()
    {
        var hud = GameHUD.FindOrCreate();
        hud?.PlayDeathOverlay();

        yield return new WaitForSecondsRealtime(LocalDeathCameraHoldDuration);

        ActivateLocalGhostMode();
        _localDeathTransitionCoroutine = null;
    }

    private void ActivateLocalGhostMode()
    {
        if (!HasLocalPresentationAuthority() || _localGhostRoot != null)
            return;

        var focusPoint = ResolveGhostFocusPoint();
        var startPosition = focusPoint + new Vector3(0f, 10f, -16f);

        _localGhostRoot = new GameObject($"{name}_LocalGhostMode");
        _localGhostRoot.transform.position = startPosition;
        _localGhostRoot.transform.rotation = Quaternion.LookRotation((focusPoint - startPosition).normalized, Vector3.up);

        _localGhostCamera = _localGhostRoot.AddComponent<Camera>();
        _localGhostCamera.tag = "MainCamera";
        _localGhostCamera.fieldOfView = 60f;
        _localGhostCamera.nearClipPlane = 0.03f;
        _localGhostCamera.farClipPlane = 400f;

        _localGhostRoot.AddComponent<AudioListener>();

        _localGhostSpectatorCamera = _localGhostRoot.AddComponent<GhostSpectatorCamera>();
        _localGhostThrowManager = _localGhostRoot.AddComponent<GhostThrowManager>();
        _localGhostThrowManager.SetGhostControlEnabled(true);
        _localGhostThrowManager.SetEnableOutOfBoundsKillCheck(false);

        SetLocalCharacterPresentationEnabled(false);
    }

    private void DestroyLocalGhostMode()
    {
        if (_localDeathTransitionCoroutine != null)
        {
            StopCoroutine(_localDeathTransitionCoroutine);
            _localDeathTransitionCoroutine = null;
        }

        if (_localGhostRoot != null)
            Destroy(_localGhostRoot);

        _localGhostRoot = null;
        _localGhostCamera = null;
        _localGhostSpectatorCamera = null;
        _localGhostThrowManager = null;
    }

    private void SetLocalCharacterPresentationEnabled(bool enabled)
    {
        if (!HasLocalPresentationAuthority())
            return;

        CacheOwnedPresentationComponents();

        foreach (var camera in _ownedCamerasCache)
        {
            if (camera == null)
                continue;

            camera.enabled = enabled;
            if (!enabled && camera.CompareTag("MainCamera"))
                camera.tag = "Untagged";
            else if (enabled)
                camera.tag = "MainCamera";
        }

        foreach (var listener in _ownedListenersCache)
        {
            if (listener != null)
                listener.enabled = enabled;
        }

        foreach (var cameraRig in _ownedCameraRigsCache)
        {
            if (cameraRig == null)
                continue;

            cameraRig.enabled = enabled;
            if (enabled)
                cameraRig.SetTarget(_cameraFollowAnchor != null ? _cameraFollowAnchor : transform);
        }

        foreach (var controller in _ownedCameraModeControllersCache)
        {
            if (controller == null)
                continue;

            controller.enabled = enabled;
            if (enabled)
                controller.BindLocalPlayer(gameObject);
        }
    }

    private Vector3 ResolveGhostFocusPoint()
    {
        var alivePlayers = FindObjectsOfType<PlayerStats>(true)
            .Where(stats => stats != null && stats.currentHealth > 0)
            .Select(stats => stats.transform.position)
            .ToArray();

        if (alivePlayers.Length == 0)
            return transform.position + Vector3.up * 1.5f;

        var center = Vector3.zero;
        for (var i = 0; i < alivePlayers.Length; i++)
            center += alivePlayers[i];

        return center / alivePlayers.Length;
    }

    private bool HasLocalPresentationAuthority()
    {
        return Runner == null || HasInputAuthority;
    }

    private int ResolveConfiguredMaxHealth()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(1, CombatSettings.Instance.hiddenHealthMax)
            : DefaultHiddenMaxHealth;
    }

    private float ResolveConfiguredHealthHitImmunity()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(0f, CombatSettings.Instance.hiddenHealthHitImmunity)
            : DefaultHealthHitImmunity;
    }

    private float ResolveConfiguredRecentHealthDamageWindow()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(0f, CombatSettings.Instance.hiddenHealthRecentDamageWindow)
            : DefaultRecentHealthDamageWindow;
    }

    private float ResolveConfiguredRecentHealthDamageCap()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(1f, CombatSettings.Instance.hiddenHealthRecentDamageCap)
            : DefaultRecentHealthDamageCap;
    }

    private float ResolveConfiguredLowHealthStunBonus()
    {
        return CombatSettings.Instance != null
            ? Mathf.Max(0f, CombatSettings.Instance.hiddenHealthLowHpStunBonus)
            : DefaultLowHealthStunBonus;
    }

    private float ResolveConfiguredDeathCollapseDuration()
    {
        if (CombatSettings.Instance == null)
            return 2.6f;

        return Mathf.Clamp(CombatSettings.Instance.stunMinDuration + 1.1f, CombatSettings.Instance.stunMinDuration, CombatSettings.Instance.stunMaxDuration);
    }

    private float GetHealthNormalized()
    {
        var maxHealth = Mathf.Max(1, GetMaxHealth());
        return Mathf.Clamp01((float)GetCurrentHealth() / maxHealth);
    }

    private int GetMaxHealth()
    {
        if (IsNetworkReady)
        {
            if (NetworkedMaxHealth > 0)
                return NetworkedMaxHealth;

            return ResolveConfiguredMaxHealth();
        }

        return Mathf.Max(1, _localMaxHealth);
    }

    private void SetMaxHealth(int value)
    {
        var clamped = Mathf.Max(1, value);
        if (IsNetworkReady)
            NetworkedMaxHealth = clamped;
        else
            _localMaxHealth = clamped;
    }

    private int GetCurrentHealth()
    {
        if (IsNetworkReady)
            return Mathf.Clamp(NetworkedCurrentHealth, 0, Mathf.Max(1, GetMaxHealth()));

        return Mathf.Clamp(_localCurrentHealth, 0, Mathf.Max(1, GetMaxHealth()));
    }

    private void SetCurrentHealth(int value)
    {
        var clamped = Mathf.Clamp(value, 0, Mathf.Max(1, GetMaxHealth()));
        if (IsNetworkReady)
            NetworkedCurrentHealth = clamped;
        else
            _localCurrentHealth = clamped;
    }

    private bool GetIsDeadState()
    {
        if (IsNetworkReady)
            return NetworkedIsDead;

        return _localIsDead;
    }

    private void SetIsDeadState(bool value)
    {
        if (IsNetworkReady)
            NetworkedIsDead = value;
        else
            _localIsDead = value;
    }

    private int GetDeathSequence()
    {
        if (IsNetworkReady)
            return NetworkedDeathSequence;

        return _localDeathSequence;
    }

    private void SetDeathSequence(int value)
    {
        if (IsNetworkReady)
            NetworkedDeathSequence = value;
        else
            _localDeathSequence = value;
    }

    private float GetRecentHealthDamage()
    {
        if (IsNetworkReady)
            return NetworkedRecentHealthDamage;

        return _localRecentHealthDamage;
    }

    private void SetRecentHealthDamage(float value)
    {
        var clamped = Mathf.Max(0f, value);
        if (IsNetworkReady)
            NetworkedRecentHealthDamage = clamped;
        else
            _localRecentHealthDamage = clamped;
    }

    private float GetRecentHealthWindowRemaining()
    {
        if (IsNetworkReady)
            return NetworkedRecentHealthWindowRemaining;

        return _localRecentHealthWindowRemaining;
    }

    private void SetRecentHealthWindowRemaining(float value)
    {
        var clamped = Mathf.Max(0f, value);
        if (IsNetworkReady)
            NetworkedRecentHealthWindowRemaining = clamped;
        else
            _localRecentHealthWindowRemaining = clamped;
    }

    private float GetHealthHitImmunityRemaining()
    {
        if (IsNetworkReady)
            return NetworkedHealthHitImmunityRemaining;

        return _localHealthHitImmunityRemaining;
    }

    private void SetHealthHitImmunityRemaining(float value)
    {
        var clamped = Mathf.Max(0f, value);
        if (IsNetworkReady)
            NetworkedHealthHitImmunityRemaining = clamped;
        else
            _localHealthHitImmunityRemaining = clamped;
    }

    private float GetDeathFreezeRemaining()
    {
        if (IsNetworkReady)
            return NetworkedDeathFreezeRemaining;

        return _localDeathFreezeRemaining;
    }

    private void SetDeathFreezeRemaining(float value)
    {
        var clamped = Mathf.Max(0f, value);
        if (IsNetworkReady)
            NetworkedDeathFreezeRemaining = clamped;
        else
            _localDeathFreezeRemaining = clamped;
    }
}
