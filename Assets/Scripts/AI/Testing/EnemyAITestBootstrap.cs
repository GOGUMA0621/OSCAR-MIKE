using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OskarMike.AI.Testing
{
    public sealed class EnemyAITestBootstrap : MonoBehaviour
    {
        public NetworkManager manager;
        public EnemyAgent patrolPrefab;
        public EnemyPatrolRoute patrolRoute;
        public Transform patrolSpawn;
        public Camera overviewCamera;
        public Font koreanFont;
        public string address = "127.0.0.1";
        private string status = "호스트 또는 클라이언트로 시작하세요.";
        private bool wasConnected;

        private void Awake()
        {
            manager.ConnectionApprovalCallback = Approve;
            manager.OnServerStarted += SpawnPatrol;
            manager.OnClientDisconnectCallback += Disconnected;
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = manager.ConnectedClients.Count < 4;
            response.CreatePlayerObject = response.Approved;
            var body = manager.NetworkConfig.PlayerPrefab.GetComponent<CharacterController>();
            response.Position = new Vector3(-12 + (int)(request.ClientNetworkId % 4) * 2,
                body.height * .5f - body.center.y + .1f, -12);
            response.Rotation = Quaternion.identity;
            response.Pending = false;
            if (!response.Approved) response.Reason = "최대 4명까지 접속할 수 있습니다.";
        }

        private void SpawnPatrol()
        {
            foreach (var zone in FindObjectsByType<ReinforcementZone>(FindObjectsSortMode.None)) zone.ResetServerSession();
            EnemyAgent patrol = Instantiate(patrolPrefab, patrolSpawn.position, patrolSpawn.rotation);
            patrol.Initialize(patrolRoute);
            patrol.NetworkObject.Spawn(true);
        }

        private void Disconnected(ulong clientId)
        {
            if (clientId == manager.LocalClientId)
            { status = "세션이 종료되었습니다."; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        private void Update()
        {
            bool connected = manager.IsConnectedClient;
            if (overviewCamera != null) overviewCamera.gameObject.SetActive(!connected);
            if (wasConnected && !connected) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            wasConnected = connected;
        }

        public void StartHost()
        {
            manager.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", 7777, "0.0.0.0");
            status = manager.StartHost() ? "세션: 호스트" : "호스트 시작 실패";
        }
        public void StartClient()
        {
            manager.GetComponent<UnityTransport>().SetConnectionData(address, 7777);
            status = manager.StartClient() ? "서버에 접속 중..." : "접속 시작 실패";
        }

        private void OnGUI()
        {
            if (koreanFont != null) GUI.skin.font = koreanFont;
            GUILayout.BeginArea(new Rect(12, 12, 340, 360), GUI.skin.box);
            GUILayout.Label("적 AI 테스트 — ESC: 마우스 잠금 전환");
            GUILayout.Label(status);
            if (!manager.IsListening)
            {
                address = GUILayout.TextField(address);
                if (GUILayout.Button("호스트 시작")) StartHost();
                if (GUILayout.Button("클라이언트 접속")) StartClient();
            }
            else if (GUILayout.Button("세션 종료")) manager.Shutdown();
            foreach (var enemy in FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
                if (enemy.IsSpawned) GUILayout.Label($"{StateLabel(enemy.State)} | 경고 {enemy.AlertSequence} | 사격 {enemy.ShotSequence}");
            foreach (var zone in FindObjectsByType<ReinforcementZone>(FindObjectsSortMode.None))
                GUILayout.Label($"{zone.name}: 서버 생존 {zone.LivingCount}/{zone.livingLimit}");
            GUILayout.EndArea();
        }
        private static string StateLabel(EnemyState state) => state switch
        { EnemyState.Alert => "경고", EnemyState.Chase => "추적", EnemyState.Shoot => "사격", EnemyState.Investigate => "조사", _ => "순찰" };

        private void OnDestroy()
        {
            if (manager == null) return;
            manager.OnServerStarted -= SpawnPatrol;
            manager.OnClientDisconnectCallback -= Disconnected;
        }
    }
}
