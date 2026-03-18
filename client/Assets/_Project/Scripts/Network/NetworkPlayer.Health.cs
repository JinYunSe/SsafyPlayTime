using System;
using System.Linq;
using Fusion;
using RootMotion.Dynamics;
using SSAFYPlayTime.Game.GhostThrow;
using UnityEngine;

/// <summary>
/// NetworkPlayer HP and death handling.
/// StateAuthority owns HP mutation and broadcasts death to all clients.
/// </summary>
public sealed partial class NetworkPlayer
{
    [Networked] public float NetworkedHp { get; set; }
    [Networked] public NetworkBool IsDeadNetworked { get; set; }

    public event Action<NetworkPlayer> OnNetworkPlayerDied;

    public float MaxHp => CombatSettings.Instance != null
        ? CombatSettings.Instance.maxHealth
        : 200f;

    private void InitializeHp()
    {
        if (!HasStateAuthority)
            return;

        NetworkedHp = MaxHp;
        IsDeadNetworked = false;
    }

    public void ApplyHpDamage(float damage)
    {
        if (Runner != null && Object != null && Object.IsValid && !HasStateAuthority)
            return;

        if (IsDeadNetworked)
            return;

        var newHp = Mathf.Max(0f, NetworkedHp - Mathf.Max(0f, damage));
        NetworkedHp = newHp;

        Debug.Log($"[HP] {gameObject.name} HP: {newHp:F0}/{MaxHp:F0} (-{damage:F1})");

        if (newHp <= 0f)
            TriggerDeath();
    }

    private void TriggerDeath()
    {
        if (IsDeadNetworked)
            return;

        IsDeadNetworked = true;
        Debug.Log($"[HP] {gameObject.name} died.");

        // Freeze the character in a long stun state so the corpse cannot act.
        TriggerStun(9999f);

        if (Runner != null && Object != null && Object.IsValid)
            RPC_OnPlayerDied();
        else
            HandleDeathVisuals();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnPlayerDied()
    {
        HandleDeathVisuals();
    }

    private void HandleDeathVisuals()
    {
        var playerStats = GetComponent<PlayerStats>();
        if (playerStats == null)
            playerStats = GetComponentInChildren<PlayerStats>(true);
        if (playerStats == null)
            playerStats = GetComponentInParent<PlayerStats>();

        if (playerStats != null)
            playerStats.NotifyDeathFromNetwork();

        ApplyDeathPresentationState();

        if (Runner == null || HasInputAuthority)
            HandleLocalDeathTransition(playerStats);

        OnNetworkPlayerDied?.Invoke(this);
        Debug.Log($"[HP] {gameObject.name} death visuals and events processed.");
    }

    private void HandleLocalDeathTransition(PlayerStats playerStats)
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SwitchOwnedCameraToSpectatorMode();

        var cameraControllers = FindObjectsByType<CameraModeController>(FindObjectsSortMode.None);
        for (var i = 0; i < cameraControllers.Length; i++)
        {
            var controller = cameraControllers[i];
            if (controller != null)
                controller.ForceHandleLocalPlayerDied(playerStats);
        }
    }

    // 플레이어 카메라의 CameraRig를 끄고 GhostSpectatorCamera + GhostThrowManager를 활성화한다.
    // 멀티 환경에서 각 클라이언트가 자신의 카메라를 독립적으로 스펙테이터 모드로 전환한다.
    private void SwitchOwnedCameraToSpectatorMode()
    {
        CacheOwnedPresentationComponents();

        // CameraRig / CameraModeController 비활성화 (플레이어 추적 중단)
        if (_ownedCameraRigsCache != null)
            for (var i = 0; i < _ownedCameraRigsCache.Length; i++)
                if (_ownedCameraRigsCache[i] != null)
                    _ownedCameraRigsCache[i].enabled = false;

        if (_ownedCameraModeControllersCache != null)
            for (var i = 0; i < _ownedCameraModeControllersCache.Length; i++)
                if (_ownedCameraModeControllersCache[i] != null)
                    _ownedCameraModeControllersCache[i].enabled = false;

        // 소유 카메라에 붙은 GhostSpectatorCamera + GhostThrowManager 활성화
        if (_ownedCamerasCache == null) return;
        for (var i = 0; i < _ownedCamerasCache.Length; i++)
        {
            var cam = _ownedCamerasCache[i];
            if (cam == null) continue;

            var ghostCam = cam.GetComponent<GhostSpectatorCamera>();
            if (ghostCam != null) ghostCam.enabled = true;

            var throwManager = cam.GetComponent<GhostThrowManager>();
            if (throwManager != null)
            {
                throwManager.enabled = true;
                throwManager.ForceEnableGhostThrow($"{gameObject.name} death");
            }
        }
    }

