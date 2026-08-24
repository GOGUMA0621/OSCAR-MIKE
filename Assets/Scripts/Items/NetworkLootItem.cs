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
        private readonly NetworkVariable<byte> rarity = new NetworkVariable<byte>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<FixedString64Bytes> itemId = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Value => value.Value;
        public LootRarity Rarity => (LootRarity)rarity.Value;
        public string ItemId => itemId.Value.ToString();

        public void InitializeServer(string definitionId, LootRarity itemRarity, int itemValue)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("[NetworkLootItem] Only the server can initialize loot.");
                return;
            }

            rarity.Value = (byte)itemRarity;
            value.Value = Mathf.Max(0, itemValue);
            itemId.Value = string.IsNullOrWhiteSpace(definitionId) ? "loot_item" : definitionId;
        }
    }
}
