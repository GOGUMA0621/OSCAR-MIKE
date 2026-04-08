using System.Linq;
using OskarMike.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OskarMike.UI
{
    public class LobbyUIController : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private TextMeshProUGUI playerListText;
        [SerializeField] private TextMeshProUGUI sessionStateText;
        [SerializeField] private TextMeshProUGUI joinCodeText;
        [SerializeField] private TextMeshProUGUI localReadyStateText;
        [SerializeField] private TextMeshProUGUI readyButtonLabel;
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private Button leaveButton;

        [Header("Flow")]
        [SerializeField] private NetworkLobbyFlow lobbyFlow;

        private void OnEnable()
        {
            if (NetworkSessionManager.Instance != null)
            {
                NetworkSessionManager.Instance.ClientConnected += HandleClientChanged;
                NetworkSessionManager.Instance.ClientDisconnected += HandleClientChanged;
                NetworkSessionManager.Instance.JoinCodeChanged += HandleJoinCodeChanged;
                NetworkSessionManager.Instance.ReadyStatesChanged += HandleReadyStatesChanged;
            }

            if (startGameButton != null)
            {
                startGameButton.onClick.AddListener(OnClickStartGame);
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.AddListener(OnClickLeave);
            }

            if (readyButton != null)
            {
                readyButton.onClick.AddListener(OnClickReadyToggle);
            }

            RefreshView();
        }

        private void OnDisable()
        {
            if (NetworkSessionManager.Instance != null)
            {
                NetworkSessionManager.Instance.ClientConnected -= HandleClientChanged;
                NetworkSessionManager.Instance.ClientDisconnected -= HandleClientChanged;
                NetworkSessionManager.Instance.JoinCodeChanged -= HandleJoinCodeChanged;
                NetworkSessionManager.Instance.ReadyStatesChanged -= HandleReadyStatesChanged;
            }

            if (startGameButton != null)
            {
                startGameButton.onClick.RemoveListener(OnClickStartGame);
            }

            if (leaveButton != null)
            {
                leaveButton.onClick.RemoveListener(OnClickLeave);
            }

            if (readyButton != null)
            {
                readyButton.onClick.RemoveListener(OnClickReadyToggle);
            }
        }

        private void HandleClientChanged(ulong _)
        {
            RefreshView();
        }

        private void HandleJoinCodeChanged(string _)
        {
            RefreshView();
        }

        private void HandleReadyStatesChanged()
        {
            RefreshView();
        }

        private void RefreshView()
        {
            var session = NetworkSessionManager.Instance;
            if (session == null)
            {
                if (sessionStateText != null)
                {
                    sessionStateText.text = "Session: Offline";
                }

                if (playerListText != null)
                {
                    playerListText.text = "No active session.";
                }

                return;
            }

            if (sessionStateText != null)
            {
                sessionStateText.text = session.IsHost ? "Session: Host" : "Session: Client";
            }

            if (playerListText != null)
            {
                var ordered = session.ConnectedClientIds.OrderBy(id => id);
                playerListText.text = "Players\n" + string.Join(
                    "\n",
                    ordered.Select(id => $"- Client {id} ({(session.IsClientReady(id) ? "Ready" : "Not Ready")})"));
            }

            if (startGameButton != null)
            {
                startGameButton.interactable = session.IsHost && session.CanStartGame();
            }

            if (joinCodeText != null)
            {
                joinCodeText.text = string.IsNullOrWhiteSpace(session.CurrentJoinCode)
                    ? "Join Code: -"
                    : $"Join Code: {session.CurrentJoinCode}";
            }

            if (readyButton != null)
            {
                readyButton.interactable = session.IsClient;
            }

            var isLocalReady = session.IsLocalClientReady();
            if (localReadyStateText != null)
            {
                localReadyStateText.text = isLocalReady ? "You: Ready" : "You: Not Ready";
            }

            if (readyButtonLabel != null)
            {
                readyButtonLabel.text = isLocalReady ? "Set Not Ready" : "Set Ready";
            }
        }

        private void OnClickStartGame()
        {
            if (lobbyFlow != null)
            {
                lobbyFlow.RequestStartGameFromLobby();
            }
        }

        private void OnClickLeave()
        {
            if (NetworkSessionManager.Instance != null)
            {
                NetworkSessionManager.Instance.ShutdownSession();
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.LoadMainMenu();
            }
        }

        private void OnClickReadyToggle()
        {
            if (NetworkSessionManager.Instance == null)
            {
                return;
            }

            NetworkSessionManager.Instance.ToggleLocalReady();
            RefreshView();
        }
    }
}
