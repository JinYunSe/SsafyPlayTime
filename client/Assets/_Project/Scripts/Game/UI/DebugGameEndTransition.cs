using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using SSAFYPlayTime;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 최후의 1인이 남으면 자동으로 게임을 종료하고 결과 화면으로 전환한다.
/// 순위: 먼저 사망한 플레이어가 낮은 순위, 마지막 생존자가 1등.
/// StateAuthority(호스트)에서만 종료 판정·순위 배정을 수행하고,
/// RPC_BroadcastRankings로 모든 클라이언트에 순위를 전달한 뒤 씬을 전환한다.
/// </summary>
public class DebugGameEndTransition : NetworkBehaviour, IPlayerLeft
{
    [SerializeField] private string gameEndSceneName = "LauncherScene";

    // RankedPlayerIds[i] = (i+1)등 플레이어의 PlayerId
    [Networked, Capacity(8)]
    private NetworkArray<int> RankedPlayerIds { get; }

    [Networked]
    private int NetworkedPlayerCount { get; set; }

    // 방장 PlayerId: 모든 클라이언트에서 방장 퇴장을 감지하기 위해 Networked로 관리
    [Networked]
    private int NetworkedHostPlayerId { get; set; }

    // StateAuthority 전용: 사망 순서 기록 (인덱스 0 = 가장 먼저 사망)
    private readonly List<int> _deathOrder = new();
    private readonly HashSet<NetworkPlayer> _subscribedPlayers = new();

    private bool _triggered;
    private const float SubscribeCheckInterval = 0.5f;
    private float _subscribeCheckTimer = SubscribeCheckInterval;

    // ─── Fusion 생명주기 ───────────────────────────────────────────

    public override void Spawned()
    {
        // 방장 PlayerId 저장 (모든 클라이언트에 복제됨)
        if (HasStateAuthority && Runner != null && Runner.IsServer && Runner.LocalPlayer.IsRealPlayer)
            NetworkedHostPlayerId = Runner.LocalPlayer.PlayerId;

        if (!HasStateAuthority) return;
        SubscribeToPlayers();
    }

    // ─── IPlayerLeft: 방장 퇴장 감지 ──────────────────────────────

    public void PlayerLeft(PlayerRef player)
    {
        if (_triggered) return;
        if (NetworkedHostPlayerId <= 0) return;
        if (player.PlayerId != NetworkedHostPlayerId) return;

        _triggered = true;
        Debug.Log($"[GameEnd] 방장(Player{NetworkedHostPlayerId}) 퇴장 → 로비로 복귀");

        // _isShowingGameEndPanel 플래그를 먼저 세워 이후
        // OnShutdown/OnDisconnectedFromServer에서 중복 처리를 방지한다.
        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        if (lobby != null)
            lobby.TriggerHostExitAndReturnToLobby();
        else
            SceneManager.LoadScene(gameEndSceneName);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        foreach (var np in _subscribedPlayers)
        {
            if (np != null)
                np.OnNetworkPlayerDied -= OnNetworkPlayerDied;
        }
        _subscribedPlayers.Clear();

        if (GameResultData.Entries.Count > 0)
            Debug.Log($"[GameEnd] Despawned: GameResultData 저장됨 (count={GameResultData.Entries.Count})");
        else
            Debug.LogWarning("[GameEnd] Despawned: GameResultData 없음 - RPC가 도달하지 않았을 수 있음");
    }

    // ─── 새 플레이어 주기적 구독 등록 ─────────────────────────────

    private void Update()
    {
        if (!HasStateAuthority || _triggered) return;

        _subscribeCheckTimer -= Time.deltaTime;
        if (_subscribeCheckTimer > 0f) return;

        _subscribeCheckTimer = SubscribeCheckInterval;
        SubscribeToPlayers();
    }

    private void SubscribeToPlayers()
    {
        var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (var np in players)
        {
            if (np == null || _subscribedPlayers.Contains(np)) continue;
            _subscribedPlayers.Add(np);
            np.OnNetworkPlayerDied += OnNetworkPlayerDied;
        }
    }

    // ─── 사망 이벤트 → 순서 기록 → 종료 판정 ─────────────────────

    private void OnNetworkPlayerDied(NetworkPlayer deadPlayer)
    {
        if (!HasStateAuthority || _triggered) return;

        if (deadPlayer?.Object != null && deadPlayer.Object.InputAuthority.IsRealPlayer)
        {
            var pid = deadPlayer.Object.InputAuthority.PlayerId;
            if (!_deathOrder.Contains(pid))
            {
                _deathOrder.Add(pid);
                Debug.Log($"[GameEnd] 사망 기록: Player{pid} ({_deathOrder.Count}번째)");
            }
        }

        var aliveCount = _subscribedPlayers.Count(
            np => np != null &&
                  np.Object != null &&
                  np.Object.InputAuthority.IsRealPlayer &&
                  !np.IsDeadState);

        Debug.Log($"[GameEnd] 생존자 수: {aliveCount}명");

        if (aliveCount <= 1)
            TriggerGameEnd();
    }

    // ─── 게임 종료 트리거 ──────────────────────────────────────────

