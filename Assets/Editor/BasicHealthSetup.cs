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
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[체력] 플레이어 연결 완료");
        }
    }
}
