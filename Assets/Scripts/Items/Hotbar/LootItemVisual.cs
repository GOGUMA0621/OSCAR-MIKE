using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Items
{
    /// <summary>
    /// 루트 아이템 NetworkPrefab 의 "보이기만 하는" 사본 생성 (손에 든 아이템 표시·슬롯 아이콘 공용).
    /// 비활성 부모 아래에서 복제해 Awake 를 막고, 스크립트·NetworkObject·물리를 제거한다.
    /// </summary>
    public static class LootItemVisual
    {
        /// <summary>
        /// parent 아래에 피벗을 만들고 그 아래 사본을 둔다. 경계 중심이 피벗 원점에 오고,
        /// 가장 긴 변이 maxSize 를 넘으면 축소된다 (maxSize ≤ 0 이면 크기 유지). 렌더러가 없으면 null.
        /// </summary>
        public static Transform Create(GameObject prefab, Transform parent, float maxSize, ShadowCastingMode shadows, string name)
        {
            var staging = new GameObject("staging");
            staging.SetActive(false);
            var copy = Object.Instantiate(prefab, staging.transform, false);
            copy.name = prefab.name;
            Strip(copy);

            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            copy.transform.SetParent(pivot, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            Object.Destroy(staging);

            var renderers = copy.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Object.DestroyImmediate(pivot.gameObject);
                return null;
            }

            Bounds bounds = WorldBounds(renderers);
            foreach (var r in renderers) r.shadowCastingMode = shadows;

            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = maxSize > 0f && largest > maxSize && largest > 0.0001f ? maxSize / largest : 1f;
            Vector3 center = pivot.InverseTransformPoint(bounds.center);
            copy.transform.localScale *= scale;
            copy.transform.localPosition = -center * scale;
            return pivot;
        }

        public static Bounds WorldBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        private static void Strip(GameObject go)
        {
            // RequireComponent 의존 때문에 스크립트(NetworkBehaviour 포함)를 먼저, NetworkObject 를 마지막에 지운다.
            var behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = behaviours.Length - 1; i >= 0; i--)
                if (behaviours[i] != null && !(behaviours[i] is NetworkObject)) Object.DestroyImmediate(behaviours[i]);
            foreach (var networkObject in go.GetComponentsInChildren<NetworkObject>(true)) Object.DestroyImmediate(networkObject);
            foreach (var joint in go.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
            foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);
        }
    }
}
