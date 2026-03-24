using UnityEngine;

namespace SSAFYPlayTime
{
    // LauncherScene 전반의 세션을 관리하는 싱글톤 매니저.
    // LobbyCanvasUIController에 대한 전역 접근점을 제공하며,
    // 씬 전환 시에도 파괴되지 않도록 DontDestroyOnLoad 처리를 담당한다.
    [DisallowMultipleComponent]
    public sealed class SessionManager : MonoBehaviour
    {
        // 싱글톤 인스턴스를 저장하는 static 변수
        private static SessionManager _instance;

        [Header("References")]
        // 로비 UI 전체를 제어하는 컨트롤러. Inspector에서 반드시 할당해야 한다.
        [SerializeField] private LobbyCanvasUIController lobbyController;

        // 외부에서 싱글톤 인스턴스에 접근하는 프로퍼티
        public static SessionManager Instance => _instance;

        // LobbyCanvasUIController에 대한 읽기 전용 접근자
        public LobbyCanvasUIController LobbyController => lobbyController;

        private void Awake()
        {
            // 중복 인스턴스 방지: 이미 존재하면 새로 생성된 오브젝트를 즉시 삭제
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (lobbyController == null)
            {
                Debug.LogError("[SessionManager] lobbyController is not assigned in Inspector.", this);
            }
        }
    }
}
