using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Снимки предметов для сверки с концептом (Art/Concepts/Items.png): каждая модель и предмет на земле
    /// рендерится в отдельной preview-сцене (открытая сцена не меняется) в Temp/ItemPreviews/&lt;имя&gt;.png
    /// и общий лист Temp/ItemPreviews/Sheet.png. Меню ColorsAction → Items → Render Previews.
    /// </summary>
    public static class ItemPreviewRenderer
    {
        private const string Out = "Temp/ItemPreviews";
        private const int Size = 384;

        private struct Shot
        {
            public string Name;
            public string Prefab;
            public Vector3 Euler;
            public Vector3 View;
            public Color Tint;
            public bool OpenChest;
        }

        [MenuItem("ColorsAction/Items/Render Previews")]
        public static string Render()
        {
            var blue = new Color(0.18f, 0.42f, 1f);
            var red = new Color(0.91f, 0.25f, 0.24f);
            var green = new Color(0.24f, 0.75f, 0.35f);
            var shots = new List<Shot>
            {
                new Shot { Name = "Sword", Prefab = "Items/Sword", Euler = new Vector3(0, 0, -35), View = new Vector3(0.5f, 0.45f, 1f), Tint = blue },
                new Shot { Name = "Shield", Prefab = "Items/Shield", Euler = new Vector3(0, -15, 0), View = new Vector3(0.5f, 0.35f, 1f), Tint = blue },
                new Shot { Name = "Shotgun", Prefab = "Items/Shotgun", Euler = new Vector3(0, 90, 25), View = new Vector3(0.3f, 0.4f, 1f), Tint = red },
                new Shot { Name = "Club", Prefab = "Items/Club", Euler = new Vector3(0, 0, -25), View = new Vector3(0.5f, 0.45f, 1f) },
                new Shot { Name = "Key", Prefab = "Items/Key", Euler = new Vector3(0, 0, -45), View = new Vector3(0.5f, 0.45f, 1f) },
                new Shot { Name = "Flask", Prefab = "Items/Flask", Euler = Vector3.zero, View = new Vector3(0.3f, 0.35f, 1f), Tint = green },
                new Shot { Name = "Chest", Prefab = Chest.PrefabPath, Euler = new Vector3(0, -25, 0), View = new Vector3(0.5f, 0.6f, 1f) },
                new Shot { Name = "ChestOpen", Prefab = Chest.PrefabPath, Euler = new Vector3(0, -25, 0), View = new Vector3(0.5f, 0.6f, 1f), OpenChest = true },
                new Shot { Name = "PickupSword", Prefab = "Items/Pickups/Sword", Euler = new Vector3(0, 0, 0), View = new Vector3(0.6f, 0.75f, 1f), Tint = blue },
                new Shot { Name = "PickupShotgun", Prefab = "Items/Pickups/Shotgun", Euler = new Vector3(0, 0, 0), View = new Vector3(0.6f, 0.75f, 1f), Tint = red },
                new Shot { Name = "PickupKey", Prefab = "Items/Pickups/Key", Euler = new Vector3(0, 0, 0), View = new Vector3(0.6f, 0.75f, 1f) },
                new Shot { Name = "PickupFlask", Prefab = "Items/Pickups/Flask", Euler = new Vector3(0, 0, 0), View = new Vector3(0.6f, 0.75f, 1f), Tint = green },
            };

            Directory.CreateDirectory(Out);
            var images = new List<Texture2D>();
            foreach (var shot in shots)
                images.Add(RenderOne(shot));

            const int columns = 4;
            var rows = (images.Count + columns - 1) / columns;
            var sheet = new Texture2D(columns * Size, rows * Size, TextureFormat.RGB24, false);
            for (int i = 0; i < images.Count; i++)
            {
                var x = i % columns * Size;
                var y = (rows - 1 - i / columns) * Size;
                sheet.SetPixels(x, y, Size, Size, images[i].GetPixels());
                Object.DestroyImmediate(images[i]);
            }

            var path = Path.GetFullPath(Path.Combine(Out, "Sheet.png"));
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Debug.Log("Item previews: " + path);
            return path;
        }

        private static Texture2D RenderOne(Shot shot)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var prefab = Resources.Load<GameObject>(shot.Prefab);
                var go = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.rotation = Quaternion.Euler(shot.Euler);
                if (shot.OpenChest)
                {
                    var lid = go.transform.Find("Body/Lid");
                    if (lid != null)
                        lid.localRotation = Quaternion.Euler(-105f, 0f, 0f);
                }

                foreach (var visual in go.GetComponentsInChildren<ItemVisual>())
                    if (shot.Tint.a > 0f)
                        visual.SetColor(shot.Tint, 0.15f);
                var pickup = go.GetComponent<PickupVisual>();
                if (pickup != null)
                    pickup.Setup(shot.Tint.a > 0f ? shot.Tint : new Color(0.62f, 0.63f, 0.65f));
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                    ps.Simulate(2f, true, true);

                var bounds = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    if (!r.name.Contains("Beam"))
                        bounds.Encapsulate(r.bounds);

                var sun = new GameObject("Sun").AddComponent<Light>();
                SceneManager.MoveGameObjectToScene(sun.gameObject, scene);
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                sun.color = new Color(1f, 0.97f, 0.93f);
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                sun.shadows = LightShadows.Soft;

                var camGo = new GameObject("Camera");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.93f, 0.89f, 0.9f);
                cam.fieldOfView = 25f;
                var radius = bounds.extents.magnitude;
                var distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                cam.transform.position = bounds.center + shot.View.normalized * distance;
                cam.transform.LookAt(bounds.center);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = distance * 3f;

                var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 4;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(Out, shot.Name + ".png"), tex.EncodeToPNG());
                return tex;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
