using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Weapons
{
    /// <summary>
    /// 날아가는 총알 궤적 (시각 전용, 네트워크 상태 없음).
    /// 시작점에서 끝점(탄착 지점)까지 일정 속도로 이동하는 짧은 빛줄기를 LineRenderer 로 그린다.
    /// <see cref="PlayerWeaponView"/> 가 풀링해서 사용한다.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class BulletTracer : MonoBehaviour
    {
        private LineRenderer line;
        private Vector3 start;
        private Vector3 direction;
        private float distance;
        private float traveled;
        private float speed;
        private float length;

        public bool Busy => gameObject.activeSelf;

        public static BulletTracer Create(Material material)
        {
            var go = new GameObject("총알 궤적", typeof(LineRenderer));
            var line = go.GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off;
            line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            line.sharedMaterial = material;
            var tracer = go.AddComponent<BulletTracer>();
            tracer.line = line;
            go.SetActive(false);
            return tracer;
        }

        /// <param name="from">총구 위치</param>
        /// <param name="to">탄착 지점</param>
        /// <param name="metersPerSecond">궤적 이동 속도 (실제 탄속보다 느리게 해야 눈에 보인다)</param>
        /// <param name="streakLength">빛줄기 길이 (m)</param>
        public void Launch(Vector3 from, Vector3 to, float metersPerSecond, float streakLength,
                           float width, Color color)
        {
            Vector3 delta = to - from;
            distance = delta.magnitude;
            if (distance < 0.01f) { gameObject.SetActive(false); return; }

            start = from;
            direction = delta / distance;
            traveled = 0f;
            speed = Mathf.Max(1f, metersPerSecond);
            length = Mathf.Max(0.05f, streakLength);

            line.widthCurve = new AnimationCurve(new Keyframe(0f, width * 0.25f), new Keyframe(1f, width));
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.Lerp(color, Color.white, 0.6f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
            line.colorGradient = gradient;

            gameObject.SetActive(true);
            Apply();
        }

        private void Update()
        {
            traveled += speed * Time.deltaTime;
            // 머리가 탄착점에 닿은 뒤 꼬리까지 들어가면 종료
            if (traveled - length >= distance) { gameObject.SetActive(false); return; }
            Apply();
        }

        private void Apply()
        {
            float head = Mathf.Min(traveled, distance);
            float tail = Mathf.Clamp(traveled - length, 0f, distance);
            line.SetPosition(0, start + direction * tail);   // 꼬리 (투명)
            line.SetPosition(1, start + direction * head);   // 머리 (밝음)
        }
    }
}
