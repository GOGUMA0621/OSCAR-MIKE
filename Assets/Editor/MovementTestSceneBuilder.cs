using System;
using System.Linq;
using OskarMike.Testing;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OskarMike.EditorTools
{
    public static class MovementTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/MovementTest.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

        [MenuItem("Oskar Mike/Testing/Create Movement Test Scene")]
        public static void CreateMovementTestScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "MovementTest";

            CreatePrimitive("Floor", PrimitiveType.Cube, new Vector3(0f, -0.05f, 8f), new Vector3(24f, 0.1f, 24f));
            CreatePrimitive("Vault_Low_Obstacle", PrimitiveType.Cube, new Vector3(0f, 0.3f, 5f), new Vector3(2f, 0.6f, 0.5f));
            CreatePrimitive("Vault_Medium_Obstacle", PrimitiveType.Cube, new Vector3(-3f, 0.45f, 5f), new Vector3(2f, 0.9f, 0.5f));
            CreatePrimitive("Too_Tall_Obstacle", PrimitiveType.Cube, new Vector3(3f, 0.9f, 5f), new Vector3(2f, 1.8f, 0.5f));
            CreatePrimitive("Slide_Dive_Lane_Left", PrimitiveType.Cube, new Vector3(-4f, 0.05f, 10f), new Vector3(0.25f, 0.1f, 8f));
            CreatePrimitive("Slide_Dive_Lane_Right", PrimitiveType.Cube, new Vector3(4f, 0.05f, 10f), new Vector3(0.25f, 0.1f, 8f));

            CreateLighting();
            CreateOverviewCamera();
            CopyNetworkManagerFromMainMenu();

            var helperObject = new GameObject("MovementTestAutoHost");
            helperObject.AddComponent<MovementTestAutoHost>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MovementTestSceneBuilder] Created movement test scene at {ScenePath}.");
        }

        private static void CreatePrimitive(string name, PrimitiveType type, Vector3 position, Vector3 scale)
        {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.position = position;
            obj.transform.localScale = scale;
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void CreateOverviewCamera()
        {
            var cameraObject = new GameObject("Overview Camera");
            var camera = cameraObject.AddComponent<Camera>();
            // Keep this as an optional editor/debug viewpoint. If it renders by default,
            // it competes with the spawned owner's first-person camera in Game view.
            camera.enabled = false;
            cameraObject.transform.position = new Vector3(0f, 8f, -8f);
            cameraObject.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            camera.clearFlags = CameraClearFlags.Skybox;
        }

        private static void CopyNetworkManagerFromMainMenu()
        {
            var activeScene = SceneManager.GetActiveScene();
            var mainMenuScene = EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Additive);
            var source = mainMenuScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<NetworkManager>(true))
                .FirstOrDefault();

            if (source == null)
            {
                EditorSceneManager.CloseScene(mainMenuScene, true);
                throw new InvalidOperationException("Could not find NetworkManager in MainMenu scene.");
            }

            var clone = UnityEngine.Object.Instantiate(source.gameObject);
            clone.name = "NetworkManager";
            SceneManager.MoveGameObjectToScene(clone, activeScene);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.transform.localScale = Vector3.one;

            EditorSceneManager.CloseScene(mainMenuScene, true);
        }
    }
}
