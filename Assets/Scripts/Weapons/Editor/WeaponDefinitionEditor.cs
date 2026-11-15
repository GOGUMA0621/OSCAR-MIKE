using UnityEditor;
using UnityEngine;

namespace OskarMike.Weapons.Editor
{
    /// <summary>
    /// WeaponDefinition 인스펙터: 기본 필드 아래에 리코일·스프레이 2D 미리보기를 그린다.
    /// - 선: 연사 시 조준점이 이동하는 경로 (리코일 곡선, 무작위 제외)
    /// - 원: 각 발의 기본 퍼짐 범위 (스프레이 곡선)
    /// - 점(선택): 무작위 좌우 반동 + 퍼짐을 포함한 모의 탄착 한 세트
    /// </summary>
    [CustomEditor(typeof(WeaponDefinition))]
    public sealed class WeaponDefinitionEditor : UnityEditor.Editor
    {
        private const float PreviewHeight = 300f;
        private const float Padding = 18f;

        private static bool showSpread = true;
        private static bool showSimulation = true;
        private static bool showNumbers = true;
        private int seed = 1;

        private static readonly Color Background = new Color(0.13f, 0.13f, 0.14f);
        private static readonly Color GridColor = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color AxisColor = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color PathColor = new Color(1f, 0.72f, 0.25f);
        private static readonly Color SpreadColor = new Color(0.4f, 0.75f, 1f, 0.35f);
        private static readonly Color SimColor = new Color(1f, 0.35f, 0.3f, 0.95f);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var def = (WeaponDefinition)target;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("리코일 · 스프레이 미리보기", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                showSpread = GUILayout.Toggle(showSpread, "퍼짐 원", EditorStyles.miniButtonLeft);
                showSimulation = GUILayout.Toggle(showSimulation, "모의 탄착", EditorStyles.miniButtonMid);
                showNumbers = GUILayout.Toggle(showNumbers, "발 번호", EditorStyles.miniButtonMid);
                if (GUILayout.Button("다시 굴리기", EditorStyles.miniButtonRight)) seed++;
            }

            Rect rect = GUILayoutUtility.GetRect(10f, PreviewHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) DrawPreview(rect, def);

            int shots = def.MagazineSize;
            Vector2 end = def.RecoilAt(shots);
            EditorGUILayout.HelpBox(
                $"탄창 {shots}발 기준 누적 반동: 위 {end.x:0.0}° / 좌우 {end.y:+0.0;-0.0;0}°  ·  " +
                $"첫 발 퍼짐 {def.SpreadAt(0):0.00}° → 마지막 {def.SpreadAt(shots - 1):0.00}°\n" +
                "위쪽 = 조준점이 올라가는 방향 (플레이어는 마우스를 반대로 당겨 상쇄). 실제 탄은 이 경로 위에서 원 크기만큼 흩어집니다.\n" +
                "자세·이동 배율과 반동 복귀는 미리보기에 포함되지 않습니다.",
                MessageType.None);
        }

