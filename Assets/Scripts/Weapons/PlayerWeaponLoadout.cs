using System;
using OskarMike.AI;
using OskarMike.Items;
using OskarMike.Network.Player;
using OskarMike.Testing;
using Unity.Netcode;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OskarMike.Weapons
{
    /// <summary>슬롯별 탄약 (탄창 / 예비탄).</summary>
    [Serializable]
    public struct WeaponAmmo : INetworkSerializeByMemcpy, IEquatable<WeaponAmmo>
    {
        public short Magazine;
        public short Reserve;

        public WeaponAmmo(int magazine, int reserve)
        {
            Magazine = (short)Mathf.Clamp(magazine, 0, short.MaxValue);
            Reserve = (short)Mathf.Clamp(reserve, 0, short.MaxValue);
        }

        public bool Equals(WeaponAmmo other) => Magazine == other.Magazine && Reserve == other.Reserve;
        public override bool Equals(object obj) => obj is WeaponAmmo other && Equals(other);
        public override int GetHashCode() => (Magazine << 16) ^ Reserve;
    }

    /// <summary>원격 피어 사격 연출용: 발사 순번 + 탄이 멈춘 지점 (트레이서 끝점).</summary>
    [Serializable]
    public struct ShotEvent : INetworkSerializeByMemcpy, IEquatable<ShotEvent>
    {
        public int Sequence;
        public Vector3 End;

        public bool Equals(ShotEvent other) => Sequence == other.Sequence && End == other.End;
        public override bool Equals(object obj) => obj is ShotEvent other && Equals(other);
        public override int GetHashCode() => Sequence;
    }

    /// <summary>
    /// 플레이어 무장 (주무장 + 보조무장). 스폰 시 기본 지급.
    ///
    /// [오너] 1 / 2 → 무장 전환 요청, 좌클릭 → 사격 요청, R → 재장전 요청.
    /// [서버] 상태 검증 후 탄약 차감·히트스캔·피해 적용. 상태는 NetworkVariable 로 동기화 (ClientRpc 없음).
    ///
    /// 더플백 착용 중(<see cref="PlayerLootCarrier.CanUsePrimaryWeapon"/> == false)에는 주무장을 쓸 수 없고,
    /// 서버가 자동으로 보조무장으로 전환한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerNetworkController))]
    public sealed class PlayerWeaponLoadout : NetworkBehaviour
    {
        public enum HitFeedback : byte { None, Body, Head }

        [Header("기본 지급 무장")]
        [SerializeField] private WeaponDefinition primary;
        [SerializeField] private WeaponDefinition secondary;

        [Header("입력 액션 이름")]
        [SerializeField] private string primaryActionName = "PrimaryWeapon";
        [SerializeField] private string secondaryActionName = "SecondaryWeapon";
        [SerializeField] private string fireActionName = "Fire";
        [SerializeField] private string reloadActionName = "Reload";

        [Header("판정")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("다른 플레이어 피격 허용")]
        [SerializeField] private bool friendlyFire;
        [Tooltip("서버가 허용하는 사격 원점과 플레이어 위치의 최대 거리 (m)")]
        [Min(0.5f)] [SerializeField] private float maxOriginDistance = 2.5f;
        [Tooltip("지연으로 몰려 도착한 사격 RPC 허용 비율 (발사 간격 × (1 - 값))")]
        [Range(0f, 0.5f)] [SerializeField] private float fireIntervalTolerance = 0.25f;

        private readonly NetworkVariable<WeaponSlot> activeSlot = new NetworkVariable<WeaponSlot>(
            WeaponSlot.Primary, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<WeaponAmmo> primaryAmmo = new NetworkVariable<WeaponAmmo>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<WeaponAmmo> secondaryAmmo = new NetworkVariable<WeaponAmmo>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> reloading = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 원격 피어 사격 연출용 (발사 1회마다 Sequence +1, End = 탄착 지점).
        private readonly NetworkVariable<ShotEvent> lastShot = new NetworkVariable<ShotEvent>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        // 오너 히트마커용: (명중 횟수 << 1) | (머리면 1). 값 하나로 순서 문제 없이 부위까지 전달.
        private readonly NetworkVariable<int> hitSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerNetworkController controller;
        private PlayerHealth health;
        private PlayerLootCarrier lootCarrier;
        private Camera localCamera;

#if ENABLE_INPUT_SYSTEM
        private PlayerInput playerInput;
        private InputAction primaryAction;
        private InputAction secondaryAction;
        private InputAction fireAction;
        private InputAction reloadAction;
#endif

        // 서버 전용
        private double serverNextFireAt;
        private double serverReadyAt;
        private double serverReloadEndAt;

        // 오너 전용
        private double localNextFireAt;
        private double localReadyAt;
        private double localReloadStartedAt;

        private readonly RaycastHit[] hitBuffer = new RaycastHit[32];

        // ── 공개 상태 ──────────────────────────────────────
        /// <summary>
        /// 발사 연출 (local: true = 오너 로컬 즉시 예측, false = 원격 피어 동기화 / end: 탄착 지점).
        /// </summary>
        public event Action<bool, Vector3> ShotFired;
        /// <summary>오너 전용: 서버가 명중을 확인했을 때 (true = 머리).</summary>
        public event Action<bool> HitConfirmed;
        public event Action<WeaponSlot> ActiveSlotChanged;

        public WeaponSlot ActiveSlot => activeSlot.Value;
        public WeaponDefinition ActiveDefinition => GetDefinition(activeSlot.Value);
        public WeaponAmmo ActiveAmmo => GetAmmo(activeSlot.Value);
        public bool IsReloading => reloading.Value;
        public bool PrimaryBlocked => lootCarrier != null && !lootCarrier.CanUsePrimaryWeapon;
        public bool IsDrawing => Time.timeAsDouble < localReadyAt;

        /// <summary>오너 전용: 0~1 재장전 진행도 (재장전 중이 아니면 0).</summary>
        public float ReloadProgress
        {
            get
            {
                var def = ActiveDefinition;
                if (!reloading.Value || def == null) return 0f;
                return Mathf.Clamp01((float)((Time.timeAsDouble - localReloadStartedAt) / def.ReloadTime));
            }
        }

        public WeaponDefinition GetDefinition(WeaponSlot slot) => slot == WeaponSlot.Primary ? primary : secondary;
        public WeaponAmmo GetAmmo(WeaponSlot slot) => slot == WeaponSlot.Primary ? primaryAmmo.Value : secondaryAmmo.Value;
        private NetworkVariable<WeaponAmmo> AmmoVariable(WeaponSlot slot) => slot == WeaponSlot.Primary ? primaryAmmo : secondaryAmmo;

        // ══════════════════════════════════════════════════
        // 생명주기
        // ══════════════════════════════════════════════════

        private void Awake()
        {
            controller = GetComponent<PlayerNetworkController>();
            health = GetComponent<PlayerHealth>();
            lootCarrier = GetComponent<PlayerLootCarrier>();
            localCamera = GetComponentInChildren<Camera>(true);
#if ENABLE_INPUT_SYSTEM
            playerInput = GetComponent<PlayerInput>();
#endif
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                primaryAmmo.Value = primary != null ? new WeaponAmmo(primary.MagazineSize, primary.StartingReserve) : default;
                secondaryAmmo.Value = secondary != null ? new WeaponAmmo(secondary.MagazineSize, secondary.StartingReserve) : default;
                activeSlot.Value = primary != null ? WeaponSlot.Primary : WeaponSlot.Secondary;
                reloading.Value = false;
                serverReadyAt = serverNextFireAt = 0;
            }

            activeSlot.OnValueChanged += OnActiveSlotChanged;
            reloading.OnValueChanged += OnReloadingChanged;
            lastShot.OnValueChanged += OnLastShotChanged;
            hitSequence.OnValueChanged += OnHitSequenceChanged;

#if ENABLE_INPUT_SYSTEM
            if (IsOwner && playerInput != null && playerInput.actions != null)
            {
                primaryAction = FindAction(primaryActionName);
                secondaryAction = FindAction(secondaryActionName);
                fireAction = FindAction(fireActionName);
                reloadAction = FindAction(reloadActionName);
            }
#endif
        }

        public override void OnNetworkDespawn()
        {
            activeSlot.OnValueChanged -= OnActiveSlotChanged;
            reloading.OnValueChanged -= OnReloadingChanged;
            lastShot.OnValueChanged -= OnLastShotChanged;
            hitSequence.OnValueChanged -= OnHitSequenceChanged;
#if ENABLE_INPUT_SYSTEM
            primaryAction = secondaryAction = fireAction = reloadAction = null;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private InputAction FindAction(string actionName)
        {
            var action = playerInput.actions.FindAction(actionName, throwIfNotFound: false);
            if (action == null) Debug.LogWarning($"[PlayerWeaponLoadout] Action '{actionName}' not found.");
            return action;
        }
#endif

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();
            if (IsOwner) OwnerTick();
        }

        // ══════════════════════════════════════════════════
        // 오너: 입력
        // ══════════════════════════════════════════════════

        private void OwnerTick()
        {
            bool canAct = controller != null && controller.GameplayInputEnabled && !IsDead;
            if (!canAct) return;

#if ENABLE_INPUT_SYSTEM
            if (primaryAction != null && primaryAction.WasPressedThisFrame()) RequestSlot(WeaponSlot.Primary);
            if (secondaryAction != null && secondaryAction.WasPressedThisFrame()) RequestSlot(WeaponSlot.Secondary);
            if (reloadAction != null && reloadAction.WasPressedThisFrame()) RequestReload();

            var def = ActiveDefinition;
            if (fireAction == null || def == null) return;
            bool wantsFire = def.Automatic ? fireAction.IsPressed() : fireAction.WasPressedThisFrame();
            if (!wantsFire || Cursor.lockState != CursorLockMode.Locked) return;

            if (ActiveAmmo.Magazine <= 0)
            {
                // 빈 탄창으로 클릭하면 재장전 시도
                if (fireAction.WasPressedThisFrame()) RequestReload();
                return;
            }
            if (!CanFireLocally()) return;

            localNextFireAt = Time.timeAsDouble + def.FireInterval;
            Transform eye = localCamera != null ? localCamera.transform : transform;
            // 퍼짐은 오너가 계산해 보낸다 → 로컬 트레이서와 서버 판정 방향이 일치.
            Vector3 dir = ApplySpread(eye.forward, def.SpreadDegrees);
            Vector3 end = FindImpact(eye.position, dir, def.Range, out RaycastHit hit) ? hit.point : eye.position + dir * def.Range;
            FireServerRpc(eye.position, dir);
            ShotFired?.Invoke(true, end);
#endif
        }

        private bool CanFireLocally()
        {
            double now = Time.timeAsDouble;
            if (now < localNextFireAt || now < localReadyAt || reloading.Value) return false;
            if (activeSlot.Value == WeaponSlot.Primary && PrimaryBlocked) return false;
            return MovementAllowsFire();
        }

        private void RequestSlot(WeaponSlot slot)
        {
            if (slot == activeSlot.Value || GetDefinition(slot) == null) return;
            if (slot == WeaponSlot.Primary && PrimaryBlocked) return;
            SelectSlotServerRpc(slot);
        }

        private void RequestReload()
        {
            var def = ActiveDefinition;
            var ammo = ActiveAmmo;
            if (def == null || reloading.Value || ammo.Magazine >= def.MagazineSize || ammo.Reserve <= 0) return;
            ReloadServerRpc();
        }

        // ══════════════════════════════════════════════════
        // ServerRpc
        // ══════════════════════════════════════════════════

        [ServerRpc]
        private void SelectSlotServerRpc(WeaponSlot slot)
        {
            if (!IsSpawned || IsDead) return;
            if (slot != WeaponSlot.Primary && slot != WeaponSlot.Secondary) return;
            if (slot == WeaponSlot.Primary && PrimaryBlocked) return;
            ServerSwitchTo(slot);
        }

        [ServerRpc]
        private void ReloadServerRpc()
        {
            if (!IsSpawned || IsDead || reloading.Value) return;
            var def = ActiveDefinition;
            if (def == null || Time.timeAsDouble < serverReadyAt) return;
            var ammo = ActiveAmmo;
            if (ammo.Magazine >= def.MagazineSize || ammo.Reserve <= 0) return;
            reloading.Value = true;
            serverReloadEndAt = Time.timeAsDouble + def.ReloadTime;
        }

        [ServerRpc]
        private void FireServerRpc(Vector3 origin, Vector3 direction)
        {
            if (!IsSpawned || IsDead) return;
            if (!IsFinite(origin) || !IsFinite(direction) || direction.sqrMagnitude < 0.0001f) return;
            if ((origin - transform.position).sqrMagnitude > maxOriginDistance * maxOriginDistance) return;

            var slot = activeSlot.Value;
            var def = GetDefinition(slot);
            if (def == null || reloading.Value) return;
            if (slot == WeaponSlot.Primary && PrimaryBlocked) return;
            if (!MovementAllowsFire()) return;

            double now = Time.timeAsDouble;
            if (now < serverReadyAt || now < serverNextFireAt) return;

            var ammoVar = AmmoVariable(slot);
            var ammo = ammoVar.Value;
            if (ammo.Magazine <= 0) return;

            ammo.Magazine--;
            ammoVar.Value = ammo;
            serverNextFireAt = now + def.FireInterval * (1f - fireIntervalTolerance);

            Vector3 dir = direction.normalized;
            HitFeedback result = ServerTrace(origin, dir, def, out Vector3 end);
            lastShot.Value = new ShotEvent { Sequence = unchecked(lastShot.Value.Sequence + 1), End = end };
            if (result != HitFeedback.None)
            {
                int count = (hitSequence.Value >> 1) + 1;
                hitSequence.Value = ((count & 0x3FFFFFFF) << 1) | (result == HitFeedback.Head ? 1 : 0);
            }
        }

        // ══════════════════════════════════════════════════
        // 서버 로직
        // ══════════════════════════════════════════════════

        private bool IsDead => health != null && health.Dead;

        private void ServerTick()
        {
            if (IsDead)
            {
                if (reloading.Value) reloading.Value = false;
                return;
            }

            // 더플백 착용 시 주무장 → 보조무장 강제 전환
            if (activeSlot.Value == WeaponSlot.Primary && PrimaryBlocked && secondary != null)
                ServerSwitchTo(WeaponSlot.Secondary);

            if (reloading.Value && Time.timeAsDouble >= serverReloadEndAt)
            {
                var def = ActiveDefinition;
                var ammoVar = AmmoVariable(activeSlot.Value);
                var ammo = ammoVar.Value;
                if (def != null)
                {
                    int amount = Mathf.Min(def.MagazineSize - ammo.Magazine, ammo.Reserve);
                    if (amount > 0) ammoVar.Value = new WeaponAmmo(ammo.Magazine + amount, ammo.Reserve - amount);
                }
                reloading.Value = false;
            }
        }

        private void ServerSwitchTo(WeaponSlot slot)
        {
            var def = GetDefinition(slot);
            if (def == null || slot == activeSlot.Value) return;
            reloading.Value = false;
            activeSlot.Value = slot;
            serverReadyAt = Time.timeAsDouble + def.DrawTime;
        }

        private bool MovementAllowsFire()
        {
            if (controller == null) return true;
            if (controller.MoveState == PlayerMoveState.FullSprint) return false;
            var action = controller.ActionState;
            return action != PlayerActionState.Vaulting && action != PlayerActionState.Diving && action != PlayerActionState.Sliding;
        }

        private HitFeedback ServerTrace(Vector3 origin, Vector3 direction, WeaponDefinition def, out Vector3 end)
        {
            if (!FindImpact(origin, direction, def.Range, out RaycastHit hit))
            {
                end = origin + direction * def.Range;
                return HitFeedback.None;
            }
            end = hit.point;
            TryApplyHit(hit.collider, def.Damage, out HitFeedback feedback);
            return feedback;
        }

        /// <summary>
        /// 탄이 멈추는 첫 지점 (서버 판정·로컬 트레이서 공용). 자기 자신은 무시하고,
        /// 트리거는 피격 대상일 때만, 일반 콜라이더(벽 등)는 항상 탄을 멈춘다.
        /// </summary>
        private bool FindImpact(Vector3 origin, Vector3 direction, float range, out RaycastHit result)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, hitBuffer, range, hitMask, QueryTriggerInteraction.Collide);
            Array.Sort(hitBuffer, 0, count, Comparer.Instance);
            for (int i = 0; i < count; i++)
            {
                Collider col = hitBuffer[i].collider;
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (!col.isTrigger || IsShootable(col)) { result = hitBuffer[i]; return true; }
            }
            result = default;
            return false;
        }

        private bool IsShootable(Collider col)
        {
            if (col.TryGetComponent(out RangeTargetHitbox rangeBox) && rangeBox.target != null) return true;
            if (col.TryGetComponent(out DuffelBagHitbox bagBox) && bagBox.bag != null) return true;
            if (col.GetComponentInParent<EnemyHealth>() != null) return true;
            var player = col.GetComponentInParent<PlayerHealth>();
            return player != null && player.gameObject != gameObject;
        }

        /// <summary>대상 처리 여부를 반환 (true = 탄이 여기서 멈춤). 실제 피해가 들어갔으면 feedback 설정.</summary>
        private bool TryApplyHit(Collider col, float damage, out HitFeedback feedback)
        {
            feedback = HitFeedback.None;
            HitRegion region = col.TryGetComponent(out WeaponHitbox marker) ? marker.region : HitRegion.Body;

            if (col.TryGetComponent(out RangeTargetHitbox rangeBox) && rangeBox.target != null)
            {
                if (rangeBox.target.ApplyHit(damage, rangeBox.region)) feedback = ToFeedback(rangeBox.region);
                return true;
            }

            if (col.TryGetComponent(out DuffelBagHitbox bagBox) && bagBox.bag != null)
            {
                if (bagBox.bag.ServerApplyHit(damage)) feedback = HitFeedback.Body;
                return true;
            }

            var enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                if (enemy.ApplyHit(damage, region, NetworkObject)) feedback = ToFeedback(region);
                return true;
            }

            var player = col.GetComponentInParent<PlayerHealth>();
            if (player != null && player.gameObject != gameObject)
            {
                if (friendlyFire && player.ApplyHit(damage, region, NetworkObject)) feedback = ToFeedback(region);
                return true;
            }

            return false;
        }

        private static HitFeedback ToFeedback(HitRegion region) => region == HitRegion.Head ? HitFeedback.Head : HitFeedback.Body;

        private static Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            if (degrees <= 0f) return direction;
            Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
            Quaternion basis = Quaternion.LookRotation(direction);
            return (direction + basis * new Vector3(offset.x, offset.y, 0f)).normalized;
        }

        private static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        private sealed class Comparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly Comparer Instance = new Comparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        // ══════════════════════════════════════════════════
        // 동기화 콜백
        // ══════════════════════════════════════════════════

        private void OnActiveSlotChanged(WeaponSlot previous, WeaponSlot current)
        {
            var def = GetDefinition(current);
            localReadyAt = Time.timeAsDouble + (def != null ? def.DrawTime : 0f);
            ActiveSlotChanged?.Invoke(current);
        }

        private void OnReloadingChanged(bool previous, bool current)
        {
            if (current) localReloadStartedAt = Time.timeAsDouble;
        }

        private void OnLastShotChanged(ShotEvent previous, ShotEvent current)
        {
            // 오너는 입력 시점에 이미 로컬 연출을 재생했다.
            if (!IsOwner && current.Sequence != previous.Sequence) ShotFired?.Invoke(false, current.End);
        }

        private void OnHitSequenceChanged(int previous, int current)
        {
            if (IsOwner) HitConfirmed?.Invoke((current & 1) == 1);
        }
    }
}
