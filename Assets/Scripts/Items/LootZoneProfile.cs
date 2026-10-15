using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootZone", menuName = "Items/Loot Zone Profile")]
    public sealed class LootZoneProfile : ScriptableObject
    {
        [SerializeField] private string zoneId = "default";
        [Min(1)] [SerializeField] private int budgetWeight = 1;
        [Min(0)] [SerializeField] private int industrialWeight = 1;
        [Min(0)] [SerializeField] private int electronicsWeight = 1;
        [Min(0)] [SerializeField] private int junkWeight = 1;
        [Min(0)] [SerializeField] private int valuablesWeight = 1;
        [Min(0)] [SerializeField] private int militaryWeight = 1;
        [Min(0)] [SerializeField] private int intelWeight = 1;
        [Min(0)] [SerializeField] private int consumablesWeight = 1;
        [Min(0)] [SerializeField] private int keyWeight = 1;
        [Min(0)] [SerializeField] private int drugsWeight = 1;
        [SerializeField] private bool overrideValueWeights;
        [SerializeField] private LootEconomyProfile valueWeightOverride;

        public string ZoneId => zoneId;
        public int BudgetWeight => budgetWeight;

        public int GetCategoryWeight(LootCategory category)
        {
            return category switch
            {
                LootCategory.Industrial => industrialWeight,
                LootCategory.Electronics => electronicsWeight,
                LootCategory.Junk => junkWeight,
                LootCategory.Valuables => valuablesWeight,
                LootCategory.Military => militaryWeight,
                LootCategory.Intel => intelWeight,
                LootCategory.Consumables => consumablesWeight,
                LootCategory.Key => keyWeight,
                LootCategory.Drugs => drugsWeight,
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
            industrialWeight = Mathf.Max(0, industrialWeight);
            electronicsWeight = Mathf.Max(0, electronicsWeight);
            junkWeight = Mathf.Max(0, junkWeight);
            valuablesWeight = Mathf.Max(0, valuablesWeight);
            militaryWeight = Mathf.Max(0, militaryWeight);
            intelWeight = Mathf.Max(0, intelWeight);
            consumablesWeight = Mathf.Max(0, consumablesWeight);
            keyWeight = Mathf.Max(0, keyWeight);
            drugsWeight = Mathf.Max(0, drugsWeight);
        }
    }
}
