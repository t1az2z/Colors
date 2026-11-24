using UnityEditor;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Настройки импорта моделей из Blender (Assets/Game/Art/Models): Read/Write для деформации на CPU,
    /// без анимаций и без встроенных материалов (материалы назначаются в префабах).
    /// </summary>
    public sealed class ModelImportSettings : AssetPostprocessor
    {
        private const string Folder = "Assets/Game/Art/Models/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder))
                return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = false;
            importer.weldVertices = false;
            importer.importNormals = ModelImporterNormals.Import;
        }
    }
}