    private void TriggerGameEnd()
    {
        if (_triggered) return;
        _triggered = true;

        var winner = _subscribedPlayers.FirstOrDefault(
            np => np != null &&
                  np.Object != null &&
                  np.Object.InputAuthority.IsRealPlayer &&
                  !np.IsDeadState);

        string winnerName = winner != null
            ? $"Player{winner.Object.InputAuthority.PlayerId}"
            : "없음 (전원 사망)";
        Debug.Log($"[GameEnd] 게임 종료! 최후 생존자: {winnerName}");

        AssignDeathOrderRankings(winner);
        StartCoroutine(LoadSceneAfterSync());
    }

    // ─── 순위 배정 ─────────────────────────────────────────────────
    // 1등   = 마지막 생존자
    // 2등~  = 사망 역순 (나중에 죽을수록 높은 순위)
    // 꼴등  = 가장 먼저 죽은 플레이어

    private void AssignDeathOrderRankings(NetworkPlayer winner)
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner == null) return;

        var rankedIds = new List<int>();

        // 1등: 마지막 생존자
        if (winner?.Object != null && winner.Object.InputAuthority.IsRealPlayer)
            rankedIds.Add(winner.Object.InputAuthority.PlayerId);

        // 2등~: 나중에 죽은 순서부터 (역순)
        for (int i = _deathOrder.Count - 1; i >= 0; i--)
        {
            var pid = _deathOrder[i];
            if (!rankedIds.Contains(pid))
                rankedIds.Add(pid);
        }

        // 누락된 플레이어 보완 (비정상 케이스 방어)
        foreach (var pid in runner.ActivePlayers
                     .Where(p => p.IsRealPlayer)
                     .Select(p => p.PlayerId))
        {
            if (!rankedIds.Contains(pid))
                rankedIds.Add(pid);
        }

        int count = Mathf.Min(rankedIds.Count, 8);
        NetworkedPlayerCount = count;
        for (int i = 0; i < count; i++)
            RankedPlayerIds.Set(i, rankedIds[i]);

        Debug.Log($"[GameEnd] 순위: {string.Join(", ", rankedIds.Select((id, i) => $"{i + 1}등=Player{id}"))}");
    }

    // ─── 씬 전환 ───────────────────────────────────────────────────

    private IEnumerator LoadSceneAfterSync()
    {
        // NetworkArray 변경분이 클라이언트에 전달되도록 2프레임 대기
        yield return null;
        yield return null;

        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning)
        {
            RPC_BroadcastRankings();
            // RPC 전달 보장을 위해 2프레임 추가 대기
            yield return null;
            yield return null;
            runner.LoadScene(gameEndSceneName, LoadSceneMode.Single);
        }
        else
        {
            // 네트워크 없는 로컬 테스트
            SetMockRankingsForLocalTest();
            var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
            if (lobby != null)
                lobby.LoadSceneAndShowGameEndPanel(gameEndSceneName);
            else
                SceneManager.LoadScene(gameEndSceneName);
        }
    }

    // ─── RPC: 순위 브로드캐스트 ───────────────────────────────────

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastRankings()
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner == null || !runner.LocalPlayer.IsRealPlayer) return;

        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        SaveRankingsToGameResultData(runner, lobby);
        lobby?.NotifyGameEndTransition();
        Debug.Log($"[GameEnd] RPC_BroadcastRankings 완료: count={NetworkedPlayerCount}");
    }

    // ─── GameResultData 저장 ───────────────────────────────────────

    private void SaveRankingsToGameResultData(NetworkRunner runner, LobbyCanvasUIController lobby)
    {
        int count = NetworkedPlayerCount;
        if (count == 0) return;

        var charIndexByPlayerId = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)
            .Where(np => np.Object != null && np.Object.InputAuthority.IsRealPlayer)
            .ToDictionary(np => np.Object.InputAuthority.PlayerId, np => np.CharacterTypeIndex);

        GameResultData.Clear();
        for (int i = 0; i < count && i < 8; i++)
        {
            int pid = RankedPlayerIds[i];
            int rank = i + 1;
            string nickname = lobby != null ? lobby.GetParticipantNickname(pid) : $"Player{pid}";
            charIndexByPlayerId.TryGetValue(pid, out var charIdx);
            GameResultData.AddEntry(pid, nickname, rank, charIdx);
        }

        int localPid = runner.LocalPlayer.PlayerId;
        GameResultData.LocalPlayerRank = GameResultData.GetRank(localPid);
        Debug.Log($"[GameEnd] GameResultData 저장 완료: localRank={GameResultData.LocalPlayerRank}, count={count}");
    }

    // ─── 로컬 테스트용 목업 ────────────────────────────────────────

    private static void SetMockRankingsForLocalTest()
    {
        GameResultData.Clear();
        GameResultData.AddEntry(1, "테스트1", 1);
        GameResultData.AddEntry(2, "테스트2", 2);
        GameResultData.AddEntry(3, "테스트3", 3);
        GameResultData.AddEntry(4, "테스트4", 4);
        GameResultData.LocalPlayerRank = 1;
    }
}
