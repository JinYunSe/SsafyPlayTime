using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

    // RankedPlayerIds/RankedCharIndices: StateAuthority에서 직렬화 페이로드 생성에 사용.
    // 클라이언트는 RPC 파라미터(payload)로 직접 전달받으므로 Networked 동기화 타이밍에 의존하지 않는다.
    [Networked, Capacity(8)]
    private NetworkArray<int> RankedPlayerIds { get; }

    [Networked, Capacity(8)]
    private NetworkArray<int> RankedCharIndices { get; }

    [Networked]
    private int NetworkedPlayerCount { get; set; }

    // StateAuthority 전용: 사망 순서 기록 (인덱스 0 = 가장 먼저 사망)
    private readonly List<int> _deathOrder = new();
    private readonly HashSet<NetworkPlayer> _subscribedPlayers = new();

    // 게임 중 한 번이라도 참가한 모든 플레이어 ID 캐시 (퇴장해도 유지)
    private readonly HashSet<int> _allRegisteredPlayerIds = new();
    // PlayerId → CharacterTypeIndex 캐시 (퇴장 후에도 캐릭터 인덱스 보존)
    private readonly Dictionary<int, int> _cachedCharIndexByPlayerId = new();
    // 게임 도중 퇴장(탈주)한 플레이어 ID 목록 — GameEndPanel에 "(탈주)" 표시용
    private readonly HashSet<int> _leftPlayerIds = new();

    private bool _triggered;
    private const float SubscribeCheckInterval = 0.5f;
    private float _subscribeCheckTimer = SubscribeCheckInterval;
    // 모든 플레이어 구독 완료 시 true → 주기 탐색(FindObjectsByType) 중단
    private bool _allPlayersSubscribed;

    public override void Spawned()
    {
        if (!HasStateAuthority) return;
        SubscribeToPlayers();
    }

    // ─── IPlayerLeft ───────────────────────────────────────────────
    // 방장 퇴장은 LobbyCanvasUIController.OnShutdown/OnDisconnectedFromServer에서 처리한다.
    // Host migration 도중 PlayerLeft가 발동해도 _triggered를 세우지 않아 migration 처리가 방해받지 않는다.

    public void PlayerLeft(PlayerRef player)
    {
        if (_triggered) return;
        if (!HasStateAuthority) return;

        // _allRegisteredPlayerIds가 비어있으면 아직 초기화 전 (host migration 직후 등).
        // 이 상태에서 aliveCount를 계산하면 0이 나와 게임이 오발동된다.
        if (_allRegisteredPlayerIds.Count == 0) return;

        // Host migration 후 새 방장이 됐을 때 PlayerLeft(구 방장)가 발동해
        // TriggerGameEnd와 TriggerHostExitAndReturnToLobby가 동시에 실행되는 레이스를 방지한다.
        // LobbyCanvasUIController에서 이미 방장 이탈 처리가 시작된 경우 게임 종료를 발동하지 않는다.
        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        if (lobby != null && lobby.IsShowingGameEndOrReturningToLobby) return;

        // 탈주 기록 (GameEndPanel에 "(탈주)" 표시)
        _leftPlayerIds.Add(player.PlayerId);

        // 캐시 기반 aliveCount 계산: NetworkObject 상태에 의존하지 않는다.
        var deadOrLeft = new HashSet<int>(_deathOrder) { player.PlayerId };
        var aliveCount = _allRegisteredPlayerIds.Count(id => !deadOrLeft.Contains(id));

        Debug.Log($"[GameEnd] Player{player.PlayerId} 퇴장 → 생존자 수: {aliveCount}명");

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

        if (!HasStateAuthority || _triggered || _allPlayersSubscribed)
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
            if (player == null) continue;

            // CharacterTypeIndex 캐시 갱신 (0.5s 주기 → 확정값 보장)
            if (player.Object != null && player.Object.InputAuthority.IsRealPlayer)
            {
                var pid = player.Object.InputAuthority.PlayerId;
                _allRegisteredPlayerIds.Add(pid);
                if (player.CharacterTypeIndex >= 0)
                    _cachedCharIndexByPlayerId[pid] = player.CharacterTypeIndex;
                else if (!_cachedCharIndexByPlayerId.ContainsKey(pid))
                    _cachedCharIndexByPlayerId[pid] = -1;
            }

            if (_subscribedPlayers.Contains(player)) continue;

            _subscribedPlayers.Add(player);
            player.OnNetworkPlayerDied += OnNetworkPlayerDied;
        }

        // 등록된 모든 플레이어가 구독 완료되면 주기 탐색 중단
        if (_allRegisteredPlayerIds.Count > 0 &&
            _subscribedPlayers.Count >= _allRegisteredPlayerIds.Count)
        {
            _allPlayersSubscribed = true;
            Debug.Log("[GameEnd] 모든 플레이어 구독 완료 → FindObjectsByType 주기 탐색 중단");
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

        // _allRegisteredPlayerIds가 비어있으면 아직 초기화 전이므로 계산하지 않는다.
        if (_allRegisteredPlayerIds.Count == 0) return;

        // PlayerLeft와 동일한 캐시 기반 계산으로 통일
        var aliveCount = _allRegisteredPlayerIds.Count(id => !_deathOrder.Contains(id));

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

        // _allRegisteredPlayerIds - _deathOrder = 마지막 생존자 ID
        // NetworkObject 상태 의존 없이 캐시만으로 winner를 결정한다.
        var winnerPlayerId = _allRegisteredPlayerIds.FirstOrDefault(id => !_deathOrder.Contains(id));

        AssignDeathOrderRankings(winnerPlayerId);
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
        {
            RankedPlayerIds.Set(i, playerIds[i]);
            var charIdx = _cachedCharIndexByPlayerId.TryGetValue(playerIds[i], out var ci) ? ci : -1;
            RankedCharIndices.Set(i, charIdx);
        }
    }

    private void AssignDeathOrderRankings(int winnerPlayerId)
    {
        var rankedIds = new List<int>();

        if (winnerPlayerId > 0)
            rankedIds.Add(winnerPlayerId);

        // 사망 역순 (마지막 사망 = 높은 순위)
        for (var i = _deathOrder.Count - 1; i >= 0; i--)
        {
            var playerId = _deathOrder[i];
            if (!rankedIds.Contains(playerId))
                rankedIds.Add(playerId);
        }

        // 퇴장 플레이어 포함 (ActivePlayers 대신 캐시 사용)
        foreach (var playerId in _allRegisteredPlayerIds)
        {
            if (!rankedIds.Contains(playerId))
                rankedIds.Add(playerId);
        }

        var count = Mathf.Min(rankedIds.Count, 8);
        NetworkedPlayerCount = count;
        for (var i = 0; i < count; i++)
        {
            RankedPlayerIds.Set(i, rankedIds[i]);
            var charIdx = _cachedCharIndexByPlayerId.TryGetValue(rankedIds[i], out var ci) ? ci : -1;
            RankedCharIndices.Set(i, charIdx);
        }

        Debug.Log($"[GameEnd] Rankings: {string.Join(", ", rankedIds.Select((id, i) => $"{i + 1}=Player{id}"))}");
    }

    private IEnumerator LoadSceneAfterSync(NetworkRunner runner = null)
    {
        yield return null;
        yield return null;

        runner ??= Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning)
        {
            // 랭킹 데이터를 직렬화해 RPC 파라미터로 직접 전달한다.
            // (Networked 배열 동기화 타이밍 의존 제거)
            var count = NetworkedPlayerCount;
            var payload = BuildRankingPayload(count);
            RPC_BroadcastRankings(payload);

            // RPC가 클라이언트에 도달할 시간을 준 뒤 runner.LoadScene으로 씬 전환한다.
            // runner.LoadScene은 Fusion이 모든 클라이언트를 동기적으로 전환하고
            // OnSceneLoadDone을 발동시켜 초기화 로직(ResetCharacterSlotState 등)이 실행된다.
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

    // ─── 랭킹 직렬화 / 역직렬화 ──────────────────────────────────
    // 형식: "count|playerId0,charIdx0,isLeft0|playerId1,charIdx1,isLeft1|..."
    // isLeft: 1=게임 도중 탈주, 0=정상 종료

    private string BuildRankingPayload(int count)
    {
        var sb = new StringBuilder();
        sb.Append(count);
        for (var i = 0; i < count && i < 8; i++)
        {
            var pid = RankedPlayerIds[i];
            sb.Append('|');
            sb.Append(pid);
            sb.Append(',');
            sb.Append(RankedCharIndices[i]);
            sb.Append(',');
            sb.Append(_leftPlayerIds.Contains(pid) ? 1 : 0);
        }
        return sb.ToString();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastRankings(string rankingPayload)
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        if (runner == null || !runner.LocalPlayer.IsRealPlayer)
            return;

        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();

        // 1) 페이로드에서 순위 데이터를 직접 복원 (Networked 배열 동기화 불필요)
        SaveRankingsToGameResultData(runner, lobby, rankingPayload);

        // 2) _pendingGameEndPanel 플래그 세우기
        //    씬 전환 중 OnShutdown/OnDisconnectedFromServer가 TriggerHostExitAndReturnToLobby를 잘못 발동하지 않도록 보호.
        //    씬 전환 완료 후 OnSceneLoadDone에서 _pendingGameEndPanel을 확인해 ShowGameEndPanel을 호출한다.
        lobby?.NotifyGameEndTransition();

        Debug.Log($"[GameEnd] RPC_BroadcastRankings complete. payload={rankingPayload}");
        // 씬 전환은 호스트의 runner.LoadScene이 Fusion을 통해 모든 클라이언트에 전달한다.
        // SceneManager.LoadScene을 직접 호출하면 OnSceneLoadDone이 발동하지 않아
        // ResetCharacterSlotState 등 초기화 로직이 누락되므로 여기서 씬 전환하지 않는다.
    }

    private static void SaveRankingsToGameResultData(NetworkRunner runner, LobbyCanvasUIController lobby, string payload)
    {
        if (string.IsNullOrEmpty(payload))
            return;

        var parts = payload.Split('|');
        if (!int.TryParse(parts[0], out var count) || count <= 0)
            return;

        GameResultData.Clear();
        for (var i = 0; i < count && i + 1 < parts.Length; i++)
        {
            var entry = parts[i + 1].Split(',');
            if (entry.Length < 2) continue;
            if (!int.TryParse(entry[0], out var playerId)) continue;
            if (!int.TryParse(entry[1], out var charIndex)) continue;
            var isLeft = entry.Length >= 3 && entry[2] == "1";

            var rank = i + 1;
            var nickname = lobby != null ? lobby.GetParticipantNickname(playerId) : $"Player{playerId}";
            if (isLeft) nickname += " (탈주)";
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
