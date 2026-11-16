using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OskarMike.Network.Player.EditorTools
{
    /// <summary>
    /// 1인칭 몸 메시 굽기: 플레이어 몸 메시에서 머리·목·어깨·양팔 삼각형을 지운 사본을 만든다.
    /// 메뉴: Tools/OSCAR-MIKE/Bake First-Person Body Mesh
    /// 결과: Assets/Models/FirstPerson/{원본 메시 이름}_FP.asset, 두 플레이어 프리팹의 FirstPersonBodyView 에 연결.
    /// 정점 가중치 합이 threshold 를 넘는 "제거 뼈" 정점이 하나라도 있는 삼각형을 지운다. 정점·뼈 구조는 그대로라
    /// 원본과 같은 bones/rootBone 으로 스키닝된다.
    /// </summary>
    public static class FirstPersonBodyMeshBaker
    {
        private const string Folder = "Assets/Models/FirstPerson";
        private const float Threshold = 0.5f;
        private static readonly string[] PrefabPaths = { "Assets/Prefabs/Player.prefab" };

        // 어깨(쇄골)까지 지운다: 카메라 바로 옆이라 남기면 팔을 뗀 단면이 눈앞에 크게 보인다.
        private static readonly HumanBodyBones[] RemovedRoots =
        {
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm
        };

        [MenuItem("Tools/OSCAR-MIKE/Bake First-Person Body Mesh")]
        public static void Bake()
        {
            Mesh baked = null;
            foreach (string path in PrefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var view = root.GetComponent<FirstPersonBodyView>();
                    var animator = root.GetComponentInChildren<Animator>(true);
                    var body = animator != null ? animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                    if (view == null || body == null || !animator.isHuman)
                    {
                        Debug.LogWarning($"[FirstPersonBodyMeshBaker] {path}: FirstPersonBodyView / 휴머노이드 몸 메시가 없습니다.");
                        continue;
                    }
                    if (baked == null) baked = BakeMesh(animator, body);
                    if (baked == null) continue;

                    var so = new SerializedObject(view);
                    so.FindProperty("firstPersonMesh").objectReferenceValue = baked;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Debug.Log($"[FirstPersonBodyMeshBaker] {path} 에 {baked.name} 연결");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
        }

        private static Mesh BakeMesh(Animator animator, SkinnedMeshRenderer body)
        {
            Mesh source = body.sharedMesh;

            var removedTransforms = new HashSet<Transform>();
            foreach (var bone in RemovedRoots)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t == null) continue;
                foreach (var child in t.GetComponentsInChildren<Transform>(true)) removedTransforms.Add(child);
            }

            Transform[] bones = body.bones;
            var removedBone = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++) removedBone[i] = bones[i] != null && removedTransforms.Contains(bones[i]);

            BoneWeight[] weights = source.boneWeights;
            var removedVertex = new bool[weights.Length];
            int removedCount = 0;
            for (int v = 0; v < weights.Length; v++)
            {
                BoneWeight w = weights[v];
                float sum = 0f;
                if (IsRemoved(removedBone, w.boneIndex0)) sum += w.weight0;
                if (IsRemoved(removedBone, w.boneIndex1)) sum += w.weight1;
                if (IsRemoved(removedBone, w.boneIndex2)) sum += w.weight2;
                if (IsRemoved(removedBone, w.boneIndex3)) sum += w.weight3;
                removedVertex[v] = sum > Threshold;
                if (removedVertex[v]) removedCount++;
            }

            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + "_FP";
            int keptTriangles = 0, totalTriangles = 0;
            for (int sub = 0; sub < source.subMeshCount; sub++)
            {
                int[] tris = source.GetTriangles(sub);
                var kept = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    totalTriangles++;
                    if (removedVertex[tris[i]] || removedVertex[tris[i + 1]] || removedVertex[tris[i + 2]]) continue;
                    kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                    keptTriangles++;
                }
                mesh.SetTriangles(kept, sub);
            }

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }
            string assetPath = $"{Folder}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
                EditorUtility.SetDirty(mesh);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, assetPath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FirstPersonBodyMeshBaker] {assetPath}: 정점 {removedCount}/{weights.Length} 제거 대상, 삼각형 {keptTriangles}/{totalTriangles} 유지");
            return mesh;
        }

        private static bool IsRemoved(bool[] removedBone, int index) => index >= 0 && index < removedBone.Length && removedBone[index];
    }
}
