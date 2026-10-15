#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OskarMike.MapGeneration;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OskarMike.Items.Editor
{
    public static class LootCatalogImporter
    {
        private const string CatalogPath = "Tools/LootCatalog/LootCatalog.tsv";
        private const string GeneratedRoot = "Assets/Items/Catalog/Generated";
        private const string DefinitionRoot = GeneratedRoot + "/Definitions";
        private const string PrefabRoot = GeneratedRoot + "/Prefabs";
        private const string ProfileRoot = GeneratedRoot + "/Profiles";
        private const string PackRoot = GeneratedRoot + "/ContentPacks";
        private const string LootTablePath = ProfileRoot + "/CatalogLootTable.asset";
        private const string EconomyPath = ProfileRoot + "/DefaultLootEconomy.asset";
        private const string ZonePath = ProfileRoot + "/DefaultLootZone.asset";
        private const string NetworkPrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

        private sealed class Row
        {
            public int Sequence;
            public string AssetName;
            public string DisplayName;
            public string PackName;
            public byte MinValueSteps;
            public byte MaxValueSteps;
            public LootCategory Category;
            public int BasePrice;
            public float PriceVariance;
            public bool RequestedSpawnEnabled;
            public string[] AllowedZones;
            public string Notes;
        }

        [MenuItem("Tools/OSCAR-MIKE/Items/Import Items from Loot Catalog")]
        public static void ImportCatalog()
        {
            EnsureFolders();
            List<string> warnings = new List<string>();
            List<string> errors = new List<string>();
            List<Row> rows = ReadRows(errors);
            ValidateRows(rows, warnings, errors);
            if (errors.Count > 0)
            {
                LogReport(rows.Count, 0, 0, warnings, errors);
                return;
            }

            Dictionary<string, LootContentPack> packs = CreateContentPacks();
            var definitions = new List<LootItemDefinition>(rows.Count);
            var networkPrefabs = new List<NetworkObject>(rows.Count);
            int connectedPrefabs = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (Row row in rows)
                {
                    if (!packs.TryGetValue(row.PackName, out LootContentPack pack))
                    {
                        errors.Add($"#{row.Sequence} 알 수 없는 콘텐츠 팩: {row.PackName}");
                        continue;
                    }

                    List<GameObject> sourceMatches = FindExactPrefabs(pack.AssetRoot, row.AssetName);
                    if (sourceMatches.Count != 1)
                    {
                        errors.Add($"#{row.Sequence} {row.PackName}/{row.AssetName}: 프리팹 {sourceMatches.Count}개 발견");
                    }

                    NetworkObject wrapper = sourceMatches.Count == 1
                        ? CreateOrGetNetworkWrapper(row, sourceMatches[0])
                        : null;
                    if (wrapper != null)
                    {
                        networkPrefabs.Add(wrapper);
                        connectedPrefabs++;
                    }

                    LootItemDefinition definition = CreateOrUpdateDefinition(row, pack, wrapper);
                    definitions.Add(definition);
                }

                LootEconomyProfile economy = CreateOrGetProfile<LootEconomyProfile>(EconomyPath);
                LootZoneProfile zone = CreateOrGetProfile<LootZoneProfile>(ZonePath);
                ConfigureDefaultZone(zone);
                LootTable table = CreateOrUpdateLootTable(definitions);
                AssignDefaultZoneToRooms(zone);
                ConfigureOpenScene(table, zone, economy);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            RegisterNetworkPrefabs(networkPrefabs);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            LogReport(rows.Count, connectedPrefabs, definitions.Count, warnings, errors);
        }

        [MenuItem("Tools/OSCAR-MIKE/Items/Validate Loot Catalog")]
        public static void ValidateCatalog()
        {
            var warnings = new List<string>();
            var errors = new List<string>();
            List<Row> rows = ReadRows(errors);
            ValidateRows(rows, warnings, errors);
            int found = 0;
            Dictionary<string, string> roots = GetPackRoots();
            foreach (Row row in rows)
            {
                if (!roots.TryGetValue(row.PackName, out string root)) continue;
                int count = FindExactPrefabs(root, row.AssetName).Count;
                if (count == 1) found++;
                else errors.Add($"#{row.Sequence} {row.PackName}/{row.AssetName}: 프리팹 {count}개 발견");
            }
            LogReport(rows.Count, found, 0, warnings, errors);
        }

        private static List<Row> ReadRows(List<string> errors)
        {
            var rows = new List<Row>();
            if (!File.Exists(CatalogPath))
            {
                errors.Add($"카탈로그 파일 없음: {CatalogPath}");
                return rows;
            }

            string[] lines = File.ReadAllLines(CatalogPath);
            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                if (string.IsNullOrWhiteSpace(lines[lineIndex])) continue;
                string[] c = lines[lineIndex].Split('\t');
                if (c.Length < 10)
                {
                    errors.Add($"{lineIndex + 1}행: 필수 열 개수 {c.Length}, 필요 10");
                    continue;
                }
                if (c.Length < 12) Array.Resize(ref c, 12);

                try
                {
                    var row = new Row
                    {
                        Sequence = int.Parse(c[0], CultureInfo.InvariantCulture),
                        AssetName = CanonicalizeAssetName(c[1]),
                        DisplayName = c[2].Trim(),
                        PackName = c[3].Trim(),
                        MinValueSteps = ParseValueSteps(c[4]),
                        MaxValueSteps = ParseValueSteps(c[5]),
                        Category = ParseEnum<LootCategory>(c[6]),
                        BasePrice = int.Parse(c[7], CultureInfo.InvariantCulture),
                        PriceVariance = float.Parse(c[8], CultureInfo.InvariantCulture),
                        RequestedSpawnEnabled = bool.Parse(c[9]),
                        AllowedZones = (c[10] ?? string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(value => value.Trim()).ToArray(),
                        Notes = (c[11] ?? string.Empty).Trim()
                    };
                    if (row.MinValueSteps > row.MaxValueSteps)
                        throw new FormatException("최소 밸류가 최대 밸류보다 큼");
                    rows.Add(row);
                }
                catch (Exception exception)
                {
                    errors.Add($"{lineIndex + 1}행 파싱 실패: {exception.Message}");
                }
            }
            return rows;
        }

        private static void ValidateRows(List<Row> rows, List<string> warnings, List<string> errors)
        {
            if (rows.Count != 87) errors.Add($"카탈로그 행 수 {rows.Count}, 필요 87");
            var sequenceSet = new HashSet<int>();
            var idSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Row row in rows)
            {
                if (!sequenceSet.Add(row.Sequence)) errors.Add($"중복 순번: {row.Sequence}");
                string id = BuildItemId(row.PackName, row.AssetName);
                if (!idSet.Add(id)) errors.Add($"중복 아이템 ID: {id}");
                if (row.MinValueSteps < 2 || row.MaxValueSteps > 10) errors.Add($"#{row.Sequence} 밸류 범위 오류");

                const string warningKeyword = "주의:";
                int warningIndex = row.Notes.IndexOf(warningKeyword, StringComparison.OrdinalIgnoreCase);
                if (warningIndex >= 0)
                {
                    string message = row.Notes.Substring(warningIndex + warningKeyword.Length).Trim();
                    if (string.IsNullOrEmpty(message)) message = "확인이 필요한 항목입니다.";
                    warnings.Add($"#{row.Sequence} {row.AssetName}: {message}");
                }
            }
        }

        private static Dictionary<string, LootContentPack> CreateContentPacks()
        {
            var result = new Dictionary<string, LootContentPack>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in GetPackRoots())
            {
                string path = $"{PackRoot}/{pair.Key}.asset";
                LootContentPack pack = AssetDatabase.LoadAssetAtPath<LootContentPack>(path);
                if (pack == null)
                {
                    pack = ScriptableObject.CreateInstance<LootContentPack>();
                    AssetDatabase.CreateAsset(pack, path);
                }
                var serialized = new SerializedObject(pack);
                serialized.FindProperty("packId").stringValue = pair.Key.ToLowerInvariant();
                serialized.FindProperty("assetRoot").stringValue = pair.Value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                result[pair.Key] = pack;
            }
            return result;
        }

        private static Dictionary<string, string> GetPackRoots()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PolygonCasino"] = "Assets/Synty/PolygonCasino",
                ["PolygonGangWarfare"] = "Assets/Synty/PolygonGangWarfare",
                ["PolygonMilitary"] = "Assets/Synty/PolygonMilitary"
            };
        }

        private static List<GameObject> FindExactPrefabs(string root, string assetName)
        {
            return AssetDatabase.FindAssets($"{assetName} t:Prefab", new[] { root })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), assetName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(prefab => prefab != null)
                .ToList();
        }

        private static NetworkObject CreateOrGetNetworkWrapper(Row row, GameObject sourcePrefab)
        {
            string wrapperName = $"{row.PackName}_{row.AssetName}_NetworkLoot";
            string path = $"{PrefabRoot}/{wrapperName}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<NetworkObject>();

            var root = new GameObject(wrapperName);
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkLootItem>();
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);

            Bounds bounds = CalculateLocalBounds(root);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = bounds.center;
            collider.size = new Vector3(
                Mathf.Max(0.05f, bounds.size.x),
                Mathf.Max(0.05f, bounds.size.y),
                Mathf.Max(0.05f, bounds.size.z));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab.GetComponent<NetworkObject>();
        }

        private static Bounds CalculateLocalBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.25f);
            Bounds worldBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) worldBounds.Encapsulate(renderers[i].bounds);
            return new Bounds(root.transform.InverseTransformPoint(worldBounds.center), worldBounds.size);
        }

        private static LootItemDefinition CreateOrUpdateDefinition(Row row, LootContentPack pack, NetworkObject wrapper)
        {
            string id = BuildItemId(row.PackName, row.AssetName);
            string path = $"{DefinitionRoot}/{row.Sequence:000}_{row.PackName}_{row.AssetName}.asset";
            LootItemDefinition definition = AssetDatabase.LoadAssetAtPath<LootItemDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<LootItemDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("itemId").stringValue = id;
            serialized.FindProperty("displayName").stringValue = row.DisplayName;
            serialized.FindProperty("sourceAssetName").stringValue = row.AssetName;
            serialized.FindProperty("contentPack").objectReferenceValue = pack;
            serialized.FindProperty("category").enumValueIndex = (int)row.Category;
            serialized.FindProperty("minValueSteps").intValue = row.MinValueSteps;
            serialized.FindProperty("maxValueSteps").intValue = row.MaxValueSteps;
            serialized.FindProperty("basePrice").intValue = row.BasePrice;
            serialized.FindProperty("priceVariance").floatValue = row.PriceVariance;
            serialized.FindProperty("spawnWeight").intValue = 1;
            serialized.FindProperty("spawnEnabled").boolValue = row.RequestedSpawnEnabled && wrapper != null;
            serialized.FindProperty("notes").stringValue = row.Notes;
            serialized.FindProperty("networkPrefab").objectReferenceValue = wrapper;
            SerializedProperty zones = serialized.FindProperty("allowedZoneIds");
            zones.arraySize = row.AllowedZones.Length;
            for (int i = 0; i < zones.arraySize; i++) zones.GetArrayElementAtIndex(i).stringValue = row.AllowedZones[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private static LootTable CreateOrUpdateLootTable(IReadOnlyList<LootItemDefinition> definitions)
        {
            LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>(LootTablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LootTable>();
                AssetDatabase.CreateAsset(table, LootTablePath);
            }
            var serialized = new SerializedObject(table);
            SerializedProperty items = serialized.FindProperty("items");
            items.arraySize = definitions.Count;
            for (int i = 0; i < definitions.Count; i++) items.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return table;
        }

        private static T CreateOrGetProfile<T>(string path) where T : ScriptableObject
        {
            T profile = AssetDatabase.LoadAssetAtPath<T>(path);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static void ConfigureDefaultZone(LootZoneProfile zone)
        {
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("zoneId").stringValue = "default";
            serialized.FindProperty("budgetWeight").intValue = 1;
            string[] categoryWeights =
            {
                "industrialWeight", "electronicsWeight", "junkWeight", "valuablesWeight", "militaryWeight",
                "intelWeight", "consumablesWeight", "keyWeight", "drugsWeight"
            };
            foreach (string propertyName in categoryWeights)
                serialized.FindProperty(propertyName).intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(zone);
        }

        private static void RegisterNetworkPrefabs(IReadOnlyList<NetworkObject> prefabs)
        {
            NetworkPrefabsList listAsset = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabListPath);
            if (listAsset == null) return;
            var serialized = new SerializedObject(listAsset);
            SerializedProperty list = serialized.FindProperty("List");
            var registered = new HashSet<GameObject>();
            for (int i = 0; i < list.arraySize; i++)
            {
                GameObject existing = list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue as GameObject;
                if (existing != null) registered.Add(existing);
            }
            for (int prefabIndex = 0; prefabIndex < prefabs.Count; prefabIndex++)
            {
                NetworkObject prefab = prefabs[prefabIndex];
                if (prefab == null || !registered.Add(prefab.gameObject)) continue;
                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                SerializedProperty entry = list.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("Override").intValue = 0;
                entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab.gameObject;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignDefaultZoneToRooms(LootZoneProfile zone)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:RoomConfig"))
            {
                RoomConfig room = AssetDatabase.LoadAssetAtPath<RoomConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (room == null) continue;
                string existingPath = room.lootZone != null ? AssetDatabase.GetAssetPath(room.lootZone) : string.Empty;
                if (room.lootZone != null && !existingPath.StartsWith("Assets/Items/Prototype/", StringComparison.OrdinalIgnoreCase))
                    continue;
                room.lootZone = zone;
                EditorUtility.SetDirty(room);
            }
        }

        private static void ConfigureOpenScene(LootTable table, LootZoneProfile zone, LootEconomyProfile economy)
        {
            ProceduralMapGenerator generator = UnityEngine.Object.FindFirstObjectByType<ProceduralMapGenerator>();
            if (generator == null) return;
            NetworkLootSpawner spawner = generator.GetComponent<NetworkLootSpawner>();
            if (spawner == null) spawner = Undo.AddComponent<NetworkLootSpawner>(generator.gameObject);
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("mapGenerator").objectReferenceValue = generator;
            serialized.FindProperty("lootTable").objectReferenceValue = table;
            serialized.FindProperty("fallbackZone").objectReferenceValue = zone;
            serialized.FindProperty("economyProfile").objectReferenceValue = economy;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        private static byte ParseValueSteps(string value)
        {
            float parsed = float.Parse(value.Trim(), CultureInfo.InvariantCulture);
            int steps = Mathf.RoundToInt(parsed * 2f);
            if (steps < 2 || steps > 10 || Mathf.Abs(parsed * 2f - steps) > 0.001f)
                throw new FormatException($"0.5 단위가 아닌 밸류: {value}");
            return (byte)steps;
        }

        private static T ParseEnum<T>(string value) where T : struct
        {
            string normalized = value.Trim();
            int parenthesis = normalized.IndexOf('(');
            if (parenthesis >= 0) normalized = normalized.Substring(0, parenthesis).Trim();
            if (!Enum.TryParse(normalized, true, out T result)) throw new FormatException($"알 수 없는 {typeof(T).Name}: {value}");
            return result;
        }

        private static string CanonicalizeAssetName(string value)
        {
            string result = value.Trim();
            if (result.EndsWith(" (1)", StringComparison.Ordinal)) result = result.Substring(0, result.Length - 4);
            return result;
        }

        private static string BuildItemId(string packName, string assetName)
        {
            return $"{packName}.{assetName}".ToLowerInvariant();
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Items");
            EnsureFolder("Assets/Items/Catalog");
            EnsureFolder(GeneratedRoot);
            EnsureFolder(DefinitionRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder(ProfileRoot);
            EnsureFolder(PackRoot);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void LogReport(int rows, int connected, int definitions,
            IReadOnlyList<string> warnings, IReadOnlyList<string> errors)
        {
            string summary = $"[LootCatalog] rows={rows}, connectedPrefabs={connected}, definitions={definitions}, " +
                             $"warnings={warnings.Count}, errors={errors.Count}";
            if (errors.Count == 0) Debug.Log(summary); else Debug.LogError(summary);
            foreach (string warning in warnings) Debug.LogWarning($"[LootCatalog] {warning}");
            foreach (string error in errors) Debug.LogError($"[LootCatalog] {error}");
        }
    }
}
#endif
