using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OskarMike.Items
{
    /// <summary>
    /// 슬롯 아이콘: 아이템 모델을 화면 밖(아래 5km)의 전용 카메라로 한 번 찍어 RenderTexture 로 캐시한다.
    /// 아이콘 이미지를 따로 만들지 않아도 모든 루트 아이템에 쓸 수 있다. 로컬 전용 (네트워크 무관).
    /// </summary>
    public static class LootItemIconCache
    {
        private const int IconResolution = 128;
        private const float FieldOfView = 22f;
        private static readonly Vector3 StudioPosition = new Vector3(0f, -5000f, 0f);
        // 살짝 위에서 비스듬히 내려다보는 각도
        private static readonly Quaternion ModelRotation = Quaternion.Euler(18f, -35f, 0f);

        private static readonly Dictionary<string, RenderTexture> cache = new Dictionary<string, RenderTexture>();
        private static Transform studio;
        private static Camera camera;

        /// <summary>아이콘 (정의나 모델이 없으면 null).</summary>
        public static Texture Get(LootItemDefinition definition)
        {
            if (definition == null || definition.NetworkPrefab == null) return null;
            string key = definition.ItemId;
            if (cache.TryGetValue(key, out RenderTexture cached) && cached != null && cached.IsCreated()) return cached;

            RenderTexture texture = Render(definition.NetworkPrefab.gameObject);
            if (texture != null) cache[key] = texture;
            return texture;
        }

        private static RenderTexture Render(GameObject prefab)
        {
            EnsureStudio();

            var pivot = LootItemVisual.Create(prefab, studio, 0f, ShadowCastingMode.Off, "아이콘 모델");
            if (pivot == null) return null;
            pivot.localRotation = ModelRotation;

            // 회전 후 경계로 거리 계산 → 가장 긴 변이 화면을 채우도록
            Bounds bounds = LootItemVisual.WorldBounds(pivot.GetComponentsInChildren<Renderer>(true));
            float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
            // 경계 구는 긴 물체에서 여유가 크므로 조금 당겨 찍는다
            float distance = radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) * 0.78f;
            camera.transform.SetPositionAndRotation(bounds.center + Vector3.back * distance, Quaternion.identity);
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 1.5f);
            camera.farClipPlane = distance + radius * 1.5f;

            var texture = new RenderTexture(IconResolution, IconResolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "아이템 아이콘 " + prefab.name,
                antiAliasing = 4
            };
            texture.Create();
            camera.targetTexture = texture;
            camera.Render();
            camera.targetTexture = null;

            Object.DestroyImmediate(pivot.gameObject);
            return texture;
        }

        private static void EnsureStudio()
        {
            if (studio != null) return;

            studio = new GameObject("아이템 아이콘 촬영장").transform;
            studio.position = StudioPosition;
            Object.DontDestroyOnLoad(studio.gameObject);

            var cameraGo = new GameObject("아이콘 카메라");
            cameraGo.transform.SetParent(studio, false);
            camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false;   // Render() 로만 그린다
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.fieldOfView = FieldOfView;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            var data = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            // 촬영장 근처만 비추는 조명 (씬에 영향 없음)
            AddLight(new Vector3(-1.5f, 2f, -2.5f), 2.2f);
            AddLight(new Vector3(2f, 0.5f, -1.5f), 1.0f);
        }

        private static void AddLight(Vector3 localPosition, float intensity)
        {
            var go = new GameObject("아이콘 조명");
            go.transform.SetParent(studio, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 30f;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }
    }
}
