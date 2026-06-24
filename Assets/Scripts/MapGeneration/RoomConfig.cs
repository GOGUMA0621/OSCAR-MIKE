using UnityEngine;

namespace OskarMike.MapGeneration
{
    [CreateAssetMenu(menuName = "Map Generation/Room Config")]
    public class RoomConfig : ScriptableObject
    {
        public GameObject prefab;
        public Vector2Int size = Vector2Int.one;
        [Min(1)] public int weight = 1;
    }
}
