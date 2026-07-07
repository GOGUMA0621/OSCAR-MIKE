using System;
using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.Progression
{
    [Serializable]
    public struct SkillNodeId : IEquatable<SkillNodeId>
    {
        [SerializeField] private string value;

        public string Value => value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrWhiteSpace(value);

        public SkillNodeId(string value)
        {
            this.value = value;
        }

        public bool Equals(SkillNodeId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SkillNodeId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(SkillNodeId left, SkillNodeId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SkillNodeId left, SkillNodeId right)
        {
            return !left.Equals(right);
        }

        public static bool IsNullOrEmpty(SkillNodeId id)
        {
            return id.IsEmpty;
        }
    }

    [Serializable]
    public struct SkillPathId : IEquatable<SkillPathId>
    {
        [SerializeField] private string value;

        public string Value => value ?? string.Empty;
        public bool IsEmpty => string.IsNullOrWhiteSpace(value);

        public SkillPathId(string value)
        {
            this.value = value;
        }

        public bool Equals(SkillPathId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is SkillPathId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(SkillPathId left, SkillPathId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SkillPathId left, SkillPathId right)
        {
            return !left.Equals(right);
        }
    }

    public enum SkillEffectType
    {
        None = 0,
        MaxStaminaAdd = 10,
        StaminaRecoveryAdd = 11,
        SprintDrainMultiplier = 12,
        JumpCostMultiplier = 13,
        ParkourCostMultiplier = 14,
        WalkSpeedMultiplier = 20,
        SprintSpeedMultiplier = 21,
        CrouchSpeedMultiplier = 22,
        ProneSpeedMultiplier = 23,
        CarryCapacityAdd = 30,
        InventorySlotsAdd = 31,
        InteractionSpeedMultiplier = 40,
        Custom = 1000
    }

    [Serializable]
    public class SkillEffectDefinition
    {
        [SerializeField] private SkillEffectType type = SkillEffectType.None;
        [SerializeField] private float value = 1f;
        [SerializeField] private string customKey;

        public SkillEffectType Type => type;
        public float Value => value;
        public string CustomKey => customKey;
    }

    [Serializable]
    public class SkillNodeDefinition
    {
        [Header("Identity")]
        [SerializeField] private SkillNodeId id;
        [SerializeField] private string displayName;
        [TextArea]
        [SerializeField] private string description;
        [SerializeField] private Sprite icon;

        [Header("Progression")]
        [Min(1)]
        [SerializeField] private int tier = 1;
        [Min(0)]
        [SerializeField] private int cost = 1;
        [SerializeField] private bool isFinalNode;
        [SerializeField] private List<SkillNodeId> prerequisites = new List<SkillNodeId>();

        [Header("Effects")]
        [SerializeField] private List<SkillEffectDefinition> effects = new List<SkillEffectDefinition>();

        public SkillNodeId Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public int Tier => tier;
        public int Cost => cost;
        public bool IsFinalNode => isFinalNode;
        public IReadOnlyList<SkillNodeId> Prerequisites =>
            prerequisites != null ? prerequisites : Array.Empty<SkillNodeId>();
        public IReadOnlyList<SkillEffectDefinition> Effects =>
            effects != null ? effects : Array.Empty<SkillEffectDefinition>();
    }

    [Serializable]
    public class SkillPathDefinition
    {
        [Header("Identity")]
        [SerializeField] private SkillPathId id;
        [SerializeField] private string displayName;
        [TextArea]
        [SerializeField] private string description;

        [Header("Nodes")]
        [SerializeField] private List<SkillNodeDefinition> nodes = new List<SkillNodeDefinition>();

        public SkillPathId Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public IReadOnlyList<SkillNodeDefinition> Nodes =>
            nodes != null ? nodes : Array.Empty<SkillNodeDefinition>();

        internal List<SkillNodeDefinition> MutableNodes => nodes;
    }
}
