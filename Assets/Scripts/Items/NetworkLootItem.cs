using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

namespace OskarMike.Items
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkLootItem : NetworkBehaviour
    {
        private readonly NetworkVariable<int> value = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> valueSteps = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> usageCategory = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> itemId = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Value => value.Value;
        public float ItemValue => valueSteps.Value * 0.5f;
        public byte ValueSteps => valueSteps.Value;
        public LootUsageCategory UsageCategory => (LootUsageCategory)usageCategory.Value;
        public string ItemId => itemId.Value.ToString();

        public void InitializeServer(string definitionId, byte itemValueSteps,
            LootUsageCategory itemUsageCategory, int itemPrice)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("[NetworkLootItem] Only the server can initialize loot.");
                return;
            }

            valueSteps.Value = (byte)Mathf.Clamp(itemValueSteps, 2, 10);
            usageCategory.Value = (byte)itemUsageCategory;
            value.Value = Mathf.Max(0, itemPrice);
            itemId.Value = string.IsNullOrWhiteSpace(definitionId) ? "loot_item" : definitionId;
        }
    }
}
