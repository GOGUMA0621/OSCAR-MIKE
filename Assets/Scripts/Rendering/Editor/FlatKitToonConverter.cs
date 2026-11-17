using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OskarMike.Rendering.EditorTools
{
    /// <summary>
    /// 프로젝트 머티리얼을 Flatkit "FlatKit/Stylized Surface"(툰)로 일괄 변환한다.
    /// 메뉴: Tools/OSCAR-MIKE/Convert Materials To Flatkit Toon
    ///
    /// 대상: URP Lit/Simple Lit/Complex Lit, Synty Generic_Basic 계열, Synty PolygonShader(트라이플래너 미사용).
    /// 색, 알베도/노멀/이미시브 텍스처(+타일링), 알파 클립/반투명, 컬링을 옮기고
    /// 툰 기본값(단일 셀, 그늘색 = 베이스색 × ShadeFactor, 유니티 그림자 Multiply)을 넣는다.
    /// 제외: Assets/FlatKit, FBX 내장 머티리얼, 파티클/데칼/스카이/네온 등 특수 셰이더.
    /// 원본 복구는 git으로 한다. 이미 변환된 머티리얼은 건너뛴다.
    /// </summary>
    public static class FlatKitToonConverter
    {
        private const string ToonShaderName = "FlatKit/Stylized Surface";

        // 툰 스타일 기본값
        private const float ShadeFactor = 0.55f;       // 그늘색 = 베이스색 × 이 값
        private const float SelfShadingSize = 0.55f;
        private const float ShadowEdgeSize = 0.04f;
        private const float Flatness = 0.85f;
        private const float UnityShadowPower = 0.55f;   // 받는 그림자 세기 (Multiply)
        private const float LightContribution = 1f;     // 라이트 색·세기 반영 비율

        private static readonly string[] SkipFolders =
        {
            "Assets/FlatKit",
        };

        private enum SourceKind { None, UrpLit, SyntyGeneric, SyntyPolygon }

        [MenuItem("Tools/OSCAR-MIKE/Convert Materials To Flatkit Toon")]
        public static void ConvertAllMenu()
        {
            if (!EditorUtility.DisplayDialog("Flatkit 툰 변환",
                    "URP Lit / Synty 머티리얼을 FlatKit Stylized Surface로 변환합니다.\n원본 복구는 git으로 해야 합니다. 계속할까요?",
                    "변환", "취소"))
                return;
            Debug.Log(ConvertAll());
        }

        public static string ConvertAll()
        {
            var toon = Shader.Find(ToonShaderName);
            if (toon == null)
                return "[FlatKitToon] '" + ToonShaderName + "' 셰이더가 없습니다. Flatkit URP 패키지를 먼저 언팩하세요.";

            var converted = new List<string>();
            var skipped = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".mat") || IsSkippedFolder(path)) continue;

                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null || mat.shader == null) continue;
                    if (mat.isVariant) continue; // 머티리얼 베리언트는 부모 셰이더를 따른다

                    var kind = Classify(mat);
                    if (kind == SourceKind.None) continue;

                    if (kind == SourceKind.SyntyPolygon && GetFloat(mat, "_Enable_Triplanar_Texture") > 0.5f)
                    {
                        skipped.Add(path + " (트라이플래너)");
                        continue;
                    }

                    Convert(mat, kind, toon);
                    EditorUtility.SetDirty(mat);
                    converted.Add(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();

            var sb = new StringBuilder();
            sb.AppendLine("[FlatKitToon] 변환 " + converted.Count + "개, 건너뜀 " + skipped.Count + "개");
            foreach (var p in skipped) sb.AppendLine("  skip: " + p);
            return sb.ToString();
        }

        private static bool IsSkippedFolder(string path)
        {
            foreach (var f in SkipFolders)
                if (path.StartsWith(f + "/")) return true;
            return false;
        }

        private static SourceKind Classify(Material mat)
        {
            var name = mat.shader.name;
            switch (name)
            {
                case "Universal Render Pipeline/Lit":
                case "Universal Render Pipeline/Simple Lit":
                case "Universal Render Pipeline/Complex Lit":
                    return SourceKind.UrpLit;
                case "Synty/Generic_Basic":
                case "Synty/Generic_Standard":
                case "Synty/Generic_Basic_Bloody":
                case "Synty/Generic_Basic_Specular":
                case "Synty/Generic_Basic_Specular_Bloody":
                    return SourceKind.SyntyGeneric;
                case "Synty/PolygonShader":
                    return SourceKind.SyntyPolygon;
                default:
                    return SourceKind.None;
            }
        }

        private struct Source
        {
            public Color color;
            public Texture albedo;
            public Vector2 tiling, offset;
            public Texture normal;
            public bool emission;
            public Color emissionColor;
            public Texture emissionMap;
            public bool transparent;
            public float blend;
            public bool alphaClip;
            public float cutoff;
            public float cull;
        }

        private static Source Read(Material m, SourceKind kind)
        {
            var s = new Source
            {
                color = Color.white, tiling = Vector2.one, offset = Vector2.zero,
                cutoff = 0.5f, cull = 2f,
            };

            switch (kind)
            {
                case SourceKind.UrpLit:
                    s.color = GetColor(m, "_BaseColor", Color.white);
                    s.albedo = GetTex(m, "_BaseMap");
                    if (m.HasProperty("_BaseMap"))
                    {
                        s.tiling = m.GetTextureScale("_BaseMap");
                        s.offset = m.GetTextureOffset("_BaseMap");
                    }
                    s.normal = GetTex(m, "_BumpMap");
                    s.emission = m.IsKeywordEnabled("_EMISSION");
                    s.emissionColor = GetColor(m, "_EmissionColor", Color.black);
                    s.emissionMap = GetTex(m, "_EmissionMap");
                    s.transparent = GetFloat(m, "_Surface") > 0.5f;
                    s.blend = GetFloat(m, "_Blend");
                    s.alphaClip = GetFloat(m, "_AlphaClip") > 0.5f;
                    s.cutoff = GetFloat(m, "_Cutoff", 0.5f);
                    s.cull = GetFloat(m, "_Cull", 2f);
                    break;

                case SourceKind.SyntyGeneric:
                    s.color = m.HasProperty("_BaseColor") ? GetColor(m, "_BaseColor", Color.white)
                                                          : GetColor(m, "_Base_Color", Color.white);
                    s.albedo = GetTex(m, "_Albedo_Map");
                    if (m.HasProperty("_Albedo_Map"))
                    {
                        s.tiling = m.GetTextureScale("_Albedo_Map");
                        s.offset = m.GetTextureOffset("_Albedo_Map");
                    }
                    s.normal = GetTex(m, "_Normal_Map");
                    s.emission = GetFloat(m, "_Enable_Emission") > 0.5f;
                    s.emissionColor = GetColor(m, "_Emission_Color", Color.black);
                    s.emissionMap = GetTex(m, "_Emission_Map");
                    s.transparent = GetFloat(m, "_Surface") > 0.5f;
                    s.blend = GetFloat(m, "_Blend");
                    s.alphaClip = GetFloat(m, "_AlphaClip") > 0.5f;
                    s.cutoff = GetFloat(m, "_Alpha_Clip_Threshold", 0.5f);
                    s.cull = GetFloat(m, "_Cull", 2f);
                    break;

                case SourceKind.SyntyPolygon:
                    s.color = GetColor(m, "_Color_Tint", Color.white);
                    if (GetFloat(m, "_Enable_Base_Texture") > 0.5f)
                    {
                        s.albedo = GetTex(m, "_Base_Texture");
                        s.tiling = GetVector(m, "_Base_Tiling", Vector2.one);
                        s.offset = GetVector(m, "_Base_Offset", Vector2.zero);
                    }
                    if (GetFloat(m, "_Enable_Normal_Texture") > 0.5f)
                        s.normal = GetTex(m, "_Normal_Texture");
                    s.emission = GetFloat(m, "_Enable_Emission_Texture") > 0.5f;
                    s.emissionColor = GetColor(m, "_Emission_Color_Tint", Color.black);
                    s.emissionMap = GetTex(m, "_Emission_Texture");
                    s.transparent = GetFloat(m, "_Surface") > 0.5f;
                    s.blend = GetFloat(m, "_Blend");
                    s.alphaClip = GetFloat(m, "_AlphaClip") > 0.5f;
                    s.cutoff = 0.5f;
                    s.cull = GetFloat(m, "_Cull", 2f);
                    break;
            }
            return s;
        }

        private static void Convert(Material mat, SourceKind kind, Shader toon)
        {
            var s = Read(mat, kind);
            int queueOffset = (int)GetFloat(mat, "_QueueOffset");

            mat.shader = toon;
            mat.shaderKeywords = new string[0];

            // 색 / 셀 셰이딩
            mat.SetColor("_BaseColor", s.color);
            var shade = new Color(s.color.r * ShadeFactor, s.color.g * ShadeFactor, s.color.b * ShadeFactor, 1f);
            mat.SetFloat("_CelPrimaryMode", 1f); // Single
            mat.SetColor("_ColorDim", shade);
            mat.SetFloat("_SelfShadingSize", SelfShadingSize);
            mat.SetFloat("_ShadowEdgeSize", ShadowEdgeSize);
            mat.SetFloat("_Flatness", Flatness);
            // 라이트 색·세기 반영 (0이면 모든 라이트를 흰색 1로 취급 → 실내 스포트 밑이 두 배로 밝아짐)
            mat.SetFloat("_LightContribution", LightContribution);

            // 받는 그림자
            mat.SetFloat("_UnityShadowMode", 1f); // Multiply
            mat.SetFloat("_UnityShadowPower", UnityShadowPower);
            mat.SetFloat("_UnityShadowSharpness", 1f);

            // 텍스처
            mat.SetTexture("_BaseMap", s.albedo);
            mat.SetTextureScale("_BaseMap", s.tiling);
            mat.SetTextureOffset("_BaseMap", s.offset);
            mat.SetFloat("_TextureBlendingMode", 0f); // Multiply
            mat.SetFloat("_TextureImpact", 1f);
            mat.SetTexture("_BumpMap", s.normal);

            mat.SetTexture("_EmissionMap", s.emission ? s.emissionMap : null);
            mat.SetColor("_EmissionColor", s.emission ? s.emissionColor : Color.black);

            // 키워드 (KeywordEnum/Toggle 드로어 기준으로 재적용 후 수동 보정)
            MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            SetKeyword(mat, "_CELPRIMARYMODE_SINGLE", true);
            SetKeyword(mat, "_UNITYSHADOWMODE_MULTIPLY", true);
            SetKeyword(mat, "_TEXTUREBLENDINGMODE_MULTIPLY", true);
            SetKeyword(mat, "_NORMALMAP", s.normal != null);
            SetKeyword(mat, "_EMISSION", s.emission);
            mat.globalIlluminationFlags = s.emission
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            // 표면 / 블렌드 (StylizedSurfaceEditor.HandleUrpSettings 와 동일)
            mat.SetFloat("_Cull", s.cull);
            mat.SetFloat("_QueueOffset", queueOffset);
            mat.SetFloat("_AlphaClip", s.alphaClip ? 1f : 0f);
            mat.SetFloat("_Cutoff", s.cutoff);
            SetKeyword(mat, "_ALPHATEST_ON", s.alphaClip);
            mat.SetFloat("_Surface", s.transparent ? 1f : 0f);
            mat.SetFloat("_Blend", s.blend);

            if (!s.transparent)
            {
                mat.SetOverrideTag("RenderType", s.alphaClip ? "TransparentCutout" : "Opaque");
                mat.renderQueue = (s.alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry) + queueOffset;
                mat.SetInt("_SrcBlend", (int)BlendMode.One);
                mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                mat.SetInt("_ZWrite", 1);
                mat.SetShaderPassEnabled("ShadowCaster", true);
            }
            else
            {
                switch ((int)s.blend)
                {
                    case 1: // Premultiply
                        mat.SetInt("_SrcBlend", (int)BlendMode.One);
                        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                        SetKeyword(mat, "_ALPHAPREMULTIPLY_ON", true);
                        break;
                    case 2: // Additive
                        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)BlendMode.One);
                        break;
                    case 3: // Multiply
                        mat.SetInt("_SrcBlend", (int)BlendMode.DstColor);
                        mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                        SetKeyword(mat, "_ALPHAMODULATE_ON", true);
                        break;
                    default: // Alpha
                        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                        break;
                }
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)RenderQueue.Transparent + queueOffset;
                mat.SetShaderPassEnabled("ShadowCaster", false);
            }
        }

        private static void SetKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw); else m.DisableKeyword(kw);
        }

        private static float GetFloat(Material m, string p, float def = 0f) =>
            m.HasProperty(p) ? m.GetFloat(p) : def;

        private static Color GetColor(Material m, string p, Color def) =>
            m.HasProperty(p) ? m.GetColor(p) : def;

        private static Texture GetTex(Material m, string p) =>
            m.HasProperty(p) ? m.GetTexture(p) : null;

        private static Vector2 GetVector(Material m, string p, Vector2 def) =>
            m.HasProperty(p) ? (Vector2)m.GetVector(p) : def;
    }
}
