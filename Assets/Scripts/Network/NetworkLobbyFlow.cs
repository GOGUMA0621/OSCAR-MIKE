using OskarMike.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

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

            // 로딩 화면 표시
            var loader = LoadingScreenManager.Instance;
            if (loader != null)
            {
                loader.ShowLoading($"'{gameSceneName}' 로딩 중...");
                loader.SetProgress(0f);
            }

            // 네트워크 씬 로드 이벤트 구독
            networkManager.SceneManager.OnSceneEvent += HandleSceneEvent;
            networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            var loader = LoadingScreenManager.Instance;

            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.LoadEventCompleted:
                case SceneEventType.SynchronizeComplete:
                    if (loader != null) loader.HideLoading();
                    break;
            }
        }
    }
}