    private void DisableOwnedCamerasForDeathView()
    {
        CacheOwnedPresentationComponents();

        if (_ownedCamerasCache != null)
        {
            for (var i = 0; i < _ownedCamerasCache.Length; i++)
            {
                var ownedCamera = _ownedCamerasCache[i];
                if (ownedCamera == null)
                    continue;

                ownedCamera.enabled = false;
                if (ownedCamera.CompareTag("MainCamera"))
                    ownedCamera.tag = "Untagged";
            }
        }

        if (_ownedListenersCache != null)
        {
            for (var i = 0; i < _ownedListenersCache.Length; i++)
            {
                var listener = _ownedListenersCache[i];
                if (listener != null)
                    listener.enabled = false;
            }
        }

        if (_ownedCameraRigsCache != null)
        {
            for (var i = 0; i < _ownedCameraRigsCache.Length; i++)
            {
                var rig = _ownedCameraRigsCache[i];
                if (rig != null)
                    rig.enabled = false;
            }
        }

        if (_ownedCameraModeControllersCache != null)
        {
            for (var i = 0; i < _ownedCameraModeControllersCache.Length; i++)
            {
                var controller = _ownedCameraModeControllersCache[i];
                if (controller != null)
                    controller.enabled = false;
            }
        }
    }

    private void ActivateSceneSpectatorCamera()
    {
        var ownedCameraSet = _ownedCamerasCache != null
            ? _ownedCamerasCache.Where(c => c != null).ToHashSet()
            : new System.Collections.Generic.HashSet<Camera>();

        Camera fallbackCamera = null;
        var allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (var i = 0; i < allCameras.Length; i++)
        {
            var camera = allCameras[i];
            if (camera == null || ownedCameraSet.Contains(camera))
                continue;

            fallbackCamera ??= camera;
            camera.enabled = true;

            if (!camera.CompareTag("MainCamera"))
                camera.tag = "MainCamera";

            var listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = true;

            var ghostSpectator = camera.GetComponent<GhostSpectatorCamera>();
            if (ghostSpectator != null)
                ghostSpectator.enabled = true;
        }

        if (fallbackCamera == null)
            Debug.LogWarning($"[HP] {gameObject.name} death transition found no non-owned scene camera to activate.");
    }

    private void ApplyDeathPresentationState()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }

        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        var rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        for (var i = 0; i < rigidbodies.Length; i++)
        {
            var body = rigidbodies[i];
            if (body == null)
                continue;

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        // PuppetMaster 완전 정지
        // mode = Disabled는 SwitchModes() 지연 전환이라 즉시 효과 없음.
        // 컴포넌트 비활성화 → FixedUpdate/LateUpdate 콜백 즉시 중단.
        // (muscle RB는 위 GetComponentsInChildren<Rigidbody> 루프에서 이미 처리됨)
        if (_puppetMaster != null)
        {
            // BehaviourPuppet/BehaviourPuppetDamage 비활성화
            // → 충돌 콜백에서 mode를 Active로 되돌리는 것을 차단
            foreach (var behaviour in _puppetMaster.behaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = false;
            }

            _puppetMaster.enabled = false;
        }

        // 사망 캐릭터를 (0,0,0)으로 이동 → 네트워크 동기화 부하 최소화
        if (HasStateAuthority || Runner == null)
            transform.position = Vector3.zero;
    }

    public void TakeDamageFromStats(int damage)
    {
        ApplyHpDamage(damage);
    }

    public bool TryRequestGhostThrow(bool isBanana, Vector3 spawnPos, Vector3 direction)
    {
        if (Runner == null || Object == null || !Object.IsValid)
            return false;

        if (HasStateAuthority)
            return TrySpawnGhostThrowOnStateAuthority(isBanana, spawnPos, direction);

        if (!HasInputAuthority)
            return false;

        RPC_RequestGhostThrow(isBanana, spawnPos, direction);
        return true;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestGhostThrow(bool isBanana, Vector3 spawnPos, Vector3 direction)
    {
        TrySpawnGhostThrowOnStateAuthority(isBanana, spawnPos, direction);
    }

    private bool TrySpawnGhostThrowOnStateAuthority(bool isBanana, Vector3 spawnPos, Vector3 direction)
    {
        var manager = GhostThrowManager.FindManagerForPlayer(this);
        if (manager == null)
        {
            Debug.LogWarning($"[HP] {gameObject.name} could not find GhostThrowManager for ghost throw request.");
            return false;
        }

        return manager.TrySpawnOnlineFromRequest(isBanana, spawnPos, direction);
    }
}
