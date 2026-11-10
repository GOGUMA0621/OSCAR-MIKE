using OskarMike.AI.Testing;
using OskarMike.Network.Player;
using OskarMike.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace OskarMike.Editor
{
    public static class BasicHealthSetup
    {
        [MenuItem("Tools/OSKAR MIKE/Combat/기본 체력 프리팹 연결")]
        public static void Run()
        {
            const string playerPath = "Assets/Prefabs/Player.prefab";
            var root = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                if (root.GetComponent<PlayerHealth>() == null) root.AddComponent<PlayerHealth>();
                var hud = root.GetComponent<PlayerHealthHud>() ?? root.AddComponent<PlayerHealthHud>();
                var serialized = new SerializedObject(hud);
                serialized.FindProperty("font").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Pretendard-Regular SDF.asset");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, playerPath);
                // TestPlayer is a generated copy; retain its GUID and its test input adapter.
                if (root.GetComponent<EnemyAITestPlayer>() == null) root.AddComponent<EnemyAITestPlayer>();
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/AI/Prefabs/TestPlayer.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[체력] 제품/AI 테스트 플레이어 연결 완료");
        }
    }
}
