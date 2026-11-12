using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OskarMike.Items
{
    /// <summary>
    /// 플레이어의 파밍 상태 (개인 슬롯 N칸 + 손 1개 + 더플백 운반).
    ///
    /// [오너] F(Interact) / G(Throw) 입력 → 카메라 레이캐스트로 대상 탐색 → ServerRpc.
    /// [서버] 거리/상태 재검증 후 줍기·투입·메기·던지기 처리. 상태는 NetworkVariable/NetworkList 로 동기화.
    ///
    /// F : 아이템 → 슬롯(빈칸) 또는 손 / 더플백(땅·동료) → 내 아이템 전부 투입, 없으면 메기
    ///     대상 없음 + 더플백 착용 중 → 내 더플백에 투입
    /// G : 더플백 착용 시 던지기 (앉기/엎드리기면 발밑에 내려놓기), 아니면 손 아이템 내려놓기
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerNetworkController))]
    public sealed class PlayerLootCarrier : NetworkBehaviour, IPlayerMovementModifier
    {
        public enum InteractPromptKind : byte { None, PickUp, PickUpToHand, SlotsFull, Deposit, Shoulder, Throw }

        [Header("슬롯")]
        [Range(1, 8)] [SerializeField] private int slotCount = 4;

        [Header("상호작용")]
        [Min(0.5f)] [SerializeField] private float interactRange = 2.5f;
        [Tooltip("서버 거리 검증 여유값 (지연 보정)")]
        [Min(0f)] [SerializeField] private float serverRangeTolerance = 1.5f;
        [SerializeField] private string interactActionName = "Interact";
        [SerializeField] private string throwActionName = "Throw";

        [Header("손 아이템 내려놓기용 정의 조회")]
        [SerializeField] private LootTable[] lootTables = System.Array.Empty<LootTable>();

        private readonly NetworkList<LootSlotEntry> slots = new NetworkList<LootSlotEntry>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<LootSlotEntry> hand = new NetworkVariable<LootSlotEntry>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> carriedBagId = new NetworkVariable<ulong>(
            DuffelBag.NoCarrier, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerNetworkController controller;
        private PlayerHealth health;
        private Camera localCamera;
        private DuffelBag serverCarriedBag;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput playerInput;
        private InputAction interactAction;
        private InputAction throwAction;
#endif

        private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

        // ── 공개 상태 ──────────────────────────────────────
        public int SlotCount => slotCount;
        public int UsedSlots => slots.Count;
        public bool HasHandItem => !hand.Value.IsEmpty;
        public LootSlotEntry HandItem => hand.Value;
        public bool HasAnyItem => slots.Count > 0 || HasHandItem;
        public bool IsCarryingBag => carriedBagId.Value != DuffelBag.NoCarrier;
        /// <summary>무기 훅: 더플백 착용 중에는 보조무장(권총)만 사용 가능.</summary>
        public bool CanUsePrimaryWeapon => !IsCarryingBag;

        public int CarriedItemsValue
        {
            get
            {
                long sum = HasHandItem ? hand.Value.Price : 0;
                for (int i = 0; i < slots.Count; i++) sum += slots[i].Price;
                return (int)System.Math.Min(sum, int.MaxValue);
            }
        }

        public LootSlotEntry GetSlot(int index) => slots[index];

        /// <summary>운반 중인 더플백 (모든 피어에서 조회 가능, 없으면 null).</summary>
        public DuffelBag CarriedBag
        {
            get
            {
                if (IsServer) return serverCarriedBag;
                if (!IsCarryingBag || NetworkManager == null) return null;
                return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(carriedBagId.Value, out var obj)
                    ? obj.GetComponent<DuffelBag>() : null;
            }
        }

        // ── 오너 전용: HUD 프롬프트 ───────────────────────
        public InteractPromptKind Prompt { get; private set; }
        public DuffelBag LookedAtBag { get; private set; }

        // ── IPlayerMovementModifier (서버에서 조회) ────────
        public float SpeedMultiplier
        {
            get { var bag = CarriedBag; return bag != null ? bag.CarrySpeedMultiplier : 1f; }
        }
        public bool AllowFullSprint
        {
            get { var bag = CarriedBag; return bag == null || !bag.BlockFullSprint; }
        }

        // ══════════════════════════════════════════════════
        // 생명주기
        // ══════════════════════════════════════════════════

        private void Awake()
        {
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            localCamera = GetComponentInChildren<Camera>(true);
#if ENABLE_INPUT_SYSTEM
            playerInput = GetComponent<PlayerInput>();
#endif
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && health != null) health.Died += ServerOnDied;
#if ENABLE_INPUT_SYSTEM
            if (IsOwner && playerInput != null && playerInput.actions != null)
            {
                interactAction = playerInput.actions.FindAction(interactActionName, throwIfNotFound: false);
                throwAction = playerInput.actions.FindAction(throwActionName, throwIfNotFound: false);
                if (interactAction == null) Debug.LogWarning($"[PlayerLootCarrier] Action '{interactActionName}' not found.");
                if (throwAction == null) Debug.LogWarning($"[PlayerLootCarrier] Action '{throwActionName}' not found.");
            }
#endif
        }

        public override void OnNetworkPreDespawn()
        {
            // 플레이어가 디스폰(연결 해제 등)되기 전에 더플백을 떼어내야 함께 파괴되지 않는다.
            if (IsServer) ServerDropEverything();
            base.OnNetworkPreDespawn();
        }

        public override void OnNetworkDespawn()
        {
            if (health != null) health.Died -= ServerOnDied;
#if ENABLE_INPUT_SYSTEM
            interactAction = null;
            throwAction = null;
#endif
            Prompt = InteractPromptKind.None;
            LookedAtBag = null;
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            bool canAct = controller != null && controller.GameplayInputEnabled && (health == null || !health.Dead);
            if (!canAct) { Prompt = InteractPromptKind.None; LookedAtBag = null; return; }

            NetworkObject target = FindTarget(out DuffelBag bag, out NetworkLootItem item);
            LookedAtBag = bag;
            Prompt = EvaluatePrompt(bag, item);

#if ENABLE_INPUT_SYSTEM
            if (interactAction != null && interactAction.WasPressedThisFrame())
            {
                if (target != null) InteractServerRpc(target, true);
                else InteractServerRpc(default, false);
            }
            if (throwAction != null && throwAction.WasPressedThisFrame())
            {
                Vector3 look = localCamera != null ? localCamera.transform.forward : transform.forward;
                ThrowServerRpc(look);
            }
#endif
        }

        // ══════════════════════════════════════════════════
        // 오너: 대상 탐색
        // ══════════════════════════════════════════════════

        private NetworkObject FindTarget(out DuffelBag bag, out NetworkLootItem item)
        {
            bag = null;
            item = null;
            if (localCamera == null) return null;

            Ray ray = new Ray(localCamera.transform.position, localCamera.transform.forward);
            int count = Physics.SphereCastNonAlloc(ray, 0.12f, hitBuffer, interactRange, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            NetworkObject result = null;
            for (int i = 0; i < count; i++)
            {
                var hit = hitBuffer[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance >= best) continue;

                var lootItem = hit.collider.GetComponentInParent<NetworkLootItem>();
                if (lootItem != null && lootItem.IsSpawned)
                {
                    best = hit.distance; result = lootItem.NetworkObject; item = lootItem; bag = null;
                    continue;
                }

                var duffel = hit.collider.GetComponentInParent<DuffelBag>();
                if (duffel == null)
                {
                    // 동료 플레이어를 보고 있으면 그 플레이어가 멘 더플백을 대상으로 한다.
                    var other = hit.collider.GetComponentInParent<PlayerLootCarrier>();
                    if (other != null && other != this) duffel = other.CarriedBag;
                }
                if (duffel != null && duffel.IsSpawned)
                {
                    best = hit.distance; result = duffel.NetworkObject; bag = duffel; item = null;
                    continue;
                }

                // 벽 등 다른 물체가 먼저 가리면 그 뒤는 무시
                if (!hit.collider.isTrigger) best = hit.distance;
            }
            return result;
        }

        private InteractPromptKind EvaluatePrompt(DuffelBag bag, NetworkLootItem item)
        {
            if (item != null)
            {
                if (slots.Count < slotCount) return InteractPromptKind.PickUp;
                return HasHandItem ? InteractPromptKind.SlotsFull : InteractPromptKind.PickUpToHand;
            }
            if (bag != null)
            {
                if (HasAnyItem) return InteractPromptKind.Deposit;
                if (!bag.IsCarried && !IsCarryingBag) return InteractPromptKind.Shoulder;
                return InteractPromptKind.None;
            }
            if (IsCarryingBag) return HasAnyItem ? InteractPromptKind.Deposit : InteractPromptKind.Throw;
            return InteractPromptKind.None;
        }

        // ══════════════════════════════════════════════════
        // ServerRpc
        // ══════════════════════════════════════════════════

        [ServerRpc]
        private void InteractServerRpc(NetworkObjectReference targetRef, bool hasTarget)
        {
            if (!ServerCanAct()) return;

            if (!hasTarget || !targetRef.TryGet(out NetworkObject target) || target == null)
            {
                // 대상 없음: 착용 중인 더플백에 투입
                if (serverCarriedBag != null) ServerDepositAll(serverCarriedBag);
                return;
            }

            if (!ServerInRange(target.transform.position)) return;

            if (target.TryGetComponent(out NetworkLootItem item))
            {
                ServerPickUp(item);
                return;
            }

            if (target.TryGetComponent(out DuffelBag bag))
            {
                if (HasAnyItem) ServerDepositAll(bag);
                else if (!bag.IsCarried && serverCarriedBag == null && bag.ServerAttach(NetworkObject))
                {
                    serverCarriedBag = bag;
                    carriedBagId.Value = bag.NetworkObjectId;
                }
            }
        }

        [ServerRpc]
        private void ThrowServerRpc(Vector3 lookDirection)
        {
            if (!ServerCanAct()) return;
            if (float.IsNaN(lookDirection.x) || float.IsNaN(lookDirection.y) || float.IsNaN(lookDirection.z)) return;
            lookDirection = Vector3.ClampMagnitude(lookDirection, 1f);

            if (serverCarriedBag != null)
            {
                bool standing = controller == null || controller.Posture == PlayerPosture.Stand;
                ServerReleaseBag(lookDirection, standing);
            }
            else if (HasHandItem)
            {
                ServerDropHandItem(lookDirection);
            }
        }

        // ══════════════════════════════════════════════════
        // 서버 로직
        // ══════════════════════════════════════════════════

        private bool ServerCanAct() => IsSpawned && (health == null || !health.Dead);

        private bool ServerInRange(Vector3 targetPosition)
        {
            Vector3 eye = localCamera != null ? localCamera.transform.position : transform.position + Vector3.up * 1.6f;
            float limit = interactRange + serverRangeTolerance;
            return (targetPosition - eye).sqrMagnitude <= limit * limit
                || (targetPosition - transform.position).sqrMagnitude <= limit * limit;
        }

        private void ServerPickUp(NetworkLootItem item)
        {
            // 서버는 RPC 를 순서대로 처리하므로 먼저 도착한 요청만 IsSpawned 를 통과한다.
            if (item == null || !item.IsSpawned) return;

            var entry = LootSlotEntry.From(item);
            if (slots.Count < slotCount) slots.Add(entry);
            else if (!HasHandItem) hand.Value = entry;
            else return;

            item.NetworkObject.Despawn(true);
        }

        private void ServerDepositAll(DuffelBag bag)
        {
            if (bag == null || !bag.IsSpawned || !HasAnyItem) return;
            int total = CarriedItemsValue;
            slots.Clear();
            hand.Value = default;
            bag.ServerDeposit(total);
        }

        private void ServerReleaseBag(Vector3 lookDirection, bool throwBag)
        {
            var bag = serverCarriedBag;
            serverCarriedBag = null;
            carriedBagId.Value = DuffelBag.NoCarrier;
            if (bag != null && bag.IsSpawned) bag.ServerRelease(lookDirection, throwBag);
        }

        private void ServerDropHandItem(Vector3 lookDirection)
        {
            LootSlotEntry entry = hand.Value;
            if (entry.IsEmpty) return;

            LootItemDefinition definition = FindDefinition(entry.ItemId.ToString());
            if (definition == null || definition.NetworkPrefab == null)
            {
                Debug.LogWarning($"[PlayerLootCarrier] '{entry.ItemId}' 정의를 찾을 수 없어 내려놓을 수 없습니다. lootTables 를 확인하세요.");
                return;
            }

            Vector3 flat = Vector3.ProjectOnPlane(lookDirection, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
            Vector3 position = transform.position + Vector3.up * 0.5f + flat.normalized * 0.7f;

            NetworkObject instance = Instantiate(definition.NetworkPrefab, position, Quaternion.LookRotation(flat.normalized));
            var lootItem = instance.GetComponent<NetworkLootItem>();
            if (lootItem == null)
            {
                Destroy(instance.gameObject);
                return;
            }
            lootItem.InitializeServer(definition.ItemId, entry.ValueSteps, (LootCategory)entry.Category, entry.Price);
            instance.Spawn(true);
            hand.Value = default;
        }

        private LootItemDefinition FindDefinition(string itemId)
        {
            if (lootTables == null) return null;
            foreach (var table in lootTables)
            {
                if (table == null) continue;
                var items = table.Items;
                for (int i = 0; i < items.Count; i++)
                    if (items[i] != null && items[i].ItemId == itemId) return items[i];
            }
            return null;
        }

        private void ServerOnDied() => ServerDropEverything();

        private void ServerDropEverything()
        {
            if (serverCarriedBag != null) ServerReleaseBag(transform.forward, false);
            if (HasHandItem) ServerDropHandItem(transform.forward);
        }

        private void OnValidate()
        {
            slotCount = Mathf.Clamp(slotCount, 1, 8);
        }
    }
}
