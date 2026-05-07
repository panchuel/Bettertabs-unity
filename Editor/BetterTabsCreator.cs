using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BetterTabs
{
    internal static class BetterTabsCreator
    {
        public static string CreateFolder(string parentPath, System.Action<string> onRenameRequested)
        {
            string newPath = AssetDatabase.GenerateUniqueAssetPath(parentPath + "/New Folder");
            AssetDatabase.CreateFolder(parentPath, Path.GetFileName(newPath));
            AssetDatabase.Refresh();
            onRenameRequested?.Invoke(newPath);
            return newPath;
        }

        public static void CreateScript(string parentPath)
        {
            string templatePath = EditorApplication.applicationContentsPath
                + "/Resources/ScriptTemplates/81-C# Script-NewBehaviourScript.cs.txt";

            string destPath = AssetDatabase.GenerateUniqueAssetPath(parentPath + "/NewBehaviourScript.cs");
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(templatePath, destPath);
        }

        public static void CreateScene(string parentPath)
        {
            string destPath = AssetDatabase.GenerateUniqueAssetPath(parentPath + "/NewScene.unity");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, destPath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
        }

        public static void CreateMaterial(string parentPath)
        {
            string destPath = AssetDatabase.GenerateUniqueAssetPath(parentPath + "/New Material.mat");
            var mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, destPath);
            AssetDatabase.Refresh();
        }
    }
}
