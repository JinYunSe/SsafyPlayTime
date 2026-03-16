using System.Linq;
using SSAFYPlayTime;
using UnityEngine;
using UnityEngine.UI;

// GameEndScene에 배치하는 씬 전환 UI.
// - 순위 표시: rankingContainer 하위의 고정 RankingItemUI들을 참가 인원 수만큼 활성화
// - 같은 방으로: Fusion 세션을 유지한 채 LauncherScene 방 대기 패널로 이동
// - 처음으로: 세션을 종료하고 LauncherScene 메인 패널로 이동
public class GameEndSceneUI : MonoBehaviour
{
    [SerializeField] private Button returnToRoomButton;
    [SerializeField] private Button returnToLobbyButton;

    [Header("Ranking Display")]
    [Tooltip("순위 항목들이 생성될 부모 Transform")]
    [SerializeField] private Transform rankingContainer;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        DisplayRankings();

        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        if (lobby == null)
        {
            Debug.LogWarning("GameEndSceneUI: LobbyCanvasUIController를 찾을 수 없습니다.");
            return;
        }

        if (returnToRoomButton != null)
            returnToRoomButton.onClick.AddListener(lobby.ReturnToRoomFromGameEnd);

        if (returnToLobbyButton != null)
            returnToLobbyButton.onClick.AddListener(lobby.ReturnToLobbyFromGameEnd);
    }

    private void DisplayRankings()
    {
        if (rankingContainer == null)
            return;

        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        var entries = GameResultData.Entries
            .OrderBy(e => e.Rank)
            .ToList();

        var rankingItems = rankingContainer.GetComponentsInChildren<RankingItemUI>(true)
            .ToList();

        for (int i = 0; i < rankingItems.Count; i++)
        {
            bool shouldShow = i < entries.Count;
            rankingItems[i].gameObject.SetActive(shouldShow);

            if (shouldShow)
            {
                var entry = entries[i];
                var nickname = ResolveNickname(entry, lobby);
                rankingItems[i].SetData(entry.Rank, nickname);
            }
        }
    }

    private static string ResolveNickname(GameResultData.RankEntry entry, LobbyCanvasUIController lobby)
    {
        if (entry == null)
            return string.Empty;

        if (lobby != null)
        {
            var resolved = lobby.GetParticipantNickname(entry.PlayerId);
            if (!string.IsNullOrWhiteSpace(resolved) && resolved != $"Player{entry.PlayerId}")
                return resolved;
        }

        return entry.Nickname;
    }
}
