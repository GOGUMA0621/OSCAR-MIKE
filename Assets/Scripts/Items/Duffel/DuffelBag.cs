using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace OskarMike.Items
{
    /// <summary>
    /// 팀 공용 더플백. 아이템 자체가 아닌 "가치"만 저장하는 가상 인벤토리.
    /// - 메기/내려놓기/던지기, 투입, 피격 가치 감소는 모두 서버에서만 처리한다.
    /// - 피격 이펙트는 hitSequence NetworkVariable 변화로 모든 클라이언트에서 재생 (ClientRpc 없음).
    /// - 이속 디버프는 담긴 가치와 무관한 고정값.
    /// </summary>
    [RequireComponent(typeof(NetworkObject), typeof(Rigidbody))]
    public sealed class DuffelBag : NetworkBehaviour
    {
        public const ulong NoCarrier = ulong.MaxValue;

        [Header("운반 (고정 디버프)")]
        [Range(0.1f, 1f)] [SerializeField] private float carrySpeedMultiplier = 0.7f;
        [SerializeField] private bool blockFullSprint = true;
        [Tooltip("운반자 기준 등 위치 (로컬)")]
        [SerializeField] private Vector3 carryLocalPosition = new Vector3(0f, 1.25f, -0.25f);
        [SerializeField] private Vector3 carryLocalEuler = Vector3.zero;

        [Header("던지기")]
        [Min(0f)] [SerializeField] private float throwSpeed = 6f;
        [Min(0f)] [SerializeField] private float throwUpSpeed = 2f;
        [Min(0f)] [SerializeField] private float dropForwardOffset = 0.6f;

        [Header("피격")]
        [Tooltip("데미지 1당 현재 가치에서 잃는 비율 (0.005 = 0.5%)")]
        [Range(0f, 0.05f)] [SerializeField] private float lossPerDamage = 0.005f;
        [SerializeField] private GameObject hitEffectPrefab;
        [Min(0.1f)] [SerializeField] private float hitEffectLifetime = 3f;

        [Header("참조")]
        [Tooltip("운반 중 꺼지는 물리 콜라이더 (Hitbox 콜라이더는 제외)")]
        [SerializeField] private Collider[] physicsColliders = Array.Empty<Collider>();

        private readonly NetworkVariable<int> storedValue = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> carrierObjectId = new NetworkVariable<ulong>(
            NoCarrier, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> hitSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> lastLoss = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Rigidbody body;
        private NetworkTransform networkTransform;

        public int StoredValue => storedValue.Value;
        public int LastLoss => lastLoss.Value;
        public bool IsCarried => carrierObjectId.Value != NoCarrier;
        /// <summary>운반자 플레이어의 NetworkObjectId (없으면 <see cref="NoCarrier"/>).</summary>
        public ulong CarrierObjectId => carrierObjectId.Value;
        public float CarrySpeedMultiplier => carrySpeedMultiplier;
        public bool BlockFullSprint => blockFullSprint;

        /// <summary>모든 피어: 피격으로 가치가 줄었을 때 (잃은 금액).</summary>
        public event Action<int> ValueLost;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networkTransform = GetComponent<NetworkTransform>();
            if (physicsColliders == null || physicsColliders.Length == 0)
            {
                var all = GetComponentsInChildren<Collider>(true);
                var list = new System.Collections.Generic.List<Collider>();
                foreach (var c in all) if (c.GetComponent<DuffelBagHitbox>() == null) list.Add(c);
                physicsColliders = list.ToArray();
            }
        }

        public override void OnNetworkSpawn()
        {
            hitSequence.OnValueChanged += OnHitSequenceChanged;
            carrierObjectId.OnValueChanged += OnCarrierChanged;
            ApplyCarryPhysics(IsCarried);
        }

        public override void OnNetworkDespawn()
        {
            hitSequence.OnValueChanged -= OnHitSequenceChanged;
            carrierObjectId.OnValueChanged -= OnCarrierChanged;
        }

        // ══════════════════════════════════════════════════
        // 서버 API
        // ══════════════════════════════════════════════════

        public void ServerDeposit(int price)
        {
            if (!IsServer || price <= 0) return;
            storedValue.Value = SafeAdd(storedValue.Value, price);
        }

        public bool ServerAttach(NetworkObject carrier)
        {
            if (!IsServer || IsCarried || carrier == null || !carrier.IsSpawned) return false;
            if (!NetworkObject.TrySetParent(carrier, false))
            {
                Debug.LogWarning("[DuffelBag] 운반자에게 부착 실패 (TrySetParent).");
                return false;
            }
            carrierObjectId.Value = carrier.NetworkObjectId;
            if (networkTransform != null) networkTransform.InLocalSpace = true;
            ApplyCarryPhysics(true);
            transform.localPosition = carryLocalPosition;
            transform.localRotation = Quaternion.Euler(carryLocalEuler);
            return true;
        }

        /// <summary>운반자에게서 떼어 월드에 놓는다. throwBag=false 면 발밑에 내려놓기.</summary>
        public void ServerRelease(Vector3 lookDirection, bool throwBag)
        {
            if (!IsServer || !IsCarried) return;
            Transform carrier = transform.parent;
            Vector3 origin = carrier != null ? carrier.position : transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(lookDirection, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) flat = carrier != null ? carrier.forward : Vector3.forward;
            flat.Normalize();

            NetworkObject.TryRemoveParent(true);
            carrierObjectId.Value = NoCarrier;
            if (networkTransform != null) networkTransform.InLocalSpace = false;

            transform.position = origin + Vector3.up * (throwBag ? 1.3f : 0.4f) + flat * dropForwardOffset;
            transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            ApplyCarryPhysics(false);

            Vector3 velocity = Vector3.zero;
            if (throwBag)
            {
                Vector3 dir = lookDirection.sqrMagnitude > 0.0001f ? lookDirection.normalized : flat;
                velocity = dir * throwSpeed + Vector3.up * throwUpSpeed;
            }
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>서버: 총탄 피격. 데미지 × 고정 비율만큼 현재 가치를 잃는다.</summary>
        public bool ServerApplyHit(float damage)
        {
            if (!IsServer || !IsSpawned || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
                return false;

            int current = storedValue.Value;
            int loss = 0;
            if (current > 0)
            {
                float ratio = Mathf.Clamp01(damage * lossPerDamage);
                loss = Mathf.Clamp(Mathf.CeilToInt(current * ratio), 1, current);
                storedValue.Value = current - loss;
            }
            lastLoss.Value = loss;
            hitSequence.Value = unchecked(hitSequence.Value + 1);
            return true;
        }

        // ══════════════════════════════════════════════════
        // 내부
        // ══════════════════════════════════════════════════

        private void OnCarrierChanged(ulong previous, ulong current) => ApplyCarryPhysics(current != NoCarrier);

        private void ApplyCarryPhysics(bool carried)
        {
            // 클라이언트는 NetworkTransform 으로만 움직이므로 항상 kinematic.
            bool simulate = IsServer && !carried;
            body.isKinematic = !simulate;
            body.interpolation = simulate ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            foreach (var c in physicsColliders) if (c != null) c.enabled = !carried;
        }

        private void OnHitSequenceChanged(int previous, int current)
        {
            int loss = lastLoss.Value;
            if (hitEffectPrefab != null)
            {
                var fx = Instantiate(hitEffectPrefab, transform.position, Quaternion.LookRotation(Vector3.up));
                Destroy(fx, hitEffectLifetime);
            }
            if (loss > 0) ValueLost?.Invoke(loss);
        }

        private static int SafeAdd(int a, int b) => (int)Math.Min((long)a + b, int.MaxValue);

        private void OnValidate()
        {
            carrySpeedMultiplier = Mathf.Clamp(carrySpeedMultiplier, 0.1f, 1f);
            lossPerDamage = Mathf.Clamp(lossPerDamage, 0f, 0.05f);
        }
    }
}
