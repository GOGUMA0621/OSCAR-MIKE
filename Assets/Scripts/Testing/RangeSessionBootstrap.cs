using System.Net;
using System.Net.Sockets;
using OskarMike.Network.Player;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OskarMike.Testing
{
    /// <summary>
    /// 사격장(RangeCourse) 세션 패널. 혼자 자동 호스트하던 MovementTestAutoHost 대신,
    /// 시작 화면에서 '호스트 시작'(0.0.0.0 수신 → 같은 PC/LAN 에서 접속 가능) 또는 '주소로 참가'를 고른다.
    /// - 최대 4명, 접속 승인 시 스폰 위치를 겹치지 않게 나눈다.
    /// - 로컬 플레이어가 스폰되면 게임플레이 입력을 켠다 (사격장은 GameMap 이 아니라 자동으로 켜지지 않음).
    /// - 세션 전에는 개요 카메라를 켜 둔다.
    /// Relay(참가 코드) 접속은 다루지 않는다 — 인터넷 너머 접속은 메인 메뉴 흐름을 사용.
    /// </summary>
    public sealed class RangeSessionBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager manager;
        [SerializeField] private Camera overviewCamera;
        [SerializeField] private Font koreanFont;
        [SerializeField] private ushort port = 7777;
        [SerializeField] private string joinAddress = "127.0.0.1";
        [SerializeField, Range(1, 4)] private int maxPlayers = 4;
        [Tooltip("스폰 위치 기준점 (호스트 위치). 다음 플레이어부터 오른쪽/왼쪽으로 spawnSpacing 씩 벌린다.")]
        [SerializeField] private Vector3 spawnOrigin = Vector3.zero;
        [SerializeField, Min(0.5f)] private float spawnSpacing = 1.5f;

        private string status = "호스트를 시작하거나 주소로 참가하세요.";
        private string localAddresses;
        private bool inputEnabledForLocalPlayer;
        private bool wasListening;

        private void Awake()
        {
            if (manager == null) manager = FindFirstObjectByType<NetworkManager>();
            if (manager == null) { status = "NetworkManager 를 찾을 수 없습니다."; enabled = false; return; }
            manager.NetworkConfig.ConnectionApproval = true;
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            localAddresses = FindLocalAddresses();
        }

        private void OnDestroy()
        {
            if (manager == null) return;
            if (manager.ConnectionApprovalCallback == Approve) manager.ConnectionApprovalCallback = null;
            manager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool approved = manager.ConnectedClientsIds.Count < maxPlayers;
            response.Approved = approved;
            response.CreatePlayerObject = approved;
            if (approved)
            {
                // 0, +1, -1, +2 … 칸 (호스트는 기존과 같은 원점)
                int slot = manager.ConnectedClientsIds.Count;
                int side = slot == 0 ? 0 : (slot % 2 == 1 ? (slot + 1) / 2 : -(slot / 2));
                response.Position = spawnOrigin + Vector3.right * (side * spawnSpacing);
                response.Rotation = Quaternion.identity;
            }
            else response.Reason = $"최대 {maxPlayers}명까지 접속할 수 있습니다.";
            response.Pending = false;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (manager != null && clientId == manager.LocalClientId)
                status = string.IsNullOrEmpty(manager.DisconnectReason) ? "세션이 종료되었습니다." : manager.DisconnectReason;
        }

        private void Update()
        {
            bool listening = manager.IsListening;
            bool connected = manager.IsConnectedClient;
            if (overviewCamera != null) overviewCamera.enabled = !connected;

            if (wasListening && !listening)
            {
                inputEnabledForLocalPlayer = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            wasListening = listening;

            if (!inputEnabledForLocalPlayer && connected)
            {
                var player = manager.LocalClient?.PlayerObject;
                if (player != null && player.TryGetComponent(out PlayerNetworkController controller))
                {
                    controller.EnableGameplayInput();
                    inputEnabledForLocalPlayer = true;
                }
            }
        }

        public void StartHost()
        {
            var transport = manager.GetComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            status = manager.StartHost() ? "세션: 호스트" : "호스트 시작 실패 (포트 사용 중일 수 있음)";
        }

        public void StartClient()
        {
            var transport = manager.GetComponent<UnityTransport>();
            transport.SetConnectionData(joinAddress.Trim(), port);
            status = manager.StartClient() ? $"{joinAddress.Trim()}:{port} 에 접속 중..." : "접속 시작 실패";
        }

        private void OnGUI()
        {
            // 세션 중에는 플레이 화면을 가리지 않도록 작은 상태 줄만 표시
            if (koreanFont != null) GUI.skin.font = koreanFont;
            if (manager.IsListening)
            {
                string role = manager.IsHost ? "호스트" : "클라이언트";
                int count = manager.IsServer ? manager.ConnectedClientsIds.Count : -1;
                string players = count >= 0 ? $" · 접속 {count}/{maxPlayers}" : "";
                string address = manager.IsHost ? $" · 내 주소 {localAddresses}:{port}" : "";
                GUI.Label(new Rect(12, Screen.height - 28, 900, 24), $"세션: {role}{players}{address}");
                return;
            }

            // 화면 가운데 (왼쪽 위는 RangeTestGun 안내창 자리)
            const float width = 360f, height = 230f;
            GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height), GUI.skin.box);
            GUILayout.Label("사격장 — 세션 시작");
            GUILayout.Label(status);
            if (GUILayout.Button("호스트 시작", GUILayout.Height(30))) StartHost();
            GUILayout.Space(6);
            GUILayout.Label("참가할 주소 (같은 PC: 127.0.0.1)");
            joinAddress = GUILayout.TextField(joinAddress);
            if (GUILayout.Button("주소로 참가", GUILayout.Height(30))) StartClient();
            GUILayout.Label($"이 PC 주소: {localAddresses} · 포트 {port}");
            GUILayout.EndArea();
        }

        private static string FindLocalAddresses()
        {
            try
            {
                var list = new System.Collections.Generic.List<string>();
                foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) list.Add(ip.ToString());
                return list.Count > 0 ? string.Join(", ", list) : "127.0.0.1";
            }
            catch (SocketException) { return "127.0.0.1"; }
        }
    }
}
