using System.Collections;
using OskarMike.Network;
using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Testing
{
    public class MovementTestAutoHost : MonoBehaviour
    {
        [SerializeField] private bool autoStartHost = true;
        [SerializeField] private float movementEnableTimeout = 5f;

        private IEnumerator Start()
        {
            if (autoStartHost)
            {
                StartHostIfNeeded();
            }

            yield return EnableLocalPlayerMovementWhenReady();
        }

        private static void StartHostIfNeeded()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || networkManager.IsListening)
            {
                return;
            }

            if (NetworkSessionManager.Instance != null)
            {
                NetworkSessionManager.Instance.StartHostSession();
                return;
            }

            networkManager.StartHost();
        }

        private IEnumerator EnableLocalPlayerMovementWhenReady()
        {
            float remaining = Mathf.Max(0.1f, movementEnableTimeout);

            while (remaining > 0f)
            {
                var networkManager = NetworkManager.Singleton;
                var playerObject = networkManager != null && networkManager.LocalClient != null
                    ? networkManager.LocalClient.PlayerObject
                    : null;

                if (playerObject != null
                    && playerObject.TryGetComponent(out PlayerNetworkController playerController))
                {
                    playerController.EnableGameplayInput();
                    Debug.Log("[MovementTestAutoHost] Local player movement enabled for sandbox testing.");
                    yield break;
                }

                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.LogWarning("[MovementTestAutoHost] Timed out waiting for the local player object.");
        }
    }
}
