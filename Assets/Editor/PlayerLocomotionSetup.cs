using System;
using System.Collections.Generic;
using System.Linq;
using OskarMike.Network.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OskarMike.EditorTools
{
    public static class PlayerLocomotionSetup
    {
        public const string ControllerPath = "Assets/Animations/Player/PlayerLocomotion.controller";

        [MenuItem("Oskar Mike/Animation/Configure Animator Locomotion")]
        public static void Configure()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new InvalidOperationException("Missing PlayerLocomotion controller.");
            Undo.RegisterCompleteObjectUndo(controller, "Configure player locomotion");
            AddParameter(controller, "Speed", AnimatorControllerParameterType.Float);
            AddParameter(controller, "Posture", AnimatorControllerParameterType.Int);
            AddParameter(controller, "Action", AnimatorControllerParameterType.Int);
            AddParameter(controller, "Lean", AnimatorControllerParameterType.Float);
            AddParameter(controller, "LeanPoseTime", AnimatorControllerParameterType.Float);

            var sm = controller.layers[0].stateMachine;
            var standing = Tree(controller, sm, "StandingLocomotion", new[] { "Idle", "Walk", "TacticalRun", "Sprint" },
                new[] { 0f, 1.2f, 2.4f, 4.5f }, new[] { 1f, 1f, .85f, 1.2f }, new Vector3(300, 0));
            var crouching = Tree(controller, sm, "CrouchLocomotion", new[] { "CrouchIdle", "CrouchWalk" },
                new[] { 0f, .65f }, new[] { 1f, 1f }, new Vector3(600, 140));
            var prone = Tree(controller, sm, "ProneLocomotion", new[] { "ProneIdle", "Crawl" },
                new[] { 0f, .3f }, new[] { 1f, 1f }, new Vector3(600, 300));
            var crouchEnter = State(sm, "CrouchEnter", new Vector3(300, 140));
            crouchEnter.motion = Clip("CrouchEnter");
            var proneEnter = State(sm, "ProneEnter", new Vector3(300, 300));
            proneEnter.motion = Clip("ProneEnter");

            var actions = new List<AnimatorState>();
            string[] actionNames = { "Jump", "Fall", "VaultPlaceholder", "DivePlaceholder", "SlidePlaceholder" };
            for (int i = 0; i < actionNames.Length; i++)
            {
                var state = State(sm, actionNames[i], new Vector3(950, i * 100));
                state.motion = Clip(actionNames[i]);
                state.speed = 1;
                actions.Add(state);
            }

            string[] retired = { "Idle", "Walk", "TacticalRun", "Sprint", "CrouchIdle", "CrouchWalk", "ProneIdle", "Crawl" };
            foreach (var child in sm.states.ToArray())
                if (retired.Contains(child.state.name)) sm.RemoveState(child.state);
            var owned = new[] { standing, crouching, prone, crouchEnter, proneEnter }.Concat(actions).ToArray();
            foreach (var state in owned)
            {
                foreach (var transition in state.transitions.ToArray()) state.RemoveTransition(transition);
                state.writeDefaultValues = true;
            }
            foreach (var transition in sm.anyStateTransitions.ToArray())
                if (owned.Contains(transition.destinationState)) sm.RemoveAnyStateTransition(transition);

            // Action transitions interrupt posture entry; self-transition is disabled.
            for (int i = 0; i < actions.Count; i++)
            {
                var transition = sm.AddAnyStateTransition(actions[i]);
                ConfigureTransition(transition);
                transition.AddCondition(AnimatorConditionMode.Equals, i + 1, "Action");
            }

            PostureTransition(standing, crouchEnter, 1);
            PostureTransition(standing, proneEnter, 2);
            PostureTransition(crouching, standing, 0);
            PostureTransition(crouching, proneEnter, 2);
            PostureTransition(prone, standing, 0);
            PostureTransition(prone, crouching, 1);
            PostureTransition(crouchEnter, crouching, 1, true);
            PostureTransition(crouchEnter, standing, 0);
            PostureTransition(crouchEnter, proneEnter, 2);
            PostureTransition(proneEnter, prone, 2, true);
            PostureTransition(proneEnter, standing, 0);
            PostureTransition(proneEnter, crouching, 1);
            foreach (var action in actions)
            {
                PostureTransition(action, standing, 0);
                PostureTransition(action, crouching, 1);
                PostureTransition(action, prone, 2);
            }
            sm.defaultState = standing;

            var layers = controller.layers;
            int leanIndex = Array.FindIndex(layers, l => l.name == "Lean");
            if (leanIndex >= 0)
            {
                var leanSM = layers[leanIndex].stateMachine;
                var left = State(leanSM, "LeanLeft", new Vector3(300, 0));
                var right = State(leanSM, "LeanRight", new Vector3(600, 0));
                left.motion = Clip("LeanLeftSource"); right.motion = Clip("LeanRightSource");
                foreach (var state in new[] { left, right })
                {
                    state.timeParameter = "LeanPoseTime"; state.timeParameterActive = true;
                    foreach (var transition in state.transitions.ToArray()) state.RemoveTransition(transition);
                }
                var toRight = left.AddTransition(right); ConfigureTransition(toRight); toRight.duration = 0;
                toRight.AddCondition(AnimatorConditionMode.Greater, 0, "Lean");
                var toLeft = right.AddTransition(left); ConfigureTransition(toLeft); toLeft.duration = 0;
                toLeft.AddCondition(AnimatorConditionMode.Less, 0, "Lean");
                leanSM.defaultState = left;
            }
            // UpperBody is reserved for future weapon clips. Do not drive root or foot IK.
            int upperIndex = Array.FindIndex(layers, l => l.name == "UpperBody");
            if (upperIndex >= 0 && layers[upperIndex].avatarMask != null)
            {
                var mask = layers[upperIndex].avatarMask;
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
                mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
                EditorUtility.SetDirty(mask);
            }
            EditorUtility.SetDirty(controller);
            WirePrefab("Assets/Prefabs/Player.prefab");
            WirePrefab("Assets/AI/Prefabs/TestPlayer.prefab");
            AssetDatabase.SaveAssets();
            Debug.Log("LOCOMOTION_CONFIGURED: Animator transitions and three speed trees; legacy presenter removed from prefabs.");
        }

        public static AnimationClip Clip(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:AnimationClip", new[] { "Assets/Animations/Player" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null && clip.name == name) return clip;
            }
            throw new InvalidOperationException("Missing clip: " + name);
        }

        private static void AddParameter(AnimatorController c, string name, AnimatorControllerParameterType type)
        {
            if (!c.parameters.Any(p => p.name == name)) c.AddParameter(name, type);
            else if (c.parameters.First(p => p.name == name).type != type)
                throw new InvalidOperationException("Parameter type mismatch: " + name);
        }

        private static AnimatorState State(AnimatorStateMachine sm, string name, Vector3 position)
        {
            var state = sm.states.FirstOrDefault(s => s.state.name == name).state;
            if (state == null) return sm.AddState(name, position);
            var children = sm.states;
            for (int i = 0; i < children.Length; i++) if (children[i].state == state) children[i].position = position;
            sm.states = children;
            return state;
        }

        private static AnimatorState Tree(AnimatorController controller, AnimatorStateMachine sm, string name,
            string[] clips, float[] thresholds, float[] rates, Vector3 position)
        {
            var state = State(sm, name, position);
            var tree = state.motion as BlendTree;
            if (tree == null) { tree = new BlendTree { name = name + " Blend" }; AssetDatabase.AddObjectToAsset(tree, controller); }
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
            var children = new ChildMotion[clips.Length];
            for (int i = 0; i < clips.Length; i++)
                children[i] = new ChildMotion { motion = Clip(clips[i]), threshold = thresholds[i], timeScale = rates[i] };
            tree.children = children; state.motion = tree; state.speed = 1;
            EditorUtility.SetDirty(tree);
            return state;
        }

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false; transition.hasFixedDuration = true;
            transition.duration = .15f; transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.None;
            transition.orderedInterruption = false;
        }

        private static void PostureTransition(AnimatorState from, AnimatorState to, int posture, bool finishClip = false)
        {
            var transition = from.AddTransition(to); ConfigureTransition(transition);
            transition.AddCondition(AnimatorConditionMode.Equals, posture, "Posture");
            transition.AddCondition(AnimatorConditionMode.Equals, 0, "Action");
            if (finishClip) { transition.hasExitTime = true; transition.exitTime = 1f; }
        }

        private static void WirePrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var script in root.GetComponents<MonoBehaviour>())
                    if (script != null && script.GetType().Name == "PlayerAnimationPresenter") UnityEngine.Object.DestroyImmediate(script);
                var bridge = root.GetComponent<PlayerAnimatorParameters>() ?? root.AddComponent<PlayerAnimatorParameters>();
                if (root.GetComponent<LocalPlayerVisibility>() == null) root.AddComponent<LocalPlayerVisibility>();
                var animator = root.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var data = new SerializedObject(bridge);
                data.FindProperty("animator").objectReferenceValue = animator;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}