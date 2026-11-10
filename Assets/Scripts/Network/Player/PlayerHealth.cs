using System;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Network.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerHealth : NetworkBehaviour
    {
        [SerializeField, Min(1)] private float maximum = 100f;
        private readonly NetworkVariable<float> current = new(100f,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public float Current => current.Value;
        public float Maximum => maximum;
        public bool Dead => Current <= 0f;
        public event Action<float, float> Changed;
        public event Action Died; // Server only; once per spawned lifetime.

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                if (float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum < 1f) maximum = 100f;
                current.Value = maximum;
            }
            current.OnValueChanged += OnChanged;
            OnChanged(Current, Current);
        }

        public override void OnNetworkDespawn() => current.OnValueChanged -= OnChanged;

        public bool ApplyDamage(float amount, NetworkObject attacker = null)
        {
            if (!IsServer || !IsSpawned || Dead ||
                !DamageRules.TryCalculate(amount, HitRegion.Body, out float damage)) return false;
            if (attacker != null && (!attacker.IsSpawned || attacker.NetworkManager != NetworkManager)) return false;
            current.Value = Mathf.Max(0f, Current - damage);
            if (Dead) Died?.Invoke();
            return true;
        }

        public bool ApplyHit(float baseDamage, HitRegion region, NetworkObject attacker = null)
            => DamageRules.TryCalculate(baseDamage, region, out float damage) && ApplyDamage(damage, attacker);

#if UNITY_EDITOR
        [ContextMenu("체력 테스트/몸 피해 10")]
        private void TestBodyDamage() => ApplyHit(10f, HitRegion.Body);
        [ContextMenu("체력 테스트/머리 피해 20 (기본 10)")]
        private void TestHeadDamage() => ApplyHit(10f, HitRegion.Head);
#endif

        private void OnChanged(float previous, float value)
        {
            if (Dead && IsServer) GetComponent<PlayerNetworkController>()?.StopForDeath();
            if (Dead && IsOwner) GetComponent<PlayerNetworkController>()?.DisableGameplayInput();
            Changed?.Invoke(previous, value);
        }
    }
}
