using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// 3D-превью персонажа в инвентаре: копия куба игрока вместе с оружием в руках (снимок на момент открытия экрана),
    /// отрисованная отдельной камерой в RenderTexture далеко под миром. Вращается правым стиком и перетаскиванием мышью.
    /// Копия без скриптов, физики и частиц, на своём слое <see cref="PreviewLayer"/>; свет — общий свет сцены
    /// (свой направленный свет в URP засветил бы и уровень). Меш куба общий с игроком, поэтому покачивание видно и в превью.
    /// </summary>
    public sealed class CharacterPreview : MonoBehaviour, IDragHandler
    {
        /// <summary>Слой копии (свободный в TagManager): её снимает только камера превью.</summary>
        public const int PreviewLayer = 30;

        private static readonly Vector3 StagePosition = new Vector3(0f, -5000f, 0f);

        [SerializeField] private float stickSpeed = 220f;
        [SerializeField] private float mouseSpeed = 0.45f;
        [Tooltip("Начальный поворот: лицом к игроку, на 3/4 вправо (относительно персонажа).")]
        [SerializeField] private float startYaw = 45f;
        [SerializeField] private int textureSize = 640;

        private RawImage image;
        private RenderTexture texture;
        private Camera previewCamera;
        private GameObject stage;
        private Transform turntable;
        private float yaw;

        public float Yaw => yaw;

        /// <summary>Повесить превью на картинку <paramref name="target"/> и снять игрока.</summary>
        public static CharacterPreview Attach(RawImage target)
        {
            var preview = target.gameObject.GetComponent<CharacterPreview>() ?? target.gameObject.AddComponent<CharacterPreview>();
            preview.image = target;
            preview.Build();
            return preview;
        }

        /// <summary>Повернуть персонажа (правый стик геймпада), градусов за секунду на полном отклонении.</summary>
        public void Rotate(float axis, float dt)
        {
            if (Mathf.Abs(axis) > 0.15f)
                yaw -= axis * stickSpeed * dt;
        }

        public void OnDrag(PointerEventData e) => yaw -= e.delta.x * mouseSpeed;

        private void Build()
        {
            yaw = startYaw;
            texture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32) { name = "CharacterPreview", antiAliasing = 4 };
            image.texture = texture;
            image.color = Color.white;
            image.raycastTarget = true;

            stage = new GameObject("CharacterPreviewStage");
            stage.transform.position = StagePosition;
            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(stage.transform, false);

            var player = FindAnyObjectByType<PlayerController>();
            var source = player != null && player.Deformer != null ? player.Deformer.transform : null;
            if (source != null)
                CloneModel(source);

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(stage.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.25f, 4.6f);
            camGo.transform.LookAt(stage.transform.position + new Vector3(0f, 0.62f, 0f));
            previewCamera = camGo.AddComponent<Camera>();
            previewCamera.fieldOfView = 28f;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 20f;
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.16f, 0.17f, 0.19f, 1f);
            previewCamera.targetTexture = texture;
            previewCamera.depth = -100f;

            Apply();
        }

        /// <summary>Копия куба игрока (меш, материалы, оружие в сокетах) без поведения.</summary>
        private void CloneModel(Transform source)
        {
            // Копия создаётся под выключенным родителем: Awake скриптов копии (деформация, свечение) не запускаются.
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            var copy = Instantiate(source.gameObject, holder.transform, false);
            Strip(copy);
            copy.transform.SetParent(turntable, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            SetLayer(copy.transform);
            Destroy(holder);
            copy.SetActive(true);
        }

        private static void Strip(GameObject root)
        {
            // Эффекты (след клинка, вспышки) — целиком, вместе с их объектами.
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps != null && ps.gameObject != root)
                    DestroyImmediate(ps.gameObject);
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                DestroyImmediate(behaviour);
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(collider);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                DestroyImmediate(body);
            foreach (var trail in root.GetComponentsInChildren<TrailRenderer>(true))
                DestroyImmediate(trail);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                DestroyImmediate(light);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        private static void SetLayer(Transform t)
        {
            t.gameObject.layer = PreviewLayer;
            foreach (Transform child in t)
                SetLayer(child);
        }

        private void LateUpdate()
        {
            if (turntable != null)
                Apply();
        }

        private void Apply()
        {
            yaw = Mathf.Repeat(yaw, 360f);
            turntable.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private void OnDestroy()
        {
            if (stage != null)
                Destroy(stage);
            if (previewCamera != null)
                previewCamera.targetTexture = null;
            if (texture != null)
            {
                texture.Release();
                Destroy(texture);
            }
        }
    }
}
