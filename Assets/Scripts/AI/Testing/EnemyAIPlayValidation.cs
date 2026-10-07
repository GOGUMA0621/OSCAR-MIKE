using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OskarMike.AI.Testing;
using OskarMike.Network.Player;
using Unity.Behavior;
using Unity.Netcode;


using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace OskarMike.AI.Testing
{
    // Real NGO host + baked NavMesh + authored Behavior graph smoke test, runnable in batch mode.

    public sealed class EnemyAIPlayValidation : MonoBehaviour
    {
        private const string Key = "OskarMike.AI.Validation";
        private readonly List<string> passed = new();
        private int phase;
        private double deadline, timeout;
        private EnemyAITestBootstrap test;
        private EnemyAgent enemy;
        private NetworkObject player;
        private Vector3 initialPosition;
        private double alertAt;
        private int requests;
        private EnemyAgent otherEnemy;
        private int graphRequests;
        private bool finishing;


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            bool requested = Environment.GetCommandLineArgs().Contains("-aiValidate");
#if UNITY_EDITOR
            requested |= UnityEditor.SessionState.GetBool(Key, false);
            UnityEditor.SessionState.SetBool(Key, false);
#endif
            if (requested && Object.FindFirstObjectByType<EnemyAITestBootstrap>() != null)
                new GameObject("AI 자동 검증").AddComponent<EnemyAIPlayValidation>();
        }
        private void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            passed.Add(name); Debug.Log("[AI 검증 통과] " + name);
        }

        private void Wait(int next, double seconds)
        { phase = next; deadline = Time.timeAsDouble + seconds; }

        private void Update()
        {

            if (finishing) return;
            try
            {
                if (timeout == 0) timeout = Time.realtimeSinceStartupAsDouble + 90;
                if (Time.realtimeSinceStartupAsDouble > timeout) throw new TimeoutException("AI smoke test timeout");

                if (Time.timeAsDouble < deadline) return;
                switch (phase)
                {
                    case 0:
                        Application.runInBackground = true;

                        Debug.Log("[AI 검증] 호스트 시작");
                        test = Object.FindFirstObjectByType<EnemyAITestBootstrap>();
                        test.StartHost(); Debug.Log("[AI 검증] 호스트 시작 반환"); Wait(1, .5); break;
                    case 1:
                        player = test.manager.LocalClient.PlayerObject;
                        Check(player != null, "NGO host player spawned");
                        player.GetComponent<PlayerNetworkController>().enabled = false;
                        player.GetComponent<EnemyAITestPlayer>().enabled = false;
                        enemy = Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None).Single(e => e.IsSpawned);
                        Check(enemy.State == EnemyState.Patrol, "Only initial patrol exists");
                        initialPosition = enemy.transform.position; Wait(2, 1.5); break;
                    case 2:
                        Check(Vector3.Distance(initialPosition, enemy.transform.position) > .3f,
                            $"Behavior patrol drives NavMesh movement ({initialPosition} -> {enemy.transform.position})");
                        ResetEnemy(); MovePlayer(new Vector3(-12, 0, 5));
                        enemy.AlertStarted += () => alertAt = Time.timeAsDouble;
                        enemy.ReinforcementRequested += _ => requests++;
                        Wait(3, .5); break;
                    case 3:
                        Check(enemy.State == EnemyState.Alert && enemy.AlertSequence == 1,
                            $"Sight enters warning (state={enemy.State}, canSee={enemy.CanSee(player)}, player={player.transform.position}, eyeTarget={player.GetComponent<CharacterController>().bounds.center}, forward={enemy.transform.forward})");
                        Check(requests == 0 && TotalLiving() == 0 && enemy.ShotSequence == 0, "No early reinforcement or firing");
                        deadline = alertAt + 1.3; phase = 4; break;
                    case 4:
                        Check(requests == 1 && TotalLiving() == 2, "80 percent warning spawns exactly one wave");
                        Check(enemy.ShotSequence == 0, "No firing before warning completes");
                        Wait(5, .6); break;
                    case 5:
                        Check(enemy.State == EnemyState.Shoot && enemy.ShotSequence > 0, "Visible target fires after warning");
                        Check(Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None).Where(e => e.IsSpawned && e != enemy)
                            .All(e => !e.canRequestReinforcements), "Reinforcements cannot call reinforcements");
                        int before = TotalLiving();
                        Check(ReinforcementZone.Request(enemy, player.transform.position) == 0 && TotalLiving() == before,
                            "Cooldown blocks repeated request to sole nearby zone");
                        MovePlayer(new Vector3(20, 0, -20)); Wait(6, .3); break;
                    case 6:
                        Check(enemy.State == EnemyState.Investigate, "Lost sight investigates last seen position");
                        MovePlayer(enemy.transform.position + enemy.transform.forward * 3); Wait(7, .3); break;
                    case 7:
                        Check(enemy.AlertSequence == 1 && requests == 1, "Same encounter reacquires without second warning");
                        DespawnAll(); MovePlayer(new Vector3(20, 0, -20));
                        ResetEnemy(); TestHearingMath();
                        Emit(AINoiseKind.Walk, new Vector3(-12, .2f, -8)); Wait(8, .2); break;
                    case 8:
                        Check(enemy.State == EnemyState.Patrol, "Walk unheard at eight metres");
                        Emit(AINoiseKind.Sprint, new Vector3(-12, .2f, -8)); Wait(9, .3); break;
                    case 9:
                        Check(enemy.State == EnemyState.Investigate && enemy.AlertSequence == 0 && enemy.ShotSequence == 0,
                            "Sprint heard behind enemy; investigation without warning or shot");
                        Check(Vector3.Distance(enemy.LastKnownPosition, new Vector3(-12, .2f, -8)) < .1f,
                            "Sound stores emission position, not live source position");
                        Wait(10, 5); break;
                    case 10:
                        Check(enemy.State == EnemyState.Patrol, "Investigation expires and resumes patrol");
                        ResetEnemy(); MovePlayer(new Vector3(-12, 0, 5)); Wait(11, .4); break;
                    case 11:
                        Check(enemy.State == EnemyState.Alert, "Cancellation test warning started");
                        enemy.NetworkObject.Despawn(true); MovePlayer(new Vector3(20, 0, -20)); Wait(12, 1.5); break;
                    case 12:
                        Check(TotalLiving() == 0, "Despawn before signal cancels reinforcement");
                        TestZoneBudget();
                        DespawnAll(); MovePlayer(new Vector3(20, 0, -20)); ResetEnemy();
                        otherEnemy = Object.Instantiate(test.patrolPrefab, new Vector3(-20, 0, 0), Quaternion.identity);
                        otherEnemy.Initialize(null); otherEnemy.NetworkObject.Spawn(true);
                        enemy.Write("SightRange", 2f);
                        enemy.Write("AlertDuration", .6f);
                        enemy.Write("SignalProgress", .5f);
                        enemy.Write("ChaseSpeed", 1.25f);
                        enemy.Write("InvestigationWait", .5f);
                        Check(Mathf.Approximately(otherEnemy.sightRange, 12) && Mathf.Approximately(otherEnemy.alertDuration, 1.5f),
                            "Blackboard overrides are isolated between enemies");
                        Check(Mathf.Approximately(test.patrolPrefab.sightRange, 12), "Runtime tuning leaves prefab defaults intact");
                        MovePlayer(new Vector3(-12, 0, 5)); Wait(14, .3); break;
                    case 13:
                        Check(!test.manager.IsListening, "Host shutdown completes");
                        Finish(null); break;
                    case 14:
                        Check(!enemy.HasVisual && enemy.AlertSequence == 0, "Blackboard SightRange changes actual sensing");
                        enemy.ReinforcementRequested += _ => graphRequests++;
                        enemy.Write("SightRange", 12f); Wait(15, .2); break;
                    case 15:
                        Check(enemy.State == EnemyState.Alert && graphRequests == 0, "Graph begins warning using modified tuning");
                        MovePlayer(new Vector3(20, 0, -20)); Wait(16, .7); break;
                    case 16:
                        Check(graphRequests == 1 && !enemy.Read<bool>("AlertInProgress") && enemy.ShotSequence == 0,
                            "Bound alert duration and signal progress survive visual loss, request once, no blind firing");
                        DespawnAll(); ResetEnemy();
                        enemy.Write("ChaseSpeed", 1.25f); enemy.Write("InvestigationWait", .5f);
                        Emit(AINoiseKind.Sprint, new Vector3(-12, .2f, -2)); Wait(17, .4); break;
                    case 17:
                        Check(enemy.State == EnemyState.Investigate && Mathf.Approximately(enemy.GetComponent<NavMeshAgent>().speed, 1.25f),
                            "Main Blackboard speed reaches investigation subgraph");
                        enemy.Write("ChaseSpeed", 4f);
                        Emit(AINoiseKind.Sprint, new Vector3(-10, .2f, -2)); Wait(18, .4); break;
                    case 18:
                        Check(enemy.Read<int>("InvestigatedRevision") == enemy.ClueRevision && enemy.HasClue &&
                            Vector3.Distance(enemy.Read<Vector3>("InvestigationDestination"), new Vector3(-10, .2f, -2)) < .1f,
                            "New sound restarts investigation without clearing the new clue");
                        Check(Mathf.Approximately(enemy.GetComponent<NavMeshAgent>().speed, 4f), "Live Blackboard speed edits reach active movement");
                        Wait(19, 2); break;
                    case 19:
                        Check(enemy.State == EnemyState.Patrol && !enemy.HasClue, "Bound investigation wait completes and returns to patrol");
                        ResetEnemy(); graphRequests = 0;
                        enemy.ReinforcementRequested += _ => graphRequests++;
                        MovePlayer(new Vector3(-12, 0, 5)); Wait(20, .3); break;
                    case 20:
                        Check(enemy.State == EnemyState.Alert, "Disable cancellation warning started");
                        enemy.enabled = false; Wait(21, 1.5); break;
                    case 21:
                        Check(graphRequests == 0 && !enemy.GetComponent<BehaviorGraphAgent>().enabled && !enemy.GetComponent<NavMeshAgent>().enabled,
                            "Disabling enemy cancels graph and pending reinforcement");
                        test.manager.Shutdown(); Wait(13, .5); break;
                }
            }
            catch (Exception ex) { Finish(ex); }
        }

        private void ResetEnemy()
        {
            if (enemy != null && enemy.IsSpawned) enemy.NetworkObject.Despawn(true);
            enemy = Object.Instantiate(test.patrolPrefab, new Vector3(-12, 0, 0), Quaternion.identity);
            enemy.Initialize(null); enemy.NetworkObject.Spawn(true);
            // Keep patrol stationary for reproducible stimulus distances; graph remains running.
            enemy.patrolSpeed = 0;
            Physics.SyncTransforms();
        }
        private void MovePlayer(Vector3 position)
        {
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
            position.y += cc.height * .5f - cc.center.y + .1f;
            player.transform.position = position; cc.enabled = true; Physics.SyncTransforms();
        }
        private int TotalLiving() => Object.FindObjectsByType<ReinforcementZone>(FindObjectsSortMode.None).Sum(z => z.LivingCount);
        private void DespawnAll()
        {
            foreach (var actor in Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
                if (actor.IsSpawned) actor.NetworkObject.Despawn(true);
        }
        private void Emit(AINoiseKind kind, Vector3 position)
        {
            var definition = Definition(kind);
            ServerNoiseBus.Emit(definition, position, player);
        }
        private void TestHearingMath()
        {
            var definition = Definition(AINoiseKind.Walk);
            var noise = new AINoise(definition, new Vector3(1.5f, 1.5f, 3), player, Time.timeAsDouble);
            Check(AISpatialQuery.TryHear(new Vector3(1.5f, 1.5f, 7), noise, ~0, out _), "Unobstructed walk heard at four metres");
            Check(!AISpatialQuery.TryHear(new Vector3(5.5f, 1.5f, 3), noise, ~0, out _), "Wall halves walk hearing range");
            foreach (AINoiseKind kind in Enum.GetValues(typeof(AINoiseKind)))
            {
                var profile = Definition(kind);
                noise = new AINoise(profile, new Vector3(-20, 1, -15), player, Time.timeAsDouble);
                Check(AISpatialQuery.TryHear(noise.Position + Vector3.forward * (profile.hearingRadius - .01f), noise, ~0, out _) &&
                    !AISpatialQuery.TryHear(noise.Position + Vector3.forward * (profile.hearingRadius + .01f), noise, ~0, out _), "Hearing radius boundary " + kind);
            }
        }
        private void TestZoneBudget()
        {
            var zones = Object.FindObjectsByType<ReinforcementZone>(FindObjectsSortMode.None);
            foreach (var zone in zones) zone.enabled = false;
            var custom = new GameObject("Budget test zone").AddComponent<ReinforcementZone>();
            custom.enemyPrefab = test.patrolPrefab; custom.returnRoute = test.patrolRoute;
            custom.transform.position = new Vector3(-12, 0, 5); custom.cooldown = 0; custom.livingLimit = 3;
            custom.spawnPoints = Enumerable.Range(0, 4).Select(i =>
            { var point = new GameObject().transform; point.position = new Vector3(-18 + i * 2, 0, 10); return point; }).ToArray();
            ResetEnemy();
            Check(ReinforcementZone.Request(enemy, Vector3.zero) == 2, "Wave size respected");
            Physics.SyncTransforms();
            Check(ReinforcementZone.Request(enemy, Vector3.zero) == 1 && custom.LivingCount == 3, "Wave clipped to remaining population capacity");
            Check(ReinforcementZone.Request(enemy, Vector3.zero) == 0, "Population cap rejects excess requests");
            var reinforcement = Object.FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None).First(e => e.IsSpawned && e != enemy);
            reinforcement.ReleasePopulation(); reinforcement.ReleasePopulation();
            Check(custom.LivingCount == 2, "Population release is idempotent");
            reinforcement.NetworkObject.Despawn(true);
            Check(custom.LivingCount == 2, "Despawn does not double-release death slot");
        }
        private AINoiseDefinition Definition(AINoiseKind kind)
        {
            var emitter = test.manager.NetworkConfig.PlayerPrefab.GetComponent<PlayerFootstepEmitter>();
            return kind switch { AINoiseKind.Walk => emitter.walk, AINoiseKind.Sprint => emitter.sprint,
                AINoiseKind.Crouch => emitter.crouch, _ => emitter.prone };
        }
        private void Finish(Exception error)
        {
            finishing = true;


            string report = $"Passed: {passed.Count}\n" + string.Join("\n", passed) + "\n" + (error == null ? "SUCCESS" : error.ToString());
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/ai-validation.txt", report);
            if (error != null) Debug.LogException(error); else Debug.Log("[AI 검증] 모든 항목 통과");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(error == null ? 0 : 1);
#else
            Application.Quit(error == null ? 0 : 1);
#endif
        }
    }
}