        private void DrawPreview(Rect rect, WeaponDefinition def)
        {
            EditorGUI.DrawRect(rect, Background);
            int shots = Mathf.Max(1, def.MagazineSize);

            // 경로 + 모의 탄착 계산
            var path = new Vector2[shots + 1];
            var spread = new float[shots + 1];
            for (int i = 0; i <= shots; i++)
            {
                path[i] = def.RecoilAt(i);
                spread[i] = def.SpreadAt(i);
            }

            var sim = new Vector2[shots];
            var rng = new System.Random(seed);
            float randomYaw = 0f;
            for (int i = 0; i < shots; i++)
            {
                // i 번째 발은 그때까지의 반동(path[i]) + 지금까지 쌓인 무작위 좌우에서 발사된다
                Vector2 aim = path[i] + new Vector2(0f, randomYaw);
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float radius = Mathf.Sqrt((float)rng.NextDouble()) * spread[i];
                sim[i] = aim + new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                randomYaw += ((float)rng.NextDouble() * 2f - 1f) * def.RecoilRandomYaw;
            }

            // 화면 맞춤 범위 (x = yaw, y = pitch)
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
            for (int i = 0; i <= shots; i++)
            {
                float r = showSpread ? spread[i] : 0f;
                minX = Mathf.Min(minX, path[i].y - r); maxX = Mathf.Max(maxX, path[i].y + r);
                minY = Mathf.Min(minY, path[i].x - r); maxY = Mathf.Max(maxY, path[i].x + r);
            }
            if (showSimulation)
                foreach (var s in sim)
                {
                    minX = Mathf.Min(minX, s.y); maxX = Mathf.Max(maxX, s.y);
                    minY = Mathf.Min(minY, s.x); maxY = Mathf.Max(maxY, s.x);
                }
            float halfX = Mathf.Max(Mathf.Abs(minX), Mathf.Abs(maxX), 1f); // 좌우 대칭, 최소 ±1°
            float spanY = Mathf.Max(maxY - minY, 2f);

            Rect area = new Rect(rect.x + Padding, rect.y + Padding, rect.width - Padding * 2f, rect.height - Padding * 2f);
            float scale = Mathf.Min(area.width / (halfX * 2f), area.height / spanY);
            Vector2 origin = new Vector2(area.center.x, area.yMax + minY * scale); // pitch 0 위치

            Vector3 ToScreen(Vector2 recoil) => new Vector3(origin.x + recoil.y * scale, origin.y - recoil.x * scale, 0f);

            GUI.BeginClip(rect);
            Vector3 clipOffset = new Vector3(-rect.x, -rect.y, 0f);
            Handles.BeginGUI();

            // 1도 격자
            Handles.color = GridColor;
            for (int d = -Mathf.CeilToInt(halfX); d <= Mathf.CeilToInt(halfX); d++)
            {
                float x = origin.x + d * scale;
                Handles.DrawLine(new Vector3(x, area.yMin) + clipOffset, new Vector3(x, area.yMax) + clipOffset);
            }
            for (int d = Mathf.FloorToInt(minY); d <= Mathf.CeilToInt(maxY); d++)
            {
                float y = origin.y - d * scale;
                Handles.DrawLine(new Vector3(area.xMin, y) + clipOffset, new Vector3(area.xMax, y) + clipOffset);
            }
            Handles.color = AxisColor;
            Handles.DrawLine(new Vector3(origin.x, area.yMin) + clipOffset, new Vector3(origin.x, area.yMax) + clipOffset);
            Handles.DrawLine(new Vector3(area.xMin, origin.y) + clipOffset, new Vector3(area.xMax, origin.y) + clipOffset);

            // 퍼짐 원
            if (showSpread)
            {
                Handles.color = SpreadColor;
                for (int i = 0; i < shots; i++)
                    Handles.DrawWireDisc(ToScreen(path[i]) + clipOffset, Vector3.forward, Mathf.Max(1f, spread[i] * scale));
            }

            // 조준점 경로
            var line = new Vector3[shots + 1];
            for (int i = 0; i <= shots; i++) line[i] = ToScreen(path[i]) + clipOffset;
            Handles.color = PathColor;
            Handles.DrawAAPolyLine(3f, line);
            for (int i = 0; i < shots; i++) Handles.DrawSolidDisc(line[i], Vector3.forward, 2.2f);

            // 모의 탄착
            if (showSimulation)
            {
                Handles.color = SimColor;
                foreach (var s in sim) Handles.DrawSolidDisc(ToScreen(s) + clipOffset, Vector3.forward, 2.6f);
            }
            Handles.EndGUI();

            // 발 번호 (1, 5, 10, ...)
            if (showNumbers)
            {
                var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 1f, 1f, 0.75f) } };
                for (int i = 0; i < shots; i++)
                {
                    int n = i + 1;
                    if (n != 1 && n % 5 != 0) continue;
                    Vector3 p = ToScreen(path[i]) + clipOffset;
                    GUI.Label(new Rect(p.x + 5f, p.y - 8f, 30f, 16f), n.ToString(), style);
                }
            }

            var axisStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 1f, 1f, 0.4f) } };
            GUI.Label(new Rect(4f, 2f, 200f, 16f), "↑ 위 (pitch)   ·   격자 1°", axisStyle);
            GUI.Label(new Rect(rect.width - 70f, rect.height - 16f, 70f, 16f), "→ 오른쪽", axisStyle);
            GUI.EndClip();
        }
    }
}
