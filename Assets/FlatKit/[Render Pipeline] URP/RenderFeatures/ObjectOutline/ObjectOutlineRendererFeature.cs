using System.Collections.Generic;
using ExternPropertyAttributes;
using JetBrains.Annotations;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

#if UNITY_2023_3_OR_NEWER
using UnityEngine.Rendering.Universal;
#else
using UnityEngine.Experimental.Rendering.Universal;
#endif

namespace FlatKit {
#if UNITY_EDITOR
[CustomEditor(typeof(ObjectOutlineRendererFeature))]
public class ObjectOutlineRendererFeatureEditor : Editor {
    private bool _advancedSettingsFoldout;

    public override void OnInspectorGUI() {
        var feature = target as ObjectOutlineRendererFeature;
        if (feature == null) return;

#if !UNITY_2022_3_OR_NEWER
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("This feature requires Unity 2022.3 or newer.", MessageType.Error);
        EditorGUILayout.Space(-10);
#else
        // Default properties.
        _advancedSettingsFoldout = EditorGUILayout.Foldout(_advancedSettingsFoldout, "Advanced Settings");
        if (_advancedSettingsFoldout) {
            EditorGUI.indentLevel++;
            // Style the background of the foldout.
            EditorGUILayout.BeginVertical("HelpBox");
            base.OnInspectorGUI();
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }

        // Custom properties.
        var autoReferenceMaterials =
            serializedObject.FindProperty(nameof(ObjectOutlineRendererFeature.autoReferenceMaterials));
        EditorGUI.indentLevel--;
        autoReferenceMaterials.boolValue = EditorGUILayout.ToggleLeft(
            new GUIContent(autoReferenceMaterials.displayName, autoReferenceMaterials.tooltip),
            autoReferenceMaterials.boolValue);
        EditorGUI.indentLevel++;
        if (autoReferenceMaterials.boolValue) {
            EditorGUI.indentLevel++;
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.BeginVertical("HelpBox");
            var materials = serializedObject.FindProperty(nameof(ObjectOutlineRendererFeature.materials));
            EditorGUILayout.PropertyField(materials, true);
            EditorGUILayout.EndVertical();
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;
        }
#endif
    }
}
#endif

public class ObjectOutlineRendererFeature : RenderObjects {
    public const string OutlinePassName = "Outline";

    [ReadOnly]
    [Tooltip("Materials using this feature. The list is updated automatically based on the `Enable Outline` toggle " +
             "on materials using the Stylized Surface shader.")]
    [HideInInspector]
    [SerializeField]
    public List<Material> materials = new();

    [Tooltip("Keep track of materials using outlines and automatically delete this feature if no materials use it." +
             "If disabled, you must manually remove this feature when no materials use it.")]
    [HideInInspector]
    [SerializeField]
    public bool autoReferenceMaterials = true;

    public override void Create() {
#if UNITY_2022_3_OR_NEWER
        // Shader overrides bypass the SRP Batcher; draw each material's Outline pass.
        settings.overrideMode = RenderObjectsSettings.OverrideMaterialMode.None;
        settings.overrideShader = null;
        settings.overrideShaderPassIndex = 0;
#endif

        if (settings.filterSettings.LayerMask == 0) {
            settings.filterSettings.LayerMask = -1;
        }
        settings.filterSettings.PassNames = new[] { OutlinePassName };

        if (autoReferenceMaterials) {
            // Remove any invalid materials.
            materials.RemoveAll(m => m == null);

            // Migrate registered materials to pass filtering.
            foreach (var material in materials) {
                material.SetShaderPassEnabled(OutlinePassName, true);
            }
        } else {
            materials.Clear();
        }

        base.Create();
    }

    // Syncs the pass and returns whether this feature is still used.
    [MustUseReturnValue]
    public bool RegisterMaterial(Material material, bool active) {
        if (material == null) return materials.Count > 0;

        material.SetShaderPassEnabled(OutlinePassName, active);

        if (!autoReferenceMaterials) return true;

        if (active) {
            if (!materials.Contains(material)) {
                materials.Add(material);
            }
        } else {
            materials.Remove(material);
        }

        return materials.Count > 0;
    }
}
}
