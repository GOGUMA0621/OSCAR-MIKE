using Unity.Netcode;
using UnityEngine;

namespace OskarMike.Items
{
    /// <summary>
    /// 팀당 1개의 더플백을 서버에서 스폰한다. GameMap 등 게임플레이 씬에 배치.
    /// </summary>
    public sealed class DuffelBagSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject duffelBagPrefab;
        [Tooltip("비어 있으면 이 오브젝트 위치에 스폰")]
        [SerializeField] private Transform spawnPoint;

        private bool spawned;

        private void Start() => TrySpawn();

        private void Update()
        {
            // 네트워크가 씬 로드 이후에 시작되는 경우(테스트 씬 직접 실행 등)를 대비
            if (!spawned) TrySpawn();
        }

        [ContextMenu("Spawn Duffel Bag (Server)")]
        public void TrySpawn()
        {
            if (spawned) return;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer) return;
            if (duffelBagPrefab == null)
            {
                Debug.LogWarning("[DuffelBagSpawner] duffelBagPrefab 이 지정되지 않았습니다.");
                spawned = true;
                return;
            }

            spawned = true;
            Transform point = spawnPoint != null ? spawnPoint : transform;
            NetworkObject instance = Instantiate(duffelBagPrefab, point.position, point.rotation);
            instance.Spawn(true);
        }
    }
}
