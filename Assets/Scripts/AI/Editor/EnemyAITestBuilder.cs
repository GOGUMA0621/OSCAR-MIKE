using System;
using System.Linq;
using OskarMike.AI.Testing;
using Unity.AI.Navigation;
using Unity.Behavior;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OskarMike.AI.Editor
{
    public static class EnemyAITestBuilder
    {
        public const string Root = "Assets/AI";
        public const string ScenePath = "Assets/Scenes/EnemyAITest.unity";

        public static void BuildExecutable()
        {
            BuildGraph();
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Builds/AI/EnemyAITest.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("AI test executable build failed: " + report.summary.result);
        }

        [MenuItem("Tools/OSKAR MIKE/AI/테스트 씬 생성")]
        public static void Build()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(Root); EnsureFolder(Root + "/Definitions"); EnsureFolder(Root + "/Prefabs");
            var noises = new[]
            {
                Noise(AINoiseKind.Walk, 5, .5f), Noise(AINoiseKind.Sprint, 12, .3f),
                Noise(AINoiseKind.Crouch, 2, .7f), Noise(AINoiseKind.Prone, 1, .9f)
            };
            BehaviorGraph graph = BuildGraph();
            GameObject enemy = BuildEnemy(graph);
            GameObject player = BuildPlayer(noises);
            var defaults = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (defaults != null && !defaults.Contains(enemy))
            { defaults.Add(new NetworkPrefab { Prefab = enemy }); EditorUtility.SetDirty(defaults); }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var floor = Box("바닥", new Vector3(0, -.25f, 0), new Vector3(50, .5f, 50));
            Box("시야 차단 벽", new Vector3(2, 1.5f, 3), new Vector3(.5f, 3, 10));
            Box("닫힌 문 시험", new Vector3(-5, 1.5f, 3), new Vector3(2, 3, .25f));
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 30, -25);
            camera.transform.LookAt(Vector3.zero); camera.gameObject.AddComponent<AudioListener>();
            var route = Route("순찰 경로", new[] { new Vector3(-8, 0, 0), new Vector3(-8, 0, 8), new Vector3(-2, 0, 8), new Vector3(-2, 0, 0) });
            for (int i = 0; i < 3; i++)
            {
                Vector3 center = new Vector3(-12 + i * 12, 0, 14);
                var zone = new GameObject($"증원 구역 {i + 1}").AddComponent<ReinforcementZone>();
                zone.transform.position = center;
                zone.enemyPrefab = enemy.GetComponent<EnemyAgent>();
                zone.returnRoute = Route($"증원 순찰 경로 {i + 1}", new[] { center + Vector3.left * 3, center + Vector3.right * 3 });
                zone.spawnPoints = Enumerable.Range(0, 4).Select(n =>
                {
                    var point = new GameObject($"스폰 지점 {n + 1}").transform;
                    point.SetParent(zone.transform); point.position = center + new Vector3((n % 2) * 2, 0, (n / 2) * 2);
                    return point;
                }).ToArray();
            }
            for (int distance = 1; distance <= 12; distance++)
            {
                GameObject marker = new GameObject($"거리 표시 {distance}m");
                marker.transform.position = new Vector3(-18, .03f, -10 + distance);
                var label = marker.AddComponent<TextMesh>(); label.text = distance + "m";
                label.characterSize = .3f; label.fontSize = 40; label.color = Color.yellow;
                marker.transform.rotation = Quaternion.Euler(90, 0, 0);
            }
            var surface = new GameObject("NavMesh").AddComponent<NavMeshSurface>();
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            AssetDatabase.CreateAsset(surface.navMeshData, AssetDatabase.GenerateUniqueAssetPath(Root + "/EnemyAITestNavMesh.asset"));

            var network = new GameObject("AI 테스트 네트워크");
            var transport = network.AddComponent<UnityTransport>();
            var manager = network.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport, PlayerPrefab = player,
                EnableSceneManagement = true, ConnectionApproval = true
            };
            var list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            list.Add(new NetworkPrefab { Prefab = player }); list.Add(new NetworkPrefab { Prefab = enemy });
            string listPath = Root + "/TestNetworkPrefabs.asset";
            var existing = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(listPath);
            if (existing != null) { EditorUtility.CopySerialized(list, existing); Object.DestroyImmediate(list); list = existing; }
            else AssetDatabase.CreateAsset(list, listPath);
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            var test = new GameObject("AI 테스트 시작").AddComponent<EnemyAITestBootstrap>();
            test.manager = manager; test.patrolPrefab = enemy.GetComponent<EnemyAgent>(); test.patrolRoute = route;
            test.patrolSpawn = new GameObject("초기 순찰 적 위치").transform;
            test.patrolSpawn.position = new Vector3(-8, 0, 0);
            test.overviewCamera = camera;
            test.koreanFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Pretendard-Regular.otf");
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[적 AI] Behavior 그래프, 프리팹, 소리 정의 및 EnemyAITest 씬 생성 완료.");
        }

        private static GameObject Box(string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.position = position; go.transform.localScale = scale; return go;
        }
        private static EnemyPatrolRoute Route(string name, Vector3[] positions)
        {
            var route = new GameObject(name).AddComponent<EnemyPatrolRoute>();
            route.points = positions.Select((position, i) =>
            { var point = new GameObject($"순찰 지점 {i + 1}").transform; point.SetParent(route.transform); point.position = position; return point; }).ToArray();
            return route;
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/'); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
        private static AINoiseDefinition Noise(AINoiseKind kind, float radius, float interval)
        {
            string path = Root + "/Definitions/" + kind + ".asset";
            var definition = AssetDatabase.LoadAssetAtPath<AINoiseDefinition>(path);
            if (definition != null) return definition;
            definition = ScriptableObject.CreateInstance<AINoiseDefinition>();
            definition.kind = kind; definition.hearingRadius = radius; definition.interval = interval;
            AssetDatabase.CreateAsset(definition, path); return definition;
        }
        private static GameObject BuildPlayer(AINoiseDefinition[] noises)
        {
            const string source = "Assets/Prefabs/Player.prefab";
            var contents = PrefabUtility.LoadPrefabContents(source);
            var emitter = contents.GetComponent<PlayerFootstepEmitter>() ?? contents.AddComponent<PlayerFootstepEmitter>();
            emitter.walk = noises[0]; emitter.sprint = noises[1]; emitter.crouch = noises[2]; emitter.prone = noises[3];
            PrefabUtility.SaveAsPrefabAsset(contents, source);
            if (contents.GetComponent<EnemyAITestPlayer>() == null) contents.AddComponent<EnemyAITestPlayer>();
            var result = PrefabUtility.SaveAsPrefabAsset(contents, Root + "/Prefabs/TestPlayer.prefab");
            PrefabUtility.UnloadPrefabContents(contents); return result;
        }
        private static GameObject BuildEnemy(BehaviorGraph graph)
        {
            var go = new GameObject("PatrolEnemy");
            go.AddComponent<NetworkObject>(); go.AddComponent<NetworkTransform>();
            var collider = go.AddComponent<CapsuleCollider>(); collider.center = Vector3.up; collider.height = 2; collider.radius = .4f;
            var agent = go.AddComponent<NavMeshAgent>(); agent.radius = .4f; agent.height = 2; agent.stoppingDistance = .25f;
            var audio = go.AddComponent<AudioSource>(); audio.spatialBlend = 1; audio.playOnAwake = false;
            go.AddComponent<BehaviorGraphAgent>().Graph = graph;
            go.AddComponent<EnemyAgent>();
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "임시 적 모델"; visual.transform.SetParent(go.transform); visual.transform.localPosition = Vector3.up;
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            var forward = Box("시선 방향", new Vector3(0, 1.6f, .5f), new Vector3(.15f, .15f, .6f));
            forward.transform.SetParent(go.transform); Object.DestroyImmediate(forward.GetComponent<Collider>());
            var result = PrefabUtility.SaveAsPrefabAsset(go, Root + "/Prefabs/PatrolEnemy.prefab");
            Object.DestroyImmediate(go); return result;
        }

        private static BehaviorGraph BuildGraph() => EnemyBehaviorGraphBuilder.EnsureGraph();
    }
}
