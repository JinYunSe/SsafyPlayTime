using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 종료 UI. EndGameManager.OnGameEnd 이벤트를 구독해
/// 마지막 생존자가 결정되면 화면에 "END" 텍스트를 표시합니다.
/// Canvas 하위 GameObject에 붙이세요.
/// </summary>
public class EndGameUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("게임 종료 시 활성화될 루트 패널 (기본적으로 비활성화)")]
    public GameObject endPanel;

    [Tooltip("END 텍스트 (선택 사항 - 없으면 endPanel의 Text 컴포넌트 자동 탐색)")]
    public Text endText;

    private void Start()
    {
        // 초기에는 패널 숨기기
        if (endPanel != null)
            endPanel.SetActive(false);

        // EndGameManager가 씬에 있으면 이벤트 구독
        if (EndGameManager.Instance != null)
        {
            EndGameManager.Instance.OnGameEnd += ShowEndScreen;
        }
        else
        {
            Debug.LogWarning("[EndGameUI] EndGameManager.Instance를 찾을 수 없습니다. 씬에 EndGameManager가 있는지 확인하세요.");
        }
    }

    private void OnDestroy()
    {
        if (EndGameManager.Instance != null)
            EndGameManager.Instance.OnGameEnd -= ShowEndScreen;
    }

    private void ShowEndScreen(PlayerStats winner)
    {
        // 패널 활성화
        if (endPanel != null)
            endPanel.SetActive(true);

        // 텍스트 설정
        if (endText != null)
            endText.text = "END";

        Debug.Log($"[EndGameUI] 게임 종료 UI 표시! 우승자: {(winner != null ? winner.name : "없음")}");
    }
}
