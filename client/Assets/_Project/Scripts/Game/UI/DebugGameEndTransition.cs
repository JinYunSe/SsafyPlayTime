using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using SSAFYPlayTime;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ends the match and moves back to LauncherScene.
/// - Host automatically finishes the match when one real player remains.
/// - When debugEnabled is true, F9 can still force a debug transition.
/// </summary>
public class DebugGameEndTransition : NetworkBehaviour, IPlayerLeft
{
    [Header("Debug")]
    [Tooltip("When enabled, pressing F9 forces an immediate game-end transition.")]
    [SerializeField] private bool debugEnabled = false;

    [SerializeField] private string gameEndSceneName = "LauncherScene";

    // RankedPlayerIds[i] = PlayerId of rank (i + 1).
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

        // 방장 퇴장 → 로비로 복귀
        if (NetworkedHostPlayerId > 0 && player.PlayerId == NetworkedHostPlayerId)
        {
            _triggered = true;
            Debug.Log($"[GameEnd] 방장(Player{NetworkedHostPlayerId}) 퇴장 → 로비로 복귀");

            // _isShowingGameEndPanel 플래그를 먼저 세워 이후
            // OnShutdown/OnDisconnectedFromServer에서 중복 처리를 방지한다.
            var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
            if (lobby != null)
                lobby.TriggerHostExitAndReturnToLobby();
            else
                SceneManager.LoadScene(gameEndSceneName);
            return;
        }

        // 비방장 퇴장 → 생존자 수 재체크 (StateAuthority만)
        if (!HasStateAuthority) return;

        var aliveCount = _subscribedPlayers.Count(
            np => np != null &&
                  np.Object != null &&
                  np.Object.InputAuthority.IsRealPlayer &&
                  np.Object.InputAuthority.PlayerId != player.PlayerId &&
                  !np.IsDeadState);

        Debug.Log($"[GameEnd] 비방장(Player{player.PlayerId}) 퇴장 → 생존자 수: {aliveCount}명");

        if (aliveCount <= 1)
            TriggerGameEnd();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        foreach (var player in _subscribedPlayers)
        {
            if (player != null)
                player.OnNetworkPlayerDied -= OnNetworkPlayerDied;
        }

        _subscribedPlayers.Clear();

