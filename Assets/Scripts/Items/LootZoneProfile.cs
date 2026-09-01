using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootZone", menuName = "Items/Loot Zone Profile")]
    public sealed class LootZoneProfile : ScriptableObject
    {
        [SerializeField] private string zoneId = "default";
        [Min(1)] [SerializeField] private int budgetWeight = 1;
        [Min(0)] [SerializeField] private int junkWeight = 1;
        [Min(0)] [SerializeField] private int industrialWeight = 1;
        [Min(0)] [SerializeField] private int militaryWeight = 1;
        [Min(0)] [SerializeField] private int suppliesWeight = 1;
        [Min(0)] [SerializeField] private int specialWeight = 1;
        [SerializeField] private bool overrideValueWeights;
        [SerializeField] private LootEconomyProfile valueWeightOverride;

        public string ZoneId => zoneId;
        public int BudgetWeight => budgetWeight;

        public int GetUsageWeight(LootUsageCategory usage)
        {
            return usage switch
            {
                LootUsageCategory.Junk => junkWeight,
                LootUsageCategory.Industrial => industrialWeight,
                LootUsageCategory.Military => militaryWeight,
                LootUsageCategory.Supplies => suppliesWeight,
                LootUsageCategory.Special => specialWeight,
                _ => 0
            };
        }

        public int GetValueWeight(byte valueSteps, LootEconomyProfile fallback)
        {
            LootEconomyProfile profile = overrideValueWeights && valueWeightOverride != null
                ? valueWeightOverride
                : fallback;
            return profile != null ? profile.GetValueWeight(valueSteps) : 0;
        }

        private void OnValidate()
        {
            zoneId = string.IsNullOrWhiteSpace(zoneId) ? name : zoneId.Trim();
            budgetWeight = Mathf.Max(1, budgetWeight);
            junkWeight = Mathf.Max(0, junkWeight);
            industrialWeight = Mathf.Max(0, industrialWeight);
            militaryWeight = Mathf.Max(0, militaryWeight);
            suppliesWeight = Mathf.Max(0, suppliesWeight);
            specialWeight = Mathf.Max(0, specialWeight);
        }
    }
}
