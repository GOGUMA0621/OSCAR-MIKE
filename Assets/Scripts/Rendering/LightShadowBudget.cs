using System.Collections.Generic;
using UnityEngine;

namespace OskarMike.Rendering
{
    /// <summary>
    /// 실시간 라이트 비용 조절기 (클라이언트 로컬, 씬당 1개).
    /// Forward+에서는 라이트 개수 자체보다 그림자가 비싸므로,
    /// 현재 렌더링 카메라에 가장 가까운 <see cref="maxShadowedLights"/>개의 <see cref="BudgetedLight"/>만 그림자를 켜고
    /// <see cref="cullDistance"/>보다 먼 라이트는 꺼 둔다. 등기구 메시(발광 재질)는 그대로 보이므로 먼 곳도 밝아 보인다.
    /// 성능 노브: maxShadowedLights, cullDistance, (URP 에셋의) 추가 라이트 그림자 해상도.
    /// </summary>
    public sealed class LightShadowBudget : MonoBehaviour
    {
        [Min(0)] [SerializeField] private int maxShadowedLights = 6;
        [Min(1f)] [SerializeField] private float cullDistance = 45f;
        [Min(0.02f)] [SerializeField] private float updateInterval = 0.25f;

        private readonly List<(float distSq, BudgetedLight light)> sorted = new List<(float, BudgetedLight)>();
        private float nextUpdate;

        public int MaxShadowedLights { get => maxShadowedLights; set => maxShadowedLights = Mathf.Max(0, value); }
        public float CullDistance { get => cullDistance; set => cullDistance = Mathf.Max(1f, value); }

        private void OnEnable()
        {
            nextUpdate = 0f;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + updateInterval;

            Camera cam = FindViewCamera();
            if (cam == null) return;
            Apply(cam.transform.position);
        }

        private void Apply(Vector3 viewPos)
        {
            IReadOnlyList<BudgetedLight> lights = BudgetedLight.Active;
            float cullSq = cullDistance * cullDistance;

            sorted.Clear();
            for (int i = 0; i < lights.Count; i++)
            {
                BudgetedLight bl = lights[i];
                if (bl == null || bl.Light == null) continue;
                // 라이트가 닿는 거리만큼 여유를 둬서, 컬링 경계에서 바닥의 빛이 갑자기 꺼지지 않게 한다.
                float reach = cullDistance + bl.Light.range;
                float d = (bl.transform.position - viewPos).sqrMagnitude;
                bool inRange = d <= reach * reach;
                if (bl.Light.enabled != inRange) bl.Light.enabled = inRange;
                if (inRange) sorted.Add((d, bl));
                else if (bl.Light.shadows != LightShadows.None) bl.Light.shadows = LightShadows.None;
            }

            sorted.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            for (int i = 0; i < sorted.Count; i++)
            {
                BudgetedLight bl = sorted[i].light;
                LightShadows want = i < maxShadowedLights && sorted[i].distSq <= cullSq ? bl.ShadowType : LightShadows.None;
                if (bl.Light.shadows != want) bl.Light.shadows = want;
            }
        }

        private static Camera FindViewCamera()
        {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;

            Camera best = null;
            foreach (Camera c in Camera.allCameras)
            {
                if (c.targetTexture != null) continue; // 아이콘 렌더 카메라 등 제외
                if (best == null || c.depth > best.depth) best = c;
            }
            return best;
        }
    }
}
