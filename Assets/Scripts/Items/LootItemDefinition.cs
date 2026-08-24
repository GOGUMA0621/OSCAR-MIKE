using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootItem", menuName = "Items/Loot Item Definition")]
    public sealed class LootItemDefinition : ScriptableObject
    {
        [SerializeField] private string itemId = "loot_item";
        [SerializeField] private string displayName = "아이템";
        [SerializeField] private LootRarity rarity = LootRarity.Common;
        [Min(0)] [SerializeField] private int baseValue = 20;
        [Range(0f, 1f)] [SerializeField] private float valueVariance = 0.15f;
        [Min(1)] [SerializeField] private int spawnWeight = 1;
        [Tooltip("비어 있으면 모든 지역에서 생성 가능합니다.")]
        [SerializeField] private List<string> allowedZoneIds = new List<string>();
        [SerializeField] private NetworkObject networkPrefab;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public LootRarity Rarity => rarity;
        public int BaseValue => baseValue;
        public float ValueVariance => valueVariance;
        public int SpawnWeight => spawnWeight;
        public NetworkObject NetworkPrefab => networkPrefab;

        public bool IsAllowedInZone(string zoneId)
        {
            if (allowedZoneIds == null || allowedZoneIds.Count == 0)
                return true;

            for (int i = 0; i < allowedZoneIds.Count; i++)
            {
                if (string.Equals(allowedZoneIds[i], zoneId, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public int RollValue(System.Random random, int maximumValue)
        {
            float variance = Mathf.Clamp01(valueVariance);
            int min = Mathf.Max(0, Mathf.RoundToInt(baseValue * (1f - variance)));
            int max = Mathf.Max(min, Mathf.RoundToInt(baseValue * (1f + variance)));
            max = Mathf.Min(max, Mathf.Max(min, maximumValue));
            return random.Next(min, max + 1);
        }

        private void OnValidate()
        {
            itemId = string.IsNullOrWhiteSpace(itemId) ? name : itemId.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
            baseValue = Mathf.Max(0, baseValue);
            spawnWeight = Mathf.Max(1, spawnWeight);
        }
    }
}
