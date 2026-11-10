using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OskarMike.EditorTools
{
    public static class PlayerAnimationValidation
    {
        [MenuItem("Oskar Mike/Animation/Validate Animator Locomotion")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerLocomotionSetup.ControllerPath);
            foreach (string name in new[] { "StandingLocomotion", "CrouchLocomotion", "ProneLocomotion" })
            {
                var state = controller.layers[0].stateMachine.states.First(s => s.state.name == name).state;
                var tree = state.motion as BlendTree;
                Check(tree != null && tree.blendParameter == "Speed" && tree.children.Length >= 2 &&
                    tree.children.All(c => c.motion != null), name + " tree");
            }
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var anim = root.GetComponentInChildren<Animator>(true);
                anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                anim.Rebind(); anim.Update(0);
                Check(!anim.applyRootMotion, "Root Motion disabled");
                Check(root.GetComponent<OskarMike.Network.Player.PlayerAnimatorParameters>() != null, "Parameter bridge connected");
                Check(!root.GetComponents<MonoBehaviour>().Any(s => s != null && s.GetType().Name == "PlayerAnimationPresenter"), "Legacy presenter removed");
                anim.SetInteger("Action", 0); anim.SetInteger("Posture", 0);
                anim.SetFloat("Speed", 1.2f); Advance(anim, .3f);
                Check(anim.GetCurrentAnimatorStateInfo(0).IsName("StandingLocomotion"), "Standing state");
                Check(anim.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Walk" && c.weight > .95f), "Speed selects Walk");
                anim.SetFloat("Speed", 4.5f); Advance(anim, .3f);
                Check(anim.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Sprint" && c.weight > .95f), "Speed selects Sprint");
                anim.SetInteger("Posture", 1); anim.SetFloat("Speed", .65f); Advance(anim, .4f);
                Check(anim.GetCurrentAnimatorStateInfo(0).IsName("CrouchEnter"), "Crouch entry not cut at 0.25 seconds");
                Advance(anim, 1.2f);
                Check(anim.GetCurrentAnimatorStateInfo(0).IsName("CrouchLocomotion"), "Crouch entry completes");
                Check(anim.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "CrouchWalk" && c.weight > .95f), "Crouch speed selects walk");
                anim.SetInteger("Posture", 2); anim.SetFloat("Speed", .3f); Advance(anim, 1.6f);
                Check(anim.GetCurrentAnimatorStateInfo(0).IsName("ProneLocomotion"), "Prone entry completes");
                Check(anim.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Crawl" && c.weight > .95f), "Prone speed selects crawl");
                string[] actions = { "Jump", "Fall", "VaultPlaceholder", "DivePlaceholder", "SlidePlaceholder" };
                for (int i = 0; i < actions.Length; i++)
                {
                    anim.SetInteger("Action", i + 1); Advance(anim, .4f);
                    Check(anim.GetCurrentAnimatorStateInfo(0).IsName(actions[i]), "Action " + actions[i]);
                }
                anim.SetInteger("Action", 0); anim.SetInteger("Posture", 1); Advance(anim, .4f);
                Check(anim.GetCurrentAnimatorStateInfo(0).IsName("CrouchLocomotion"), "Slide returns directly to crouch");
                anim.SetInteger("Posture", 0); Advance(anim, .4f);
                anim.SetInteger("Posture", 2); Advance(anim, .2f);
                anim.SetInteger("Posture", 0); Advance(anim, .5f);
                Check(anim.GetCurrentAnimatorStateInfo(0).IsName("StandingLocomotion"), "Cancelled posture entry returns to standing");
                int lean = anim.GetLayerIndex("Lean");
                anim.SetLayerWeight(lean, 1); anim.SetFloat("LeanPoseTime", 1); anim.SetFloat("Lean", 1); Advance(anim, .2f);
                Check(anim.GetCurrentAnimatorStateInfo(lean).IsName("LeanRight"), "Lean right parameter");
                anim.SetFloat("Lean", -1); Advance(anim, .2f);
                Check(anim.GetCurrentAnimatorStateInfo(lean).IsName("LeanLeft"), "Lean left parameter");
                Debug.Log("LOCOMOTION_VALIDATION_OK");
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void Advance(Animator animator, float seconds)
        { for (int i = 0; i < Mathf.CeilToInt(seconds * 60); i++) animator.Update(1f / 60); }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); Debug.Log("[Locomotion PASS] " + message); }
    }
}