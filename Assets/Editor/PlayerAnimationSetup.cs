using System;
using System.IO;
using System.Linq;
using OskarMike.Network.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OskarMike.EditorTools
{
    public static class PlayerAnimationSetup
    {
        private const string Output = "Assets/Animations/Player";
        private const string PlayerPath = "Assets/Prefabs/Player.prefab";

        [MenuItem("Oskar Mike/Animation/Build Dummy Player Animations")]
        public static void Build()
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var projectLayers = tags.FindProperty("layers");
            if (LayerMask.NameToLayer("LocalPlayerVisual") < 0)
            {
                int available = -1;
                for (int i = 8; i < projectLayers.arraySize; i++)
                    if (string.IsNullOrEmpty(projectLayers.GetArrayElementAtIndex(i).stringValue)) { available = i; break; }
                if (available < 0) throw new InvalidOperationException("No free layer for the owner's first-person visual mask.");
                projectLayers.GetArrayElementAtIndex(available).stringValue = "LocalPlayerVisual";
                tags.ApplyModifiedPropertiesWithoutUndo();
            }
            Directory.CreateDirectory(Output);
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAllAssetsAtPath("Assets/animtion_demo.fbx").OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            var dummy = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Kevin Iglesias/Human Character Dummy/Models/HumanCharacterDummy_M.fbx");
            if (dummy == null) throw new InvalidOperationException("Dummy FBX is missing.");
            var avatar = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(dummy)).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("Dummy must have a valid Humanoid avatar.");

            string controllerPath = Output + "/PlayerLocomotion.controller";
            // Update the generated asset in place so prefab references remain stable.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            else
            {
                foreach (var layer in controller.layers)
                    foreach (var state in layer.stateMachine.states) layer.stateMachine.RemoveState(state.state);
                while (controller.layers.Length > 1) controller.RemoveLayer(1);
                controller.parameters = Array.Empty<AnimatorControllerParameter>();
            }
            var machine = controller.layers[0].stateMachine;
            AnimatorState Add(string stateName, string clipName, bool loop, float speed = 1f)
            {
                if (!source.TryGetValue("Skeleton|" + clipName, out var sourceClip))
                    throw new InvalidOperationException("Missing animation: " + clipName);
                string path = Output + "/" + stateName + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = UnityEngine.Object.Instantiate(sourceClip);
                    AssetDatabase.CreateAsset(clip, path);
                }
                else EditorUtility.CopySerialized(sourceClip, clip);
                clip.name = stateName;
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = loop;
                settings.loopBlend = loop;
                settings.loopBlendOrientation = true;
                settings.loopBlendPositionXZ = true;
                settings.loopBlendPositionY = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                var state = machine.AddState(stateName);
                state.motion = clip;
                state.speed = speed;
                state.writeDefaultValues = true;
                return state;
            }
            machine.defaultState = Add("Idle", "idle", true);
            Add("Walk", "walk", true);
            Add("TacticalRun", "run", true, 0.85f);
            Add("Sprint", "run", true, 1.2f);
            Add("CrouchIdle", "idle_sit", true);
            Add("CrouchWalk", "walk_sti", true);
            Add("ProneIdle", "idle_Prone", true);
            Add("Crawl", "Prone_walk", true);
            Add("CrouchEnter", "sit", false);
            Add("ProneEnter", "Prone", false);
            Add("Jump", "jump", false);
            Add("Fall", "jump_idle", true);
            Add("SlidePlaceholder", "idle_sit", true);
            Add("DivePlaceholder", "idle_Prone", true);
            Add("VaultPlaceholder", "jump_idle", true);
            var leftClip = Add("LeanLeftSource", "Q", false).motion;
            var rightClip = Add("LeanRightSource", "E", false).motion;
            // Apply only the lean difference, preserving the crouch/walk pose underneath.
            AnimationUtility.SetAdditiveReferencePose((AnimationClip)leftClip, source["Skeleton|idle"], 0f);
            AnimationUtility.SetAdditiveReferencePose((AnimationClip)rightClip, source["Skeleton|idle"], 0f);
            foreach (var state in machine.states.Where(s => s.state.name.EndsWith("Source")).ToArray())
                machine.RemoveState(state.state);
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Output + "/LeanUpperBody.mask");
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, Output + "/LeanUpperBody.mask");
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                         AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                         AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
                mask.SetHumanoidBodyPartActive(part, true);
            controller.AddLayer("Lean");
            controller.AddParameter("LeanPoseTime", AnimatorControllerParameterType.Float);
            var layers = controller.layers;
            layers[1].avatarMask = mask;
            layers[1].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[1].defaultWeight = 0f;
            foreach (var pair in new[] { ("LeanLeft", leftClip), ("LeanRight", rightClip) })
            {
                var state = layers[1].stateMachine.AddState(pair.Item1);
                state.motion = pair.Item2;
                state.timeParameter = "LeanPoseTime";
                state.timeParameterActive = true;
            }
            controller.layers = layers;
            EditorUtility.SetDirty(mask);
            EditorUtility.SetDirty(controller);

            var material = AssetDatabase.LoadAssetAtPath<Material>(Output + "/DummyURP.mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, Output + "/DummyURP.mat");
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Kevin Iglesias/Human Character Dummy/Textures/HumanCharacterDummy_ColorPalette.png"));
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);

            var player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var existing = player.transform.Find("CharacterVisual");
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(dummy, player.transform);
                visual.name = "CharacterVisual";
                // CharacterController keeps its root approximately one skin width above
                // contacted ground. Offset only the presentation model so its feet remain
                // visually grounded without changing authoritative movement or collision.
                var characterController = player.GetComponent<CharacterController>();
                float visualGroundingOffset = characterController != null ? -characterController.skinWidth : 0f;
                visual.transform.localPosition = Vector3.up * visualGroundingOffset;
                visual.transform.localRotation = Quaternion.identity;
                var animator = visual.GetComponent<Animator>();
                if (animator == null) animator = visual.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => material).ToArray();
                // The old capsule is a debug visual; retain its physical CharacterController.
                foreach (var renderer in player.GetComponents<Renderer>()) renderer.enabled = false;
                var presenter = player.GetComponent<PlayerAnimationPresenter>();
                if (presenter == null) presenter = player.AddComponent<PlayerAnimationPresenter>();
                var serialized = new SerializedObject(presenter);
                serialized.FindProperty("animator").objectReferenceValue = animator;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets();
            Debug.Log("PLAYER_ANIMATION_SETUP_OK: 13 source clips, Humanoid dummy, 3 locomotion stages and Q/E lean.");
        }
    }
}
