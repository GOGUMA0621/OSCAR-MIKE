using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OskarMike.Items
{
    /// <summary>
    /// 방 입구의 재스폰 버튼 (씬 배치 NetworkObject).
    ///
    /// [로컬] 로컬 플레이어가 버튼을 바라보고 상호작용 거리 안이면 "[F] 아이템 재스폰" 프롬프트를 띄우고,
    ///        F(Interact) 를 누르면 <see cref="RequestRespawnServerRpc"/> 를 보낸다.
    /// [서버] 거리·쿨다운 검증 후 <see cref="TableLootSpawner.ServerRespawn"/> 실행.
    ///        눌림 애니메이션은 <c>pressSequence</c> NetworkVariable 로 모든 피어에서 재생.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LootRespawnButton : NetworkBehaviour
    {
        [SerializeField] private TableLootSpawner spawner;
        [Tooltip("눌림 애니메이션을 줄 버튼 캡 (로컬 -Z 방향으로 들어감)")]
        [SerializeField] private Transform buttonCap;
        [Min(0.5f)] [SerializeField] private float interactRange = 2.5f;
        [Tooltip("서버 거리 검증 여유값")]
        [Min(0f)] [SerializeField] private float serverRangeTolerance = 1.5f;
        [Min(0f)] [SerializeField] private float cooldown = 2f;
        [SerializeField] private string interactActionName = "Interact";
        [SerializeField] private float pressDepth = 0.03f;

        private readonly NetworkVariable<int> pressSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly RaycastHit[] hitBuffer = new RaycastHit[8];
        private double serverNextAllowedTime;
        private float pressAnim;
        private Vector3 capRestLocal;
        private bool showPrompt;
        private GUIStyle promptStyle;

        // 로컬 플레이어 캐시
        private NetworkObject cachedPlayer;
        private PlayerNetworkController localController;
        private PlayerHealth localHealth;
        private Camera localCamera;
#if ENABLE_INPUT_SYSTEM
        private InputAction interactAction;
#endif

        private void Awake()
        {
            if (spawner == null) spawner = FindFirstObjectByType<TableLootSpawner>();
            if (buttonCap != null) capRestLocal = buttonCap.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            pressSequence.OnValueChanged += OnPressed;
        }

        public override void OnNetworkDespawn()
        {
            pressSequence.OnValueChanged -= OnPressed;
            showPrompt = false;
        }

        private void OnPressed(int previous, int current) => pressAnim = 1f;

        private void Update()
        {
            AnimateCap();

            showPrompt = false;
            if (!IsSpawned || !ResolveLocalPlayer()) return;
            if (localController == null || !localController.GameplayInputEnabled) return;
            if (localHealth != null && localHealth.Dead) return;
            if (!IsLookedAt()) return;

            showPrompt = true;
#if ENABLE_INPUT_SYSTEM
            if (interactAction != null && interactAction.WasPressedThisFrame())
                RequestRespawnServerRpc();
#endif
        }

        private void AnimateCap()
        {
            if (buttonCap == null) return;
            pressAnim = Mathf.MoveTowards(pressAnim, 0f, Time.deltaTime * 4f);
            buttonCap.localPosition = capRestLocal + Vector3.back * (pressDepth * Mathf.Sin(pressAnim * Mathf.PI * 0.5f));
        }

        private bool ResolveLocalPlayer()
        {
            NetworkManager manager = NetworkManager;
            NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null) { cachedPlayer = null; return false; }
            if (player == cachedPlayer) return true;

            cachedPlayer = player;
            localController = player.GetComponent<PlayerNetworkController>();
            localHealth = player.GetComponent<PlayerHealth>();
            localCamera = player.GetComponentInChildren<Camera>(true);
#if ENABLE_INPUT_SYSTEM
            var input = player.GetComponent<PlayerInput>();
            interactAction = input != null && input.actions != null
                ? input.actions.FindAction(interactActionName, throwIfNotFound: false)
                : null;
#endif
            return true;
        }

        private bool IsLookedAt()
        {
            if (localCamera == null) return false;
            Ray ray = new Ray(localCamera.transform.position, localCamera.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, hitBuffer, interactRange, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            bool onButton = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hitBuffer[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(cachedPlayer.transform)) continue;
                if (hit.collider.isTrigger && !hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                onButton = hit.collider.transform.IsChildOf(transform);
            }
            return onButton;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestRespawnServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) || client.PlayerObject == null)
                return;

            var health = client.PlayerObject.GetComponent<PlayerHealth>();
            if (health != null && health.Dead) return;

            float maxDistance = interactRange + serverRangeTolerance + 1f; // 카메라 높이 보정
            if ((client.PlayerObject.transform.position - transform.position).sqrMagnitude > maxDistance * maxDistance)
                return;

            double now = NetworkManager.ServerTime.Time;
            if (now < serverNextAllowedTime) return;
            serverNextAllowedTime = now + cooldown;

            if (spawner == null)
            {
                Debug.LogWarning("[LootRespawnButton] TableLootSpawner 가 지정되지 않았습니다.");
                return;
            }

            spawner.ServerRespawn();
            pressSequence.Value++;
        }

        private void OnGUI()
        {
            if (!showPrompt) return;
            if (promptStyle == null)
            {
                promptStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 20,
                    fontStyle = FontStyle.Bold
                };
                promptStyle.normal.textColor = Color.white;
            }

            const string text = "[F] 아이템 재스폰";
            Rect rect = new Rect(0f, Screen.height * 0.5f + 40f, Screen.width, 30f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, promptStyle);
            GUI.color = Color.white;
            GUI.Label(rect, text, promptStyle);
        }

        private void OnValidate()
        {
            interactRange = Mathf.Max(0.5f, interactRange);
            serverRangeTolerance = Mathf.Max(0f, serverRangeTolerance);
            cooldown = Mathf.Max(0f, cooldown);
        }
    }
}
