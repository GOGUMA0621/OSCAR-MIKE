using UnityEditor;
using UnityEditor.SceneManagement;

namespace OskarMike.AI.Editor
{
    public static class EnemyAIValidation
    {
        [MenuItem("Tools/OSKAR MIKE/AI/자동 검증")]
        public static void Run()
        {
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(EnemyAITestBuilder.ScenePath);
            SessionState.SetBool("OskarMike.AI.Validation", true);
            EditorApplication.EnterPlaymode();
        }
    }
}
