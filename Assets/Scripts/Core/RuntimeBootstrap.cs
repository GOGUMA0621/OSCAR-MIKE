using OskarMike.Network;
using OskarMike.Services;
using UnityEngine;

namespace OskarMike.Core
{
    public class RuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private bool createGameManagerIfMissing = true;
        [SerializeField] private bool createSessionManagerIfMissing = true;
        [SerializeField] private bool createUgsServiceManagerIfMissing = true;
        [SerializeField] private bool createLoadingScreenIfMissing = true;

        private void Awake()
        {
            if (createGameManagerIfMissing && GameManager.Instance == null)
            {
                var gameManagerObject = new GameObject("GameManager");
                gameManagerObject.AddComponent<GameManager>();
            }

            if (createSessionManagerIfMissing && NetworkSessionManager.Instance == null)
            {
                var sessionManagerObject = new GameObject("NetworkSessionManager");
                sessionManagerObject.AddComponent<NetworkSessionManager>();
            }

            if (createUgsServiceManagerIfMissing && UgsServiceManager.Instance == null)
            {
                var ugsManagerObject = new GameObject("UgsServiceManager");
                ugsManagerObject.AddComponent<UgsServiceManager>();
            }

            if (createLoadingScreenIfMissing && LoadingScreenManager.Instance == null)
            {
                var loaderObject = new GameObject("LoadingScreenManager");
                loaderObject.AddComponent<LoadingScreenManager>();
            }
        }
    }
}
