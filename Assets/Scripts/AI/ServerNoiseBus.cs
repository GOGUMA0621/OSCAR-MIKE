using System;
using Unity.Netcode;
using UnityEngine;

namespace OskarMike.AI
{
    public readonly struct AINoise
    {
        public readonly AINoiseKind Kind;
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly double Time;
        public readonly NetworkObject Source;
        public AINoise(AINoiseDefinition definition, Vector3 position, NetworkObject source, double time)
        { Kind = definition.kind; Radius = definition.hearingRadius; Position = position; Source = source; Time = time; }
    }

    public static class ServerNoiseBus
    {
        public static event Action<AINoise> Emitted;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Emitted = null;

        public static void Emit(AINoiseDefinition definition, Vector3 position, NetworkObject source)
        {
            if (definition == null || source == null || !source.IsSpawned ||
                source.NetworkManager == null || !source.NetworkManager.IsServer) return;
            Emitted?.Invoke(new AINoise(definition, position, source, UnityEngine.Time.timeAsDouble));
        }
    }
}
