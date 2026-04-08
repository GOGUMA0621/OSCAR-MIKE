using OskarMike.Network;
using UnityEngine;

namespace OskarMike.Network
{
    public class NetworkLobbyFlow : MonoBehaviour
    {
        [SerializeField] private string gameSceneName = "GameMap";

        public void RequestStartGameFromLobby()
        {
            if (NetworkSessionManager.Instance == null)
            {
                Debug.LogError("[NetworkLobbyFlow] NetworkSessionManager not found.");
                return;
            }

            if (!NetworkSessionManager.Instance.IsHost)
            {
                Debug.LogWarning("[NetworkLobbyFlow] Only host can start the game.");
                return;
            }

            if (!NetworkSessionManager.Instance.CanStartGame())
            {
                Debug.LogWarning("[NetworkLobbyFlow] Start blocked: no players connected or invalid state.");
                return;
            }

            var networkManager = NetworkSessionManager.Instance.ActiveNetworkManager;
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogError("[NetworkLobbyFlow] NetworkManager is not in server mode.");
                return;
            }

            networkManager.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }
}
