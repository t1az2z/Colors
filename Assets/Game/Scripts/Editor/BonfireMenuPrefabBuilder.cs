using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Префаб меню костра Resources/UI/BonfireMenu.prefab (Canvas + CanvasScaler 1920×1080, как остальные экраны
    /// из <see cref="UiPrefabBuilder"/>). Раскладка — <see cref="BonfireView.BuildDefault"/>; после сборки префаб
    /// можно править руками, повторный запуск его перезапишет.
    /// </summary>
    public static class BonfireMenuPrefabBuilder
    {
        private const string Folder = "Assets/Game/Resources/UI";
        private const string Path = Folder + "/BonfireMenu.prefab";

        [MenuItem("ColorsAction/UI/Build Bonfire Menu Prefab")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Resources"))
                AssetDatabase.CreateFolder("Assets/Game", "Resources");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Game/Resources", "UI");

            var view = BonfireView.BuildDefault();
            try
            {
                PrefabUtility.SaveAsPrefabAsset(view.gameObject, Path);
            }
            finally
            {
                Object.DestroyImmediate(view.gameObject);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[UI] Bonfire menu prefab saved: {Path}");
        }
    }
}
