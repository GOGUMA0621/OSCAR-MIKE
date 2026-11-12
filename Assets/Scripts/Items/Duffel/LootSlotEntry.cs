using System;
using Unity.Collections;
using Unity.Netcode;

namespace OskarMike.Items
{
    /// <summary>
    /// 플레이어 개인 슬롯/손에 보관되는 아이템 정보.
    /// 월드 오브젝트는 줍는 순간 Despawn 되고, 이 값만 서버 권한으로 동기화된다.
    /// </summary>
    public struct LootSlotEntry : INetworkSerializable, IEquatable<LootSlotEntry>
    {
        public FixedString64Bytes ItemId;
        public int Price;
        public byte ValueSteps;
        public byte Category;

        public bool IsEmpty => ItemId.Length == 0;

        public static LootSlotEntry From(NetworkLootItem item) => new LootSlotEntry
        {
            ItemId = string.IsNullOrEmpty(item.ItemId) ? "loot_item" : item.ItemId,
            Price = item.Value,
            ValueSteps = item.ValueSteps,
            Category = (byte)item.Category
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Price);
            serializer.SerializeValue(ref ValueSteps);
            serializer.SerializeValue(ref Category);
        }

        public bool Equals(LootSlotEntry other) =>
            ItemId.Equals(other.ItemId) && Price == other.Price &&
            ValueSteps == other.ValueSteps && Category == other.Category;

        public override bool Equals(object obj) => obj is LootSlotEntry other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ItemId, Price, ValueSteps, Category);
    }
}
