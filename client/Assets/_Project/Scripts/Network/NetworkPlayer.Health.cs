using System;
using Fusion;
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
        var cameraControllers = FindObjectsByType<CameraModeController>(FindObjectsSortMode.None);
        for (var i = 0; i < cameraControllers.Length; i++)
        {
            var controller = cameraControllers[i];
            if (controller != null)
                controller.ForceHandleLocalPlayerDied(playerStats);
        }

        var ghostManagers = FindObjectsByType<GhostThrowManager>(FindObjectsSortMode.None);
        for (var i = 0; i < ghostManagers.Length; i++)
        {
            var manager = ghostManagers[i];
            if (manager != null)
                manager.ForceEnableGhostThrow($"{gameObject.name} death");
        }

        if (ghostManagers.Length == 0)
            Debug.LogWarning($"[HP] {gameObject.name} death transition found no GhostThrowManager in scene.");
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
        var manager = GetComponentInChildren<GhostThrowManager>(true);
        if (manager == null)
        {
            Debug.LogWarning($"[HP] {gameObject.name} could not find GhostThrowManager for ghost throw request.");
            return false;
        }

        return manager.TrySpawnOnlineFromRequest(isBanana, spawnPos, direction);
    }
}
