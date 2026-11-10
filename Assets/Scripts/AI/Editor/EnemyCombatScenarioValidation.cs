using System;
using OskarMike.AI.Testing;
using OskarMike.Network.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace OskarMike.AI.Editor
{
    // Real host + authored NavMesh. Tests temporal behavior rather than calling Recover directly.
    public static class EnemyCombatScenarioValidation
    {
        private const string Key = "OskarMike.CombatScenario";
        private static int phase;
        private static double deadline, at, hitAt;
        private static float healthAfterHit;
        private static EnemyAITestBootstrap test;
        private static EnemyAgent enemy;
        private static EnemyHealth health;
        private static EnemyCombatController combat;
        private static NetworkObject player, intruder;
        private static GameObject wall;

        [InitializeOnLoadMethod]
        private static void Register() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }
        [MenuItem("Tools/OSKAR MIKE/AI/엄폐 회복 및 타깃 전환 검증")]
        public static void Run()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(EnemyAITestBuilder.ScenePath);
            SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
        }
        private static void Check(bool value, string name)
        { if (!value) throw new InvalidOperationException(name); Debug.Log("[전투 시나리오 통과] " + name); }
        private static void MovePlayer(NetworkObject value, Vector3 feet)
        {
            var controller = value.GetComponent<CharacterController>();
            controller.enabled = false;
            value.transform.position = feet + Vector3.up * (controller.height * .5f - controller.center.y + .1f);
            controller.enabled = true;
            value.GetComponent<PlayerNetworkController>().enabled = false;
            var testInput = value.GetComponent<EnemyAITestPlayer>(); if (testInput != null) testInput.enabled = false;
            Physics.SyncTransforms();
        }
        private static void Poll()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
            EditorApplication.isPaused = false; EditorApplication.QueuePlayerLoopUpdate();
            try
            {
                double now = Time.timeAsDouble;
                if (phase == 0)
                {
                    deadline = EditorApplication.timeSinceStartup + 60;
                    test = UnityEngine.Object.FindFirstObjectByType<EnemyAITestBootstrap>();
                    test.StartHost(); phase = 1; return;
                }
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException($"전투 시나리오 시간 초과: {phase}, {combat?.DiagnosticState}");
                if (phase == 1)
                {
                    enemy = UnityEngine.Object.FindFirstObjectByType<EnemyAgent>();
                    if (enemy == null || !enemy.IsSpawned || !test.manager.IsHost) return;
                    player = test.manager.LocalClient.PlayerObject;
                    enemy.canRequestReinforcements = false; enemy.route = null; enemy.patrolSpeed = 0;
                    enemy.GetComponent<NavMeshAgent>().Warp(new Vector3(-12, 0, 0));
                    enemy.transform.rotation = Quaternion.identity;
                    MovePlayer(player, new Vector3(-12, 0, 5));
                    health = enemy.GetComponent<EnemyHealth>(); combat = enemy.GetComponent<EnemyCombatController>();
                    at = now + 4; phase = 2; return;
                }
                if (phase == 2)
                {
                    if (now < at) return;
                    Check(enemy.ShotSequence > 0 && enemy.Target == player, "공격 슬롯에서 사격 시작");
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name = "회복 검증 엄폐물";
                    Vector3 start = enemy.transform.position;
                    wall.transform.position = new Vector3(start.x + 1.5f, 1.5f, (start.z + player.transform.position.z) * .5f);
                    wall.transform.localScale = new Vector3(2, 3, .4f); Physics.SyncTransforms();
                    health.ApplyDamage(50, player); hitAt = now; phase = 3; return;
                }
                if (phase == 3)
                {
                    if (now - hitAt < combat.recoveryQuietTime && health.Current > 50) throw new InvalidOperationException("피격 후 3초 이전 회복");
                    if (health.Current <= 50) return;
                    Check(combat.Recovering && combat.HoldingCover && combat.SafeCover(enemy.transform.position), "엄폐 도착 후 회복");
                    Check(!combat.HasAttackSlot && !combat.CanFire, "회복 중 공격 슬롯과 사격 차단");
                    health.ApplyDamage(5, player); healthAfterHit = health.Current; hitAt = now; phase = 4; return;
                }
                if (phase == 4)
                {
                    if (now - hitAt < combat.recoveryQuietTime)
                    {
                        if (health.Current > healthAfterHit) throw new InvalidOperationException("재피격 후 회복 대기 미적용");
                        return;
                    }
                    if (health.Recovery != RecoveryState.Complete) return;
                    Check(Mathf.Approximately(health.Current, 75), "재피격 후 회복 재개 및 75% 종료");
                    UnityEngine.Object.DestroyImmediate(wall); wall = null;
                    MovePlayer(player, enemy.transform.position + enemy.transform.forward * 4);
                    at = now + 3; phase = 5; return;
                }
                if (phase == 5)
                {
                    if (now < at) return;
                    intruder = UnityEngine.Object.Instantiate(test.manager.NetworkConfig.PlayerPrefab).GetComponent<NetworkObject>();
                    intruder.Spawn();
                    Vector3 direction = Vector3.ProjectOnPlane(player.transform.position - enemy.transform.position, Vector3.up).normalized;
                    MovePlayer(intruder, enemy.transform.position + direction * 3 + Vector3.Cross(Vector3.up, direction));
                    at = now + .3; phase = 6; return;
                }
                if (phase == 6)
                {
                    if (now < at) return;
                    Check(enemy.Target == player && enemy.CanSee(intruder), "단순 난입은 기존 타깃 유지");
                    health.ApplyDamage(1, intruder); at = now; phase = 7; return;
                }
                if (phase == 7)
                {
                    if (now - at < combat.reactionDelay)
                    {
                        if (enemy.Target == intruder) throw new InvalidOperationException("반응 시간 이전 타깃 전환");
                        return;
                    }
                    if (enemy.Target != intruder) return;
                    Check(combat.LockActive, "피격 반응 지연 후 타깃 잠금");
                    health.ApplyDamage(1, player); at = now; phase = 8; return;
                }
                if (phase == 8)
                {
                    if (now - at < 2.5)
                    {
                        if (enemy.Target == player) throw new InvalidOperationException("잠금 중 원래 타깃으로 전환");
                        return;
                    }
                    Check(health.Recovery == RecoveryState.Complete, "회복 완료 상태 유지");
                    Finish(0);
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(1); }
        }
        private static void Finish(int code)
        {
            SessionState.SetBool(Key, false); phase = 0;
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            if (Application.isBatchMode) EditorApplication.Exit(code); else EditorApplication.ExitPlaymode();
        }
    }
}
