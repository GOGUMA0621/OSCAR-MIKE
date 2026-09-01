using UnityEngine;

namespace OskarMike.MapGeneration
{
    public sealed class MinimapDoorTrigger : MonoBehaviour
    {
        private MinimapDiscoveryController discovery;
        private int targetRoomIndex;
        private Vector3 doorPosition;
        private Vector3 insideDirection;

        public void Initialize(MinimapDiscoveryController owner, int roomIndex,
            Vector3 position, Vector3 roomDirection)
        {
            discovery = owner;
            targetRoomIndex = roomIndex;
            doorPosition = position;
            insideDirection = roomDirection;
        }

        private void OnTriggerStay(Collider other)
        {
            if (discovery == null || discovery.IsRoomRevealed(targetRoomIndex)
                || !discovery.IsTrackedCollider(other))
                return;

            Vector3 offset = other.transform.position - doorPosition;
            offset.y = 0f;
            if (Vector3.Dot(offset, insideDirection) > 0.05f)
                discovery.RevealRoom(targetRoomIndex);
        }
    }
}
