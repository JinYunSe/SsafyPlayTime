using SSAFYPlayTime;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ESC 키로 일시정지 패널을 토글하고 3가지 버튼 동작을 처리합니다.
/// - 게임 진행: 패널 닫기
/// - 로비로 이동: 로컬 캐릭터 사망 처리 후 LauncherScene으로 복귀
/// - 게임 종료: 애플리케이션 종료
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button lobbyButton;
    [SerializeField] private Button quitButton;

    // 패널을 열 때 커서가 잠긴 상태였으면 닫을 때 다시 잠근다.
    // 고스트 모드처럼 이미 커서가 풀려 있는 경우엔 닫아도 잠그지 않는다.
    private bool _wasLockedBeforeOpen;

    private void Start()
    {
        if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
        if (lobbyButton != null)    lobbyButton.onClick.AddListener(OnGoToLobby);
        if (quitButton != null)     quitButton.onClick.AddListener(OnQuitGame);

        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            SetPanelActive(pausePanel != null && !pausePanel.activeSelf);
    }

    private void SetPanelActive(bool active)
    {
        if (pausePanel == null) return;

        if (active)
        {
            _wasLockedBeforeOpen = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            if (_wasLockedBeforeOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        pausePanel.SetActive(active);
    }

    // ─── 버튼 콜백 ──────────────────────────────────────────────────

    private void OnContinue()
    {
        SetPanelActive(false);
    }

    private void OnGoToLobby()
    {
        SetPanelActive(false);

        var lobby = FindAnyObjectByType<LobbyCanvasUIController>();
        if (lobby != null)
        {
            lobby.LeaveGameVoluntarily();
        }
        else
        {
            // 네트워크 없이 실행 중인 경우 씬 직접 전환
            SceneManager.LoadScene("LauncherScene");
        }
    }

    private void OnQuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
