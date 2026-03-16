using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using SSAFYPlayTime;
using UnityEngine;
using UnityEngine.SceneManagement;

// [디버그 전용] GameScene → GameEndScene 즉시 전환 트리거.
// - debugEnabled를 켜면 F9 키로 활성화된다.
// - 호스트: 현재 참가자 순위를 랜덤으로 배정하고 NetworkArray에 기록한 뒤 GameEndScene으로 전환.
// - 클라이언트: 호스트가 보낸 NetworkArray 값을 Despawned()에서 읽어 GameResultData에 저장.
public class DebugGameEndTransition : NetworkBehaviour
{
    [Header("Debug")]
    [Tooltip("켜면 F9 키로 GameEndScene으로 즉시 전환 가능 (테스트용)")]
    [SerializeField] private bool debugEnabled = false;

    [SerializeField] private string gameEndSceneName = "GameEndScene";

    // 호스트가 기록하고 모든 클라이언트에 동기화되는 순위 데이터
    // RankedPlayerIds[i] = (i+1)등 플레이어의 PlayerId
    [Networked, Capacity(8)]
    private NetworkArray<int> RankedPlayerIds { get; }

    [Networked]
    private int NetworkedPlayerCount { get; set; }

    private bool _triggered;

    private void Update()
    {
        if (!debugEnabled || _triggered) return;
        if (!Input.GetKeyDown(KeyCode.F9)) return;

        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        Debug.Log($"[Debug] F9 입력 감지: runnerExists={runner != null}, isRunning={runner != null && runner.IsRunning}, isServer={runner != null && runner.IsServer}, hasStateAuthority={HasStateAuthority}, hasInputAuthority={HasInputAuthority}", this);

        if (runner != null && runner.IsRunning && runner.IsServer)
        {
            TriggerDebugGameEnd(runner);
        }
        else if (runner != null && runner.IsRunning)
        {
            _triggered = true;
            Debug.Log("[Debug] 클라이언트가 게임 종료 RPC를 StateAuthority에 요청합니다.", this);
            RPC_RequestDebugGameEnd();
        }
        else if (runner == null || !runner.IsRunning)
        {
            _triggered = true;
            // 네트워크 없이 로컬 테스트: 목업 데이터로 즉시 전환
            SetMockRankingsForLocalTest();
            SceneManager.LoadScene(gameEndSceneName);
        }
        // 클라이언트는 호스트가 씬 전환을 주도하므로 아무것도 하지 않음
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDebugGameEnd()
    {
        var runner = Runner ?? FindAnyObjectByType<NetworkRunner>();
        Debug.Log($"[Debug] RPC_RequestDebugGameEnd 수신: runnerExists={runner != null}, isRunning={runner != null && runner.IsRunning}, isServer={runner != null && runner.IsServer}", this);
        if (runner == null || !runner.IsRunning || !runner.IsServer)
            return;

        TriggerDebugGameEnd(runner);
    }

    private void TriggerDebugGameEnd(NetworkRunner runner)
    {
        if (_triggered)
            return;

        _triggered = true;
        Debug.Log("[Debug] 게임 종료 트리거 실행 시작", this);
        AssignRandomRankings(runner);
        // 네트워크 상태 동기화를 위해 짧게 대기 후 씬 전환
        StartCoroutine(LoadSceneAfterSync(runner));
    }

    // 호스트에서 현재 접속 중인 플레이어를 랜덤으로 섞어 순위를 배정한다.
    private void AssignRandomRankings(NetworkRunner runner)
    {
        var playerIds = runner.ActivePlayers
            .Where(p => p.IsRealPlayer)
            .Select(p => p.PlayerId)
            .ToList();

        Shuffle(playerIds);

        int count = Mathf.Min(playerIds.Count, 8);
        NetworkedPlayerCount = count;
        for (int i = 0; i < count; i++)
            RankedPlayerIds.Set(i, playerIds[i]);

        Debug.Log($"[Debug] 순위 배정: {string.Join(", ", playerIds.Select((id, i) => $"{i + 1}등=Player{id}"))}");
    }

    private IEnumerator LoadSceneAfterSync(NetworkRunner runner)
    {
        // Fusion이 NetworkArray 변경분을 클라이언트에 전송하도록 2프레임 대기
        yield return null;
        yield return null;

        if (runner != null && runner.IsRunning)
        {
            Debug.Log($"[Debug] F9 → {gameEndSceneName} 전환 (Fusion)");
            runner.LoadScene(gameEndSceneName, LoadSceneMode.Single);
        }
    }

    // 씬 전환 시 모든 클라이언트의 Despawned가 호출된다.
    // 이 시점에 NetworkArray 값을 읽어 정적 GameResultData에 저장한다.
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (!hasState || runner == null || !runner.LocalPlayer.IsRealPlayer)
            return;

        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();

        GameResultData.Clear();
        int count = NetworkedPlayerCount;
        for (int i = 0; i < count && i < 8; i++)
        {
            int pid = RankedPlayerIds[i];
            int rank = i + 1;
            string nickname = lobby != null ? lobby.GetParticipantNickname(pid) : $"Player{pid}";
            GameResultData.AddEntry(pid, nickname, rank);
        }

        int localPid = runner.LocalPlayer.PlayerId;
        GameResultData.LocalPlayerRank = GameResultData.GetRank(localPid);
        Debug.Log($"[Debug] GameResultData 저장 완료: localRank={GameResultData.LocalPlayerRank}, count={count}");
    }

    // 네트워크 없이 로컬 테스트할 때 목업 데이터를 GameResultData에 직접 기록한다.
    private static void SetMockRankingsForLocalTest()
    {
        GameResultData.Clear();
        GameResultData.AddEntry(1, "테스트1", 1);
        GameResultData.AddEntry(2, "테스트2", 2);
        GameResultData.AddEntry(3, "테스트3", 3);
        GameResultData.AddEntry(4, "테스트4", 4);
        GameResultData.LocalPlayerRank = 1;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
