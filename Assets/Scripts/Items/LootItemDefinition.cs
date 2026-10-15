using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootItem", menuName = "Items/Loot Item Definition")]
    public sealed class LootItemDefinition : ScriptableObject
    {
        [SerializeField] private string itemId = "loot_item";
        [SerializeField] private string displayName = "아이템";
        [SerializeField] private string sourceAssetName;
        [SerializeField] private LootContentPack contentPack;
        [SerializeField] private LootCategory category = LootCategory.Junk;
        [Tooltip("실제 밸류의 2배입니다. 1점=2, 5점=10")]
        [Range(2, 10)] [SerializeField] private byte minValueSteps = 2;
        [Range(2, 10)] [SerializeField] private byte maxValueSteps = 2;
        [Min(0)] [SerializeField] private int basePrice = 25;
        [Range(0f, 1f)] [SerializeField] private float priceVariance = 0.15f;
        [Min(1)] [SerializeField] private int spawnWeight = 1;
        [SerializeField] private bool spawnEnabled;
        [SerializeField] private List<string> allowedZoneIds = new List<string>();
        [TextArea] [SerializeField] private string notes;
        [SerializeField] private NetworkObject networkPrefab;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string SourceAssetName => sourceAssetName;
        public LootContentPack ContentPack => contentPack;
        public LootCategory Category => category;
        public byte MinValueSteps => minValueSteps;
        public byte MaxValueSteps => maxValueSteps;
        public float MinValue => minValueSteps * 0.5f;
        public float MaxValue => maxValueSteps * 0.5f;
        public int BasePrice => basePrice;
        public float PriceVariance => priceVariance;
        public int SpawnWeight => spawnWeight;
        public bool SpawnEnabled => spawnEnabled;
        public string Notes => notes;
        public NetworkObject NetworkPrefab => networkPrefab;

        public bool ContainsValue(byte valueSteps) => valueSteps >= minValueSteps && valueSteps <= maxValueSteps;

        public bool IsAllowedInZone(string zoneId)
        {
            if (allowedZoneIds == null || allowedZoneIds.Count == 0) return true;
            for (int i = 0; i < allowedZoneIds.Count; i++)
            {
                if (string.Equals(allowedZoneIds[i], zoneId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void OnValidate()
        {
            itemId = string.IsNullOrWhiteSpace(itemId) ? name : itemId.Trim().ToLowerInvariant();
            displayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim();
            sourceAssetName = sourceAssetName?.Trim() ?? string.Empty;
            minValueSteps = (byte)Mathf.Clamp(minValueSteps, 2, 10);
            maxValueSteps = (byte)Mathf.Clamp(maxValueSteps, minValueSteps, 10);
            basePrice = Mathf.Max(0, basePrice);
            spawnWeight = Mathf.Max(1, spawnWeight);
        }
    }
}
