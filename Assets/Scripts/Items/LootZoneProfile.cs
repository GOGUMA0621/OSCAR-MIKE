using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootZone", menuName = "Items/Loot Zone Profile")]
    public sealed class LootZoneProfile : ScriptableObject
    {
        [SerializeField] private string zoneId = "default";
        [Min(1)] [SerializeField] private int budgetWeight = 1;
        [Min(0)] [SerializeField] private int commonWeight = 70;
        [Min(0)] [SerializeField] private int uncommonWeight = 25;
        [Min(0)] [SerializeField] private int rareWeight = 5;

        public string ZoneId => zoneId;
        public int BudgetWeight => budgetWeight;

        public int GetRarityWeight(LootRarity rarity)
        {
            return rarity switch
            {
                LootRarity.Uncommon => uncommonWeight,
                LootRarity.Rare => rareWeight,
                _ => commonWeight
            };
        }

        private void OnValidate()
        {
            zoneId = string.IsNullOrWhiteSpace(zoneId) ? name : zoneId.Trim();
            budgetWeight = Mathf.Max(1, budgetWeight);
            commonWeight = Mathf.Max(0, commonWeight);
            uncommonWeight = Mathf.Max(0, uncommonWeight);
            rareWeight = Mathf.Max(0, rareWeight);
        }
    }
}
