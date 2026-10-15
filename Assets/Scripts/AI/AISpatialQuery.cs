using OskarMike.Network.Player;
using UnityEngine;

namespace OskarMike.AI
{
    public static class AISpatialQuery
    {
        // Ignore actors, not just their root colliders. Props and closed doors still occlude.
        public static bool IsBlocked(Vector3 from, Vector3 to, LayerMask mask)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.0001f) return false;
            foreach (RaycastHit hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude,
                         mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerNetworkController>() != null ||
                    hit.collider.GetComponentInParent<EnemyAgent>() != null) continue;
                return true;
            }
            return false;
        }

        public static bool TryHear(Vector3 listener, AINoise noise, LayerMask mask, out float score)
        {
            float radius = noise.Radius * (IsBlocked(listener, noise.Position, mask) ? 0.5f : 1f);
            score = radius > 0 ? Vector3.Distance(listener, noise.Position) / radius : float.PositiveInfinity;
            return score <= 1f;
        }
    }
}
