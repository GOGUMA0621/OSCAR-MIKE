using System;
using System.IO;
using System.Linq;
using OskarMike.Network.Player;
using Unity.Behavior;
using UnityEngine;
using UnityEngine.AI;

namespace OskarMike.AI.Testing
{
    // Opt-in standalone, multi-process regression probe. Not active in normal play.
    public sealed class EnemyAINetworkProbe : MonoBehaviour
    {
        private EnemyAITestBootstrap test;
        private bool host, late, connected, stimulated, ready, clientSimulation;
        private int maxPlayers, maxEnemies;
        private uint maxAlerts, maxShots, presentedAlerts;
        private double started, allJoinedAt = -1;
        private string id;
        private bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!args.Contains("-aiNetworkHost") && !args.Contains("-aiNetworkClient")) return;
            if (FindFirstObjectByType<EnemyAITestBootstrap>() != null)
                new GameObject("AI 네트워크 검증").AddComponent<EnemyAINetworkProbe>();
        }
        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            host = args.Contains("-aiNetworkHost"); late = args.Contains("-aiLate");
            int index = Array.IndexOf(args, "-aiId");
            id = host ? "host" : index >= 0 && index + 1 < args.Length ? args[index + 1] : "client";
            if (id.Any(c => !char.IsLetterOrDigit(c))) throw new ArgumentException("Invalid probe id");
            Application.runInBackground = true;
            started = Time.realtimeSinceStartupAsDouble;
            test = FindFirstObjectByType<EnemyAITestBootstrap>();
            if (host) test.StartHost(); else test.StartClient();
        }
        private void Update()
        {
            if (test == null || finished) return;
            if (Time.realtimeSinceStartupAsDouble - started > 90) { Finish(false, "timeout"); return; }
            if (!test.manager.IsConnectedClient)
            {
                if (connected && !host)
                    Finish(maxPlayers == 4 && maxEnemies >= 3 && maxAlerts > 0 && maxShots > 0 &&
                        !clientSimulation && (!late || presentedAlerts == 0), "host shutdown received");
                return;
            }
            connected = true;
            var local = test.manager.LocalClient.PlayerObject;
            if (local != null)
            {
                local.GetComponent<PlayerNetworkController>().enabled = false;
                local.GetComponent<EnemyAITestPlayer>().enabled = false;
            }
            maxPlayers = Mathf.Max(maxPlayers, FindObjectsByType<PlayerNetworkController>(FindObjectsSortMode.None).Count(p => p.IsSpawned));
            var enemies = FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None).Where(e => e.IsSpawned).ToArray();
            maxEnemies = Mathf.Max(maxEnemies, enemies.Length);
            foreach (var enemy in enemies)
            {
                maxAlerts = Math.Max(maxAlerts, enemy.AlertSequence); maxShots = Math.Max(maxShots, enemy.ShotSequence);
                presentedAlerts = Math.Max(presentedAlerts, enemy.PresentedAlertCount);
                if (!host && (enemy.GetComponent<BehaviorGraphAgent>().enabled || enemy.GetComponent<NavMeshAgent>().enabled)) clientSimulation = true;
            }
            if (!host) return;
            if (!stimulated && maxPlayers >= 3 && local != null && enemies.Length == 1)
            {
                var cc = local.GetComponent<CharacterController>(); cc.enabled = false;
                local.transform.position = enemies[0].transform.position + enemies[0].transform.forward * 4;
                local.transform.position += Vector3.up * (cc.height * .5f - cc.center.y + .1f);
                cc.enabled = true; Physics.SyncTransforms(); stimulated = true;
            }
            if (!ready && maxEnemies >= 3 && maxShots > 0)
            { Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/ai-network-ready.txt", "ready"); ready = true; }
            if (maxPlayers == 4 && allJoinedAt < 0) allJoinedAt = Time.realtimeSinceStartupAsDouble;
            if (allJoinedAt >= 0 && Time.realtimeSinceStartupAsDouble - allJoinedAt > 5)
                Finish(maxEnemies >= 3 && maxAlerts > 0 && maxShots > 0, "four-player replication");
        }
        private void Finish(bool success, string reason)
        {
            finished = true;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ai-network-" + id + ".txt",
                $"{(success ? "SUCCESS" : "FAIL")} {reason}\nplayers={maxPlayers}, enemies={maxEnemies}, alerts={maxAlerts}, shots={maxShots}, presentedAlerts={presentedAlerts}, clientSimulation={clientSimulation}, late={late}");
            test.manager.Shutdown();
            Application.Quit(success ? 0 : 1);
        }
    }
}
