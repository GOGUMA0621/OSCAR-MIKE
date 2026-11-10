using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace OskarMike.AI.Editor
{
    // Runs without modifying saved scenes. Also callable using -executeMethod.
    public static class EnemyCombatValidation
    {
        private const string RegressionKey = "OskarMike.CombatRegression";
        [InitializeOnLoadMethod]
        private static void RegisterBatchPump()
        {
            if (!Application.isBatchMode) return;
            EditorApplication.update += () =>
            {
                if (!SessionState.GetBool(RegressionKey, false) || !EditorApplication.isPlaying) return;
                // Headless editors have no visible Game view requesting player-loop updates.
                EditorApplication.isPaused = false;
                EditorApplication.QueuePlayerLoopUpdate();
            };
        }
        public static void RunRegression()
        {
            SessionState.SetBool(RegressionKey, true);
            EnemyAIValidation.Run();
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); Debug.Log("[전투 검증 통과] " + message); }

        [MenuItem("Tools/OSKAR MIKE/AI/전투 구조 및 사선 검증")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AI/Prefabs/PatrolEnemy.prefab");
            Check(prefab != null && prefab.GetComponent<EnemyHealth>() != null && prefab.GetComponent<EnemyCombatController>() != null, "프리팹 체력·전투 연결");
            var first = UnityEngine.Object.Instantiate(prefab);
            var second = UnityEngine.Object.Instantiate(prefab);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var player = new GameObject("검증 플레이어");
            try
            {
                Vector3 origin = new(10000, 10000, 10000);
                first.transform.position = origin; second.transform.position = origin + Vector3.forward * 3;
                wall.transform.position = origin + Vector3.right * 10;
                player.transform.position = origin + Vector3.forward * 2;
                var cc = player.AddComponent<CharacterController>(); cc.center = Vector3.up; cc.height = 2;
                player.AddComponent<OskarMike.Network.Player.PlayerNetworkController>();
                foreach (var go in new[] { first, second })
                    typeof(EnemyCombatController).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(go.GetComponent<EnemyCombatController>(), null);
                Physics.SyncTransforms();
                var query = new CombatPositionQuery(); var enemy = first.GetComponent<EnemyAgent>();
                Vector3 from = origin + Vector3.up, to = from + Vector3.forward * 6;
                Check(query.Blocked(from, to, enemy, true, out bool actor) && actor, "앞 AI가 사격 차단");
                Check(!query.Blocked(from, to, enemy, false, out _), "AI는 엄폐물이 아님");
                second.transform.position = origin + Vector3.right * 5; Physics.SyncTransforms();
                Check(!query.Blocked(from, to, enemy, true, out _), "자신과 플레이어는 사격 차단에서 제외");
                wall.transform.position = origin + Vector3.forward * 4 + Vector3.up;
                wall.transform.localScale = new Vector3(2, 3, .5f); Physics.SyncTransforms();
                Check(query.Blocked(from, to, enemy, true, out actor) && !actor, "벽은 사격 차단");
                Check(query.Blocked(from, to, enemy, false, out _), "벽은 엄폐 제공");
                wall.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
                Check(!query.Blocked(from, to, enemy, true, out _), "트리거는 사격 차단에서 제외");
                var a = first.GetComponent<EnemyCombatController>(); var b = second.GetComponent<EnemyCombatController>();
                Check(CombatSlotManager.Reserve(a, origin), "첫 슬롯 예약");
                Check(!CombatSlotManager.Reserve(b, origin), "중복 슬롯 거부");
                CombatSlotManager.Release(a);
                Check(CombatSlotManager.Reserve(b, origin), "반환된 슬롯 재사용");
                var health = first.GetComponent<EnemyHealth>(); float previous = health.Current;
                Check(!health.ApplyDamage(20) && health.Current == previous, "서버 스폰 전 피해 거부");
                Check(!health.ApplyDamage(float.NaN), "잘못된 피해 거부");
                Debug.Log("[전투 검증 완료] 구조·사선·예약 검증 통과. 네트워크 및 회복 시나리오는 플레이 모드에서 별도 실행하세요.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second);
                UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(player);
            }
        }
    }
}
