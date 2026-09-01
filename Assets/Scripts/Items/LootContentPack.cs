using UnityEngine;

namespace OskarMike.Items
{
    [CreateAssetMenu(fileName = "LootContentPack", menuName = "Items/Loot Content Pack")]
    public sealed class LootContentPack : ScriptableObject
    {
        [SerializeField] private string packId = "content_pack";
        [SerializeField] private string assetRoot = "Assets";

        public string PackId => packId;
        public string AssetRoot => assetRoot;

        private void OnValidate()
        {
            packId = string.IsNullOrWhiteSpace(packId) ? name.ToLowerInvariant() : packId.Trim().ToLowerInvariant();
            assetRoot = string.IsNullOrWhiteSpace(assetRoot) ? "Assets" : assetRoot.Trim().Replace('\\', '/');
        }
    }
}
