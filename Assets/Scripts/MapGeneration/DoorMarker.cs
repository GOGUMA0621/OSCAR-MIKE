using UnityEngine;

namespace OskarMike.MapGeneration
{
    public class DoorMarker : MonoBehaviour
    {
        public float width = 2f;
        public float height = 3f;

        private void OnDrawGizmos()
        {
            var color = Gizmos.color;
            Gizmos.color = Color.green;
            Vector3 dir = transform.forward * 0.8f;
            Vector3 right = transform.right * width * 0.5f;
            Vector3 pos = transform.position;
            Gizmos.DrawLine(pos - right, pos + right);
            Gizmos.DrawLine(pos, pos + dir);
            Gizmos.color = color;
        }

        private void OnDrawGizmosSelected()
        {
            var color = Gizmos.color;
            Gizmos.color = new Color(0, 1, 0, 0.15f);
            Vector3 center = transform.position + transform.forward * 0.5f;
            Gizmos.DrawCube(center, new Vector3(width, height, 1f));
            Gizmos.color = color;
        }
    }
}
