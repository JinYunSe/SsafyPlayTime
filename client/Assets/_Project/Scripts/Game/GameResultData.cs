using System.Collections.Generic;

// 게임 종료 후 씬 간에 순위 데이터를 보관하는 정적 클래스.
// DebugGameEndTransition(또는 향후 전투 시스템)이 기록하고,
// GameEndScene UI 표시 및 "같은 방으로" 방장 결정에 사용한다.
public static class GameResultData
{
    public sealed class RankEntry
    {
        public int PlayerId;
        public string Nickname;
        public int Rank; // 1=1등, 숫자가 클수록 낮은 순위
    }

    private static readonly List<RankEntry> _entries = new();

    // 로컬 플레이어의 순위 (씬 전환 후에도 유지)
    public static int LocalPlayerRank { get; set; } = 0;

    public static IReadOnlyList<RankEntry> Entries => _entries;

    public static void Clear()
    {
        _entries.Clear();
        LocalPlayerRank = 0;
    }

    public static void AddEntry(int playerId, string nickname, int rank)
    {
        if (playerId > 0)
            _entries.Add(new RankEntry { PlayerId = playerId, Nickname = nickname, Rank = rank });
    }

    public static int GetRank(int playerId)
    {
        foreach (var e in _entries)
            if (e.PlayerId == playerId) return e.Rank;
        return 0;
    }
}
