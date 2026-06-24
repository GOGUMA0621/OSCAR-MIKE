using OskarMike.UI;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LobbyUIInstaller))]
public class LobbyUIInstallerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var installer = (LobbyUIInstaller)target;

        if (GUILayout.Button("Create Lobby UI", GUILayout.Height(40)))
        {
            installer.CreateLobbyUI();
        }
    }
}
