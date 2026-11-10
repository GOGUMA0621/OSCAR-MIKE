using UnityEngine;
using UnityEngine.AI;
using OskarMike.Network.Player;

namespace OskarMike.AI
{
    public enum CombatQueryKind { Attack, Cover, SideStep }
    public readonly struct CombatPositionRequest
    {
        public readonly uint Id;
        public readonly ulong TargetId;
        public readonly double CreatedAt;
        public readonly Vector3 Origin, Target;
        public readonly CombatQueryKind Kind;
        public CombatPositionRequest(uint id, ulong targetId, Vector3 origin, Vector3 target, CombatQueryKind kind)
        { Id = id; TargetId = targetId; Origin = origin; Target = target; Kind = kind; CreatedAt = Time.timeAsDouble; }
    }
    public readonly struct CombatPositionResult
    {
        public readonly CombatPositionRequest Request;
        public readonly bool Found;
        public readonly Vector3 Position;
        public CombatPositionResult(CombatPositionRequest request, bool found, Vector3 position)
        { Request = request; Found = found; Position = position; }
    }
    // Replace the executor later; reservations and final validation remain on the host.
    public interface ICombatPositionExecutor
    {
        CombatPositionResult Execute(CombatPositionRequest request, EnemyCombatController owner);
    }

    public sealed class CombatPositionQuery : ICombatPositionExecutor
    {
        private readonly RaycastHit[] hits = new RaycastHit[128];
        private readonly Collider[] overlaps = new Collider[64];
        private NavMeshPath path;
        private readonly Vector3[] corners = new Vector3[128];

        public bool Blocked(Vector3 from, Vector3 to, EnemyAgent self, bool actors, out bool byEnemy)
        {
            byEnemy = false;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .0001f) return false;
            int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, self.obstructionMask, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return true; // Saturation is conservatively blocked.
            bool blocked = false;
            for (int i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (collider.GetComponentInParent<PlayerNetworkController>() != null ||
                    collider.GetComponentInParent<Unity.Netcode.NetworkObject>()?.IsPlayerObject == true) continue;
                var enemy = collider.GetComponentInParent<EnemyAgent>();
                if (enemy == self || (!actors && enemy != null)) continue;
                if (enemy != null) byEnemy = true;
                blocked = true;
            }
            return blocked;
        }

        public bool Occupied(Vector3 p, EnemyCombatController owner)
        {
            float radius = owner.Mover.radius;
            int count = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (radius + .05f),
                p + Vector3.up * Mathf.Max(radius + .05f, owner.Mover.height - radius), radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return true;
            for (int i = 0; i < count; i++)
                if (!overlaps[i].transform.IsChildOf(owner.transform)) return true;
            return false;
        }

        public CombatPositionResult Execute(CombatPositionRequest request, EnemyCombatController owner)
        {
            path ??= new NavMeshPath();
            Vector3 best = default;
            float bestLength = float.PositiveInfinity;
            Vector3 forward = Vector3.ProjectOnPlane(request.Target - request.Origin, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            int count = request.Kind == CombatQueryKind.SideStep ? 4 : 32;
            for (int i = 0; i < count; i++)
            {
                Vector3 candidate;
                if (request.Kind == CombatQueryKind.SideStep)
                    candidate = request.Origin + right * ((i % 2 == 0 ? -1 : 1) * (i / 2 + 1));
                else
                {
                    float angle = (i % 16) * Mathf.PI * 2 / 16;
                    float radius = request.Kind == CombatQueryKind.Cover ? owner.coverRadius * (i < 16 ? .5f : 1) : owner.Enemy.firingRange * (i < 16 ? .65f : .85f);
                    candidate = (request.Kind == CombatQueryKind.Cover ? request.Origin : request.Target) + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
                }
                var filter = new NavMeshQueryFilter { agentTypeID = owner.Mover.agentTypeID, areaMask = owner.Mover.areaMask };
                // Player roots can be at capsule centre rather than feet. Project the
                // ring down by up to an agent height before checking reachability.
                if (!NavMesh.SamplePosition(candidate, out var hit, Mathf.Max(.6f, owner.Mover.height), filter)) continue;
                candidate = hit.position;
                if (request.Kind == CombatQueryKind.Cover && Vector3.Distance(candidate, request.Origin) > owner.coverRadius) continue;
                if (!CombatSlotManager.Available(owner, candidate) || Occupied(candidate, owner)) continue;
                if (request.Kind == CombatQueryKind.Cover ? !owner.SafeCover(candidate) : !owner.ValidAttack(candidate, request.Target)) continue;
                if (request.Kind == CombatQueryKind.Attack && !CombatSlotManager.HasPriority(owner, request.TargetId, candidate)) continue;
                if (!owner.Mover.CalculatePath(candidate, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                int n = path.GetCornersNonAlloc(corners);
                if (n == corners.Length) continue;
                float length = 0;
                for (int j = 1; j < n; j++) length += Vector3.Distance(corners[j - 1], corners[j]);
                // No long detours when looking for nearby cover.
                if (request.Kind == CombatQueryKind.Cover && length > owner.coverRadius * 2) continue;
                if (request.Kind != CombatQueryKind.Cover && length > Vector3.Distance(request.Origin, candidate) + owner.coverRadius) continue;
                if (length >= bestLength) continue;
                bestLength = length; best = candidate;
            }
            return new CombatPositionResult(request, !float.IsPositiveInfinity(bestLength), best);
        }
    }
}
