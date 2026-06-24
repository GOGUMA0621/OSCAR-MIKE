using System.Linq;
using OskarMike.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OskarMike.UI
{
    public class LobbyUIController : MonoBehaviour
    {
        [Header("UI References")]
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

        public void SetPlayerListText(TextMeshProUGUI v) { playerListText = v; }
        public void SetSessionStateText(TextMeshProUGUI v) { sessionStateText = v; }
        public void SetJoinCodeText(TextMeshProUGUI v) { joinCodeText = v; }
        public void SetLocalReadyStateText(TextMeshProUGUI v) { localReadyStateText = v; }
        public void SetReadyButtonLabel(TextMeshProUGUI v) { readyButtonLabel = v; }
        public void SetStartGameButton(Button v) { startGameButton = v; }
        public void SetReadyButton(Button v) { readyButton = v; }
    public void SetLeaveButton(Button v) { leaveButton = v; }
    public void SetNetworkLobbyFlow(NetworkLobbyFlow v) { lobbyFlow = v; }

    private void Awake()
        {
            if (lobbyFlow == null)
                lobbyFlow = FindFirstObjectByType<NetworkLobbyFlow>();
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (NetworkSessionManager.Instance != null)
            {
                NetworkSessionManager.Instance.ClientConnected += HandleClientChanged;
                NetworkSessionManager.Instance.ClientDisconnected += HandleClientChanged;
                NetworkSessionManager.Instance.JoinCodeChanged += HandleJoinCodeChanged;
                NetworkSessionManager.Instance.ReadyStatesChanged += HandleReadyStatesChanged;
            }
            BindButtons();
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
            UnbindButtons();
        }

        private void BindButtons()
        {
            if (startGameButton != null) startGameButton.onClick.AddListener(OnClickStartGame);
            if (leaveButton != null) leaveButton.onClick.AddListener(OnClickLeave);
            if (readyButton != null) readyButton.onClick.AddListener(OnClickReadyToggle);
        }

        private void UnbindButtons()
        {
            if (startGameButton != null) startGameButton.onClick.RemoveListener(OnClickStartGame);
            if (leaveButton != null) leaveButton.onClick.RemoveListener(OnClickLeave);
            if (readyButton != null) readyButton.onClick.RemoveListener(OnClickReadyToggle);
        }

        private void HandleClientChanged(ulong _) => RefreshView();
        private void HandleJoinCodeChanged(string _) => RefreshView();
        private void HandleReadyStatesChanged() => RefreshView();

        private void RefreshView()
        {
            var session = NetworkSessionManager.Instance;

            if (session == null)
            {
                if (sessionStateText != null) sessionStateText.text = "세션: 오프라인";
                if (playerListText != null) playerListText.text = "활성 세션이 없습니다.";
                return;
            }

            if (sessionStateText != null)
                sessionStateText.text = session.IsHost ? "세션: 호스트" : "세션: 클라이언트";

            if (playerListText != null)
            {
                var ordered = session.ConnectedClientIds.OrderBy(id => id);
                playerListText.text = "플레이어\n" + string.Join("\n",
                    ordered.Select(id => $"- 클라이언트 {id} ({(session.IsClientReady(id) ? "준비" : "대기")})"));
            }

            if (startGameButton != null)
                startGameButton.interactable = session.IsHost && session.CanStartGame();

            if (joinCodeText != null)
                joinCodeText.text = string.IsNullOrWhiteSpace(session.CurrentJoinCode)
                    ? "참가 코드: -" : $"참가 코드: {session.CurrentJoinCode}";

            if (readyButton != null)
                readyButton.interactable = session.IsClient;

            var ready = session.IsLocalClientReady();
            if (localReadyStateText != null)
                localReadyStateText.text = ready ? "상태: 준비됨" : "상태: 준비 안됨";
            if (readyButtonLabel != null)
                readyButtonLabel.text = ready ? "준비 취소" : "준비";
        }

        private void OnClickStartGame()
        {
            if (lobbyFlow != null) lobbyFlow.RequestStartGameFromLobby();
        }

        private void OnClickLeave()
        {
            NetworkSessionManager.Instance?.ShutdownSession();
            GameManager.Instance?.LoadMainMenu();
        }

        private void OnClickReadyToggle()
        {
            if (NetworkSessionManager.Instance == null) return;
            NetworkSessionManager.Instance.ToggleLocalReady();
            RefreshView();
        }
    }
}
