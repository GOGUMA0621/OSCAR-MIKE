using System.Collections.Generic;
using OskarMike.MapGeneration;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Items
{
    public sealed class NetworkLootSpawner : MonoBehaviour
    {
        private struct SpawnCandidate
        {
            public Vector3 position;
            public Quaternion rotation;
            public LootZoneProfile zone;
            public int weight;
        }

        [Header("References")]
        [SerializeField] private ProceduralMapGenerator mapGenerator;
        [SerializeField] private LootTable lootTable;
        [SerializeField] private LootZoneProfile fallbackZone;
        [SerializeField] private LootEconomyProfile economyProfile;

        [Header("Prototype Fallback")]
        [Tooltip("방 프리팹에 LootSpawnPoint가 없을 때 테스트용 위치를 계산합니다.")]
        [SerializeField] private bool generateFallbackPoints = true;
        [Min(0)] [SerializeField] private int fallbackPointsPerRoom = 4;
        [Min(0f)] [SerializeField] private float fallbackWallInset = 1.25f;
        [Min(0f)] [SerializeField] private float floorOffset = 0.2f;
        [SerializeField] private bool debugLogging;

        private bool spawned;

        private void Awake()
        {
            if (mapGenerator == null)
                mapGenerator = FindFirstObjectByType<ProceduralMapGenerator>();
        }

        private void OnEnable()
        {
            if (mapGenerator != null) mapGenerator.MapGenerated += HandleMapGenerated;
        }

        private void Start()
        {
            if (mapGenerator != null && mapGenerator.PlacedRooms.Count > 0) TrySpawnAll();
        }

        private void OnDisable()
        {
            if (mapGenerator != null) mapGenerator.MapGenerated -= HandleMapGenerated;
        }

        private void HandleMapGenerated() => TrySpawnAll();

        [ContextMenu("Spawn Loot (Server)")]
        public void TrySpawnAll()
        {
            if (spawned || !CanServerSpawn()) return;
            if (mapGenerator == null || lootTable == null || economyProfile == null)
            {
                Debug.LogWarning("[NetworkLootSpawner] Map generator, loot table, or economy profile is not assigned.");
                return;
            }

            var random = new System.Random(mapGenerator.EffectiveSeed ^ unchecked((int)0x51ED270B));
            List<SpawnCandidate> candidates = CollectCandidates(random);
            if (candidates.Count == 0)
            {
                Debug.LogWarning("[NetworkLootSpawner] No loot spawn points are available.");
                return;
            }

            spawned = true;
            int totalValue = 0;
            int spawnedCount = 0;
            int targetBudget = economyProfile.TotalPriceBudget;
            int lowerTarget = Mathf.RoundToInt(targetBudget * (1f - economyProfile.BudgetTolerance));
            int upperTarget = Mathf.RoundToInt(targetBudget * (1f + economyProfile.BudgetTolerance));

            while (candidates.Count > 0 && totalValue < lowerTarget)
            {
                int candidateIndex = PickCandidateIndex(candidates, random);
                SpawnCandidate candidate = candidates[candidateIndex];
                candidates.RemoveAt(candidateIndex);

                int remaining = Mathf.Max(0, upperTarget - totalValue);
                if (!lootTable.TryRoll(random, candidate.zone, economyProfile, remaining,
                        out LootItemDefinition definition, out byte valueSteps, out int value))
                    continue;

                NetworkObject instance = Instantiate(definition.NetworkPrefab, candidate.position, candidate.rotation);
                NetworkLootItem lootItem = instance.GetComponent<NetworkLootItem>();
                if (lootItem == null)
                {
                    Debug.LogError($"[NetworkLootSpawner] Prefab '{definition.NetworkPrefab.name}' needs NetworkLootItem.");
                    Destroy(instance.gameObject);
                    continue;
                }

                lootItem.InitializeServer(definition.ItemId, valueSteps, definition.Category, value);
                instance.Spawn(true);
                totalValue += value;
                spawnedCount++;
            }

            if (debugLogging || totalValue < lowerTarget)
            {
                Debug.Log($"[NetworkLootSpawner] Spawned {spawnedCount} items, value={totalValue}, " +
                          $"target={targetBudget} (allowed {lowerTarget}-{upperTarget}).");
            }
        }

        private List<SpawnCandidate> CollectCandidates(System.Random random)
        {
            var result = new List<SpawnCandidate>();
            for (int roomIndex = 0; roomIndex < mapGenerator.PlacedRooms.Count; roomIndex++)
            {
                ProceduralMapGenerator.PlacedRoomData room = mapGenerator.PlacedRooms[roomIndex];
                LootZoneProfile zone = room.config != null && room.config.lootZone != null
                    ? room.config.lootZone
                    : fallbackZone;
                if (zone == null) continue;

                LootSpawnPoint[] points = room.instance != null
                    ? room.instance.GetComponentsInChildren<LootSpawnPoint>(true)
                    : System.Array.Empty<LootSpawnPoint>();
                for (int i = 0; i < points.Length; i++)
                {
                    result.Add(new SpawnCandidate
                    {
                        position = points[i].transform.position,
                        rotation = points[i].transform.rotation,
                        zone = zone,
                        weight = zone.BudgetWeight * points[i].SelectionWeight
                    });
                }

                if (points.Length == 0 && generateFallbackPoints)
                    AddFallbackCandidates(result, mapGenerator.GetRoomBounds(roomIndex), room.worldPos.y, zone, random);
            }
            return result;
        }

        private void AddFallbackCandidates(List<SpawnCandidate> candidates, Bounds bounds, float floorY,
            LootZoneProfile zone, System.Random random)
        {
            float minX = bounds.min.x + fallbackWallInset;
            float maxX = bounds.max.x - fallbackWallInset;
            float minZ = bounds.min.z + fallbackWallInset;
            float maxZ = bounds.max.z - fallbackWallInset;
            if (minX > maxX || minZ > maxZ) return;

            for (int i = 0; i < fallbackPointsPerRoom; i++)
            {
                candidates.Add(new SpawnCandidate
                {
                    position = new Vector3(
                        Mathf.Lerp(minX, maxX, (float)random.NextDouble()),
                        floorY + floorOffset,
                        Mathf.Lerp(minZ, maxZ, (float)random.NextDouble())),
                    rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f),
                    zone = zone,
                    weight = zone.BudgetWeight
                });
            }
        }

        private static int PickCandidateIndex(List<SpawnCandidate> candidates, System.Random random)
        {
            int totalWeight = 0;
            for (int i = 0; i < candidates.Count; i++) totalWeight += Mathf.Max(1, candidates[i].weight);
            int roll = random.Next(totalWeight);
            for (int i = 0; i < candidates.Count; i++)
            {
                int weight = Mathf.Max(1, candidates[i].weight);
                if (roll < weight) return i;
                roll -= weight;
            }
            return candidates.Count - 1;
        }

        private static bool CanServerSpawn()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        private void OnValidate()
        {
            fallbackPointsPerRoom = Mathf.Max(0, fallbackPointsPerRoom);
            fallbackWallInset = Mathf.Max(0f, fallbackWallInset);
            floorOffset = Mathf.Max(0f, floorOffset);
        }
    }
}
