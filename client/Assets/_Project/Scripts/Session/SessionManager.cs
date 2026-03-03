using UnityEngine;

namespace SSAFYPlayTime
{
    [DisallowMultipleComponent]
    public sealed class SessionManager : MonoBehaviour
    {
        private static SessionManager _instance;

        [Header("References")]
        [SerializeField] private LobbyCanvasUIController lobbyController;

        public static SessionManager Instance => _instance;
        public LobbyCanvasUIController LobbyController => lobbyController;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (lobbyController == null)
            {
                Debug.LogError("[SessionManager] lobbyController is not assigned in Inspector.", this);
            }
        }
    }
}
