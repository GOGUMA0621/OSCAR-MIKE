using System.Threading.Tasks;
using OskarMike.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OskarMike.UI
{
    public class MainMenuUIController : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinByCodeButton;
        [SerializeField] private Button quitButton;

        [Header("Join by Code")]
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private TextMeshProUGUI statusText;

        private void OnEnable()
        {
            RegisterSessionEvents();
        }

        private void Awake()
        {
            if (hostButton != null)
            {
                hostButton.onClick.AddListener(OnClickHost);
            }

            if (joinByCodeButton != null)
            {
                joinByCodeButton.onClick.AddListener(OnClickJoinByCode);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(OnClickQuit);
            }
        }

        private void OnDisable()
        {
            UnregisterSessionEvents();
        }

        private void OnDestroy()
        {
            UnregisterSessionEvents();

            if (hostButton != null)
            {
                hostButton.onClick.RemoveListener(OnClickHost);
            }

            if (joinByCodeButton != null)
            {
                joinByCodeButton.onClick.RemoveListener(OnClickJoinByCode);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnClickQuit);
            }
        }

        private async void OnClickHost()
        {
            RegisterSessionEvents();
            SetStatus("Creating online session...");
            SetButtonsInteractable(false);

            if (NetworkSessionManager.Instance == null)
            {
                Debug.LogError("[MainMenuUIController] Missing NetworkSessionManager.");
                SetStatus("Network session manager is missing.");
                SetButtonsInteractable(true);
                return;
            }

            if (!await NetworkSessionManager.Instance.StartRelayHostSessionAsync())
            {
                SetButtonsInteractable(true);
                return;
            }
        }

        private async void OnClickJoinByCode()
        {
            RegisterSessionEvents();
            SetButtonsInteractable(false);

            if (NetworkSessionManager.Instance == null)
            {
                Debug.LogError("[MainMenuUIController] Missing NetworkSessionManager.");
                SetStatus("Network session manager is missing.");
                SetButtonsInteractable(true);
                return;
            }

            var joinCode = joinCodeInput != null ? joinCodeInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                SetStatus("Enter a join code.");
                SetButtonsInteractable(true);
                return;
            }

            SetStatus("Joining session...");
            if (!await NetworkSessionManager.Instance.StartClientSessionWithJoinCodeAsync(joinCode))
            {
                SetButtonsInteractable(true);
                return;
            }
        }

        private void HandleSessionStarted()
        {
            SetStatus("Entering lobby...");

            if (GameManager.Instance != null)
            {
                GameManager.Instance.LoadLobby();
            }
        }

        private void HandleSessionStartFailed(string reason)
        {
            Debug.LogWarning($"[MainMenuUIController] Session start failed: {reason}");
            SetStatus(reason);
            SetButtonsInteractable(true);
        }

        private void RegisterSessionEvents()
        {
            if (NetworkSessionManager.Instance == null)
            {
                return;
            }

            NetworkSessionManager.Instance.SessionStarted -= HandleSessionStarted;
            NetworkSessionManager.Instance.SessionStartFailed -= HandleSessionStartFailed;
            NetworkSessionManager.Instance.SessionStarted += HandleSessionStarted;
            NetworkSessionManager.Instance.SessionStartFailed += HandleSessionStartFailed;
        }

        private void UnregisterSessionEvents()
        {
            if (NetworkSessionManager.Instance == null)
            {
                return;
            }

            NetworkSessionManager.Instance.SessionStarted -= HandleSessionStarted;
            NetworkSessionManager.Instance.SessionStartFailed -= HandleSessionStartFailed;
        }

        private void OnClickQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (hostButton != null)
            {
                hostButton.interactable = interactable;
            }

            if (joinByCodeButton != null)
            {
                joinByCodeButton.interactable = interactable;
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
