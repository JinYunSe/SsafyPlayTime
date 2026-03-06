using System.Linq;

using UnityEngine;

public class CameraModeController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CameraRig cameraRig;
    [SerializeField] private SpectatorCamera spectatorCamera;

    [Header("Auto Bind Local Player")]
    [SerializeField] private string localPlayerTag = "Player";
    [SerializeField] private Transform manualLocalTarget; // 필요하면 여기 지정

    private PlayerStats _localPlayerStats;

    private void Start()
    {
        // 수동 지정 우선
        if (manualLocalTarget != null)
        {
            BindLocalPlayer(manualLocalTarget.gameObject);
            return;
        }

        // 태그로 자동 탐색
        var go = GameObject.FindGameObjectWithTag(localPlayerTag);
        if (go != null)
        {
            BindLocalPlayer(go);
            return;
        }

        Debug.LogError($"CameraModeController: 로컬 플레이어를 못 찾음, Tag={localPlayerTag} 확인 필요");
    }

    public void BindLocalPlayer(GameObject localPlayer)
    {
        if (localPlayer == null)
        {
            Debug.LogError("CameraModeController: localPlayer가 null");
            return;
        }

        _localPlayerStats = localPlayer.GetComponent<PlayerStats>();
        if (_localPlayerStats == null)
        {
            Debug.LogError($"CameraModeController: {localPlayer.name} 에 PlayerStats가 없음 (관전 전환 불가)");
            return;
        }

        // 중복 구독 방지
        _localPlayerStats.OnDied -= HandleLocalPlayerDied;
        _localPlayerStats.OnDied += HandleLocalPlayerDied;

        // Alive mode
        spectatorCamera.EnableSpectator(false);
        cameraRig.enabled = true;
        cameraRig.SetTarget(localPlayer.transform);

        Debug.Log($"CameraModeController: Alive mode, target = {localPlayer.name}");
    }

    private void HandleLocalPlayerDied(PlayerStats dead)
    {
        Debug.Log($"CameraModeController: Local player died -> {dead.gameObject.name}, switch to spectator");

        // Spectator mode: 살아있는 플레이어들 전부 모아서 타겟으로
        var alivePlayers = FindObjectsOfType<PlayerStats>()
            .Where(p => p != null && p.gameObject.activeInHierarchy && p.currentHealth > 0)
            .Select(p => p.transform)
            .ToList();

        Debug.Log($"CameraModeController: alive targets = {alivePlayers.Count}");

        cameraRig.enabled = false;

        spectatorCamera.SetTargets(alivePlayers);
        spectatorCamera.EnableSpectator(true);
    }
}
