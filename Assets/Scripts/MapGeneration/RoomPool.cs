using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.MapGeneration
{
    [CreateAssetMenu(menuName = "Map Generation/Room Pool")]
    public class RoomPool : ScriptableObject
    {
        public List<RoomConfig> rooms = new List<RoomConfig>();

        public RoomConfig PickRandom(System.Random rng)
        {
            if (rooms.Count == 0) return null;

            int totalWeight = 0;
            foreach (var r in rooms) totalWeight += r.weight;

            int roll = rng.Next(totalWeight);
            int cumulative = 0;
            foreach (var r in rooms)
            {
                cumulative += r.weight;
                if (roll < cumulative) return r;
            }

            return rooms[^1];
        }
    }
}
