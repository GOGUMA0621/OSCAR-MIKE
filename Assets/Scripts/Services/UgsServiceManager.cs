using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace OskarMike.Services
{
    public class UgsServiceManager : MonoBehaviour
    {
        private const string ProjectNotLinkedMessage = "Unity Dashboard Project ID is not linked. Open Project Settings > Services and link this project before using Relay/UGS.";

        public static UgsServiceManager Instance { get; private set; }

        [Header("UGS")]
        [SerializeField] private string environmentName = "production";

        private Task<bool> initializationTask;

        public event Action<string> InitializationFailed;

        public bool IsInitialized => UnityServices.State == ServicesInitializationState.Initialized;
        public bool IsSignedIn => AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => AuthenticationService.Instance.PlayerId;
        public string LastInitializationError { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public Task<bool> EnsureInitializedAsync()
        {
            initializationTask ??= InitializeInternalAsync();
            return initializationTask;
        }

        private async Task<bool> InitializeInternalAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Application.cloudProjectId))
                {
                    LastInitializationError = ProjectNotLinkedMessage;
                    InitializationFailed?.Invoke(LastInitializationError);
                    Debug.LogError($"[UgsServiceManager] {LastInitializationError}");
                    initializationTask = null;
                    return false;
                }

                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    var options = new InitializationOptions();
                    if (!string.IsNullOrWhiteSpace(environmentName))
                    {
                        options.SetOption("environmentName", environmentName);
                    }

                    await UnityServices.InitializeAsync(options);
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                LastInitializationError = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                LastInitializationError = exception.Message;
                InitializationFailed?.Invoke(LastInitializationError);
                Debug.LogError($"[UgsServiceManager] Initialization failed: {exception}");
                initializationTask = null;
                return false;
            }
        }
    }
}