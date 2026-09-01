using System;
using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootTable", menuName = "Items/Loot Table")]
    public sealed class LootTable : ScriptableObject
    {
        private const int MaxRollAttempts = 16;
        [SerializeField] private List<LootItemDefinition> items = new List<LootItemDefinition>();
        public IReadOnlyList<LootItemDefinition> Items => items;

        public bool TryRoll(System.Random random, LootZoneProfile zone, LootEconomyProfile economy,
            int maximumPrice, out LootItemDefinition item, out byte valueSteps, out int price)
        {
            item = null;
            valueSteps = 0;
            price = 0;
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (zone == null || economy == null || maximumPrice < 0) return false;

            for (int attempt = 0; attempt < MaxRollAttempts; attempt++)
            {
                LootCategory category = RollCategory(random, zone);
                byte targetValue = RollValueSteps(random, zone, economy);
                if (targetValue == 0) return false;
                if (!TryPickItem(random, zone, category, targetValue, maximumPrice, out item)) continue;

                valueSteps = targetValue;
                price = LootPriceCalculator.RollPrice(item, random, maximumPrice);
                return true;
            }

            return false;
        }

        private static LootCategory RollCategory(System.Random random, LootZoneProfile zone)
        {
            int total = 0;
            for (byte i = 0; i < 9; i++) total += zone.GetCategoryWeight((LootCategory)i);
            if (total <= 0) return LootCategory.Junk;
            int roll = random.Next(total);
            for (byte i = 0; i < 9; i++)
            {
                int weight = zone.GetCategoryWeight((LootCategory)i);
                if (roll < weight) return (LootCategory)i;
                roll -= weight;
            }
            return LootCategory.Junk;
        }

        private static byte RollValueSteps(System.Random random, LootZoneProfile zone, LootEconomyProfile economy)
        {
            int total = 0;
            for (byte steps = 2; steps <= 10; steps++) total += zone.GetValueWeight(steps, economy);
            if (total <= 0) return 0;
            int roll = random.Next(total);
            for (byte steps = 2; steps <= 10; steps++)
            {
                int weight = zone.GetValueWeight(steps, economy);
                if (roll < weight) return steps;
                roll -= weight;
            }
            return 0;
        }

        private bool TryPickItem(System.Random random, LootZoneProfile zone, LootCategory category,
            byte valueSteps, int maximumPrice, out LootItemDefinition result)
        {
            result = null;
            int totalWeight = 0;
            for (int i = 0; i < items.Count; i++)
            {
                LootItemDefinition candidate = items[i];
                if (IsEligible(candidate, zone, category, valueSteps, maximumPrice)) totalWeight += candidate.SpawnWeight;
            }
            if (totalWeight <= 0) return false;

            int roll = random.Next(totalWeight);
            for (int i = 0; i < items.Count; i++)
            {
                LootItemDefinition candidate = items[i];
                if (!IsEligible(candidate, zone, category, valueSteps, maximumPrice)) continue;
                if (roll < candidate.SpawnWeight)
                {
                    result = candidate;
                    return true;
                }
                roll -= candidate.SpawnWeight;
            }
            return false;
        }

        private static bool IsEligible(LootItemDefinition item, LootZoneProfile zone,
            LootCategory category, byte valueSteps, int maximumPrice)
        {
            return item != null
                && item.SpawnEnabled
                && item.NetworkPrefab != null
                && item.Category == category
                && item.ContainsValue(valueSteps)
                && item.IsAllowedInZone(zone.ZoneId)
                && LootPriceCalculator.GetMinimumPrice(item) <= maximumPrice;
        }
    }
}
