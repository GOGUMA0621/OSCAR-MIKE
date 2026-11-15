using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Items
{
    /// <summary>
    /// [서버] <see cref="LootTableSurface"/> 테이블들 위에 루트 아이템을 스폰한다.
    /// 네트워크가 켜지면 한 번 자동 스폰하고, <see cref="ServerRespawn"/> 으로 다시 깔 수 있다
    /// (이전에 이 스포너가 놓은 아이템 중 아직 테이블에 남은 것은 제거).
    /// </summary>
    public sealed class TableLootSpawner : MonoBehaviour
    {
        public const int TierCount = 5;

        /// <summary>등급 하나의 가치 티어(1~5) 가중치.</summary>
        [System.Serializable]
        public struct GradeTierWeights
        {
            [HideInInspector] public string label;
            [Min(0)] public int tier1;
            [Min(0)] public int tier2;
            [Min(0)] public int tier3;
            [Min(0)] public int tier4;
            [Min(0)] public int tier5;

            public GradeTierWeights(string label, int t1, int t2, int t3, int t4, int t5)
            {
                this.label = label;
                tier1 = t1; tier2 = t2; tier3 = t3; tier4 = t4; tier5 = t5;
            }

            public int Get(int tier) => tier switch
            {
                1 => tier1, 2 => tier2, 3 => tier3, 4 => tier4, 5 => tier5, _ => 0
            };
        }

        private static GradeTierWeights[] DefaultGradeWeights() => new[]
        {
            new GradeTierWeights("일반", 60, 25, 10, 4, 1),
            new GradeTierWeights("고급", 35, 35, 20, 8, 2),
            new GradeTierWeights("희귀", 15, 30, 30, 18, 7),
            new GradeTierWeights("영웅", 5, 15, 30, 30, 20),
            new GradeTierWeights("전설", 0, 5, 20, 35, 40)
        };

        [Header("루트 데이터")]
        [SerializeField] private LootTable lootTable;
        [Tooltip("선택: 지정하면 아이템의 allowedZoneIds 를 이 존 ID 로 필터링")]
        [SerializeField] private LootZoneProfile zone;
        [Tooltip("아이템 1개 가격 상한")]
        [Min(0)] [SerializeField] private int maxPricePerItem = 100000;

        [Header("등급별 가치 티어 확률 (일반/고급/희귀/영웅/전설 순)")]
        [Tooltip("티어 = 아이템 가치 1~5. 테이블 카테고리에 해당 티어 아이템이 없으면 그 티어는 빼고 다시 비율을 맞춘다.")]
        [SerializeField] private GradeTierWeights[] gradeWeights = DefaultGradeWeights();

        [Header("테이블")]
        [Tooltip("비어 있으면 씬의 모든 LootTableSurface 를 사용")]
        [SerializeField] private LootTableSurface[] tables = System.Array.Empty<LootTableSurface>();

        [Header("배치")]
        [Tooltip("아이템끼리 최소 간격 (발자국 반경에 더해짐, m)")]
        [Min(0f)] [SerializeField] private float itemSpacing = 0.08f;
        [Tooltip("테이블 윗면에서 띄울 높이 (m)")]
        [Min(0f)] [SerializeField] private float surfaceOffset = 0.005f;
        [Min(1)] [SerializeField] private int placementAttempts = 12;
        [SerializeField] private bool spawnOnNetworkStart = true;
        [SerializeField] private bool debugLogging = true;

        private readonly List<NetworkObject> spawnedItems = new List<NetworkObject>();
        private readonly List<Vector3> occupied = new List<Vector3>(); // xz = 위치, y = 반경
        private System.Random random;
        private bool autoSpawned;

        public int LastSpawnCount { get; private set; }
        public int LastSpawnValue { get; private set; }

        private void Awake()
        {
            random = new System.Random(System.Environment.TickCount);
            if (tables == null || tables.Length == 0)
                tables = FindObjectsByType<LootTableSurface>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        private void Update()
        {
            // 씬 로드 이후에 호스트가 시작되는 테스트 씬 대응 (DuffelBagSpawner 와 같은 방식)
            if (autoSpawned || !spawnOnNetworkStart) return;
            if (!CanServerSpawn()) return;
            autoSpawned = true;
            ServerRespawn();
        }

        [ContextMenu("Respawn Table Loot (Server)")]
        public void ServerRespawn()
        {
            if (!CanServerSpawn()) return;
            if (lootTable == null)
            {
                Debug.LogWarning("[TableLootSpawner] lootTable 이 지정되지 않았습니다.");
                return;
            }

            ClearSpawned();

            int count = 0;
            int value = 0;
            foreach (LootTableSurface table in tables)
            {
                if (table == null || !table.isActiveAndEnabled) continue;
                SpawnOnTable(table, ref count, ref value);
            }

            LastSpawnCount = count;
            LastSpawnValue = value;
            if (debugLogging)
                Debug.Log($"[TableLootSpawner] 테이블 {tables.Length}개에 아이템 {count}개 스폰 (총 가치 {value}).");
        }

        private void ClearSpawned()
        {
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                NetworkObject item = spawnedItems[i];
                // 플레이어가 주운 아이템은 이미 디스폰(파괴)되어 있다.
                if (item != null && item.IsSpawned) item.Despawn(true);
            }
            spawnedItems.Clear();
        }

        private void SpawnOnTable(LootTableSurface table, ref int count, ref int value)
        {
            if (!table.TryGetTop(out Vector3 top, out Quaternion tableRotation, out Vector2 half)) return;

            occupied.Clear();
            CollectCandidates(table);
            if (candidates.Count == 0)
            {
                Debug.LogWarning($"[TableLootSpawner] '{table.name}' 에 허용된 카테고리({table.AllowedCategories}) 아이템이 없습니다.", table);
                return;
            }

            int target = table.RollItemCount(random);
            int placed = 0;
            for (int n = 0; n < target; n++)
            {
                if (!TryRollForTable(table.Grade, out LootItemDefinition definition, out byte valueSteps, out int price))
                    continue;

                Quaternion rotation = tableRotation * Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                NetworkObject instance = Instantiate(definition.NetworkPrefab, top, rotation);
                NetworkLootItem lootItem = instance.GetComponent<NetworkLootItem>();
                if (lootItem == null)
                {
                    Debug.LogError($"[TableLootSpawner] '{definition.NetworkPrefab.name}' 에 NetworkLootItem 이 없습니다.");
                    Destroy(instance.gameObject);
                    continue;
                }

                if (!TryPlace(instance.transform, top, tableRotation, half))
                {
                    Destroy(instance.gameObject); // 자리가 없으면 건너뛴다
                    continue;
                }

                lootItem.InitializeServer(definition.ItemId, valueSteps, definition.Category, price);
                instance.Spawn(true);
                spawnedItems.Add(instance);
                count++;
                placed++;
                value += price;
            }

            if (debugLogging)
                Debug.Log($"[TableLootSpawner] '{table.name}' 등급={table.Grade} 카테고리={(table.AllowedCategories == 0 ? "전체" : table.AllowedCategories.ToString())} → {placed}개", table);
        }

        // ── 등급 롤 ─────────────────────────────────────────
        private readonly List<LootItemDefinition> candidates = new List<LootItemDefinition>();
        private readonly int[] tierWeightBuffer = new int[TierCount + 1];

        private void CollectCandidates(LootTableSurface table)
        {
            candidates.Clear();
            IReadOnlyList<LootItemDefinition> items = lootTable.Items;
            for (int i = 0; i < items.Count; i++)
            {
                LootItemDefinition item = items[i];
                if (item == null || !item.SpawnEnabled || item.NetworkPrefab == null) continue;
                if (!table.AllowsCategory(item.Category)) continue;
                if (zone != null && !item.IsAllowedInZone(zone.ZoneId)) continue;
                if (LootPriceCalculator.GetMinimumPrice(item) > maxPricePerItem) continue;
                candidates.Add(item);
            }
        }

        /// <summary>
        /// 1) 후보가 있는 티어 중 등급 가중치로 티어 선택 → 2) 그 티어를 낼 수 있는 아이템을 SpawnWeight 로 선택
        /// → 3) 아이템 가치 범위 ∩ 티어 범위에서 valueSteps 선택. 후보가 있으면 실패하지 않는다.
        /// </summary>
        private bool TryRollForTable(LootTableGrade grade, out LootItemDefinition item, out byte valueSteps, out int price)
        {
            item = null;
            valueSteps = 0;
            price = 0;
            if (candidates.Count == 0) return false;

            GradeTierWeights weights = GetGradeWeights(grade);
            int total = 0;
            for (int tier = 1; tier <= TierCount; tier++)
            {
                int w = HasCandidateForTier(tier) ? Mathf.Max(0, weights.Get(tier)) : 0;
                tierWeightBuffer[tier] = w;
                total += w;
            }

            int chosenTier;
            if (total > 0)
            {
                int roll = random.Next(total);
                chosenTier = TierCount;
                for (int tier = 1; tier <= TierCount; tier++)
                {
                    if (roll < tierWeightBuffer[tier]) { chosenTier = tier; break; }
                    roll -= tierWeightBuffer[tier];
                }
            }
            else
            {
                // 가중치 > 0 인 티어에 후보가 전혀 없으면 (예: 전설 + 열쇠만) 후보가 있는 가장 높은 티어로 대체
                chosenTier = HighestAvailableTier();
            }

            int itemTotal = 0;
            for (int i = 0; i < candidates.Count; i++)
                if (CanProduceTier(candidates[i], chosenTier)) itemTotal += candidates[i].SpawnWeight;
            if (itemTotal <= 0) return false;

            int itemRoll = random.Next(itemTotal);
            for (int i = 0; i < candidates.Count; i++)
            {
                LootItemDefinition candidate = candidates[i];
                if (!CanProduceTier(candidate, chosenTier)) continue;
                if (itemRoll < candidate.SpawnWeight) { item = candidate; break; }
                itemRoll -= candidate.SpawnWeight;
            }
            if (item == null) return false;

            GetTierStepRange(chosenTier, out int tierMin, out int tierMax);
            int min = Mathf.Max(item.MinValueSteps, tierMin);
            int max = Mathf.Min(item.MaxValueSteps, tierMax);
            valueSteps = (byte)random.Next(min, max + 1);
            price = LootPriceCalculator.RollPrice(item, random, maxPricePerItem);
            return true;
        }

        private GradeTierWeights GetGradeWeights(LootTableGrade grade)
        {
            int index = (int)grade;
            if (gradeWeights != null && index < gradeWeights.Length) return gradeWeights[index];
            return DefaultGradeWeights()[Mathf.Clamp(index, 0, TierCount - 1)];
        }

        private bool HasCandidateForTier(int tier)
        {
            for (int i = 0; i < candidates.Count; i++)
                if (CanProduceTier(candidates[i], tier)) return true;
            return false;
        }

        private int HighestAvailableTier()
        {
            for (int tier = TierCount; tier >= 1; tier--)
                if (HasCandidateForTier(tier)) return tier;
            return 1;
        }

        /// <summary>티어 t = valueSteps (2t-1)~(2t), 즉 가치 (t-0.5)~t. 2~10 으로 클램프.</summary>
        private static void GetTierStepRange(int tier, out int min, out int max)
        {
            min = Mathf.Clamp(tier * 2 - 1, 2, 10);
            max = Mathf.Clamp(tier * 2, 2, 10);
        }

        private static bool CanProduceTier(LootItemDefinition item, int tier)
        {
            GetTierStepRange(tier, out int min, out int max);
            return item.MinValueSteps <= max && item.MaxValueSteps >= min;
        }

        /// <summary>디버그: 등급·카테고리로 n번 굴린 티어별 횟수 (index 1~5). 서버 없이 에디터에서도 동작.</summary>
        public int[] SimulateTierCounts(LootTableSurface table, int rolls)
        {
            var counts = new int[TierCount + 1];
            if (lootTable == null || table == null) return counts;
            if (random == null) random = new System.Random(1234);
            CollectCandidates(table);
            for (int i = 0; i < rolls; i++)
            {
                if (!TryRollForTable(table.Grade, out LootItemDefinition item, out byte steps, out _)) continue;
                if (!table.AllowsCategory(item.Category)) counts[0]++; // 0 = 카테고리 위반 (있으면 안 됨)
                else counts[(steps + 1) / 2]++;
            }
            return counts;
        }

        /// <summary>테이블 윗면 안의 빈 자리를 찾아 바닥면이 윗면에 닿도록 놓는다.</summary>
        private bool TryPlace(Transform item, Vector3 top, Quaternion tableRotation, Vector2 half)
        {
            if (!TryGetBounds(item, out Bounds bounds)) bounds = new Bounds(item.position, Vector3.one * 0.2f);
            Vector3 pivotToCenter = bounds.center - item.position;
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);

            float rangeX = Mathf.Max(0f, half.x - radius);
            float rangeZ = Mathf.Max(0f, half.y - radius);

            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                Vector3 local = new Vector3(
                    Mathf.Lerp(-rangeX, rangeX, (float)random.NextDouble()), 0f,
                    Mathf.Lerp(-rangeZ, rangeZ, (float)random.NextDouble()));
                Vector3 desiredCenter = top + tableRotation * local;
                if (Overlaps(desiredCenter, radius)) continue;

                Vector3 position = desiredCenter - new Vector3(pivotToCenter.x, 0f, pivotToCenter.z);
                // 바닥(min.y)을 윗면 높이에 맞춘다.
                position.y = top.y + surfaceOffset + (item.position.y - bounds.min.y);
                item.position = position;
                occupied.Add(new Vector3(desiredCenter.x, radius, desiredCenter.z));
                return true;
            }
            return false;
        }

        private bool Overlaps(Vector3 center, float radius)
        {
            for (int i = 0; i < occupied.Count; i++)
            {
                Vector3 o = occupied[i];
                float dx = o.x - center.x;
                float dz = o.z - center.z;
                float min = o.y + radius + itemSpacing;
                if (dx * dx + dz * dz < min * min) return true;
            }
            return false;
        }

        private static bool TryGetBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (has) return true;
            foreach (Collider c in root.GetComponentsInChildren<Collider>())
            {
                if (!has) { bounds = c.bounds; has = true; }
                else bounds.Encapsulate(c.bounds);
            }
            return has;
        }

        private static bool CanServerSpawn()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        private void OnValidate()
        {
            maxPricePerItem = Mathf.Max(0, maxPricePerItem);
            itemSpacing = Mathf.Max(0f, itemSpacing);
            surfaceOffset = Mathf.Max(0f, surfaceOffset);
            placementAttempts = Mathf.Max(1, placementAttempts);

            // 등급 5개 고정, 라벨은 인스펙터 요소 이름용
            GradeTierWeights[] defaults = DefaultGradeWeights();
            if (gradeWeights == null || gradeWeights.Length != defaults.Length)
            {
                var resized = new GradeTierWeights[defaults.Length];
                for (int i = 0; i < resized.Length; i++)
                    resized[i] = gradeWeights != null && i < gradeWeights.Length ? gradeWeights[i] : defaults[i];
                gradeWeights = resized;
            }
            for (int i = 0; i < gradeWeights.Length; i++) gradeWeights[i].label = defaults[i].label;
        }
    }
}
