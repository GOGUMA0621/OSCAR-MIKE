using UnityEngine;
using OskarMike.Items;

namespace OskarMike.MapGeneration
{
    [CreateAssetMenu(menuName = "Map Generation/Room Config")]
    public class RoomConfig : ScriptableObject
    {
        public GameObject prefab;
        public Vector2Int size = Vector2Int.one;
        [Min(1)] public int weight = 1;
        [Tooltip("이 방에 적용할 폐품 지역 프로필입니다.")]
        public LootZoneProfile lootZone;
    }
}
