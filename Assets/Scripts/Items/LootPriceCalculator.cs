using UnityEngine;

namespace OskarMike.Items
{
    public static class LootPriceCalculator
    {
        public static int GetMinimumPrice(LootItemDefinition item)
        {
            return Mathf.Max(0, Mathf.RoundToInt(item.BasePrice * (1f - item.PriceVariance)));
        }

        public static int RollPrice(LootItemDefinition item, System.Random random, int maximumPrice)
        {
            int min = GetMinimumPrice(item);
            int max = Mathf.Max(min, Mathf.RoundToInt(item.BasePrice * (1f + item.PriceVariance)));
            max = Mathf.Min(max, Mathf.Max(min, maximumPrice));
            return random.Next(min, max + 1);
        }
    }
}
