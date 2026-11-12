using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 탄착 효과 풀 (시각 전용). 파티클 프리팹 인스턴스를 재사용하고,
    /// 탄흔은 개수 상한 + 수명이 지나면 숨긴다. 월드 루트에 생성되며 소유자가 <see cref="Dispose"/> 한다.
    /// </summary>
    public sealed class WeaponImpactPool
    {
        private readonly GameObject prefab;
        private readonly int capacity;
        private readonly float lifetime;
        private readonly List<GameObject> items = new List<GameObject>();
        private readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        private readonly List<float> expiresAt = new List<float>();
        private int next;

        /// <param name="lifetime">0 이하 = 파티클 재생이 끝날 때까지 (파티클용), 양수 = 그 시간 뒤 숨김 (탄흔용)</param>
        public WeaponImpactPool(GameObject prefab, int capacity, float lifetime = 0f)
        {
            this.prefab = prefab;
            this.capacity = Mathf.Max(1, capacity);
            this.lifetime = lifetime;
        }

        public bool IsValid => prefab != null;

        public void Spawn(Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;
            int index = Acquire();
            var go = items[index];
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            var ps = systems[index];
            if (ps != null)
            {
                ps.Clear(true);
                ps.Play(true);
            }
            expiresAt[index] = lifetime > 0f ? Time.time + lifetime : float.PositiveInfinity;
        }

        /// <summary>수명이 지난 탄흔 숨기기 (매 프레임 호출).</summary>
        public void Tick()
        {
            if (lifetime <= 0f) return;
            float now = Time.time;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].activeSelf && now >= expiresAt[i]) items[i].SetActive(false);
        }

        public void Dispose()
        {
            foreach (var go in items) if (go != null) Object.Destroy(go);
            items.Clear();
            systems.Clear();
            expiresAt.Clear();
        }

        private int Acquire()
        {
            // 쉬고 있는 인스턴스 우선
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                bool idle = systems[i] != null ? !systems[i].IsAlive(true) : !items[i].activeSelf;
                if (idle) return i;
            }
            if (items.Count < capacity)
            {
                var go = Object.Instantiate(prefab);
                go.name = prefab.name;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
                items.Add(go);
                systems.Add(go.GetComponent<ParticleSystem>());
                expiresAt.Add(float.PositiveInfinity);
                return items.Count - 1;
            }
            // 가득 차면 가장 오래된 것부터 재사용
            int index = next;
            next = (next + 1) % items.Count;
            return index;
        }
    }
}
