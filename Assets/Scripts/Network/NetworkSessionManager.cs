using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OskarMike.Network.Player;
using OskarMike.Services;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine.SceneManagement;

namespace OskarMike.Network
{
    public class NetworkSessionManager : MonoBehaviour
    {
        public static NetworkSessionManager Instance { get; private set; }

        [Header("Session")]
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private string defaultAddress = "127.0.0.1";
        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private string relayConnectionType = "dtls";

        [Header("References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private UnityTransport unityTransport;

        private readonly HashSet<ulong> connectedClientIds = new HashSet<ulong>();
        private NetworkManager subscribedNetworkManager;
        private bool awaitingClientConnection;
        private bool isBusy;
        private string currentJoinCode = string.Empty;

        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action SessionStarted;
        public event Action<string> SessionStartFailed;
        public event Action<string> JoinCodeChanged;
        public event Action ReadyStatesChanged;

        public IReadOnlyCollection<ulong> ConnectedClientIds => connectedClientIds;
        public bool IsHost => networkManager != null && networkManager.IsHost;
        public bool IsClient => networkManager != null && networkManager.IsClient;
        public bool IsServer => networkManager != null && networkManager.IsServer;
        public NetworkManager ActiveNetworkManager => networkManager;
        public bool IsBusy => isBusy;
        public string CurrentJoinCode => currentJoinCode;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            RegisterCallbacks();
            PlayerNetworkController.ReadyStateChangedGlobal += HandleReadyStateChanged;
        }

        private void OnDisable()
        {
            PlayerNetworkController.ReadyStateChangedGlobal -= HandleReadyStateChanged;
            UnregisterCallbacks();
        }

        public bool StartHostSession()
        {
            ResolveReferences();
            RegisterCallbacks();
            if (!CanStartSession())
            {
                return false;
            }

            connectedClientIds.Clear();
            awaitingClientConnection = false;
            ConfigureTransport(defaultAddress, defaultPort);
            var started = networkManager.StartHost();
            if (started)
            {
                Debug.Log("[NetworkSessionManager] Host started.");
                return true;
            }

            SessionStartFailed?.Invoke("Failed to start host session.");
            return false;
        }

        public async Task<bool> StartRelayHostSessionAsync()
        {
            ResolveReferences();
            RegisterCallbacks();
            if (!CanStartSession() || isBusy)
            {
                return false;
            }

            if (!await EnsureServicesReadyAsync())
            {
                return false;
            }

            try
            {
                isBusy = true;
                connectedClientIds.Clear();
                awaitingClientConnection = false;
                SetJoinCode(string.Empty);

                var allocation = await RelayService.Instance.CreateAllocationAsync(Mathf.Max(1, maxPlayers - 1));
                var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                unityTransport.SetRelayServerData(allocation.ToRelayServerData(relayConnectionType));
                SetJoinCode(joinCode);

                if (networkManager.StartHost())
                {
                    Debug.Log($"[NetworkSessionManager] Relay host started with join code {joinCode}.");
                    return true;
                }

                SetJoinCode(string.Empty);
                SessionStartFailed?.Invoke("Failed to start host after Relay allocation.");
                return false;
            }
            catch (Exception exception)
            {
                SetJoinCode(string.Empty);
                SessionStartFailed?.Invoke($"Host session failed: {exception.Message}");
                Debug.LogError($"[NetworkSessionManager] Relay host session failed: {exception}");
                return false;
            }
            finally
            {
                isBusy = false;
            }
        }

        public bool StartClientSession(string address, ushort port)
        {
            ResolveReferences();
            RegisterCallbacks();
            if (!CanStartSession())
            {
                return false;
            }

            connectedClientIds.Clear();
            awaitingClientConnection = true;
            ConfigureTransport(address, port);
            var started = networkManager.StartClient();
            if (started)
            {
                Debug.Log("[NetworkSessionManager] Client started.");
                return true;
            }

            awaitingClientConnection = false;
            SessionStartFailed?.Invoke("Failed to start client session.");
            return false;
        }

        public async Task<bool> StartClientSessionWithJoinCodeAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                Debug.LogWarning("[NetworkSessionManager] Join code is empty.");
                return false;
            }

