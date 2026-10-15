using UnityEngine;

namespace OskarMike.AI
{
    public sealed class EnemyPatrolRoute : MonoBehaviour
    {
        public Transform[] points = System.Array.Empty<Transform>();
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] == null) continue;
                Gizmos.DrawSphere(points[i].position, 0.2f);
                Transform next = points[(i + 1) % points.Length];
                if (next != null) Gizmos.DrawLine(points[i].position, next.position);
            }
        }
    }
}
