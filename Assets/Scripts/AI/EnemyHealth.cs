using System;
using Unity.Netcode;
using UnityEngine;
using OskarMike.Network.Player;

namespace OskarMike.AI
{
    public enum RecoveryState : byte { Unused, InProgress, Complete }

    [DisallowMultipleComponent]
    public sealed class EnemyHealth : NetworkBehaviour
    {
        [Min(1)] public float maximum = 100;
        private readonly NetworkVariable<float> health = new(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public float Current => health.Value;
        public bool Dead => Current <= 0;
        public RecoveryState Recovery { get; private set; }
        public double LastHitAt { get; private set; } = double.NegativeInfinity;
        public event Action<NetworkObject> Damaged;
        public event Action Died;
        private bool initialized;

        public override void OnNetworkSpawn()
        {
            if (IsServer && !initialized) { maximum = Mathf.Max(1, maximum); health.Value = maximum; initialized = true; }
        }

        // Called by server weapon/damage simulation, never by a client RPC.
        public bool ApplyHit(float baseDamage, HitRegion region, NetworkObject attacker = null)
            => DamageRules.TryCalculate(baseDamage, region, out float damage) && ApplyDamage(damage, attacker);

        public bool ApplyDamage(float amount, NetworkObject attacker = null)
        {
            if (!IsServer || !IsSpawned || Dead || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
            if (attacker != null && attacker.NetworkManager != NetworkManager) return false;
            health.Value = Mathf.Max(0, health.Value - amount);
            LastHitAt = Time.timeAsDouble;
            if (!Dead) Damaged?.Invoke(attacker);
            else
            {
                GetComponent<EnemyCombatController>()?.Cancel();
                var enemy = GetComponent<EnemyAgent>();
                enemy.ReleasePopulation();
                enemy.enabled = false;
                Died?.Invoke();
            }
            return true;
        }

        public void Recover(float amount, float ceiling)
        {
            if (!IsServer || !IsSpawned || Dead || Recovery == RecoveryState.Complete || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            Recovery = RecoveryState.InProgress;
            health.Value = Mathf.Min(maximum * ceiling, Current + amount);
            if (Current >= maximum * ceiling) Recovery = RecoveryState.Complete;
        }
    }
}
