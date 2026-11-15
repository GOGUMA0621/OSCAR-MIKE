using OskarMike.Network.Player;
using OskarMike.Weapons;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OskarMike.Items
{
    /// <summary>
    /// 오너 전용 핫바 입력 (로컬 MonoBehaviour, 네트워크 상태 없음).
    ///
    /// 3 / 4 / 5 / 6 : 아이템 슬롯 1~4 손에 들기 (빈 칸이면 빈손). 같은 키를 다시 누르면 무기로 복귀.
    /// X             : 아이템 집어넣고 마지막 무기로 복귀.
    /// 마우스 휠      : 1 → 2 → 3 … 6 순환 (빈 아이템 칸·쓸 수 없는 무기는 건너뜀).
    ///
    /// 1 / 2 (무기)는 <see cref="PlayerWeaponLoadout"/> 가 그대로 처리한다. 실제 상태 변경은
    /// <see cref="PlayerLootCarrier.RequestSelectSlot"/> / <see cref="PlayerWeaponLoadout.RequestSlot"/> 의 ServerRpc 로 서버가 확정한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerLootCarrier))]
    public sealed class PlayerHotbarInput : MonoBehaviour
    {
        [Header("입력 액션 이름")]
        [SerializeField] private string[] itemSlotActionNames = { "ItemSlot1", "ItemSlot2", "ItemSlot3", "ItemSlot4" };
        [SerializeField] private string holsterActionName = "Holster";
        [SerializeField] private string cycleActionName = "CycleSlot";

        [Tooltip("휠 한 칸에 여러 단계가 넘어가지 않도록 하는 최소 간격 (초)")]
        [Min(0f)] [SerializeField] private float cycleCooldown = 0.08f;
        [Tooltip("휠 요청 후 서버 확정 전까지 로컬 커서를 유지하는 시간 (초)")]
        [Min(0f)] [SerializeField] private float pendingHold = 0.35f;

        private const int WeaponCount = 2;

        private PlayerLootCarrier carrier;
        private PlayerWeaponLoadout loadout;
        private PlayerNetworkController controller;
        private PlayerHealth health;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput playerInput;
        private InputAction[] itemActions;
        private InputAction holsterAction;
        private InputAction cycleAction;
        private bool bound;
#endif
        private float nextCycleAt;
        private int pendingCursor = -1;
        private float pendingUntil;

        private void Awake()
        {
            carrier = GetComponent<PlayerLootCarrier>();
            loadout = GetComponent<PlayerWeaponLoadout>();
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
#if ENABLE_INPUT_SYSTEM
            playerInput = GetComponent<PlayerInput>();
#endif
        }

        private void Update()
        {
            if (carrier == null || !carrier.IsSpawned || !carrier.IsOwner) return;
            bool canAct = controller != null && controller.GameplayInputEnabled && (health == null || !health.Dead);
            if (!canAct) return;
#if ENABLE_INPUT_SYSTEM
            if (!bound) Bind();

            for (int i = 0; i < itemActions.Length; i++)
            {
                if (itemActions[i] == null || !itemActions[i].WasPressedThisFrame()) continue;
                if (i >= carrier.SlotCount) continue;
                // 같은 칸을 다시 누르면 무기로 복귀
                carrier.RequestSelectSlot(carrier.SelectedSlot == i ? PlayerLootCarrier.NoSlot : i);
                pendingCursor = -1;
            }

            if (holsterAction != null && holsterAction.WasPressedThisFrame() && carrier.WeaponHolstered)
            {
                carrier.RequestSelectSlot(PlayerLootCarrier.NoSlot);
                pendingCursor = -1;
            }

            if (cycleAction != null && Time.unscaledTime >= nextCycleAt)
            {
                float scroll = cycleAction.ReadValue<float>();
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    nextCycleAt = Time.unscaledTime + cycleCooldown;
                    Cycle(scroll < 0f ? 1 : -1);   // 휠 아래 = 다음 칸
                }
            }
#endif
        }

        /// <summary>커서: 0 = 주무장, 1 = 보조무장, 2.. = 아이템 슬롯.</summary>
        private int CurrentCursor()
        {
            if (pendingCursor >= 0 && Time.unscaledTime < pendingUntil) return pendingCursor;
            if (carrier.WeaponHolstered) return WeaponCount + carrier.SelectedSlot;
            return loadout != null ? (int)loadout.ActiveSlot : WeaponCount;
        }

        private void Cycle(int direction)
        {
            int total = WeaponCount + carrier.SlotCount;
            int cursor = CurrentCursor();
            for (int step = 1; step < total; step++)
            {
                int next = ((cursor + direction * step) % total + total) % total;
                if (!IsSelectable(next)) continue;
                Select(next);
                pendingCursor = next;
                pendingUntil = Time.unscaledTime + pendingHold;
                return;
            }
        }

        private bool IsSelectable(int cursor)
        {
            if (cursor < WeaponCount)
            {
                if (loadout == null) return false;
                var slot = (WeaponSlot)cursor;
                if (loadout.GetDefinition(slot) == null) return false;
                return !(slot == WeaponSlot.Primary && loadout.PrimaryBlocked);
            }
            return !carrier.GetSlot(cursor - WeaponCount).IsEmpty;
        }

        private void Select(int cursor)
        {
            if (cursor < WeaponCount) loadout.RequestSlot((WeaponSlot)cursor);
            else carrier.RequestSelectSlot(cursor - WeaponCount);
        }

#if ENABLE_INPUT_SYSTEM
        private void Bind()
        {
            bound = true;
            itemActions = new InputAction[itemSlotActionNames.Length];
            if (playerInput == null || playerInput.actions == null) return;
            for (int i = 0; i < itemSlotActionNames.Length; i++) itemActions[i] = Find(itemSlotActionNames[i]);
            holsterAction = Find(holsterActionName);
            cycleAction = Find(cycleActionName);
        }

        private InputAction Find(string actionName)
        {
            var action = playerInput.actions.FindAction(actionName, throwIfNotFound: false);
            if (action == null) Debug.LogWarning($"[PlayerHotbarInput] Action '{actionName}' not found.");
            return action;
        }
#endif
    }
}
