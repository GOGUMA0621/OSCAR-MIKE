using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.AI
{
    // Scene/session ownership is carried by each owner; no network objects or RPCs required.
    public static class CombatSlotManager
    {
        private struct Job { public EnemyCombatController Owner; public CombatPositionRequest Request; }
        private static readonly List<Job> jobs = new();
        private static readonly Dictionary<EnemyCombatController, Vector3> reservations = new();
        private struct Waiter { public EnemyCombatController Owner; public ulong Target; }
        private static readonly List<Waiter> waiting = new();
        private static UnityEngine.AI.NavMeshPath priorityPath;
        private static int frame = -1;
        public static int QueriesPerFrame = 2;
        public static ICombatPositionExecutor Executor { get; set; } = new CombatPositionQuery();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { jobs.Clear(); waiting.Clear(); reservations.Clear(); frame = -1; QueriesPerFrame = 2; Executor = new CombatPositionQuery(); }

        public static void WaitForSlot(EnemyCombatController owner, ulong target)
        {
            foreach (var waiter in waiting) if (waiter.Owner == owner) return;
            waiting.Add(new Waiter { Owner = owner, Target = target });
        }
        public static void LeaveQueue(EnemyCombatController owner)
        { for (int i = waiting.Count - 1; i >= 0; i--) if (waiting[i].Owner == owner) waiting.RemoveAt(i); }
        public static bool HasPriority(EnemyCombatController owner, ulong target, Vector3 point)
        {
            priorityPath ??= new UnityEngine.AI.NavMeshPath();
            foreach (var waiter in waiting)
            {
                if (waiter.Owner == owner) break;
                var prior = waiter.Owner;
                if (prior == null || !prior.Enemy.ServerActive || prior.Recovering || waiter.Target != target ||
                    prior.Enemy.NetworkManager != owner.Enemy.NetworkManager || !prior.Mover.enabled || !prior.Mover.isOnNavMesh) continue;
                if (prior.ValidAttack(point, owner.AttackTargetPosition) && prior.Mover.CalculatePath(point, priorityPath) &&
                    priorityPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) return false;
            }
            return true;
        }

        public static bool Available(EnemyCombatController owner, Vector3 point)
        {
            foreach (var pair in reservations)
                if (pair.Key != null && pair.Key != owner && pair.Key.Enemy.NetworkManager == owner.Enemy.NetworkManager &&
                    Vector3.Distance(point, pair.Value) < Mathf.Max(owner.slotSpacing, pair.Key.slotSpacing)) return false;
            return true;
        }
        public static bool Reserve(EnemyCombatController owner, Vector3 point)
        {
            if (!Available(owner, point)) return false;
            reservations[owner] = point; return true;
        }
        public static void Release(EnemyCombatController owner)
        {
            reservations.Remove(owner);
            for (int i = jobs.Count - 1; i >= 0; i--) if (jobs[i].Owner == owner) jobs.RemoveAt(i);
        }
        public static void Enqueue(EnemyCombatController owner, CombatPositionRequest request)
        {
            for (int i = 0; i < jobs.Count; i++) if (jobs[i].Owner == owner) return;
            jobs.Add(new Job { Owner = owner, Request = request });
        }
        public static void Tick()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            for (int budget = 0; budget < Mathf.Max(1, QueriesPerFrame) && jobs.Count > 0; budget++)
            {
                Job job = jobs[0]; jobs.RemoveAt(0);
                if (job.Owner == null || !job.Owner.Enemy.ServerActive) continue;
                job.Owner.Receive(Executor.Execute(job.Request, job.Owner));
            }
        }
    }
}
