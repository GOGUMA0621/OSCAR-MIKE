using System;
using System.Reflection;
using OskarMike.Network.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OskarMike.EditorTools
{
    /// <summary>Batch host smoke check. Does not replace remote-client or visual review.</summary>
    [InitializeOnLoad]
    public static class PlayerAnimationValidation
    {
        private const string Active = "OskarMike.AnimationValidation";
        private static double started;
        private static float stageStart = -1f;
        private static int stage;
        private static int lastFrame = -1;
        private static PlayerNetworkController player;
        private static float standingHeadHeight;
        private static readonly MethodInfo Move = typeof(PlayerNetworkController).GetMethod("MoveServerRpc", BindingFlags.Instance | BindingFlags.NonPublic);

        static PlayerAnimationValidation()
        {
            started = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            PlayerAnimationSetup.Build();
            SessionState.SetBool(Active, true);
            EditorSceneManager.OpenScene("Assets/Scenes/MovementTest.unity");
            EditorApplication.EnterPlaymode();
        }

        public static void RenderPoses()
        {
            PlayerAnimationSetup.Build();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            root.transform.position = Vector3.zero;
            var animator = root.GetComponentInChildren<Animator>();
            foreach (var c in root.GetComponentsInChildren<Camera>()) c.enabled = false;
            var cameraObject = new GameObject("Review Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(2.7f, 1.6f, 3.5f);
            camera.transform.LookAt(new Vector3(0f, 0.85f, 0f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.18f, 0.21f);
            camera.fieldOfView = 35f;
            var light = new GameObject("Review Light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2f;
            light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            RenderSettings.ambientLight = Color.gray;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMaterial.color = new Color(0.3f, 0.32f, 0.35f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
            var target = new RenderTexture(512, 512, 24);
            camera.targetTexture = target;
            var sheet = new Texture2D(512 * 4, 512 * 2, TextureFormat.RGB24, false);
            string[] states = { "Idle", "Walk", "TacticalRun", "Sprint", "CrouchIdle", "ProneIdle", "LeanLeft", "LeanRight" };
            for (int i = 0; i < states.Length; i++)
            {
                animator.Rebind();
                animator.SetLayerWeight(1, i >= 6 ? 1f : 0f);
                animator.Play(i >= 6 ? "Idle" : states[i], 0, 0.4f);
                if (i >= 6)
                {
                    animator.SetFloat("LeanPoseTime", 1f);
                    animator.Play(states[i], 1, 1f);
                }
                animator.Update(0f);
                var head = root.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Head).position);
                Debug.Log("POSE_REVIEW " + states[i] + " head=" + head.ToString("F3"));
                // Bake explicitly: editor batch rendering may otherwise reuse stale GPU skinning data.
                var bakedObjects = new System.Collections.Generic.List<GameObject>();
                var bakedMeshes = new System.Collections.Generic.List<Mesh>();
                foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh();
                    skin.BakeMesh(mesh);
                    var baked = new GameObject("Baked pose", typeof(MeshFilter), typeof(MeshRenderer));
                    baked.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
                    baked.transform.localScale = skin.transform.lossyScale;
                    baked.GetComponent<MeshFilter>().sharedMesh = mesh;
                    baked.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    skin.enabled = false;
                    bakedObjects.Add(baked);
                    bakedMeshes.Add(mesh);
                }
                camera.Render();
                RenderTexture.active = target;
                var tile = new Texture2D(512, 512, TextureFormat.RGB24, false);
                tile.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                tile.Apply();
                sheet.SetPixels((i % 4) * 512, (1 - i / 4) * 512, 512, 512, tile.GetPixels());
                UnityEngine.Object.DestroyImmediate(tile);
                foreach (var baked in bakedObjects) UnityEngine.Object.DestroyImmediate(baked);
                foreach (var mesh in bakedMeshes) UnityEngine.Object.DestroyImmediate(mesh);
                foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.enabled = true;
            }
            sheet.Apply();
            string output = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oscar-animation-poses.png");
            System.IO.File.WriteAllBytes(output, sheet.EncodeToPNG());
            RenderTexture.active = null;
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(sheet);
            Debug.Log("POSE_REVIEW_SAVED " + output);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false)) return;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 100) throw new TimeoutException("Animation host validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                if (player == null)
                {
                    foreach (var candidate in UnityEngine.Object.FindObjectsByType<PlayerNetworkController>(FindObjectsSortMode.None))
                        if (candidate.IsSpawned && candidate.IsOwner) player = candidate;
                    if (player == null) return;
                    player.EnableGameplayInput();
                    player.enabled = false; // Supply deterministic inputs through the same RPC instead of the keyboard.
                    Require(player.GetComponent<PlayerAnimationPresenter>() != null, "Missing presenter.");
                    var animator = player.GetComponentInChildren<Animator>();
                    Require(animator != null && animator.isHuman && animator.avatar.isValid, "Invalid Humanoid animator.");
                    Require(!animator.applyRootMotion, "Root motion must be disabled.");
                    Require(animator.layerCount == 2, "Expected locomotion and lean layers.");
                }
                bool first = stageStart < 0;
                if (first) stageStart = Time.time;
                Vector2 movement = stage >= 1 && stage <= 3 ? Vector2.up : Vector2.zero;
                bool slow = stage != 2;
                bool sprint = stage == 3;
                bool crouch = stage == 7 && first;
                bool prone = stage == 8 && first;
                float lean = stage == 4 ? -1f : stage == 3 || stage == 5 || stage == 7 || stage == 8 ? 1f : 0f;
                Move.Invoke(player, new object[] { movement, 0f, false, slow, sprint, crouch, prone, lean });
                if (Time.time - stageStart < 0.5f) return;
                var anim = player.GetComponentInChildren<Animator>();
                int localLayer = LayerMask.NameToLayer("LocalPlayerVisual");
                Require(localLayer >= 0 && (player.GetComponentInChildren<Camera>().cullingMask & (1 << localLayer)) == 0,
                    "Owner camera must hide only its local body layer.");
                float headHeight = player.transform.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Head).position).y;
                if (stage == 0) standingHeadHeight = headHeight;
                if (stage == 7) Require(headHeight < standingHeadHeight - 0.2f, "Crouch lean must preserve lowered body pose.");
                if (stage == 8) Require(headHeight < standingHeadHeight - 0.7f, "Prone pose must remain low.");
                string expected = stage switch
                {
                    1 => "Walk", 2 => "TacticalRun", 3 => "Sprint",
                    7 => "CrouchIdle", 8 => "ProneIdle", _ => "Idle"
                };
                Require(anim.GetCurrentAnimatorStateInfo(0).IsName(expected), "Stage " + stage + " expected " + expected);
                if (stage == 1) Require(player.MoveState == PlayerMoveState.Walk, "Default walk mode.");
                if (stage == 2) Require(player.MoveState == PlayerMoveState.TacticalWalk, "Tactical mode.");
                if (stage == 3)
                {
                    Require(player.MoveState == PlayerMoveState.FullSprint, "Sprint mode.");
                    Require(player.GetComponent<PlayerStamina>().Stamina < 100f, "Sprint must drain stamina.");
                }
                if (stage == 4 || stage == 5 || stage == 7) Require(Mathf.Approximately(player.Lean, lean), "Lean direction.");
                if (stage == 3 || stage == 6 || stage == 8) Require(player.Lean == 0f, "Lean cancel/sprint/prone restriction.");
                Debug.Log("ANIMATION_HOST_STAGE_OK " + stage + " " + expected + " lean=" + player.Lean);
                stage++;
                stageStart = -1;
                if (stage > 8)
                {
                    SessionState.SetBool(Active, false);
                    Debug.Log("ANIMATION_HOST_VALIDATION_OK: idle, walk, tactical run, sprint/stamina, lean left/right/release, crouch lean, prone restriction.");
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception error)
            {
                SessionState.SetBool(Active, false);
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }
    }
}
