using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OskarMike.Items.EditorTools
{
    /// <summary>
    /// 로우폴리(플랫 셰이딩) 더플백 메시 생성기.
    /// 메뉴: Tools/OSCAR-MIKE/Generate Duffel Bag Model
    /// 결과물: Assets/Models/DuffelBag/ (메시 .asset, 머티리얼 3종, SM_DuffelBag.prefab)
    /// 긴 축 = X (운반 시 등에 가로로 멤), 피벗 = 메시 중심.
    /// </summary>
    public static class DuffelBagMeshGenerator
    {
        private const string Folder = "Assets/Models/DuffelBag";

        // ── 형태 파라미터 (m) ──────────────────────────────
        private const float BodyLength = 0.72f;    // 원통 몸통 길이 (캡 제외)
        private const float Radius = 0.19f;
        private const float FlatBottom = 0.8f;     // 바닥 평평하게 자르는 비율 (y >= -Radius*FlatBottom)
        private const float CapBulge = 0.05f;
        private const int Segments = 12;

        private const int SubFabric = 0, SubTrim = 1, SubMetal = 2;

        private sealed class Builder
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int>[] Subs = { new List<int>(), new List<int>(), new List<int>() };

            /// <summary>평면 사각형. outward 힌트 방향으로 앞면이 향하도록 와인딩을 맞춘다.</summary>
            public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(c - a, d - a);
                if (Vector3.Dot(n, outward) < 0f) { var t = b; b = d; d = t; n = -n; }
                n.Normalize();
                int i = Verts.Count;
                Verts.AddRange(new[] { a, b, c, d });
                Normals.AddRange(new[] { n, n, n, n });
                Uvs.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                // Unity: 삼각형 (a,b,c) 의 앞면 법선 = Cross(b-a, c-a)
                Subs[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }

            public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward) => Quad(sub, a, b, c, c, outward);

            public void Box(int sub, Vector3 center, Vector3 size, Quaternion rot)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var d in dirs)
                {
                    Vector3 u = d.x != 0 ? Vector3.up : Vector3.right;
                    Vector3 v = Vector3.Cross(d, u);
                    Vector3 c0 = d;
                    Vector3 a = P(c0.x - u.x - v.x, c0.y - u.y - v.y, c0.z - u.z - v.z);
                    Vector3 b = P(c0.x + u.x - v.x, c0.y + u.y - v.y, c0.z + u.z - v.z);
                    Vector3 c = P(c0.x + u.x + v.x, c0.y + u.y + v.y, c0.z + u.z + v.z);
                    Vector3 e = P(c0.x - u.x + v.x, c0.y - u.y + v.y, c0.z - u.z + v.z);
                    Quad(sub, a, b, c, e, rot * d);
                }
            }
        }

        private static Vector3 RingPoint(float x, float r, int i, float flatClamp)
        {
            float a = i * Mathf.PI * 2f / Segments + Mathf.PI / Segments;
            float y = Mathf.Sin(a) * r;
            float z = Mathf.Cos(a) * r;
            y = Mathf.Max(y, -flatClamp);
            return new Vector3(x, y, z);
        }

        /// <summary>x0(r0) → x1(r1) 원통/원뿔대 띠.</summary>
        private static void RingStrip(Builder b, int sub, float x0, float r0, float x1, float r1, float flat)
        {
            for (int i = 0; i < Segments; i++)
            {
                Vector3 a = RingPoint(x0, r0, i, flat), bb = RingPoint(x0, r0, i + 1, flat);
                Vector3 c = RingPoint(x1, r1, i + 1, flat), d = RingPoint(x1, r1, i, flat);
                Vector3 mid = (a + bb + c + d) * 0.25f;
                Vector3 outward = new Vector3(0f, mid.y, mid.z);
                if (Mathf.Abs(r1 - r0) > 1e-4f) outward += Vector3.right * Mathf.Sign(x1 - x0) * (r0 - r1) / Mathf.Abs(x1 - x0 + 1e-5f) * 0.2f;
                b.Quad(sub, a, bb, c, d, outward);
            }
        }

        /// <summary>x 평면의 링(도넛) 면: 반지름 rIn → rOut, 방향 dir(±X).</summary>
        private static void Annulus(Builder b, int sub, float x, float rIn, float rOut, float flatIn, float flatOut, float dir)
        {
            for (int i = 0; i < Segments; i++)
            {
                b.Quad(sub, RingPoint(x, rIn, i, flatIn), RingPoint(x, rIn, i + 1, flatIn),
                    RingPoint(x, rOut, i + 1, flatOut), RingPoint(x, rOut, i, flatOut), Vector3.right * dir);
            }
        }

        private static void Cap(Builder b, int sub, float x, float dir)
        {
            float flat = Radius * FlatBottom;
            float x1 = x + dir * CapBulge * 0.6f;
            float r1 = Radius * 0.72f;
            RingStrip(b, sub, x, Radius, x1, r1, flat);
            Vector3 tip = new Vector3(x + dir * CapBulge, -Radius * 0.05f, 0f);
            for (int i = 0; i < Segments; i++)
                b.Tri(sub, RingPoint(x1, r1, i, flat), RingPoint(x1, r1, i + 1, flat), tip, Vector3.right * dir);
        }

        private static void StrapBand(Builder b, float x, float width, float thickness)
        {
            float flat = Radius * FlatBottom;
            float rOut = Radius + thickness;
            float x0 = x - width * 0.5f, x1 = x + width * 0.5f;
            RingStrip(b, SubTrim, x0, rOut, x1, rOut, flat + thickness);
            Annulus(b, SubTrim, x0, Radius, rOut, flat, flat + thickness, -1f);
            Annulus(b, SubTrim, x1, Radius, rOut, flat, flat + thickness, 1f);
        }

        public static Mesh BuildMesh()
        {
            var b = new Builder();
            float half = BodyLength * 0.5f;
            float flat = Radius * FlatBottom;

            // 몸통: 가운데를 살짝 부풀린 3구간
            RingStrip(b, SubFabric, -half, Radius * 0.97f, -half * 0.35f, Radius, flat);
            RingStrip(b, SubFabric, -half * 0.35f, Radius, half * 0.35f, Radius, flat);
            RingStrip(b, SubFabric, half * 0.35f, Radius, half, Radius * 0.97f, flat);

            // 양 끝 캡 (트림 색)
            Cap(b, SubTrim, -half, -1f);
            Cap(b, SubTrim, half, 1f);

            // 끝단 테두리 밴드
            StrapBand(b, -half + 0.015f, 0.03f, 0.008f);
            StrapBand(b, half - 0.015f, 0.03f, 0.008f);

            // 몸통을 감싸는 손잡이 스트랩 2개
            const float strapX = 0.13f;
            StrapBand(b, -strapX, 0.045f, 0.01f);
            StrapBand(b, strapX, 0.045f, 0.01f);

            // 손잡이 아치 (상단, 스트랩 두 개를 연결)
            float top = Radius + 0.01f;
            const int arcSegs = 6;
            const float arcHeight = 0.085f;
            Vector3 prev = new Vector3(-strapX, top, 0f);
            for (int i = 1; i <= arcSegs; i++)
            {
                float t = i / (float)arcSegs;
                float xx = Mathf.Lerp(-strapX, strapX, t);
                float yy = top + Mathf.Sin(t * Mathf.PI) * arcHeight;
                Vector3 cur = new Vector3(xx, yy, 0f);
                Vector3 mid = (prev + cur) * 0.5f;
                Vector3 seg = cur - prev;
                var rot = Quaternion.FromToRotation(Vector3.right, seg.normalized);
                b.Box(SubTrim, mid, new Vector3(seg.magnitude + 0.006f, 0.014f, 0.045f), rot);
                prev = cur;
            }
            // 손잡이 그립 커버
            b.Box(SubTrim, new Vector3(0f, top + arcHeight, 0f), new Vector3(0.11f, 0.026f, 0.05f), Quaternion.identity);

            // 지퍼 라인 (상단 길이 방향, 손잡이 옆으로 비껴서)
            float zipZ = 0.06f;
            float zipY = Mathf.Sqrt(Mathf.Max(0f, Radius * Radius - zipZ * zipZ)) + 0.003f;
            float tilt = Mathf.Atan2(zipZ, zipY) * Mathf.Rad2Deg;
            var zipRot = Quaternion.Euler(-tilt, 0f, 0f);
            b.Box(SubMetal, new Vector3(0f, zipY, zipZ), new Vector3(BodyLength - 0.08f, 0.008f, 0.018f), zipRot);
            // 지퍼 손잡이
            b.Box(SubMetal, new Vector3(half - 0.09f, zipY + 0.006f, zipZ + 0.012f), new Vector3(0.012f, 0.006f, 0.035f), zipRot);

            // 옆면 포켓 (+Z 면)
            float pocketZ = Radius * 0.93f;
            b.Box(SubFabric, new Vector3(0f, -0.01f, pocketZ), new Vector3(0.2f, 0.12f, 0.05f), Quaternion.identity);
            b.Box(SubMetal, new Vector3(0f, 0.047f, pocketZ + 0.02f), new Vector3(0.17f, 0.006f, 0.008f), Quaternion.identity);

            var mesh = new Mesh { name = "SM_DuffelBag" };
            mesh.SetVertices(b.Verts);
            mesh.SetNormals(b.Normals);
            mesh.SetUVs(0, b.Uvs);
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(b.Subs[s], s);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        [MenuItem("Tools/OSCAR-MIKE/Generate Duffel Bag Model")]
        public static GameObject Generate()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Models")) AssetDatabase.CreateFolder("Assets", "Models");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Models", "DuffelBag");

            string meshPath = Folder + "/SM_DuffelBag_Mesh.asset";
            Mesh mesh = BuildMesh();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, meshPath);

            var mats = new[]
            {
                GetOrCreateMaterial("M_DuffelBag_Fabric", new Color(0.16f, 0.17f, 0.14f), 0f, 0.25f),
                GetOrCreateMaterial("M_DuffelBag_Trim", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f),
                GetOrCreateMaterial("M_DuffelBag_Metal", new Color(0.62f, 0.6f, 0.55f), 0.7f, 0.55f)
            };

            var go = new GameObject("SM_DuffelBag");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            string prefabPath = Folder + "/SM_DuffelBag.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DuffelBagMeshGenerator] 생성 완료: {prefabPath} (verts {mesh.vertexCount}, size {mesh.bounds.size})");
            return prefab;
        }

        private static Material GetOrCreateMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{Folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
