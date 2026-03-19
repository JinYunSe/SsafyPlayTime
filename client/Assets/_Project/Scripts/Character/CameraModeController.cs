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

        // 네트워크 환경에서는 NetworkPlayer.Spawned()에서 BindLocalPlayer()를 직접 호출하므로 정상
        Debug.LogWarning($"CameraModeController: Start()에서 로컬 플레이어를 못 찾음 (Tag={localPlayerTag}). 네트워크 스폰 후 BindLocalPlayer()로 바인딩 예정.");
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

        // NetworkPlayer가 있으면 카메라 Follow 앵커를 사용 (SyncRootToPhysicsBody 급변 흡수)
        var networkPlayer = localPlayer.GetComponent<NetworkPlayer>();
        var followTarget = networkPlayer != null ? networkPlayer.GetCameraFollowTarget() : localPlayer.transform;
        cameraRig.SetTarget(followTarget);

        Debug.Log($"CameraModeController: Alive mode, target = {localPlayer.name}");
    }

    private void HandleLocalPlayerDied(PlayerStats dead)
    {
        var networkPlayer = dead != null ? dead.GetComponent<NetworkPlayer>() : null;
        if (networkPlayer != null && networkPlayer.UsesLocalGhostDeathFlow)
        {
            if (cameraRig != null)
            {
                cameraRig.enabled = true;
                cameraRig.SetTarget(networkPlayer.GetCameraFollowTarget());
            }

            if (spectatorCamera != null)
                spectatorCamera.EnableSpectator(false);

            Debug.Log($"CameraModeController: Local player died -> {dead.gameObject.name}, defer to NetworkPlayer ghost death flow");
            return;
        }

        Debug.Log($"CameraModeController: Local player died -> {dead.gameObject.name}, switch to spectator");

        // Spectator mode: 살아있는 플레이어들 전부 모아서 타겟으로
        var alivePlayers = FindObjectsOfType<PlayerStats>()
            .Where(p => p != null && p.gameObject.activeInHierarchy && p.currentHealth > 0)
            .Select(p => p.transform)
            .ToList();

        Debug.Log($"CameraModeController: alive targets = {alivePlayers.Count}");

        if (cameraRig != null)
            cameraRig.enabled = false;

        if (spectatorCamera != null)
        {
            spectatorCamera.SetTargets(alivePlayers);
            spectatorCamera.EnableSpectator(true);
        }
    }
}
