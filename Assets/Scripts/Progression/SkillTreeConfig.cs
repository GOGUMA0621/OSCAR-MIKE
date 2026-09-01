using System;
using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.Progression
{
    [CreateAssetMenu(menuName = "Oskar Mike/Progression/Skill Tree Config")]
    public class SkillTreeConfig : ScriptableObject
    {
        [SerializeField] private bool limitToSingleFinalNodeClaim = true;
        [SerializeField] private List<SkillPathDefinition> paths = new List<SkillPathDefinition>();

        public bool LimitToSingleFinalNodeClaim => limitToSingleFinalNodeClaim;
        public IReadOnlyList<SkillPathDefinition> Paths =>
            paths != null ? paths : Array.Empty<SkillPathDefinition>();

        public SkillPathDefinition GetPath(SkillPathId pathId)
        {
            if (pathId.IsEmpty)
                return null;

            foreach (var path in Paths)
            {
                if (path != null && path.Id == pathId)
                    return path;
            }

            return null;
        }

        public SkillPathDefinition GetPathContaining(SkillNodeId nodeId)
        {
            if (nodeId.IsEmpty)
                return null;

            foreach (var path in Paths)
            {
                if (path == null)
                    continue;

                foreach (var node in path.Nodes)
                {
                    if (node != null && node.Id == nodeId)
                        return path;
                }
            }

            return null;
        }

        public SkillNodeDefinition GetNode(SkillNodeId nodeId)
        {
            if (nodeId.IsEmpty)
                return null;

            foreach (var node in GetAllNodes())
            {
                if (node.Id == nodeId)
                    return node;
            }

            return null;
        }

        public bool ContainsNode(SkillNodeId nodeId)
        {
            return GetNode(nodeId) != null;
        }

        public bool IsFinalNode(SkillNodeId nodeId)
        {
            var node = GetNode(nodeId);
            return node != null && node.IsFinalNode;
        }

        public IEnumerable<SkillNodeDefinition> GetAllNodes()
        {
            foreach (var path in Paths)
            {
                if (path == null)
                    continue;

                foreach (var node in path.Nodes)
                {
                    if (node != null)
                        yield return node;
                }
            }
        }

        public void CollectValidationMessages(List<string> errors, List<string> warnings)
        {
            errors.Clear();
            warnings.Clear();

            var pathIds = new HashSet<SkillPathId>();
            var nodeIds = new HashSet<SkillNodeId>();
            var prerequisiteChecks = new List<(SkillNodeDefinition Node, SkillNodeId Prerequisite)>();

            for (int pathIndex = 0; pathIndex < Paths.Count; pathIndex++)
            {
                var path = Paths[pathIndex];
                if (path == null)
                {
                    errors.Add($"Path {pathIndex} is empty.");
                    continue;
                }

                if (path.Id.IsEmpty)
                    errors.Add($"Path {pathIndex} has an empty ID.");
                else if (!pathIds.Add(path.Id))
                    errors.Add($"Duplicate path ID '{path.Id.Value}'.");

                if (path.MutableNodes == null || path.MutableNodes.Count == 0)
                    warnings.Add($"Path '{path.Id.Value}' has no skill nodes.");

                ValidateNodesInPath(path, pathIndex, nodeIds, prerequisiteChecks, errors, warnings);
            }

            foreach (var check in prerequisiteChecks)
            {
                if (!nodeIds.Contains(check.Prerequisite))
                {
                    errors.Add($"Skill node '{check.Node.Id.Value}' references missing prerequisite '{check.Prerequisite.Value}'.");
                }
            }
        }

        private static void ValidateNodesInPath(
            SkillPathDefinition path,
            int pathIndex,
            HashSet<SkillNodeId> nodeIds,
            List<(SkillNodeDefinition Node, SkillNodeId Prerequisite)> prerequisiteChecks,
            List<string> errors,
            List<string> warnings)
        {
            var tiersInPath = new HashSet<int>();

            for (int nodeIndex = 0; nodeIndex < path.Nodes.Count; nodeIndex++)
            {
                var node = path.Nodes[nodeIndex];
                if (node == null)
                {
                    errors.Add($"Path '{path.Id.Value}' has an empty node at index {nodeIndex}.");
                    continue;
                }

                if (node.Id.IsEmpty)
                    errors.Add($"Path '{path.Id.Value}' node {nodeIndex} has an empty ID.");
                else if (!nodeIds.Add(node.Id))
                    errors.Add($"Duplicate skill node ID '{node.Id.Value}'.");

                if (!tiersInPath.Add(node.Tier))
                    warnings.Add($"Path '{path.Id.Value}' has multiple nodes in tier {node.Tier}.");

                ValidateNodePrerequisites(node, pathIndex, nodeIndex, prerequisiteChecks, errors, warnings);
                ValidateNodeEffects(node, errors, warnings);
            }
        }

        private static void ValidateNodePrerequisites(
            SkillNodeDefinition node,
            int pathIndex,
            int nodeIndex,
            List<(SkillNodeDefinition Node, SkillNodeId Prerequisite)> prerequisiteChecks,
            List<string> errors,
            List<string> warnings)
        {
            var prerequisites = node.Prerequisites;
            if (prerequisites == null)
                return;

            var seenPrerequisites = new HashSet<SkillNodeId>();
            for (int prerequisiteIndex = 0; prerequisiteIndex < prerequisites.Count; prerequisiteIndex++)
            {
                var prerequisite = prerequisites[prerequisiteIndex];
                if (prerequisite.IsEmpty)
                {
                    warnings.Add($"Skill node '{node.Id.Value}' has an empty prerequisite at index {prerequisiteIndex}.");
                    continue;
                }

                if (prerequisite == node.Id)
                {
                    errors.Add($"Skill node '{node.Id.Value}' cannot require itself.");
                    continue;
                }

                if (!seenPrerequisites.Add(prerequisite))
                    warnings.Add($"Skill node '{node.Id.Value}' lists prerequisite '{prerequisite.Value}' more than once.");

                prerequisiteChecks.Add((node, prerequisite));
            }

            if (node.Tier > 1 && prerequisites.Count == 0)
                warnings.Add($"Path {pathIndex} node {nodeIndex} is tier {node.Tier} but has no prerequisites.");
        }

        private static void ValidateNodeEffects(
            SkillNodeDefinition node,
            List<string> errors,
            List<string> warnings)
        {
            var effects = node.Effects;
            if (effects == null || effects.Count == 0)
            {
                warnings.Add($"Skill node '{node.Id.Value}' has no effects.");
                return;
            }

            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                var effect = effects[effectIndex];
                if (effect == null)
                {
                    errors.Add($"Skill node '{node.Id.Value}' has an empty effect at index {effectIndex}.");
                    continue;
                }

                if (effect.Type == SkillEffectType.None)
                    warnings.Add($"Skill node '{node.Id.Value}' has an effect with type None at index {effectIndex}.");

                if (effect.Type == SkillEffectType.Custom && string.IsNullOrWhiteSpace(effect.CustomKey))
                    errors.Add($"Skill node '{node.Id.Value}' has a custom effect without a custom key.");
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            CollectValidationMessages(errors, warnings);

            foreach (var error in errors)
                Debug.LogError($"[SkillTreeConfig] {error}", this);

            foreach (var warning in warnings)
                Debug.LogWarning($"[SkillTreeConfig] {warning}", this);
        }
#endif
    }
}
