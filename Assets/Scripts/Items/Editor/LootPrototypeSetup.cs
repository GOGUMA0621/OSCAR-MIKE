#if UNITY_EDITOR
using System.Collections.Generic;
using OskarMike.MapGeneration;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OskarMike.Items.Editor
{
    public static class LootPrototypeSetup
    {
        private const string Root = "Assets/Items/Prototype";

        [MenuItem("Tools/OSCAR-MIKE/Items/Create Prototype Loot Setup")]
        public static void Create()
        {
            EnsureFolders();
            NetworkObject commonPrefab = CreatePrefab("Loot_Common", PrimitiveType.Cube, new Color(0.55f, 0.58f, 0.62f));
            NetworkObject uncommonPrefab = CreatePrefab("Loot_Uncommon", PrimitiveType.Cylinder, new Color(0.2f, 0.75f, 0.35f));
            NetworkObject rarePrefab = CreatePrefab("Loot_Rare", PrimitiveType.Sphere, new Color(0.2f, 0.5f, 1f));

            LootItemDefinition common = CreateItem("scrap_common", "일반 폐품", LootRarity.Common, 35, 8, commonPrefab, null);
            LootItemDefinition uncommon = CreateItem("scrap_uncommon", "고급 폐품", LootRarity.Uncommon, 90, 4, uncommonPrefab, null);
            LootItemDefinition rare = CreateItem("scrap_rare", "희귀 폐품", LootRarity.Rare, 220, 2, rarePrefab, null);
            LootItemDefinition special = CreateItem("restricted_sample", "지역 특수 샘플", LootRarity.Rare, 350, 1, rarePrefab,
                new[] { "restricted" });

            LootZoneProfile defaultZone = CreateZone("DefaultZone", "default", 70, 25, 5);
            CreateZone("RestrictedZone", "restricted", 45, 35, 20);
            LootTable table = CreateTable(new[] { common, uncommon, rare, special });

            RegisterNetworkPrefab(commonPrefab);
            RegisterNetworkPrefab(uncommonPrefab);
            RegisterNetworkPrefab(rarePrefab);
            AssignDefaultZoneToRooms(defaultZone);
            ConfigureOpenScene(table, defaultZone);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[LootPrototypeSetup] 프로토타입 폐품 프리팹, 데이터, 지역 프로필 및 현재 씬 설정을 생성했습니다.");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Items")) AssetDatabase.CreateFolder("Assets", "Items");
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets/Items", "Prototype");
            if (!AssetDatabase.IsValidFolder(Root + "/Prefabs")) AssetDatabase.CreateFolder(Root, "Prefabs");
            if (!AssetDatabase.IsValidFolder(Root + "/Materials")) AssetDatabase.CreateFolder(Root, "Materials");
            if (!AssetDatabase.IsValidFolder(Root + "/Data")) AssetDatabase.CreateFolder(Root, "Data");
        }

        private static NetworkObject CreatePrefab(string name, PrimitiveType primitive, Color color)
        {
            string materialPath = $"{Root}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { color = color };
                AssetDatabase.CreateAsset(material, materialPath);
            }

            string prefabPath = $"{Root}/Prefabs/{name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing.GetComponent<NetworkObject>();

            GameObject instance = GameObject.CreatePrimitive(primitive);
            instance.name = name;
            instance.transform.localScale = primitive == PrimitiveType.Cylinder
                ? new Vector3(0.35f, 0.25f, 0.35f)
                : Vector3.one * 0.5f;
            instance.GetComponent<Renderer>().sharedMaterial = material;
            instance.AddComponent<NetworkObject>();
            instance.AddComponent<NetworkLootItem>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);
            return prefab.GetComponent<NetworkObject>();
        }

        private static LootItemDefinition CreateItem(string id, string displayName, LootRarity rarity,
            int value, int weight, NetworkObject prefab, string[] zones)
        {
            string path = $"{Root}/Data/{id}.asset";
            LootItemDefinition item = AssetDatabase.LoadAssetAtPath<LootItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<LootItemDefinition>();
                AssetDatabase.CreateAsset(item, path);
            }

            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = id;
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.FindProperty("rarity").enumValueIndex = (int)rarity;
            serialized.FindProperty("baseValue").intValue = value;
            serialized.FindProperty("valueVariance").floatValue = 0.15f;
            serialized.FindProperty("spawnWeight").intValue = weight;
            serialized.FindProperty("networkPrefab").objectReferenceValue = prefab;
            SerializedProperty allowed = serialized.FindProperty("allowedZoneIds");
            allowed.arraySize = zones?.Length ?? 0;
            for (int i = 0; i < allowed.arraySize; i++) allowed.GetArrayElementAtIndex(i).stringValue = zones[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return item;
        }

        private static LootZoneProfile CreateZone(string name, string id, int common, int uncommon, int rare)
        {
            string path = $"{Root}/Data/{name}.asset";
            LootZoneProfile zone = AssetDatabase.LoadAssetAtPath<LootZoneProfile>(path);
            if (zone == null)
            {
                zone = ScriptableObject.CreateInstance<LootZoneProfile>();
                AssetDatabase.CreateAsset(zone, path);
            }
            var serialized = new SerializedObject(zone);
            serialized.FindProperty("zoneId").stringValue = id;
            serialized.FindProperty("budgetWeight").intValue = 1;
            serialized.FindProperty("commonWeight").intValue = common;
            serialized.FindProperty("uncommonWeight").intValue = uncommon;
            serialized.FindProperty("rareWeight").intValue = rare;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return zone;
        }

        private static LootTable CreateTable(IReadOnlyList<LootItemDefinition> items)
        {
            string path = $"{Root}/Data/PrototypeLootTable.asset";
            LootTable table = AssetDatabase.LoadAssetAtPath<LootTable>(path);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LootTable>();
                AssetDatabase.CreateAsset(table, path);
            }
            var serialized = new SerializedObject(table);
            SerializedProperty list = serialized.FindProperty("items");
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return table;
        }

        private static void RegisterNetworkPrefab(NetworkObject prefab)
        {
            const string path = "Assets/DefaultNetworkPrefabs.asset";
            NetworkPrefabsList listAsset = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(path);
            if (listAsset == null) return;
            var serialized = new SerializedObject(listAsset);
            SerializedProperty list = serialized.FindProperty("List");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab.gameObject)
                    return;
            }
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty entry = list.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Override").intValue = 0;
            entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignDefaultZoneToRooms(LootZoneProfile zone)
        {
            string[] guids = AssetDatabase.FindAssets("t:RoomConfig");
            foreach (string guid in guids)
            {
                RoomConfig room = AssetDatabase.LoadAssetAtPath<RoomConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (room == null || room.lootZone != null) continue;
                room.lootZone = zone;
                EditorUtility.SetDirty(room);
            }
        }

        private static void ConfigureOpenScene(LootTable table, LootZoneProfile zone)
        {
            ProceduralMapGenerator generator = Object.FindFirstObjectByType<ProceduralMapGenerator>();
            if (generator == null) return;
            NetworkLootSpawner spawner = generator.GetComponent<NetworkLootSpawner>();
            if (spawner == null) spawner = Undo.AddComponent<NetworkLootSpawner>(generator.gameObject);
            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("mapGenerator").objectReferenceValue = generator;
            serialized.FindProperty("lootTable").objectReferenceValue = table;
            serialized.FindProperty("fallbackZone").objectReferenceValue = zone;
            serialized.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
#endif
