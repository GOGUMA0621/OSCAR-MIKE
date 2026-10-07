using UnityEngine;

namespace OskarMike.AI
{
    public enum AINoiseKind { Walk, Sprint, Crouch, Prone }

    [CreateAssetMenu(menuName = "OSKAR MIKE/AI/소리 정의")]
    public sealed class AINoiseDefinition : ScriptableObject
    {
        public AINoiseKind kind;
        [Min(0.01f)] public float hearingRadius = 5f;
        [Min(0.05f)] public float interval = 0.5f;
    }
}
