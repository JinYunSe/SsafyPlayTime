using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SessionManager : MonoBehaviour
{
    private NetworkRunner _runner;

    public async void CreateRoom(string roomName)
    {
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var startGameArgs = new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = roomName,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = 4
        };

        var result = await _runner.StartGame(startGameArgs);

        if (result.Ok)
        {
            Debug.Log("Room created successfully.");
        }
        else
        {
            Debug.LogError($"Failed to create room: {result.ShutdownReason}");
        }
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}
