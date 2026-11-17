using OskarMike.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OskarMike.Testing.EditorTools
{
    /// <summary>
    /// RangeCourse를 실내 시설로 만든다: 외곽 벽(6m) + 천장 + 천장 등기구(실시간 Spot) + 실내 후처리 Volume.
    /// 메뉴: Tools/OSCAR-MIKE/Build RangeCourse Enclosure
    /// 씬 루트 "RangeCourse Enclosure"를 지우고 다시 만든다(멱등). 기존 바닥/구분벽은 건드리지 않는다.
    /// 라이트는 베이크하지 않는다(절차적 GameMap과 같은 실시간 구성의 테스트베드).
    /// </summary>
    public static class RangeCourseEnclosureBuilder
    {
        private const string SceneName = "RangeCourse";
        private const string RootName = "RangeCourse Enclosure";

        private const string ConcreteMatPath = "Assets/Materials/RangeCourse/Concrete.mat";
        private const string CeilingMatPath = "Assets/Materials/RangeCourse/Ceiling.mat";
        private const string LampMatPath = "Assets/Materials/RangeCourse/M_CeilingLight.mat";
        private const string FixturePrefabPath = "Assets/Prefabs/Lighting/CeilingLightFixture.prefab";
        private const string VolumeProfilePath = "Assets/Settings/Volumes/Indoor_Volume.asset";

        // 치수
        private const float WallThickness = 0.6f;
        private const float WallBottom = -0.3f;
        private const float CeilingHeight = 6f;      // 천장 아랫면
        private const float CeilingThickness = 0.3f;
        private const float FaceInset = 0.03f;       // 기존 벽 면보다 안쪽으로 내미는 거리

        private static readonly Color CeilingColor = new Color(0.42f, 0.43f, 0.44f, 1f);
        private static readonly Color CeilingDimColor = new Color(0.30f, 0.31f, 0.32f, 1f);

        // 등기구 / 라이트 (화면 보며 조정)
        private static readonly Vector3 LampSize = new Vector3(0.35f, 0.08f, 2.4f);
        private static readonly Color LampColor = new Color(4f, 4.1f, 4.3f, 1f); // HDR → Bloom
        private static readonly Color LightColor = new Color(0.95f, 0.98f, 1f, 1f);
        private const float LightIntensity = 0.6f;
        private const float LightRange = 14f;
        private const float SpotAngle = 150f;
        private const float InnerSpotAngle = 100f;

        // 실내 키 라이트 (그림자 없음, 지붕 통과)
        private static readonly Vector3 KeyLightEuler = new Vector3(70f, 325f, 0f);
        private static readonly Color KeyLightColor = new Color(0.96f, 0.98f, 1f);
        private const float KeyLightIntensity = 1f;

        // 환경
        // Trilight: Sky = 위를 보는 면(바닥), Ground = 아래를 보는 면(천장). 천장은 Spot이 닿지 않으므로
        // 바닥에서 튀는 빛을 흉내 내려고 Ground를 가장 밝게 둔다.
        private static readonly Color AmbientSky = new Color(0.20f, 0.21f, 0.22f);
        private static readonly Color AmbientEquator = new Color(0.24f, 0.25f, 0.26f);
        private static readonly Color AmbientGround = new Color(0.30f, 0.31f, 0.32f);
        private const float ReflectionIntensity = 0.3f;

        [MenuItem("Tools/OSCAR-MIKE/Build RangeCourse Enclosure")]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                EditorUtility.DisplayDialog("RangeCourse Enclosure", $"{SceneName} 씬을 열고 실행하세요.", "확인");
                return;
            }

            Material concrete = AssetDatabase.LoadAssetAtPath<Material>(ConcreteMatPath);
            Material ceilingMat = EnsureCeilingMaterial(concrete);
            Material lampMat = EnsureLampMaterial();
            GameObject fixturePrefab = EnsureFixturePrefab(lampMat);
            VolumeProfile profile = EnsureVolumeProfile();

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build RangeCourse Enclosure");
            Transform walls = Child(root.transform, "Walls");
            Transform ceilings = Child(root.transform, "Ceilings");
            Transform lights = Child(root.transform, "Lights");

            // ── 외곽 벽 (기존 벽 바깥에 붙여 바닥 아래~천장 위까지) ──
            // 기존 0두께 판(Range left boundary x −34, 구분벽 x −2)과 면이 겹치면 z-fighting이 나므로
            // 새 벽 안쪽 면을 3cm 안으로 내밀어 기존 판을 가린다.
            WallZ(walls, concrete, "Wall West", -34f + FaceInset - WallThickness * 0.5f, -7.6f, 312.5f);
            WallX(walls, concrete, "Wall South", -7.3f, -34.6f, 50.4f);
            WallZ(walls, concrete, "Wall Range East", -2f - FaceInset + WallThickness * 0.5f, 64f, 311.5f);
            WallX(walls, concrete, "Wall Course North", 64.3f, -2f, 51.4f);
            WallZ(walls, concrete, "Wall Course East", 50.9f, 43.7f, 64.6f);
            WallX(walls, concrete, "Wall Loot North", 44.0f, 50.4f, 91f);
            WallZ(walls, concrete, "Wall Loot East", 90.7f, -9.7f, 44.3f);
            WallX(walls, concrete, "Wall Loot South", -9.4f, 50.4f, 91f);

            // ── 천장 ──
            Ceiling(ceilings, ceilingMat, "Ceiling Range", -34.6f, -1.4f, -7.6f, 312.5f);
            // 코스/루트룸 천장은 루트룸 서벽 중앙(x 50.9)에서 맞닿게 나눈다(아랫면이 겹치면 z-fighting).
            Ceiling(ceilings, ceilingMat, "Ceiling Course", -1.4f, 50.9f, -7.6f, 64.6f);
            Ceiling(ceilings, ceilingMat, "Ceiling Loot", 50.9f, 91f, -9.7f, 44.3f);

            // ── 등기구 ──
            Transform rangeLights = Child(lights, "Range");
            for (int i = 0; i <= 20; i++)
                foreach (float x in new[] { -26f, -10f })
                    Fixture(rangeLights, fixturePrefab, x, -2f + i * 15.5f);

            Transform courseLights = Child(lights, "Course");
            foreach (float z in new[] { 2f, 15f, 28f, 41f, 54f })
                foreach (float x in new[] { 7f, 19f, 31f, 43f })
                    Fixture(courseLights, fixturePrefab, x, z);

            Transform lootLights = Child(lights, "Loot Room");
            foreach (float z in new[] { -2.5f, 10.5f, 23.5f, 36.5f })
                foreach (float x in new[] { 57.5f, 70.5f, 83.5f })
                    Fixture(lootLights, fixturePrefab, x, z);

            root.AddComponent<LightShadowBudget>();

            // ── 실내 키 라이트 ──
            // FlatKit 툰 셰이더는 메인(디렉셔널) 라이트로 밝은 면(BaseColor)/그늘 면(ColorDim)을 나눈다.
            // 메인 라이트가 없으면 모든 면이 앰비언트×BaseColor로만 그려져 거의 검게 나온다.
            // 그래서 지붕을 통과하도록 그림자 없는 디렉셔널을 "실내 기본 밝기"로 둔다.
            var keyGo = new GameObject("Indoor Key Light");
            keyGo.transform.SetParent(root.transform, false);
            keyGo.transform.rotation = Quaternion.Euler(KeyLightEuler);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = KeyLightColor;
            key.intensity = KeyLightIntensity;
            key.shadows = LightShadows.None;
            keyGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = key;

            // ── 후처리 Volume ──
            var volGo = new GameObject("Indoor Volume");
            volGo.transform.SetParent(root.transform, false);
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // ── 환경광 / 태양 ──
            GameObject sun = GameObject.Find("Sun");
            if (sun != null)
            {
                Undo.RecordObject(sun, "Disable Sun");
                sun.SetActive(false);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.reflectionIntensity = ReflectionIntensity;
            RenderSettings.fog = false;

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[RangeCourseEnclosureBuilder] 완료: 라이트 {lights.GetComponentsInChildren<Light>(true).Length}개.");
        }

        // ───────────────────────── geometry ─────────────────────────

        private static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        /// <summary>z 방향으로 긴 벽 (x 고정).</summary>
        private static void WallZ(Transform parent, Material mat, string name, float x, float z0, float z1)
        {
            float top = CeilingHeight + CeilingThickness;
            Box(parent, mat, name,
                new Vector3(x, (WallBottom + top) * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(WallThickness, top - WallBottom, z1 - z0), true);
        }

        /// <summary>x 방향으로 긴 벽 (z 고정).</summary>
        private static void WallX(Transform parent, Material mat, string name, float z, float x0, float x1)
        {
            float top = CeilingHeight + CeilingThickness;
            Box(parent, mat, name,
                new Vector3((x0 + x1) * 0.5f, (WallBottom + top) * 0.5f, z),
                new Vector3(x1 - x0, top - WallBottom, WallThickness), true);
        }

        private static void Ceiling(Transform parent, Material mat, string name, float x0, float x1, float z0, float z1)
        {
            Box(parent, mat, name,
                new Vector3((x0 + x1) * 0.5f, CeilingHeight + CeilingThickness * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, CeilingThickness, z1 - z0), true);
        }

        private static GameObject Box(Transform parent, Material mat, string name, Vector3 pos, Vector3 size, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static void Fixture(Transform parent, GameObject prefab, float x, float z)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = new Vector3(x, CeilingHeight, z);
            go.transform.rotation = Quaternion.identity;
        }

        // ───────────────────────── assets ─────────────────────────

        private static Material EnsureCeilingMaterial(Material concrete)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CeilingMatPath);
            if (mat == null)
            {
                mat = new Material(concrete);
                AssetDatabase.CreateAsset(mat, CeilingMatPath);
            }
            // 천장은 아래를 향해 Spot 직사광을 못 받으므로 FlatKit 그늘색(_ColorDim)이 곧 천장 밝기다.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", CeilingColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", CeilingColor);
            if (mat.HasProperty("_ColorDim")) mat.SetColor("_ColorDim", CeilingDimColor);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material EnsureLampMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(LampMatPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, LampMatPath);
            }
            mat.SetColor("_BaseColor", LampColor);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject EnsureFixturePrefab(Material lampMat)
        {
            // 매번 덮어써서 아래 상수 변경이 프리팹(과 씬 인스턴스)에 반영되게 한다. 경로가 같으면 GUID 유지.
            EnsureFolder("Assets/Prefabs/Lighting");

            var root = new GameObject("CeilingLightFixture");

            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "Lamp";
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(0f, -LampSize.y * 0.5f, 0f);
            lamp.transform.localScale = LampSize;
            var mr = lamp.GetComponent<MeshRenderer>();
            mr.sharedMaterial = lampMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, -LampSize.y - 0.05f, 0f);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = LightColor;
            light.intensity = LightIntensity;
            light.range = LightRange;
            light.spotAngle = SpotAngle;
            light.innerSpotAngle = InnerSpotAngle;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;
            light.shadowNearPlane = 0.2f;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            var urpLight = lightGo.AddComponent<UniversalAdditionalLightData>();
            // 그림자 아틀라스(2048)에 여러 개가 들어가도록 Medium(512) 티어. 공개 setter가 없어 직렬화 필드로 설정.
            var so = new SerializedObject(urpLight);
            var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
            if (tier != null)
            {
                tier.intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            lightGo.AddComponent<BudgetedLight>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, FixturePrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static VolumeProfile EnsureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null) return profile;
            EnsureFolder("Assets/Settings/Volumes");

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.6f);
            bloom.scatter.Override(0.6f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.4f);
            color.contrast.Override(8f);
            color.saturation.Override(-5f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.4f);

            foreach (var c in profile.components)
                AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
