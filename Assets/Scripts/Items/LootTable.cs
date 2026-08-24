using System;
using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootTable", menuName = "Items/Loot Table")]
    public sealed class LootTable : ScriptableObject
    {
        [SerializeField] private List<LootItemDefinition> items = new List<LootItemDefinition>();
        public IReadOnlyList<LootItemDefinition> Items => items;

        public bool TryRoll(System.Random random, LootZoneProfile zone, int maximumValue,
            out LootItemDefinition item, out int value)
        {
            item = null;
            value = 0;
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (zone == null) return false;

            int totalWeight = 0;
            for (int i = 0; i < items.Count; i++)
            {
                LootItemDefinition candidate = items[i];
                if (IsEligible(candidate, zone, maximumValue))
                    totalWeight += candidate.SpawnWeight * zone.GetRarityWeight(candidate.Rarity);
            }

            if (totalWeight <= 0) return false;

            int roll = random.Next(totalWeight);
            for (int i = 0; i < items.Count; i++)
            {
                LootItemDefinition candidate = items[i];
                if (!IsEligible(candidate, zone, maximumValue)) continue;

                int weight = candidate.SpawnWeight * zone.GetRarityWeight(candidate.Rarity);
                if (roll < weight)
                {
                    item = candidate;
                    value = candidate.RollValue(random, maximumValue);
                    return true;
                }
                roll -= weight;
            }

            return false;
        }

        private static bool IsEligible(LootItemDefinition item, LootZoneProfile zone, int maximumValue)
        {
            if (item == null || item.NetworkPrefab == null || item.BaseValue <= 0) return false;
            if (!item.IsAllowedInZone(zone.ZoneId) || zone.GetRarityWeight(item.Rarity) <= 0) return false;
            int minimumValue = Mathf.Max(0, Mathf.RoundToInt(item.BaseValue * (1f - item.ValueVariance)));
            return minimumValue <= maximumValue;
        }
    }
}
