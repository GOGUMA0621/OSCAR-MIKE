using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.MapGeneration
{
    public class ProceduralMapGenerator : MonoBehaviour
    {
        [Header("Settings")]
        public RoomPool roomPool;
        [Min(1)] public int roomCount = 10;
        public int seed = 0;
        public float cellSize = 4f;
        public float roomPadding = 1f;

        [Header("Room Spacing (grid cells)")]
        [Min(1)] public int minRoomGap = 3;
        [Min(1)] public int maxRoomRange = 12;

        [Header("Corridor")]
        public float corridorWidth = 2.5f;
        public float corridorFloorY = -0.25f;
        [Min(0.1f)] public float corridorThickness = 0.5f;
        public Material corridorMaterial;

        [Header("Placeholder (used when prefab is null)")]
        public bool usePlaceholdersForMissingPrefabs = true;
        public float placeholderHeight = 4f;

        [System.Serializable]
        public struct PlacedRoomData
        {
            public RoomConfig config;
            public Vector3 worldPos;
            public Vector2Int gridPos;
            public Vector2Int size;
            public GameObject instance;
        }

        [System.Serializable]
        public struct CorridorData
        {
            public Vector3 start;
            public Vector3 corner;
            public Vector3 end;
            public float width;
            public GameObject instance;
        }

        public List<PlacedRoomData> placedRooms = new List<PlacedRoomData>();
        public List<CorridorData> corridors = new List<CorridorData>();

        private System.Random rng;

        [ContextMenu("Generate Map")]
        public void Generate()
        {
            ClearMap();
            rng = seed != 0 ? new System.Random(seed) : new System.Random();

            if (roomPool == null || roomPool.rooms.Count == 0)
            {
                Debug.LogError("[MapGen] RoomPool is empty or not assigned.");
                return;
            }

            var root = new GameObject("GeneratedMap").transform;

            // 1) Place first room at origin
            var firstConfig = roomPool.PickRandom(rng);
            var firstRoom = PlaceRoom(firstConfig, Vector2Int.zero, root);
            placedRooms.Add(firstRoom);

            // 2) Place remaining rooms
            for (int i = 1; i < roomCount; i++)
            {
                var config = roomPool.PickRandom(rng);
                if (TryPlaceRoom(config, root, out var result))
                {
                    placedRooms.Add(result);
                }
            }

            Debug.Log($"[MapGen] Generated {placedRooms.Count} rooms, {corridors.Count} corridors.");
        }

        [ContextMenu("Clear Map")]
        public void ClearMap()
        {
            var existing = GameObject.Find("GeneratedMap");
            if (existing != null) DestroyImmediate(existing);
            placedRooms.Clear();
            corridors.Clear();
        }

        private PlacedRoomData PlaceRoom(RoomConfig config, Vector2Int gridPos, Transform parent)
        {
            Vector3 worldPos = new Vector3(gridPos.x * cellSize, 0f, gridPos.y * cellSize);
            GameObject instance;

            if (config.prefab != null)
            {
                instance = Instantiate(config.prefab, worldPos, Quaternion.identity, parent);
            }
            else if (usePlaceholdersForMissingPrefabs)
            {
                instance = CreatePlaceholderRoom(config, worldPos, parent);
            }
            else
            {
                Debug.LogWarning($"[MapGen] RoomConfig '{config.name}' has no prefab. Skipping.");
                instance = null;
            }

            return new PlacedRoomData
            {
                config = config,
                worldPos = worldPos,
                gridPos = gridPos,
                size = config.size,
                instance = instance
            };
        }

        private GameObject CreatePlaceholderRoom(RoomConfig config, Vector3 center, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Room_{config.name}_{center.x}_{center.z}";
            go.transform.SetParent(parent);
            go.transform.position = center;
            float sx = config.size.x * cellSize;
            float sz = config.size.y * cellSize;
            go.transform.localScale = new Vector3(sx, placeholderHeight, sz);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(
                    Random.Range(0.3f, 0.7f),
                    Random.Range(0.3f, 0.7f),
                    Random.Range(0.3f, 0.7f))
            };

            return go;
        }

        private bool TryPlaceRoom(RoomConfig config, Transform parent, out PlacedRoomData result)
        {
            const int maxAttempts = 60;
            float roomW = config.size.x * cellSize;
            float roomD = config.size.y * cellSize;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var anchor = placedRooms[rng.Next(placedRooms.Count)];
                int dx = rng.Next(minRoomGap, maxRoomRange + 1) * (rng.Next(2) == 0 ? 1 : -1);
                int dz = rng.Next(minRoomGap, maxRoomRange + 1) * (rng.Next(2) == 0 ? 1 : -1);

                var gridPos = new Vector2Int(
                    anchor.gridPos.x + anchor.size.x / 2 + dx,
                    anchor.gridPos.y + anchor.size.y / 2 + dz);

                // Bounds check
                Vector3 center = new Vector3(gridPos.x * cellSize, 0f, gridPos.y * cellSize);
                Vector3 halfSize = new Vector3(roomW * 0.5f + roomPadding, placeholderHeight * 0.5f, roomD * 0.5f + roomPadding);
                var newBounds = new Bounds(center, halfSize * 2f);

                bool overlaps = false;
                foreach (var existing in placedRooms)
                {
                    float ew = existing.size.x * cellSize;
                    float ed = existing.size.y * cellSize;
                    var eb = new Bounds(existing.worldPos, new Vector3(ew + roomPadding * 2f, placeholderHeight, ed + roomPadding * 2f));
                    if (newBounds.Intersects(eb))
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps) continue;

                // Find nearest door pair and create L-shaped corridor
                Vector3? doorA = GetClosestDoor(anchor, center);
                Vector3? doorB = GetClosestDoor(config, center, anchor.worldPos);

                if (doorA == null || doorB == null)
                {
                    result = PlaceRoom(config, gridPos, parent);
                    return true;
                }

                var dA = doorA.Value;
                var dB = doorB.Value;

                // Build L-shaped corridor (two possible corner points)
                Vector3 corner1 = new Vector3(dB.x, 0f, dA.z);
                Vector3 corner2 = new Vector3(dA.x, 0f, dB.z);

                // Pick the shorter path that doesn't overlap
                float len1 = Vector3.Distance(dA, corner1) + Vector3.Distance(corner1, dB);
                float len2 = Vector3.Distance(dA, corner2) + Vector3.Distance(corner2, dB);
                Vector3 corner = len1 <= len2 ? corner1 : corner2;

                result = PlaceRoom(config, gridPos, parent);

                var corridor = BuildCorridor(dA, corner, dB, parent);
                corridors.Add(corridor);

                return true;
            }

            result = default;
            return false;
        }

        private Vector3 GetClosestDoor(PlacedRoomData room, Vector3 target)
        {
            if (room.instance == null)
                return GetPlaceholderDoorPosition(room, target);

            var markers = room.instance.GetComponentsInChildren<DoorMarker>();
            if (markers.Length == 0)
                return GetPlaceholderDoorPosition(room, target);

            Vector3 closest = markers[0].transform.position;
            float minDist = Vector3.Distance(closest, target);
            for (int i = 1; i < markers.Length; i++)
            {
                float d = Vector3.Distance(markers[i].transform.position, target);
                if (d < minDist)
                {
                    minDist = d;
                    closest = markers[i].transform.position;
                }
            }
            return closest;
        }

        private Vector3 GetClosestDoor(RoomConfig config, Vector3 roomCenter, Vector3 target)
        {
            return GetPlaceholderDoorPosition(config, roomCenter, target);
        }

        private Vector3 GetPlaceholderDoorPosition(PlacedRoomData room, Vector3 target)
        {
            return GetPlaceholderDoorPosition(room.config, room.worldPos, target);
        }

        private Vector3 GetPlaceholderDoorPosition(RoomConfig config, Vector3 center, Vector3 target)
        {
            float hw = config.size.x * cellSize * 0.5f;
            float hd = config.size.y * cellSize * 0.5f;

            Vector3 local = target - center;
            float ax = Mathf.Abs(local.x);
            float az = Mathf.Abs(local.z);

            if (ax > az)
                return center + new Vector3(Mathf.Sign(local.x) * hw, 0f, 0f);
            else
                return center + new Vector3(0f, 0f, Mathf.Sign(local.z) * hd);
        }

        private CorridorData BuildCorridor(Vector3 start, Vector3 corner, Vector3 end, Transform parent)
        {
            var root = new GameObject("Corridor").transform;
            root.SetParent(parent);

            BuildCorridorSegment(start, corner, root);
            BuildCorridorSegment(corner, end, root);

            return new CorridorData
            {
                start = start,
                corner = corner,
                end = end,
                width = corridorWidth,
                instance = root.gameObject
            };
        }

        private void BuildCorridorSegment(Vector3 from, Vector3 to, Transform parent)
        {
            Vector3 dir = to - from;
            float length = dir.magnitude;
            if (length < 0.01f) return;

            Vector3 mid = (from + to) * 0.5f;
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = "CorridorSeg";
            seg.transform.SetParent(parent);
            seg.transform.position = new Vector3(mid.x, corridorFloorY, mid.z);
            seg.transform.localScale = new Vector3(corridorWidth, corridorThickness, length);
            seg.transform.rotation = Quaternion.LookRotation(dir);

            var renderer = seg.GetComponent<MeshRenderer>();
            if (corridorMaterial != null)
                renderer.sharedMaterial = corridorMaterial;
            else
            {
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(0.4f, 0.4f, 0.4f)
                };
            }

            DestroyImmediate(seg.GetComponent<BoxCollider>());
        }
    }
}