        if (GameResultData.Entries.Count > 0)
            Debug.Log($"[GameEnd] Despawned: GameResultData saved (count={GameResultData.Entries.Count})");
        else
            Debug.LogWarning("[GameEnd] Despawned: GameResultData missing.");
    }

    private void Update()
    {
        if (!_triggered && debugEnabled && Input.GetKeyDown(KeyCode.F9))
        {
            HandleDebugTrigger();
            return;
        }

        if (!HasStateAuthority || _triggered)
            return;

        _subscribeCheckTimer -= Time.deltaTime;
        if (_subscribeCheckTimer > 0f)
            return;

        _subscribeCheckTimer = SubscribeCheckInterval;
        SubscribeToPlayers();
    }

    private void HandleDebugTrigger()
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();

        if (runner != null && runner.IsRunning && runner.IsServer)
        {
            TriggerDebugGameEnd(runner);
            return;
        }

        if (runner != null && runner.IsRunning)
        {
            _triggered = true;
            RPC_RequestDebugGameEnd();
            return;
        }

        _triggered = true;
        SetMockRankingsForLocalTest();
        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        if (lobby != null)
            lobby.LoadSceneAndShowGameEndPanel(gameEndSceneName);
        else
            SceneManager.LoadScene(gameEndSceneName);
    }

    private void SubscribeToPlayers()
    {
        var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            if (player == null || _subscribedPlayers.Contains(player))
                continue;

            _subscribedPlayers.Add(player);
            player.OnNetworkPlayerDied += OnNetworkPlayerDied;
        }
    }

    private void OnNetworkPlayerDied(NetworkPlayer deadPlayer)
    {
        if (!HasStateAuthority || _triggered)
            return;

        if (deadPlayer?.Object != null && deadPlayer.Object.InputAuthority.IsRealPlayer)
        {
            var playerId = deadPlayer.Object.InputAuthority.PlayerId;
            if (!_deathOrder.Contains(playerId))
            {
                _deathOrder.Add(playerId);
                Debug.Log($"[GameEnd] Recorded death order for Player{playerId}.");
            }
        }

        var aliveCount = _subscribedPlayers.Count(
            player => player != null &&
                      player.Object != null &&
                      player.Object.InputAuthority.IsRealPlayer &&
                      !player.IsDeadState);

        Debug.Log($"[GameEnd] Alive real players: {aliveCount}");

        if (aliveCount <= 1)
            TriggerGameEnd();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDebugGameEnd()
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner == null || !runner.IsRunning || !runner.IsServer)
            return;

        TriggerDebugGameEnd(runner);
    }

    private void TriggerDebugGameEnd(NetworkRunner runner)
    {
        if (_triggered)
            return;

        _triggered = true;
        AssignRandomRankings(runner);
        StartCoroutine(LoadSceneAfterSync(runner));
    }

    private void TriggerGameEnd()
    {
        if (_triggered)
            return;

        _triggered = true;

        var winner = _subscribedPlayers.FirstOrDefault(
            player => player != null &&
                      player.Object != null &&
                      player.Object.InputAuthority.IsRealPlayer &&
                      !player.IsDeadState);

        AssignDeathOrderRankings(winner);
        StartCoroutine(LoadSceneAfterSync());
    }

    private void AssignRandomRankings(NetworkRunner runner)
    {
        var playerIds = runner.ActivePlayers
            .Where(player => player.IsRealPlayer)
            .Select(player => player.PlayerId)
            .ToList();

        Shuffle(playerIds);

        var count = Mathf.Min(playerIds.Count, 8);
        NetworkedPlayerCount = count;
        for (var i = 0; i < count; i++)
            RankedPlayerIds.Set(i, playerIds[i]);
    }

    private void AssignDeathOrderRankings(NetworkPlayer winner)
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner == null)
            return;

        var rankedIds = new List<int>();

        if (winner?.Object != null && winner.Object.InputAuthority.IsRealPlayer)
            rankedIds.Add(winner.Object.InputAuthority.PlayerId);

        for (var i = _deathOrder.Count - 1; i >= 0; i--)
        {
            var playerId = _deathOrder[i];
            if (!rankedIds.Contains(playerId))
                rankedIds.Add(playerId);
        }

        foreach (var playerId in runner.ActivePlayers.Where(player => player.IsRealPlayer).Select(player => player.PlayerId))
        {
            if (!rankedIds.Contains(playerId))
                rankedIds.Add(playerId);
        }

        var count = Mathf.Min(rankedIds.Count, 8);
        NetworkedPlayerCount = count;
        for (var i = 0; i < count; i++)
            RankedPlayerIds.Set(i, rankedIds[i]);

        Debug.Log($"[GameEnd] Rankings: {string.Join(", ", rankedIds.Select((id, i) => $"{i + 1}=Player{id}"))}");
    }

    private IEnumerator LoadSceneAfterSync(NetworkRunner runner = null)
    {
        yield return null;
        yield return null;

        runner ??= Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning)
        {
            RPC_BroadcastRankings();
            yield return null;
            yield return null;
            runner.LoadScene(gameEndSceneName, LoadSceneMode.Single);
            yield break;
        }

        SetMockRankingsForLocalTest();
        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        if (lobby != null)
            lobby.LoadSceneAndShowGameEndPanel(gameEndSceneName);
        else
            SceneManager.LoadScene(gameEndSceneName);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastRankings()
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner == null || !runner.LocalPlayer.IsRealPlayer)
            return;

        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        SaveRankingsToGameResultData(runner, lobby);
        lobby?.NotifyGameEndTransition();
        Debug.Log($"[GameEnd] RPC_BroadcastRankings complete: count={NetworkedPlayerCount}");
    }

    private void SaveRankingsToGameResultData(NetworkRunner runner, LobbyCanvasUIController lobby)
    {
        var count = NetworkedPlayerCount;
        if (count == 0)
            return;

        var charIndexByPlayerId = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None)
            .Where(player => player.Object != null && player.Object.InputAuthority.IsRealPlayer)
            .ToDictionary(player => player.Object.InputAuthority.PlayerId, player => player.CharacterTypeIndex);

        GameResultData.Clear();
        for (var i = 0; i < count && i < 8; i++)
        {
            var playerId = RankedPlayerIds[i];
            var rank = i + 1;
            var nickname = lobby != null ? lobby.GetParticipantNickname(playerId) : $"Player{playerId}";
            charIndexByPlayerId.TryGetValue(playerId, out var charIndex);
            GameResultData.AddEntry(playerId, nickname, rank, charIndex);
        }

        var localPlayerId = runner.LocalPlayer.PlayerId;
        GameResultData.LocalPlayerRank = GameResultData.GetRank(localPlayerId);
        Debug.Log($"[GameEnd] GameResultData saved: localRank={GameResultData.LocalPlayerRank}, count={count}");
    }

    private static void SetMockRankingsForLocalTest()
    {
        GameResultData.Clear();
        GameResultData.AddEntry(1, "Test1", 1);
        GameResultData.AddEntry(2, "Test2", 2);
        GameResultData.AddEntry(3, "Test3", 3);
        GameResultData.AddEntry(4, "Test4", 4);
        GameResultData.LocalPlayerRank = 1;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