            ResolveReferences();
            RegisterCallbacks();
            if (!CanStartSession() || isBusy)
            {
                return false;
            }

            if (!await EnsureServicesReadyAsync())
            {
                return false;
            }

            try
            {
                isBusy = true;
                connectedClientIds.Clear();
                awaitingClientConnection = true;

                var sanitizedJoinCode = joinCode.Trim().ToUpperInvariant();
                var joinAllocation = await RelayService.Instance.JoinAllocationAsync(sanitizedJoinCode);
                unityTransport.SetRelayServerData(joinAllocation.ToRelayServerData(relayConnectionType));
                SetJoinCode(sanitizedJoinCode);

                if (networkManager.StartClient())
                {
                    Debug.Log($"[NetworkSessionManager] Relay client started with join code {sanitizedJoinCode}.");
                    return true;
                }

                awaitingClientConnection = false;
                SetJoinCode(string.Empty);
                SessionStartFailed?.Invoke("Failed to start client after joining Relay allocation.");
                return false;
            }
            catch (Exception exception)
            {
                awaitingClientConnection = false;
                SetJoinCode(string.Empty);
                SessionStartFailed?.Invoke($"Join by code failed: {exception.Message}");
                Debug.LogError($"[NetworkSessionManager] Relay client session failed: {exception}");
                return false;
            }
            finally
            {
                isBusy = false;
            }
        }

        public void ShutdownSession()
        {
            ResolveReferences();
            RegisterCallbacks();
            if (networkManager == null)
            {
                return;
            }

            awaitingClientConnection = false;
            connectedClientIds.Clear();
            SetJoinCode(string.Empty);
            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
        }

        public void LoadLobbyScene()
        {
            if (networkManager == null || !networkManager.IsServer)
            {
                Debug.LogWarning("[NetworkSessionManager] Only server can load lobby scene via NetworkSceneManager.");
                return;
            }

            var sceneName = GameManager.Instance != null
                ? GameManager.Instance.LobbySceneName
                : "Lobby";

            Debug.Log($"[NetworkSessionManager] Loading lobby scene '{sceneName}' via NetworkSceneManager.");
            networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        public bool CanStartGame()
        {
            if (!IsHost || connectedClientIds.Count == 0 || connectedClientIds.Count > maxPlayers)
            {
                return false;
            }

            return AreAllConnectedPlayersReady();
        }

        public bool AreAllConnectedPlayersReady()
        {
            ResolveReferences();
            
            foreach (var clientId in connectedClientIds)
            {
                if (!TryGetPlayerNetworkController(clientId, out var playerController) || !playerController.IsReady)
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsClientReady(ulong clientId)
        {
            return TryGetPlayerNetworkController(clientId, out var playerController) && playerController.IsReady;
        }

        public bool IsLocalClientReady()
        {
            ResolveReferences();
            if (networkManager == null)
            {
                return false;
            }

            return IsClientReady(networkManager.LocalClientId);
        }

        public bool ToggleLocalReady()
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return false;
            }

            if (!TryGetPlayerNetworkController(networkManager.LocalClientId, out var playerController))
            {
                return false;
            }

            playerController.SetReady(!playerController.IsReady);
            return true;
        }

        public bool SetLocalReady(bool ready)
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return false;
            }

            if (!TryGetPlayerNetworkController(networkManager.LocalClientId, out var playerController))
            {
                return false;
            }

