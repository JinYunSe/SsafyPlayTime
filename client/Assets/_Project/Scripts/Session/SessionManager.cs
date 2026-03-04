using UnityEngine;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class SessionManager : MonoBehaviour
    {
        // 싱글톤 인스턴스를 저장할 static 변수
        private static SessionManager _instance;

        [Header("References")]
        // 로비 UI를 제어하는 컨트롤러 참조
        [SerializeField] private LobbyCanvasUIController lobbyController;

        // 외부에서 접근 가능한 싱글톤 인스턴스
        public static SessionManager Instance => _instance;

        // 로비 컨트롤러에 대한 읽기 전용 프로퍼티
        public LobbyCanvasUIController LobbyController => lobbyController;

        private void Awake()
        {
            // 이미 인스턴스가 존재하고, 그 객체가 자신이 아니라면
            // 중복 생성된 객체 제거
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            // 현재 객체를 싱글톤 인스턴스로 설정
            _instance = this;

            // Inpector에서 LobbyController가 할당되지 않았다면 에러 로그 출력
            if (lobbyController == null)
            {
                Debug.LogError("[SessionManager] lobbyController is not assigned in Inspector.", this);
            }
        }
    }
}
