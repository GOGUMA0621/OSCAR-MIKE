using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace OskarMike.AI
{
    public sealed class ReinforcementZone : MonoBehaviour
    {
        public EnemyAgent enemyPrefab;
        public Transform[] spawnPoints = System.Array.Empty<Transform>();
        public EnemyPatrolRoute returnRoute;
        [Min(0)] public float callRadius = 15;
        [Min(1)] public int waveSize = 2, livingLimit = 4;
        [Min(0)] public float cooldown = 30;
        public LayerMask occupancyMask = ~0;
        public int LivingCount => living.Count;
        public double AvailableAt => availableAt;

        private static readonly HashSet<ReinforcementZone> zones = new();
        private readonly HashSet<EnemyAgent> living = new();
        private double availableAt;
        private bool reserved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => zones.Clear();
        private void OnEnable() => zones.Add(this);
        private void OnDisable() => zones.Remove(this);
        public void Release(EnemyAgent enemy) => living.Remove(enemy);

        public void ResetServerSession()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            living.Clear(); availableAt = 0; reserved = false;
        }

        public static int Request(EnemyAgent caller, Vector3 lastSeen)
        {
            if (caller == null || !caller.ServerActive || !caller.canRequestReinforcements) return 0;
            ReinforcementZone best = null;
            List<Vector3> positions = null;
            float nearest = float.PositiveInfinity;
            foreach (var zone in zones)
            {
                if (zone == null || zone.gameObject.scene != caller.gameObject.scene) continue;
                float distance = Vector3.Distance(caller.transform.position, zone.transform.position);
                if (distance > zone.callRadius || distance >= nearest || !zone.CanSpawn()) continue;
                var candidates = zone.ValidPositions();
                if (candidates.Count == 0) continue;
                nearest = distance; best = zone; positions = candidates;
            }
            return best != null ? best.Spawn(caller.NetworkManager, lastSeen, positions) : 0;
        }

        private bool CanSpawn()
        {
            living.RemoveWhere(enemy => enemy == null);
            return isActiveAndEnabled && enemyPrefab != null && !reserved &&
                Time.timeAsDouble >= availableAt && living.Count < livingLimit;
        }

        private List<Vector3> ValidPositions()
        {
            var result = new List<Vector3>();
            if (enemyPrefab == null) return result;
            var agent = enemyPrefab.GetComponent<NavMeshAgent>();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            foreach (Transform point in spawnPoints)
            {
                if (point == null || !NavMesh.SamplePosition(point.position, out var hit, 0.75f, filter)) continue;
                bool duplicate = result.Exists(position => Vector3.Distance(position, hit.position) < agent.radius * 2.1f);
                if (duplicate || Physics.CheckCapsule(hit.position + Vector3.up * (agent.radius + 0.05f),
                        hit.position + Vector3.up * Mathf.Max(agent.radius + 0.05f, agent.height - agent.radius),
                        agent.radius, occupancyMask, QueryTriggerInteraction.Ignore)) continue;
                result.Add(hit.position);
            }
            return result;
        }

        private int Spawn(NetworkManager manager, Vector3 lastSeen, List<Vector3> positions)
        {
            if (manager == null || !manager.IsServer || !CanSpawn()) return 0;
            // Reserve before Instantiate/Spawn callbacks can re-enter the request path.
            reserved = true;
            int spawned = 0;
            int count = Mathf.Min(waveSize, livingLimit - living.Count, positions.Count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    EnemyAgent enemy = Instantiate(enemyPrefab, positions[i], Quaternion.identity);
                    enemy.Initialize(returnRoute, this, lastSeen);
                    living.Add(enemy);
                    try { enemy.NetworkObject.Spawn(true); spawned++; }
                    catch
                    {
                        living.Remove(enemy);
                        Destroy(enemy.gameObject);
                        throw;
                    }
                }
            }
            finally
            {
                if (spawned > 0) availableAt = Time.timeAsDouble + cooldown;
                reserved = false;
            }
            return spawned;
        }

        private void OnDrawGizmosSelected()
        { Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, callRadius); }
    }
}
