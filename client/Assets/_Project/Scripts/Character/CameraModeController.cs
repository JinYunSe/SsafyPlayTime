using System.Linq;

using Fusion;
using UnityEngine;

public class CameraModeController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CameraRig cameraRig;
    [SerializeField] private SpectatorCamera spectatorCamera;

    [Header("Auto Bind Local Player")]
    [SerializeField] private string localPlayerTag = "Player";
    [SerializeField] private Transform manualLocalTarget;

    private PlayerStats _localPlayerStats;
    private NetworkPlayer _localNetworkPlayer;
    private bool _hasHandledDeathTransition;

    private void Start()
    {
        var localPlayer = ResolveLocalPlayerObject();
        if (localPlayer != null)
        {
            BindLocalPlayer(localPlayer);
            return;
        }

        Debug.LogError($"CameraModeController: local player not found. Tag={localPlayerTag}");
    }

    public void BindLocalPlayer(GameObject localPlayer)
    {
        if (localPlayer == null)
        {
            Debug.LogError("CameraModeController: localPlayer is null");
            return;
        }

        _localPlayerStats = localPlayer.GetComponent<PlayerStats>();
        if (_localPlayerStats == null)
            _localPlayerStats = localPlayer.GetComponentInChildren<PlayerStats>(true);
        if (_localPlayerStats == null)
            _localPlayerStats = localPlayer.GetComponentInParent<PlayerStats>();

        _localNetworkPlayer = localPlayer.GetComponent<NetworkPlayer>();
        if (_localNetworkPlayer == null)
            _localNetworkPlayer = localPlayer.GetComponentInChildren<NetworkPlayer>(true);
        if (_localNetworkPlayer == null)
            _localNetworkPlayer = localPlayer.GetComponentInParent<NetworkPlayer>();

        if (_localPlayerStats != null)
        {
            _localPlayerStats.OnDied -= HandleLocalPlayerDied;
            _localPlayerStats.OnDied += HandleLocalPlayerDied;
        }

        if (_localPlayerStats == null && _localNetworkPlayer == null)
        {
            Debug.LogError($"CameraModeController: PlayerStats and NetworkPlayer missing on {localPlayer.name}");
            return;
        }

        spectatorCamera.EnableSpectator(false);
        cameraRig.enabled = true;

        var followTarget = _localNetworkPlayer != null ? _localNetworkPlayer.GetCameraFollowTarget() : _localPlayerStats.transform;
        cameraRig.SetTarget(followTarget);
        _hasHandledDeathTransition = false;

        Debug.Log($"CameraModeController: Alive mode, target = {localPlayer.name}");
    }

    private void Update()
    {
        if (_hasHandledDeathTransition)
            return;

        if (_localPlayerStats != null && _localPlayerStats.IsDead)
        {
            ActivateSpectatorMode();
            return;
        }

        if (_localNetworkPlayer != null && _localNetworkPlayer.IsDeadNetworked)
            ActivateSpectatorMode();
    }

    private GameObject ResolveLocalPlayerObject()
    {
        if (manualLocalTarget != null)
            return manualLocalTarget.gameObject;

        var allPlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        for (var i = 0; i < allPlayers.Length; i++)
        {
            var player = allPlayers[i];
            if (player == null)
                continue;

            var networkObject = player.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.HasInputAuthority)
                return player.gameObject;
        }

        var allStats = FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
        for (var i = 0; i < allStats.Length; i++)
        {
            var stats = allStats[i];
            if (stats == null)
                continue;

            var networkObject = stats.GetComponent<NetworkObject>();
            if (networkObject == null)
                networkObject = stats.GetComponentInParent<NetworkObject>();
            if (networkObject == null)
                networkObject = stats.GetComponentInChildren<NetworkObject>(true);

            if (networkObject != null && networkObject.HasInputAuthority)
                return stats.gameObject;
        }

        return GameObject.FindGameObjectWithTag(localPlayerTag);
    }

    private void HandleLocalPlayerDied(PlayerStats dead)
    {
        ActivateSpectatorMode(dead != null ? dead.gameObject.name : null);
    }

    private void ActivateSpectatorMode(string deadPlayerName = null)
    {
        _hasHandledDeathTransition = true;
        var name = string.IsNullOrWhiteSpace(deadPlayerName)
            ? (_localNetworkPlayer != null ? _localNetworkPlayer.gameObject.name : "local player")
            : deadPlayerName;

        Debug.Log($"CameraModeController: Local player died -> {name}, switch to spectator");

        var alivePlayers = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)
            .Where(p => p != null && p.gameObject.activeInHierarchy && !p.IsDeadNetworked)
            .Select(p => p.GetCameraFollowTarget() != null ? p.GetCameraFollowTarget() : p.transform)
            .ToList();

        Debug.Log($"CameraModeController: alive targets = {alivePlayers.Count}");

        cameraRig.enabled = false;

        spectatorCamera.SetTargets(alivePlayers);
        spectatorCamera.EnableSpectator(true);
    }

    public void ForceHandleLocalPlayerDied(PlayerStats dead)
    {
        ActivateSpectatorMode(dead != null ? dead.gameObject.name : null);
    }
}
