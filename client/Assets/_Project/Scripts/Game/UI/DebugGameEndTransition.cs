using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// [디버그 전용] GameScene → GameEndScene 즉시 전환 트리거.
// Inspector에서 debugEnabled를 켜면 활성화되며, 꺼두면 아무 동작도 하지 않는다.
// StateAuthority(호스트)에서만 Fusion runner.LoadScene을 호출해 모든 클라이언트가 함께 전환된다.
public class DebugGameEndTransition : NetworkBehaviour
{
    [Header("Debug")]
    [Tooltip("켜면 F9 키로 GameEndScene으로 즉시 전환 가능 (테스트용)")]
    [SerializeField] private bool debugEnabled = false;

    [SerializeField] private string gameEndSceneName = "GameEndScene";

    private bool _triggered = false;

    private void Update()
    {
        if (!debugEnabled || _triggered) return;
        if (!Input.GetKeyDown(KeyCode.F9)) return;

        _triggered = true;

        var runner = FindAnyObjectByType<NetworkRunner>();
        if (runner != null && runner.IsRunning && runner.IsServer)
        {
            Debug.Log("[Debug] F9 입력 → GameEndScene 전환 (Fusion)");
            runner.LoadScene(gameEndSceneName, LoadSceneMode.Single);
        }
        else
        {
            Debug.Log("[Debug] F9 입력 → GameEndScene 전환 (SceneManager)");
            SceneManager.LoadScene(gameEndSceneName);
        }
    }
}