            playerController.SetReady(ready);
            return true;
        }

        private async Task<bool> EnsureServicesReadyAsync()
        {
            if (UgsServiceManager.Instance == null)
            {
                SessionStartFailed?.Invoke("UGS service manager is missing.");
                return false;
            }

            var initialized = await UgsServiceManager.Instance.EnsureInitializedAsync();
            if (!initialized)
            {
                var reason = UgsServiceManager.Instance.LastInitializationError;
                SessionStartFailed?.Invoke(string.IsNullOrWhiteSpace(reason)
                    ? "Failed to initialize Unity Gaming Services."
                    : reason);
            }

            return initialized;
        }

        private bool CanStartSession()
        {
            if (networkManager == null || unityTransport == null)
            {
                Debug.LogError("[NetworkSessionManager] Missing NetworkManager or UnityTransport reference.");
                SessionStartFailed?.Invoke("Missing NetworkManager or UnityTransport reference.");
                return false;
            }

            if (networkManager.IsListening)
            {
                Debug.LogWarning("[NetworkSessionManager] Session is already running.");
                return false;
            }

            return true;
        }

        private void ConfigureTransport(string address, ushort port)
        {
            unityTransport.SetConnectionData(address, port);
        }

        private void SetJoinCode(string joinCode)
        {
            currentJoinCode = joinCode;
            JoinCodeChanged?.Invoke(currentJoinCode);
        }

        private void ResolveReferences()
        {
            if (networkManager == null)
            {
                networkManager = NetworkManager.Singleton;
            }

            if (networkManager == null)
            {
                networkManager = FindFirstObjectByType<NetworkManager>();
            }

            if (unityTransport == null && networkManager != null)
            {
                unityTransport = networkManager.GetComponent<UnityTransport>();
            }
        }

        private void RegisterCallbacks()
        {
            if (networkManager == null)
            {
                return;
            }

            if (subscribedNetworkManager == networkManager)
            {
                return;
            }

            UnregisterCallbacks();
            subscribedNetworkManager = networkManager;
            subscribedNetworkManager.OnClientConnectedCallback += HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        private void UnregisterCallbacks()
        {
            if (subscribedNetworkManager == null)
            {
                return;
            }

            subscribedNetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            subscribedNetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            subscribedNetworkManager = null;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (connectedClientIds.Count >= maxPlayers)
            {
                if (networkManager.IsServer)
                {
                    networkManager.DisconnectClient(clientId);
                }

                Debug.LogWarning($"[NetworkSessionManager] Rejected client {clientId}: lobby is full.");
                return;
            }

            connectedClientIds.Add(clientId);
            ClientConnected?.Invoke(clientId);
            ReadyStatesChanged?.Invoke();

            if (clientId == networkManager.LocalClientId)
            {
                awaitingClientConnection = false;
                SessionStarted?.Invoke();
            }

            Debug.Log($"[NetworkSessionManager] Client connected: {clientId} ({connectedClientIds.Count}/{maxPlayers})");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            connectedClientIds.Remove(clientId);
            ClientDisconnected?.Invoke(clientId);
            ReadyStatesChanged?.Invoke();

            if (awaitingClientConnection && clientId == networkManager.LocalClientId)
            {
                awaitingClientConnection = false;
                SessionStartFailed?.Invoke("Connection to host failed or was closed.");
                SetJoinCode(string.Empty);
            }

            Debug.Log($"[NetworkSessionManager] Client disconnected: {clientId} ({connectedClientIds.Count}/{maxPlayers})");
        }

        private bool TryGetPlayerNetworkController(ulong clientId, out PlayerNetworkController playerController)
        {
            playerController = null;
            if (networkManager == null)
            {
                return false;
            }

            if (!networkManager.ConnectedClients.TryGetValue(clientId, out var client))
            {
                return false;
            }

            if (client.PlayerObject == null)
            {
                return false;
            }

            playerController = client.PlayerObject.GetComponent<PlayerNetworkController>();
            return playerController != null;
        }

        private void HandleReadyStateChanged(ulong _, bool __)
        {
            ReadyStatesChanged?.Invoke();
        }
    }
}
