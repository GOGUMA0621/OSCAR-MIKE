using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootEconomy", menuName = "Items/Loot Economy Profile")]
    public sealed class LootEconomyProfile : ScriptableObject
    {
        [Min(0)] [SerializeField] private int totalPriceBudget = 1000;
        [Range(0f, 0.5f)] [SerializeField] private float budgetTolerance = 0.1f;
        [Min(0)] [SerializeField] private int value1Weight = 60;
        [Min(0)] [SerializeField] private int value2Weight = 25;
        [Min(0)] [SerializeField] private int value3Weight = 10;
        [Min(0)] [SerializeField] private int value4Weight = 4;
        [Min(0)] [SerializeField] private int value5Weight = 1;

        public int TotalPriceBudget => totalPriceBudget;
        public float BudgetTolerance => budgetTolerance;

        public int GetValueWeight(byte valueSteps)
        {
            int clamped = Mathf.Clamp(valueSteps, 2, 10);
            int lowerTier = clamped / 2;
            if ((clamped & 1) == 0) return GetIntegerTierWeight(lowerTier) * 2;
            return GetIntegerTierWeight(lowerTier) + GetIntegerTierWeight(lowerTier + 1);
        }

        private int GetIntegerTierWeight(int tier)
        {
            return tier switch
            {
                1 => value1Weight,
                2 => value2Weight,
                3 => value3Weight,
                4 => value4Weight,
                _ => value5Weight
            };
        }

        private void OnValidate()
        {
            totalPriceBudget = Mathf.Max(0, totalPriceBudget);
            value1Weight = Mathf.Max(0, value1Weight);
            value2Weight = Mathf.Max(0, value2Weight);
            value3Weight = Mathf.Max(0, value3Weight);
            value4Weight = Mathf.Max(0, value4Weight);
            value5Weight = Mathf.Max(0, value5Weight);
        }
    }
}
