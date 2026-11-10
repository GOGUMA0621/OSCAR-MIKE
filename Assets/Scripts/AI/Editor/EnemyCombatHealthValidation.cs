using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using OskarMike.AI.Testing;

namespace OskarMike.AI.Editor
{
    public static class EnemyCombatHealthValidation
    {
        private const string Key = "OskarMike.CombatHealthValidation";
        private static double deadline;
        private static bool started;
        [InitializeOnLoadMethod]
        private static void Register() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }

        [MenuItem("Tools/OSKAR MIKE/AI/서버 체력 검증")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(EnemyAITestBuilder.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }
        private static void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException(name); Debug.Log("[서버 체력 검증 통과] " + name); }
        private static void Poll()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.QueuePlayerLoopUpdate();
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            try
            {
                var test = UnityEngine.Object.FindFirstObjectByType<EnemyAITestBootstrap>();
                if (!started)
                {
                    started = true; deadline = EditorApplication.timeSinceStartup + 20;
                    Check(test != null, "테스트 씬 로드"); test.StartHost(); return;
                }
                if (EditorApplication.timeSinceStartup >= deadline) throw new InvalidOperationException("호스트 준비 제한 시간 초과");
                var enemy = UnityEngine.Object.FindFirstObjectByType<EnemyAgent>();
                if (enemy == null || !enemy.IsSpawned || !test.manager.IsHost) return;
                enemy.enabled = false; // Isolate health API from tactical healing.
                var health = enemy.GetComponent<EnemyHealth>();
                var player = test.manager.LocalClient.PlayerObject;
                Check(health != null && health.Current == 100, "서버 초기 체력");
                Check(!health.ApplyDamage(float.PositiveInfinity) && !health.ApplyDamage(-1), "비정상 피해 거부");
                Check(health.ApplyHit(25, OskarMike.Network.Player.HitRegion.Head, player) && health.Current == 50, "적 머리 2배, 정확히 50% 피해 적용");
                Check(health.Recovery == RecoveryState.Unused, "실제 회복 전 횟수 미소모");
                health.Recover(10, .75f);
                Check(health.Current == 60 && health.Recovery == RecoveryState.InProgress, "회복 시작");
                Check(health.ApplyHit(5, OskarMike.Network.Player.HitRegion.Body, player), "적 몸통 1배");
                Check(health.Recovery == RecoveryState.InProgress && health.Current == 55, "피격 중 회복 상태 유지");
                health.Recover(100, .75f);
                Check(health.Current == 75 && health.Recovery == RecoveryState.Complete, "75% 상한과 완료");
                health.ApplyDamage(30, player); health.Recover(100, .75f);
                Check(health.Current == 45, "두 번째 회복 거부");
                int deaths = 0; health.Died += () => deaths++;
                health.ApplyDamage(100, player);
                Check(health.Dead && deaths == 1 && !enemy.enabled, "치명타 AI 정지");
                Check(!health.ApplyDamage(1, player) && deaths == 1, "중복 사망 방지");
                var playerHealth = player.GetComponent<OskarMike.Network.Player.PlayerHealth>();
                Check(playerHealth != null && playerHealth.Current == 100, "플레이어 초기 체력");
                Check(!playerHealth.ApplyHit(float.NaN, OskarMike.Network.Player.HitRegion.Head) &&
                      !playerHealth.ApplyDamage(-1) &&
                      !playerHealth.ApplyHit(10, (OskarMike.Network.Player.HitRegion)255), "플레이어 비정상 피해 거부");
                Check(playerHealth.ApplyHit(10, OskarMike.Network.Player.HitRegion.Body) &&
                      playerHealth.Current == 90, "플레이어 몸통 1배");
                Check(playerHealth.ApplyHit(10, OskarMike.Network.Player.HitRegion.Head) &&
                      playerHealth.Current == 70, "플레이어 머리 2배");
                int playerDeaths = 0;
                playerHealth.Died += () => playerDeaths++;
                Check(playerHealth.ApplyDamage(100) && playerHealth.Current == 0 &&
                      playerDeaths == 1 && !playerHealth.ApplyDamage(1), "플레이어 사망 단일 처리");
                var controller = player.GetComponent<OskarMike.Network.Player.PlayerNetworkController>();
                controller.EnableGameplayInput();
                Check(!controller.GameplayInputEnabled, "사망 입력 재활성화 차단");
                Vector3 deadPosition = controller.transform.position;
                Quaternion deadRotation = controller.transform.rotation;
                var move = typeof(OskarMike.Network.Player.PlayerNetworkController).GetMethod("MoveServerRpc",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                move.Invoke(controller, new object[] { Vector2.one, 90f, true, false, true, true, true, 1f });
                Check(controller.transform.position == deadPosition && controller.transform.rotation == deadRotation &&
                      controller.ActionState == OskarMike.Network.Player.PlayerActionState.None &&
                      controller.Lean == 0, "사망 후 서버 이동 요청 차단");
                Check(!enemy.CanSee(player), "AI 사망 플레이어 감지 제외");
                Finish(0);
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(1); }
        }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); started = false;
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }
    }
}
