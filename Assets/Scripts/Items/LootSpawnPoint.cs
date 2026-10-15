using UnityEngine;

namespace OskarMike.Items
{
    public sealed class LootSpawnPoint : MonoBehaviour
    {
        [Min(1)] [SerializeField] private int selectionWeight = 1;
        public int SelectionWeight => selectionWeight;

        private void OnValidate() => selectionWeight = Mathf.Max(1, selectionWeight);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.75f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.2f);
            Gizmos.DrawLine(transform.position, transform.position + transform.up * 0.5f);
        }
    }
}
