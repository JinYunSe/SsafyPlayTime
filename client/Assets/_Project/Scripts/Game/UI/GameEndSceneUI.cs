using SSAFYPlayTime;
using UnityEngine;
using UnityEngine.UI;

// GameEndScene에 배치하는 씬 전환 UI.
// - 같은 방으로: Fusion 세션을 유지한 채 LauncherScene 방 대기 패널로 이동
// - 처음으로: 세션을 종료하고 LauncherScene 로비 패널로 이동
public class GameEndSceneUI : MonoBehaviour
{
    [SerializeField] private Button returnToRoomButton;
    [SerializeField] private Button returnToLobbyButton;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

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
}
