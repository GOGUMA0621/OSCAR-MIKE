using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.MapGeneration
{
    public class ProceduralMapGenerator : MonoBehaviour
    {
        public event System.Action MapGenerated;

        public enum RoomPlacementShape
        {
            DiagonalOnly,
            CardinalOnly,
            Mixed
        }

        public enum CorridorCrossingPolicy
        {
            Allow,
            Block
        }

        [Header("Settings")]
        public RoomPool roomPool;
        [Min(1)] public int roomCount = 10;
        public int seed = 0;
        [Min(0.01f)]
        public float cellSize = 4f;
        [Min(0f)]
        public float roomPadding = 1f;

        [Header("Room Spacing (grid cells)")]
        [Min(1)] public int minRoomGap = 3;
        [Min(1)] public int maxRoomRange = 12;

        [Header("Layout Shape")]
        public RoomPlacementShape placementShape = RoomPlacementShape.DiagonalOnly;
        [Range(0f, 1f)] public float cardinalPlacementChance = 0.5f;

        [Header("Corridor")]
        [Min(0.01f)]
        public float corridorWidth = 2.5f;
        public float corridorFloorY = 0f;
        public Material corridorMaterial;
        [Min(0f)]
        public float minStraight = 2f;
        public CorridorCrossingPolicy corridorCrossingPolicy = CorridorCrossingPolicy.Allow;

        [Header("Corridor Walls")]
        public bool generateCorridorWalls = true;
        [Min(0.01f)] public float corridorWallHeight = 2.5f;
        public Material corridorWallMaterial;

        [Header("Debug")]
        public bool debugLogging = false;

        [Header("Placeholder (used when prefab is null)")]
        public bool usePlaceholdersForMissingPrefabs = true;
        [Min(0.01f)]
        public float placeholderHeight = 4f;

        [System.Serializable]
        public struct PlacedRoomData
        {
            public int roomIndex;
            public RoomConfig config;
            public Vector3 worldPos;
            // Center grid coordinate of the room. Convert with GridToWorldCenter().
            public Vector2Int gridPos;
            public Vector2Int size;
            public GameObject instance;
        }

        [System.Serializable]
        public struct CorridorData
        {
            public int roomAIndex;
            public int roomBIndex;
            public Vector3 start;       // room A door
            public Vector3 extendStart; // minStraight out from room A
            public Vector3 corner;      // L-turn point
            public Vector3 extendEnd;   // minStraight out from room B
            public Vector3 end;         // room B door
            public float width;
            public GameObject instance;
        }

        private struct CorridorSegmentRect
        {
            public float minX;
            public float maxX;
            public float minZ;
            public float maxZ;
            public bool isHorizontal;
        }

        private struct WallBlockerRect
        {
            public float minX;
            public float maxX;
            public float minZ;
            public float maxZ;
        }

        private struct GenerationStats
        {
            public int targetRooms;
            public int effectiveSeed;
            public int roomRequests;
            public int roomSuccesses;
            public int roomFailures;
            public int candidateAttempts;
            public int diagonalCandidates;
            public int cardinalCandidates;
            public int roomOverlapRejects;
            public int corridorOverlapRejects;
            public int corridorBlockedRejects;
            public int corridorCrossingRejects;
        }

        public List<PlacedRoomData> placedRooms = new List<PlacedRoomData>();
        public List<CorridorData> corridors = new List<CorridorData>();

        public IReadOnlyList<PlacedRoomData> PlacedRooms => placedRooms;
        public IReadOnlyList<CorridorData> Corridors => corridors;
        public int EffectiveSeed { get; private set; }

        public Bounds GetRoomBounds(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= placedRooms.Count)
                return default;

            PlacedRoomData room = placedRooms[roomIndex];
            return new Bounds(room.worldPos, GetRoomWorldSize(room.size));
        }

        private System.Random rng;

        [ContextMenu("Generate Map")]
        public void Generate()
        {
            if (!ValidateSettings())
                return;

            ClearMap();
            var stats = new GenerationStats
            {
                targetRooms = roomCount,
                effectiveSeed = seed != 0 ? seed : System.Environment.TickCount
            };
            rng = new System.Random(stats.effectiveSeed);
            EffectiveSeed = stats.effectiveSeed;

            var root = new GameObject("GeneratedMap").transform;

            // 1) Place first room at origin
            var firstConfig = roomPool.PickRandom(rng);
            var firstRoom = PlaceRoom(firstConfig, Vector2Int.zero, root, 0);
            placedRooms.Add(firstRoom);

            // 2) Place remaining rooms
            for (int i = 1; i < roomCount; i++)
            {
                var config = roomPool.PickRandom(rng);
                stats.roomRequests++;
                if (TryPlaceRoom(config, root, ref stats, out var result))
                {
                    placedRooms.Add(result);
                    stats.roomSuccesses++;
                }
                else
                {
                    stats.roomFailures++;
                }
            }

            BuildCorridorFloor(root);

            LogGenerationSummary(stats);
            MapGenerated?.Invoke();
        }

        private void OnValidate()
        {
            roomCount = Mathf.Max(1, roomCount);
            cellSize = Mathf.Max(0.01f, cellSize);
            roomPadding = Mathf.Max(0f, roomPadding);
            minRoomGap = Mathf.Max(1, minRoomGap);
            maxRoomRange = Mathf.Max(minRoomGap, maxRoomRange);
            cardinalPlacementChance = Mathf.Clamp01(cardinalPlacementChance);
            corridorWidth = Mathf.Max(0.01f, corridorWidth);
            minStraight = Mathf.Max(0f, minStraight);
            corridorWallHeight = Mathf.Max(0.01f, corridorWallHeight);
            placeholderHeight = Mathf.Max(0.01f, placeholderHeight);
        }

        private bool ValidateSettings()
        {
            if (roomCount < 1)
            {
                Debug.LogError("[MapGen] roomCount must be at least 1.");
                return false;
            }

            if (cellSize <= 0f)
            {
                Debug.LogError("[MapGen] cellSize must be greater than 0.");
                return false;
            }

            if (roomPadding < 0f)
            {
                Debug.LogError("[MapGen] roomPadding cannot be negative.");
                return false;
            }

            if (minRoomGap < 1)
            {
                Debug.LogError("[MapGen] minRoomGap must be at least 1.");
                return false;
            }

            if (maxRoomRange < minRoomGap)
            {
                Debug.LogError($"[MapGen] maxRoomRange ({maxRoomRange}) must be greater than or equal to minRoomGap ({minRoomGap}).");
                return false;
            }

            if (cardinalPlacementChance < 0f || cardinalPlacementChance > 1f)
            {
                Debug.LogError("[MapGen] cardinalPlacementChance must be between 0 and 1.");
                return false;
            }

            if (corridorWidth <= 0f)
            {
                Debug.LogError("[MapGen] corridorWidth must be greater than 0.");
                return false;
            }

            if (minStraight < 0f)
            {
                Debug.LogError("[MapGen] minStraight cannot be negative.");
                return false;
            }

            if (corridorWallHeight <= 0f)
            {
                Debug.LogError("[MapGen] corridorWallHeight must be greater than 0.");
                return false;
            }

            if (placeholderHeight <= 0f)
            {
                Debug.LogError("[MapGen] placeholderHeight must be greater than 0.");
                return false;
            }

            if (roomPool == null)
            {
                Debug.LogError("[MapGen] RoomPool is not assigned.");
                return false;
            }

            if (roomPool.rooms == null || roomPool.rooms.Count == 0)
            {
                Debug.LogError("[MapGen] RoomPool is empty.");
                return false;
            }

            int totalWeight = 0;
            for (int i = 0; i < roomPool.rooms.Count; i++)
            {
                RoomConfig config = roomPool.rooms[i];
                if (config == null)
                {
                    Debug.LogError($"[MapGen] RoomPool contains a null room at index {i}.");
                    return false;
                }

                if (config.size.x < 1 || config.size.y < 1)
                {
                    Debug.LogError($"[MapGen] RoomConfig '{config.name}' must have a positive size. Current size: {config.size}.");
                    return false;
                }

                if (config.weight < 1)
                {
                    Debug.LogError($"[MapGen] RoomConfig '{config.name}' must have weight >= 1. Current weight: {config.weight}.");
                    return false;
                }

                totalWeight += config.weight;
            }

            if (totalWeight <= 0)
            {
                Debug.LogError("[MapGen] RoomPool total weight must be greater than 0.");
                return false;
            }

            return true;
        }

        [ContextMenu("Clear Map")]
        public void ClearMap()
        {
            var existing = GameObject.Find("GeneratedMap");
            if (existing != null) DestroyImmediate(existing);
            placedRooms.Clear();
            corridors.Clear();
        }

        private PlacedRoomData PlaceRoom(RoomConfig config, Vector2Int gridPos, Transform parent, int roomIndex)
        {
            Vector3 worldPos = GridToWorldCenter(gridPos);
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
                roomIndex = roomIndex,
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
            Vector3 roomSize = GetRoomWorldSize(config.size);
            go.transform.localScale = new Vector3(roomSize.x, placeholderHeight, roomSize.z);

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

        private bool TryPlaceRoom(RoomConfig config, Transform parent, ref GenerationStats stats, out PlacedRoomData result)
        {
            const int maxAttempts = 60;
            float halfW = corridorWidth * 0.5f;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                stats.candidateAttempts++;
                int anchorIdx = rng.Next(placedRooms.Count);
                var anchor = placedRooms[anchorIdx];
                Vector2Int gridPos = GetCandidateGridCenter(anchor, out bool usedCardinalPlacement);
                if (usedCardinalPlacement)
                    stats.cardinalCandidates++;
                else
                    stats.diagonalCandidates++;

                // Bounds check
                Vector3 center = GridToWorldCenter(gridPos);
                Bounds newBounds = CreateRoomBounds(center, config.size);
                Vector3 paddedHalfSize = newBounds.extents;

                bool overlaps = false;
                foreach (var existing in placedRooms)
                {
                    Bounds eb = CreateRoomBounds(existing.worldPos, existing.size);
                    if (newBounds.Intersects(eb))
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps)
                {
                    stats.roomOverlapRejects++;
                    continue;
                }

                // Check room doesn't overlap existing corridor floors
                overlaps = RoomOverlapsAnyCorridor(center, paddedHalfSize, halfW);
                if (overlaps)
                {
                    stats.corridorOverlapRejects++;
                    continue;
                }

                // Find nearest door pair and create L-shaped corridor
                Vector3 dA = GetClosestDoor(anchor, center);
                Vector3 dB = GetClosestDoor(config, center, anchor.worldPos);

                // Extend outward from each room door before turning
                Vector3 doorNormA = (dA - anchor.worldPos);
                doorNormA.y = 0f;
                doorNormA.Normalize();
                float effectiveMinStraight = GetEffectiveMinStraight();
                Vector3 extA = dA + doorNormA * effectiveMinStraight;

                Vector3 doorNormB = (dB - center);
                doorNormB.y = 0f;
                doorNormB.Normalize();
                Vector3 extB = dB + doorNormB * effectiveMinStraight;

                // Build L-shaped corridor between extended positions
                Vector3 corner1 = new Vector3(extB.x, 0f, extA.z);
                Vector3 corner2 = new Vector3(extA.x, 0f, extB.z);

                float len1 = Vector3.Distance(extA, corner1) + Vector3.Distance(corner1, extB);
                float len2 = Vector3.Distance(extA, corner2) + Vector3.Distance(corner2, extB);
                Vector3 corner = len1 <= len2 ? corner1 : corner2;

                if (corridorCrossingPolicy == CorridorCrossingPolicy.Block && CorridorCrossesAnyExisting(dA, extA, corner, extB, dB))
                {
                    stats.corridorCrossingRejects++;
                    continue;
                }

                // Check corridor doesn't intersect any other room (exclude anchor)
                bool corridorBlocked = false;
                foreach (var existing in placedRooms)
                {
                    if (existing.gridPos == anchor.gridPos && existing.worldPos == anchor.worldPos)
                        continue;
                    if (CorridorIntersectsRoom(dA, extA, corner, extB, dB, halfW, existing))
                    {
                        corridorBlocked = true;
                        break;
                    }
                }
                if (corridorBlocked)
                {
                    stats.corridorBlockedRejects++;
                    continue;
                }

                int newRoomIndex = placedRooms.Count;
                result = PlaceRoom(config, gridPos, parent, newRoomIndex);

                var corridor = BuildCorridor(dA, extA, corner, extB, dB, anchorIdx, newRoomIndex, parent);
                corridors.Add(corridor);

                if (debugLogging)
                {
                    string placementKind = usedCardinalPlacement ? "cardinal" : "diagonal";
                    Debug.Log($"[MapGen] Placed '{config.name}' at grid={gridPos}, world={center}, anchor='{anchor.config.name}', placement={placementKind}, attempt={attempt + 1}/{maxAttempts}.");
                }

                return true;
            }

            if (debugLogging)
                Debug.LogWarning($"[MapGen] Failed to place '{config.name}' after {maxAttempts} attempts.");

            result = default;
            return false;
        }

        private Vector3 GridToWorldCenter(Vector2Int gridPos)
        {
            return new Vector3(gridPos.x * cellSize, 0f, gridPos.y * cellSize);
        }

        private float GetEffectiveMinStraight()
        {
            return Mathf.Max(minStraight, corridorWidth * 0.5f);
        }

        private Vector3 GetRoomWorldSize(Vector2Int roomSize)
        {
            return new Vector3(roomSize.x * cellSize, placeholderHeight, roomSize.y * cellSize);
        }

        private Bounds CreateRoomBounds(Vector3 center, Vector2Int roomSize)
        {
            Vector3 worldSize = GetRoomWorldSize(roomSize);
            worldSize.x += roomPadding * 2f;
            worldSize.z += roomPadding * 2f;
            return new Bounds(center, worldSize);
        }

        private Vector2Int GetCandidateGridCenter(PlacedRoomData anchor, out bool usedCardinalPlacement)
        {
            usedCardinalPlacement = ShouldUseCardinalPlacement();
            if (usedCardinalPlacement)
                return anchor.gridPos + GetRandomCardinalOffset();

            return anchor.gridPos + GetRandomDiagonalOffset();
        }

        private void LogGenerationSummary(GenerationStats stats)
        {
            float successRate = stats.roomRequests > 0
                ? (float)stats.roomSuccesses / stats.roomRequests * 100f
                : 100f;

            Debug.Log(
                $"[MapGen] Generated {placedRooms.Count}/{stats.targetRooms} rooms, {corridors.Count} corridors. " +
                $"Seed={stats.effectiveSeed}, shape={placementShape}, cardinalChance={cardinalPlacementChance:0.##}, " +
                $"success={stats.roomSuccesses}/{stats.roomRequests} ({successRate:0.#}%).");

            if (stats.roomFailures > 0 || debugLogging)
            {
                Debug.Log(
                    $"[MapGen] Placement stats: failures={stats.roomFailures}, attempts={stats.candidateAttempts}, " +
                    $"candidates(cardinal={stats.cardinalCandidates}, diagonal={stats.diagonalCandidates}), " +
                    $"rejects(roomOverlap={stats.roomOverlapRejects}, corridorOverlap={stats.corridorOverlapRejects}, " +
                    $"corridorBlocked={stats.corridorBlockedRejects}, corridorCrossing={stats.corridorCrossingRejects}).");
            }
        }

        private bool ShouldUseCardinalPlacement()
        {
            switch (placementShape)
            {
                case RoomPlacementShape.CardinalOnly:
                    return true;
                case RoomPlacementShape.Mixed:
                    return rng.NextDouble() < cardinalPlacementChance;
                case RoomPlacementShape.DiagonalOnly:
                default:
                    return false;
            }
        }

        private Vector2Int GetRandomDiagonalOffset()
        {
            int dx = GetRandomSignedGridDistance();
            int dz = GetRandomSignedGridDistance();
            return new Vector2Int(dx, dz);
        }

        private Vector2Int GetRandomCardinalOffset()
        {
            int distance = GetRandomSignedGridDistance();
            bool useX = rng.Next(2) == 0;
            return useX ? new Vector2Int(distance, 0) : new Vector2Int(0, distance);
        }

        private int GetRandomSignedGridDistance()
        {
            int distance = rng.Next(minRoomGap, maxRoomRange + 1);
            return distance * (rng.Next(2) == 0 ? 1 : -1);
        }

        private Vector3 GetClosestDoor(PlacedRoomData room, Vector3 target)
        {
            return GetPlaceholderDoorPosition(room, target);
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

        private CorridorData BuildCorridor(Vector3 start, Vector3 extendStart, Vector3 corner, Vector3 extendEnd, Vector3 end,
            int roomAIndex, int roomBIndex, Transform parent)
        {
            return new CorridorData
            {
                roomAIndex = roomAIndex,
                roomBIndex = roomBIndex,
                start = start,
                extendStart = extendStart,
                corner = corner,
                extendEnd = extendEnd,
                end = end,
                width = corridorWidth,
                instance = null
            };
        }

        // ── corridor floor mesh ──

        private void BuildCorridorFloor(Transform parent)
        {
            if (corridors.Count == 0) return;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var vcache = new VertexCache(verts, uvs, normals);
            var wallVerts = new List<Vector3>();
            var wallTris = new List<int>();
            var wallUvs = new List<Vector2>();
            var wallNormals = new List<Vector3>();
            var wallCache = new VertexCache(wallVerts, wallUvs, wallNormals);
            var wallRects = new List<CorridorSegmentRect>();
            var wallBlockers = new List<WallBlockerRect>();
            float bot = corridorFloorY;
            float halfW = corridorWidth * 0.5f;

            for (int ci = 0; ci < corridors.Count; ci++)
            {
                var corr = corridors[ci];
                if (debugLogging)
                    Debug.Log($"[Floor] Corridor {ci}: start={corr.start} extA={corr.extendStart} corner={corr.corner} extB={corr.extendEnd} end={corr.end}");

                BuildCorridorFloorQuads(vcache, tris, corr, halfW, bot);

                // 벽: 짧은 miter edge 방식 (계단식 꺾임 방지)
                if (generateCorridorWalls)
                {
                    AddCorridorWallRects(corr, halfW, wallRects);
                    AddCorridorDoorWallBlockers(corr, halfW, wallBlockers);
                }

            }

            if (generateCorridorWalls)
            {
                AddCorridorWallsFromCorridors(wallCache, wallTris, corridors, wallRects, wallBlockers, halfW, bot, corridorWallHeight);
            }

            if (debugLogging) Debug.Log($"[Floor] Total: {verts.Count} verts, {tris.Count / 3} tris");

            if (verts.Count == 0) return;

            // Fix triangle winding — all must face up (backward merge / door turn quads can invert)
            Vector3[] varr = verts.ToArray();
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = varr[tris[i]];
                Vector3 b = varr[tris[i + 1]];
                Vector3 c = varr[tris[i + 2]];
                if (Vector3.Cross(b - a, c - a).y < 0f)
                {
                    int tmp = tris[i + 1];
                    tris[i + 1] = tris[i + 2];
                    tris[i + 2] = tmp;
                }
            }

            var mesh = new Mesh();
            mesh.vertices = varr;
            mesh.triangles = tris.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateBounds();

            var go = new GameObject("CorridorFloor");
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            if (corridorMaterial != null)
                renderer.sharedMaterial = corridorMaterial;
            else
            {
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(0.4f, 0.4f, 0.4f)
                };
            }

            if (generateCorridorWalls && wallVerts.Count > 0)
                BuildCorridorWallObject(parent, wallVerts, wallTris, wallUvs, wallNormals);
        }

        private void BuildCorridorWallObject(Transform parent, List<Vector3> verts, List<int> tris, List<Vector2> uvs, List<Vector3> normals)
        {
            var mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateBounds();

            var go = new GameObject("CorridorWalls");
            go.transform.SetParent(parent);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            if (corridorWallMaterial != null)
                renderer.sharedMaterial = corridorWallMaterial;
            else
            {
                renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    color = new Color(0.28f, 0.28f, 0.26f)
                };
            }
        }

        // ── vertex dedup cache ──

        private struct VertexCache
        {
            private readonly Dictionary<VertexKey, int> _map;
            private readonly List<Vector3> _verts;
            private readonly List<Vector2> _uvs;
            private readonly List<Vector3> _normals;
            private const float Snap = 0.001f;

            public int Count => _verts.Count;

            public VertexCache(List<Vector3> verts, List<Vector2> uvs, List<Vector3> normals)
            {
                _map = new Dictionary<VertexKey, int>();
                _verts = verts;
                _uvs = uvs;
                _normals = normals;
            }

            public int Add(Vector3 v, Vector2 uv, Vector3 n)
            {
                var key = new VertexKey(
                    (long)Mathf.Round(v.x / Snap),
                    (long)Mathf.Round(v.y / Snap),
                    (long)Mathf.Round(v.z / Snap));
                if (_map.TryGetValue(key, out int idx))
                    return idx;
                idx = _verts.Count;
                _verts.Add(v);
                _uvs.Add(uv);
                _normals.Add(n);
                _map[key] = idx;
                return idx;
            }

            public int AddUnique(Vector3 v, Vector2 uv, Vector3 n)
            {
                int idx = _verts.Count;
                _verts.Add(v);
                _uvs.Add(uv);
                _normals.Add(n);
                return idx;
            }

            private struct VertexKey
            {
                private readonly long _x;
                private readonly long _y;
                private readonly long _z;

                public VertexKey(long x, long y, long z)
                {
                    _x = x;
                    _y = y;
                    _z = z;
                }

                public override bool Equals(object obj)
                {
                    if (!(obj is VertexKey other))
                        return false;

                    return _x == other._x && _y == other._y && _z == other._z;
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = 17;
                        hash = hash * 31 + _x.GetHashCode();
                        hash = hash * 31 + _y.GetHashCode();
                        hash = hash * 31 + _z.GetHashCode();
                        return hash;
                    }
                }
            }
        }

        // ── mesh helpers ──

        private static List<Vector3> SimplifyCorridorPoints(Vector3[] rawPoints)
        {
            var points = new List<Vector3>();
            for (int i = 0; i < rawPoints.Length; i++)
            {
                if (points.Count == 0 || Vector3.Distance(points[^1], rawPoints[i]) >= 0.01f)
                    points.Add(rawPoints[i]);
            }

            for (int i = points.Count - 2; i > 0; i--)
            {
                Vector3 prev = points[i - 1];
                Vector3 current = points[i];
                Vector3 next = points[i + 1];
                if (AreCollinearXZ(prev, current, next))
                    points.RemoveAt(i);
            }

            return points;
        }

        private static bool AreCollinearXZ(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 bc = c - b;
            ab.y = 0f;
            bc.y = 0f;
            if (ab.sqrMagnitude < 0.0001f || bc.sqrMagnitude < 0.0001f)
                return true;

            return Mathf.Abs(Vector3.Cross(ab.normalized, bc.normalized).y) <= 0.001f;
        }

        private static void BuildCorridorFloorQuads(VertexCache cache, List<int> tris,
            CorridorData corr, float halfW, float bot)
        {
            BuildCorridorOutline(corr, halfW, out var left, out var right);

            if (left.Count < 2 || right.Count != left.Count)
                return;

            for (int i = 0; i < left.Count - 1; i++)
            {
                if (Vector3.Distance(left[i], left[i + 1]) < 0.01f
                    || Vector3.Distance(right[i], right[i + 1]) < 0.01f)
                    continue;

                AddFloorQuadCorners(cache, tris, left[i], right[i], right[i + 1], left[i + 1], bot);
            }
        }

        public static void BuildCorridorOutline(CorridorData corridor, float halfWidth,
            out List<Vector3> left, out List<Vector3> right)
        {
            Vector3[] rawPoints =
            {
                corridor.start,
                corridor.extendStart,
                corridor.corner,
                corridor.extendEnd,
                corridor.end
            };
            List<Vector3> simplified = SimplifyCorridorPoints(rawPoints);
            BuildCorridorEdges(simplified, halfWidth, out left, out right);
        }

        // 벽 전용 edge 생성. 꺾임점은 miter로 이어서 계단식 벽을 방지하되,
        // miter 길이를 제한해서 긴 대각선 spike가 생기지 않게 한다.
        private static void BuildCorridorEdges(List<Vector3> points, float halfW,
            out List<Vector3> left, out List<Vector3> right)
        {
            left = new List<Vector3>(points.Count);
            right = new List<Vector3>(points.Count);

            if (points.Count < 2)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                if (i == 0)
                {
                    Vector3 dir = GetSegmentDirection(points[0], points[1]);
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * halfW;
                    left.Add(points[i] - side);
                    right.Add(points[i] + side);
                }
                else if (i == points.Count - 1)
                {
                    Vector3 dir = GetSegmentDirection(points[i - 1], points[i]);
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * halfW;
                    left.Add(points[i] - side);
                    right.Add(points[i] + side);
                }
                else
                {
                    Vector3 prevDir = GetSegmentDirection(points[i - 1], points[i]);
                    Vector3 nextDir = GetSegmentDirection(points[i], points[i + 1]);
                    Vector3 prevSide = Vector3.Cross(Vector3.up, prevDir) * halfW;
                    Vector3 nextSide = Vector3.Cross(Vector3.up, nextDir) * halfW;

                    left.Add(GetClampedOffsetLineIntersection(points[i], prevDir, -prevSide, nextDir, -nextSide, halfW));
                    right.Add(GetClampedOffsetLineIntersection(points[i], prevDir, prevSide, nextDir, nextSide, halfW));
                }
            }
        }

        private static void AddCorridorWallRects(CorridorData corr, float halfW, List<CorridorSegmentRect> rects)
        {
            Vector3[] rawPoints = { corr.start, corr.extendStart, corr.corner, corr.extendEnd, corr.end };
            List<Vector3> points = SimplifyCorridorPoints(rawPoints);

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 from = points[i];
                Vector3 to = points[i + 1];
                if (Vector3.Distance(from, to) <= 0.01f)
                    continue;

                bool horizontal = Mathf.Abs(to.x - from.x) >= Mathf.Abs(to.z - from.z);
                if (horizontal)
                {
                    rects.Add(new CorridorSegmentRect
                    {
                        minX = Mathf.Min(from.x, to.x),
                        maxX = Mathf.Max(from.x, to.x),
                        minZ = from.z - halfW,
                        maxZ = from.z + halfW,
                        isHorizontal = true
                    });
                }
                else
                {
                    rects.Add(new CorridorSegmentRect
                    {
                        minX = from.x - halfW,
                        maxX = from.x + halfW,
                        minZ = Mathf.Min(from.z, to.z),
                        maxZ = Mathf.Max(from.z, to.z),
                        isHorizontal = false
                    });
                }
            }
        }

        private static void AddCorridorDoorWallBlockers(CorridorData corr, float halfW, List<WallBlockerRect> blockers)
        {
            AddDoorWallBlocker(corr.start, corr.extendStart, halfW, blockers);
            AddDoorWallBlocker(corr.end, corr.extendEnd, halfW, blockers);
        }

        private static void AddDoorWallBlocker(Vector3 door, Vector3 corridorPoint, float halfW, List<WallBlockerRect> blockers)
        {
            Vector3 dir = GetSegmentDirection(door, corridorPoint);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Vector3 center = door;
            Vector3 forwardExtent = new Vector3(Mathf.Abs(dir.x), 0f, Mathf.Abs(dir.z)) * 0.05f;
            Vector3 sideExtent = new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z)) * halfW;
            Vector3 extent = forwardExtent + sideExtent;

            blockers.Add(new WallBlockerRect
            {
                minX = center.x - extent.x,
                maxX = center.x + extent.x,
                minZ = center.z - extent.z,
                maxZ = center.z + extent.z
            });
        }

        private static void AddCorridorWallsFromCorridors(VertexCache cache, List<int> tris,
            List<CorridorData> corridorData, List<CorridorSegmentRect> rects, List<WallBlockerRect> blockers,
            float halfW, float floorY, float wallHeight)
        {
            for (int ci = 0; ci < corridorData.Count; ci++)
            {
                CorridorData corr = corridorData[ci];
                Vector3[] rawPoints = { corr.start, corr.extendStart, corr.corner, corr.extendEnd, corr.end };
                List<Vector3> points = SimplifyCorridorPoints(rawPoints);
                BuildCorridorEdges(points, halfW, out var left, out var right);

                AddWallEdgeChain(cache, tris, left, rects, blockers, floorY, wallHeight);
                AddWallEdgeChain(cache, tris, right, rects, blockers, floorY, wallHeight);
            }
        }

        private static void AddWallEdgeChain(VertexCache cache, List<int> tris,
            List<Vector3> points, List<CorridorSegmentRect> rects, List<WallBlockerRect> blockers,
            float floorY, float wallHeight)
        {
            for (int i = 0; i < points.Count - 1; i++)
                AddWallEdgeWithCuts(cache, tris, points[i], points[i + 1], rects, blockers, floorY, wallHeight);
        }

        private static void AddWallEdgeWithCuts(VertexCache cache, List<int> tris,
            Vector3 a, Vector3 b, List<CorridorSegmentRect> rects, List<WallBlockerRect> blockers,
            float floorY, float wallHeight)
        {
            const float tolerance = 0.001f;
            if (Vector3.Distance(a, b) <= 0.01f)
                return;

            bool runsAlongX = Mathf.Abs(b.x - a.x) >= Mathf.Abs(b.z - a.z);
            float fixedCoord = runsAlongX ? a.z : a.x;
            float start = runsAlongX ? a.x : a.z;
            float end = runsAlongX ? b.x : b.z;
            if (end < start)
            {
                float tmp = start;
                start = end;
                end = tmp;
            }

            var blocked = new List<Vector2>();
            for (int i = 0; i < rects.Count; i++)
            {
                CorridorSegmentRect rect = rects[i];
                if (runsAlongX)
                {
                    if (fixedCoord <= rect.minZ + tolerance || fixedCoord >= rect.maxZ - tolerance)
                        continue;

                    float overlapStart = Mathf.Max(start, rect.minX);
                    float overlapEnd = Mathf.Min(end, rect.maxX);
                    if (overlapEnd - overlapStart > tolerance)
                        blocked.Add(new Vector2(overlapStart, overlapEnd));
                }
                else
                {
                    if (fixedCoord <= rect.minX + tolerance || fixedCoord >= rect.maxX - tolerance)
                        continue;

                    float overlapStart = Mathf.Max(start, rect.minZ);
                    float overlapEnd = Mathf.Min(end, rect.maxZ);
                    if (overlapEnd - overlapStart > tolerance)
                        blocked.Add(new Vector2(overlapStart, overlapEnd));
                }
            }

            AddUnblockedWallEdgeSpans(cache, tris, blockers, blocked, runsAlongX, fixedCoord, start, end, floorY, wallHeight);
        }

        private static void AddUnblockedWallEdgeSpans(VertexCache cache, List<int> tris,
            List<WallBlockerRect> blockers, List<Vector2> blocked, bool runsAlongX,
            float fixedCoord, float start, float end, float floorY, float wallHeight)
        {
            const float minSpan = 0.01f;
            blocked.Sort((a, b) => a.x.CompareTo(b.x));

            float cursor = start;
            for (int i = 0; i < blocked.Count; i++)
            {
                float blockStart = Mathf.Clamp(blocked[i].x, start, end);
                float blockEnd = Mathf.Clamp(blocked[i].y, start, end);
                if (blockEnd <= cursor)
                    continue;

                if (blockStart - cursor > minSpan)
                    AddWallSpan(cache, tris, blockers, runsAlongX, fixedCoord, cursor, blockStart, floorY, wallHeight);

                cursor = Mathf.Max(cursor, blockEnd);
            }

            if (end - cursor > minSpan)
                AddWallSpan(cache, tris, blockers, runsAlongX, fixedCoord, cursor, end, floorY, wallHeight);
        }

        private static void AddWallSpan(VertexCache cache, List<int> tris,
            List<WallBlockerRect> blockers, bool runsAlongX, float fixedCoord,
            float start, float end, float floorY, float wallHeight)
        {
            if (runsAlongX)
            {
                AddWallPlaneUnlessBlocked(cache, tris, blockers,
                    new Vector3(start, floorY, fixedCoord),
                    new Vector3(end, floorY, fixedCoord),
                    floorY, wallHeight);
            }
            else
            {
                AddWallPlaneUnlessBlocked(cache, tris, blockers,
                    new Vector3(fixedCoord, floorY, start),
                    new Vector3(fixedCoord, floorY, end),
                    floorY, wallHeight);
            }
        }

        private static bool PointInsideAnyBlockerRect(float x, float z, List<WallBlockerRect> blockers, float tolerance)
        {
            for (int i = 0; i < blockers.Count; i++)
            {
                WallBlockerRect blocker = blockers[i];
                if (x > blocker.minX + tolerance && x < blocker.maxX - tolerance
                    && z > blocker.minZ + tolerance && z < blocker.maxZ - tolerance)
                    return true;
            }

            return false;
        }

        private static void AddWallPlaneUnlessBlocked(VertexCache cache, List<int> tris,
            List<WallBlockerRect> blockers, Vector3 a, Vector3 b, float floorY, float wallHeight)
        {
            Vector3 midpoint = (a + b) * 0.5f;
            if (PointInsideAnyBlockerRect(midpoint.x, midpoint.z, blockers, 0.001f))
                return;

            AddWallPlane(cache, tris, a, b, floorY, wallHeight);
        }

        private static void AddWallPlane(VertexCache cache, List<int> tris,
            Vector3 a, Vector3 b, float floorY, float wallHeight)
        {
            if (Vector3.Distance(a, b) <= 0.01f)
                return;

            Vector3 bottomA = WithY(a, floorY);
            Vector3 bottomB = WithY(b, floorY);
            Vector3 topB = WithY(b, floorY + wallHeight);
            Vector3 topA = WithY(a, floorY + wallHeight);
            AddWallQuad(cache, tris, bottomA, bottomB, topB, topA);
            AddWallQuad(cache, tris, bottomB, bottomA, topA, topB);
        }

        private static Vector3 WithY(Vector3 value, float y)
        {
            value.y = y;
            return value;
        }

        private static void AddQuad(VertexCache cache, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude <= 0.0001f)
                normal = Vector3.up;

            float width = Vector3.Distance(a, b);
            float height = Vector3.Distance(b, c);
            int i0 = cache.Add(a, new Vector2(0f, 0f), normal);
            int i1 = cache.Add(b, new Vector2(width, 0f), normal);
            int i2 = cache.Add(c, new Vector2(width, height), normal);
            int i3 = cache.Add(d, new Vector2(0f, height), normal);

            tris.Add(i0); tris.Add(i2); tris.Add(i1);
            tris.Add(i0); tris.Add(i3); tris.Add(i2);
        }

        private static void AddWallQuad(VertexCache cache, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude <= 0.0001f)
                normal = Vector3.up;

            float width = Vector3.Distance(a, b);
            float height = Vector3.Distance(b, c);
            int i0 = cache.AddUnique(a, new Vector2(0f, 0f), normal);
            int i1 = cache.AddUnique(b, new Vector2(width, 0f), normal);
            int i2 = cache.AddUnique(c, new Vector2(width, height), normal);
            int i3 = cache.AddUnique(d, new Vector2(0f, height), normal);

            tris.Add(i0); tris.Add(i2); tris.Add(i1);
            tris.Add(i0); tris.Add(i3); tris.Add(i2);
        }

        private static Vector3 GetSegmentDirection(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            direction.y = 0f;
            return direction.sqrMagnitude <= 0.0001f ? Vector3.forward : direction.normalized;
        }

        private static Vector3 GetClampedOffsetLineIntersection(Vector3 center,
            Vector3 previousDirection, Vector3 previousOffset,
            Vector3 nextDirection, Vector3 nextOffset, float halfW)
        {
            Vector2 p = new Vector2(center.x + previousOffset.x, center.z + previousOffset.z);
            Vector2 r = new Vector2(previousDirection.x, previousDirection.z);
            Vector2 q = new Vector2(center.x + nextOffset.x, center.z + nextOffset.z);
            Vector2 s = new Vector2(nextDirection.x, nextDirection.z);

            float cross = Cross2D(r, s);
            if (Mathf.Abs(cross) <= 0.001f)
            {
                Vector3 averaged = center + (previousOffset + nextOffset) * 0.5f;
                averaged.y = center.y;
                return averaged;
            }

            Vector2 qMinusP = q - p;
            float t = Cross2D(qMinusP, s) / cross;
            Vector2 intersection = p + r * t;
            Vector3 result = new Vector3(intersection.x, center.y, intersection.y);
            return ClampWallMiter(center, result, halfW);
        }

        private static Vector3 ClampWallMiter(Vector3 center, Vector3 point, float halfW)
        {
            float maxMiterLength = halfW * 1.41421356f + 0.01f;
            Vector3 offset = point - center;
            offset.y = 0f;
            if (offset.sqrMagnitude <= maxMiterLength * maxMiterLength)
                return point;

            Vector3 clamped = center + offset.normalized * maxMiterLength;
            clamped.y = center.y;
            return clamped;
        }

        private static float Cross2D(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static void AddFloorQuad(VertexCache cache, List<int> tris,
            Vector3 from, Vector3 to, Vector3 right, float halfW, float bot)
        {
            if (Vector3.Distance(from, to) < 0.01f)
                return;

            AddFloorQuadCorners(cache, tris,
                from - right * halfW,
                from + right * halfW,
                to + right * halfW,
                to - right * halfW,
                bot);
        }

        private static void AddFloorQuadCorners(VertexCache cache, List<int> tris,
            Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, float bot)
        {
            Vector3 n = Vector3.up;
            float width = Vector3.Distance(bl, br);
            float length = Vector3.Distance(bl, tl);
            int i0 = cache.Add(new Vector3(bl.x, bot, bl.z), new Vector2(0f, 0f), n);
            int i1 = cache.Add(new Vector3(br.x, bot, br.z), new Vector2(width, 0f), n);
            int i2 = cache.Add(new Vector3(tr.x, bot, tr.z), new Vector2(width, length), n);
            int i3 = cache.Add(new Vector3(tl.x, bot, tl.z), new Vector2(0f, length), n);

            tris.Add(i0); tris.Add(i2); tris.Add(i1);
            tris.Add(i0); tris.Add(i3); tris.Add(i2);
        }

        // ── corridor-room intersection ──

        private bool CorridorIntersectsRoom(Vector3 dA, Vector3 extA, Vector3 corner, Vector3 extB, Vector3 dB, float halfW, PlacedRoomData room)
        {
            Vector3 halfExtents = new Vector3(
                room.size.x * cellSize * 0.5f + roomPadding,
                placeholderHeight * 0.5f,
                room.size.y * cellSize * 0.5f + roomPadding);

            float minX = room.worldPos.x - halfExtents.x;
            float maxX = room.worldPos.x + halfExtents.x;
            float minZ = room.worldPos.z - halfExtents.z;
            float maxZ = room.worldPos.z + halfExtents.z;

            Vector3[][] segs = {
                new[] { dA, extA },
                new[] { extA, corner },
                new[] { corner, extB },
                new[] { extB, dB }
            };

            foreach (var seg in segs)
            {
                if (SegCenterlineIntersectsRect(seg[0], seg[1],
                    minX - halfW, minZ - halfW,
                    maxX + halfW, maxZ + halfW))
                    return true;
            }

            return false;
        }

        private bool RoomOverlapsAnyCorridor(Vector3 center, Vector3 halfSize, float halfW)
        {
            float rMinX = center.x - halfSize.x;
            float rMaxX = center.x + halfSize.x;
            float rMinZ = center.z - halfSize.z;
            float rMaxZ = center.z + halfSize.z;

            foreach (var c in corridors)
            {
                Vector3[] pts = { c.start, c.extendStart, c.corner, c.extendEnd, c.end };
                for (int i = 0; i < 4; i++)
                {
                    if (Vector3.Distance(pts[i], pts[i + 1]) < 0.01f) continue;
                    Vector3 dir = (pts[i + 1] - pts[i]).normalized;
                    Vector3 right = Vector3.Cross(Vector3.up, dir);
                    Vector3 bl = pts[i] - right * halfW;
                    Vector3 br = pts[i] + right * halfW;
                    Vector3 tl = pts[i + 1] - right * halfW;
                    Vector3 tr = pts[i + 1] + right * halfW;
                    float qMinX = Mathf.Min(bl.x, br.x, tl.x, tr.x);
                    float qMaxX = Mathf.Max(bl.x, br.x, tl.x, tr.x);
                    float qMinZ = Mathf.Min(bl.z, br.z, tl.z, tr.z);
                    float qMaxZ = Mathf.Max(bl.z, br.z, tl.z, tr.z);
                    if (qMinX < rMaxX && qMaxX > rMinX && qMinZ < rMaxZ && qMaxZ > rMinZ)
                        return true;
                }
            }
            return false;
        }

        private bool CorridorCrossesAnyExisting(Vector3 dA, Vector3 extA, Vector3 corner, Vector3 extB, Vector3 dB)
        {
            Vector3[] newPts = { dA, extA, corner, extB, dB };
            foreach (var existing in corridors)
            {
                Vector3[] existingPts = { existing.start, existing.extendStart, existing.corner, existing.extendEnd, existing.end };
                for (int i = 0; i < newPts.Length - 1; i++)
                {
                    Vector3 a = newPts[i];
                    Vector3 b = newPts[i + 1];
                    if (Vector3.Distance(a, b) < 0.01f)
                        continue;

                    for (int j = 0; j < existingPts.Length - 1; j++)
                    {
                        Vector3 c = existingPts[j];
                        Vector3 d = existingPts[j + 1];
                        if (Vector3.Distance(c, d) < 0.01f)
                            continue;

                        if (SegmentsShareEndpoint(a, b, c, d))
                            continue;

                        if (SegmentsIntersectXZ(a, b, c, d))
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool SegmentsShareEndpoint(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            const float endpointTolerance = 0.01f;
            return DistanceSqXZ(a, c) <= endpointTolerance * endpointTolerance
                || DistanceSqXZ(a, d) <= endpointTolerance * endpointTolerance
                || DistanceSqXZ(b, c) <= endpointTolerance * endpointTolerance
                || DistanceSqXZ(b, d) <= endpointTolerance * endpointTolerance;
        }

        private static bool SegmentsIntersectXZ(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector2 p1 = new Vector2(a.x, a.z);
            Vector2 q1 = new Vector2(b.x, b.z);
            Vector2 p2 = new Vector2(c.x, c.z);
            Vector2 q2 = new Vector2(d.x, d.z);

            float o1 = Orientation(p1, q1, p2);
            float o2 = Orientation(p1, q1, q2);
            float o3 = Orientation(p2, q2, p1);
            float o4 = Orientation(p2, q2, q1);

            if (o1 * o2 < 0f && o3 * o4 < 0f)
                return true;

            return ApproximatelyZero(o1) && PointOnSegment(p2, p1, q1)
                || ApproximatelyZero(o2) && PointOnSegment(q2, p1, q1)
                || ApproximatelyZero(o3) && PointOnSegment(p1, p2, q2)
                || ApproximatelyZero(o4) && PointOnSegment(q1, p2, q2);
        }

        private static float Orientation(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private static bool PointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            const float tolerance = 0.001f;
            return p.x >= Mathf.Min(a.x, b.x) - tolerance
                && p.x <= Mathf.Max(a.x, b.x) + tolerance
                && p.y >= Mathf.Min(a.y, b.y) - tolerance
                && p.y <= Mathf.Max(a.y, b.y) + tolerance;
        }

        private static bool ApproximatelyZero(float value)
        {
            return Mathf.Abs(value) <= 0.001f;
        }

        private static float DistanceSqXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static bool SegCenterlineIntersectsRect(Vector3 from, Vector3 to,
            float rMinX, float rMinZ, float rMaxX, float rMaxZ)
        {
            Vector3 d = to - from;
            if (d.sqrMagnitude < 0.0001f) return false;

            float dx = d.x;
            float dz = d.z;
            float tMin = 0f, tMax = 1f;

            // Liang-Barsky against X slabs
            if (dx != 0f)
            {
                float t1 = (rMinX - from.x) / dx;
                float t2 = (rMaxX - from.x) / dx;
                if (dx < 0f) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            else if (from.x < rMinX || from.x > rMaxX) return false;

            // Liang-Barsky against Z slabs
            if (dz != 0f)
            {
                float t1 = (rMinZ - from.z) / dz;
                float t2 = (rMaxZ - from.z) / dz;
                if (dz < 0f) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return false;
            }
            else if (from.z < rMinZ || from.z > rMaxZ) return false;

            return true;
        }
    }
}
