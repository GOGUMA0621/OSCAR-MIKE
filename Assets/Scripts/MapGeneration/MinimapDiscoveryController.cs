using System;
using System.Collections.Generic;
using OskarMike.Network.Player;
using UnityEngine;

namespace OskarMike.MapGeneration
{
    public sealed class MinimapDiscoveryController : MonoBehaviour
    {
        [SerializeField] private ProceduralMapGenerator mapGenerator;
        [SerializeField] private Transform testTarget;
        [SerializeField, Min(0.5f)] private float triggerHeight = 4f;
        [SerializeField, Min(0.1f)] private float triggerDepth = 1f;

        private readonly HashSet<int> revealedRooms = new();
        private readonly HashSet<int> revealedCorridors = new();
        private readonly List<Vector2Int> corridorRooms = new();
        private Transform trackedTarget;

        public event Action DiscoveryChanged;
        public IReadOnlyCollection<int> RevealedRooms => revealedRooms;
        public IReadOnlyCollection<int> RevealedCorridors => revealedCorridors;
        public Transform TrackedTarget => trackedTarget;
        public ProceduralMapGenerator MapGenerator => mapGenerator;

        private void Awake()
        {
            if (mapGenerator == null)
                mapGenerator = GetComponent<ProceduralMapGenerator>();
        }

        private void OnEnable()
        {
            if (mapGenerator != null)
                mapGenerator.MapGenerated += HandleMapGenerated;
        }

        private void Start()
        {
            InitializeDiscovery();
        }

        private void OnDisable()
        {
            if (mapGenerator != null)
                mapGenerator.MapGenerated -= HandleMapGenerated;
        }

        private void Update()
        {
            if (trackedTarget == null)
            {
                ResolveTrackedTarget();
                if (trackedTarget != null)
                {
                    int roomIndex = FindRoomContaining(trackedTarget.position);
                    if (roomIndex >= 0)
                        RevealRoom(roomIndex);
                }
            }
        }

        public void SetTestTarget(Transform target)
        {
            testTarget = target;
            trackedTarget = target;
            InitializeDiscovery();
        }

        public bool IsRoomRevealed(int roomIndex) => revealedRooms.Contains(roomIndex);
        public bool IsCorridorRevealed(int corridorIndex) => revealedCorridors.Contains(corridorIndex);

        public void RevealRoom(int roomIndex)
        {
            if (mapGenerator == null || roomIndex < 0 || roomIndex >= mapGenerator.PlacedRooms.Count)
                return;

            bool changed = revealedRooms.Add(roomIndex);
            for (int i = 0; i < corridorRooms.Count; i++)
            {
                Vector2Int rooms = corridorRooms[i];
                if (rooms.x == roomIndex || rooms.y == roomIndex)
                    changed |= revealedCorridors.Add(i);
            }

            if (changed)
                DiscoveryChanged?.Invoke();
        }

        internal bool IsTrackedCollider(Collider other)
        {
            if (trackedTarget == null || other == null)
                return false;

            Transform hit = other.transform;
            return hit == trackedTarget || hit.IsChildOf(trackedTarget) || trackedTarget.IsChildOf(hit);
        }

        private void HandleMapGenerated()
        {
            InitializeDiscovery();
        }

        private void InitializeDiscovery()
        {
            revealedRooms.Clear();
            revealedCorridors.Clear();
            corridorRooms.Clear();

            if (mapGenerator == null || mapGenerator.PlacedRooms.Count == 0)
            {
                DiscoveryChanged?.Invoke();
                return;
            }

            ResolveTrackedTarget();
            BuildCorridorGraphAndTriggers();

            int startRoom = FindRoomContaining(trackedTarget != null ? trackedTarget.position : Vector3.zero);
            RevealRoom(startRoom >= 0 ? startRoom : 0);
        }

        private void ResolveTrackedTarget()
        {
            if (testTarget != null)
            {
                trackedTarget = testTarget;
                return;
            }

            PlayerNetworkController[] players = FindObjectsByType<PlayerNetworkController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i].IsOwner)
                {
                    trackedTarget = players[i].transform;
                    return;
                }
            }
        }

        private int FindRoomContaining(Vector3 position)
        {
            for (int i = 0; i < mapGenerator.PlacedRooms.Count; i++)
            {
                Bounds bounds = mapGenerator.GetRoomBounds(i);
                bounds.Expand(new Vector3(0f, triggerHeight, 0f));
                if (bounds.Contains(position))
                    return i;
            }

            return -1;
        }

        private void BuildCorridorGraphAndTriggers()
        {
            Transform oldRoot = transform.Find("MinimapDoorTriggers");
            if (oldRoot != null)
            {
                oldRoot.gameObject.SetActive(false);
                Destroy(oldRoot.gameObject);
            }

            var triggerRoot = new GameObject("MinimapDoorTriggers").transform;
            triggerRoot.SetParent(transform, false);

            for (int i = 0; i < mapGenerator.Corridors.Count; i++)
            {
                ProceduralMapGenerator.CorridorData corridor = mapGenerator.Corridors[i];
                int roomA = ResolveEndpointRoom(corridor.roomAIndex, corridor.start);
                int roomB = ResolveEndpointRoom(corridor.roomBIndex, corridor.end, roomA);
                corridorRooms.Add(new Vector2Int(roomA, roomB));

                CreateDoorTrigger(triggerRoot, i, roomA, corridor.start, corridor.extendStart);
                CreateDoorTrigger(triggerRoot, i, roomB, corridor.end, corridor.extendEnd);
            }
        }

        private int ResolveEndpointRoom(int serializedIndex, Vector3 endpoint, int excludedRoom = -1)
        {
            if (serializedIndex >= 0 && serializedIndex < mapGenerator.PlacedRooms.Count
                && serializedIndex != excludedRoom
                && mapGenerator.GetRoomBounds(serializedIndex).SqrDistance(endpoint) < 0.05f)
                return serializedIndex;

            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < mapGenerator.PlacedRooms.Count; i++)
            {
                if (i == excludedRoom)
                    continue;

                float distance = mapGenerator.GetRoomBounds(i).SqrDistance(endpoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void CreateDoorTrigger(Transform root, int corridorIndex, int roomIndex,
            Vector3 doorPosition, Vector3 corridorPosition)
        {
            if (roomIndex < 0)
                return;

            Vector3 insideDirection = mapGenerator.PlacedRooms[roomIndex].worldPos - doorPosition;
            insideDirection.y = 0f;
            if (insideDirection.sqrMagnitude < 0.001f)
                insideDirection = doorPosition - corridorPosition;
            insideDirection.Normalize();

            var door = new GameObject($"DoorTrigger_{corridorIndex}_{roomIndex}");
            door.transform.SetParent(root, false);
            door.transform.position = doorPosition + Vector3.up * (triggerHeight * 0.5f);
            door.transform.rotation = Quaternion.LookRotation(insideDirection, Vector3.up);

            BoxCollider box = door.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(mapGenerator.Corridors[corridorIndex].width, triggerHeight, triggerDepth);

            MinimapDoorTrigger trigger = door.AddComponent<MinimapDoorTrigger>();
            trigger.Initialize(this, roomIndex, doorPosition, insideDirection);
        }
    }
}
