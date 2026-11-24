using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Изометрическая камера на Cinemachine: CinemachineCamera + Orbital Follow (вертикаль фиксирована — наклон из конфига)
    /// + Rotation Composer. Горизонталь вращается свободно движением мыши (курсор спрятан) и правым стиком,
    /// колесо — приближение. Числа — Configs/Camera.json. Пока открыт инвентарь, камера не вращается, курсор свободен.
    /// </summary>
    public sealed class CameraRig : GameBehaviour
    {
        [SerializeField] private CinemachineCamera virtualCamera;
        [SerializeField] private Transform followTarget;

        private CameraConfig config;
        private GameInput input;
        private GameSession session;
        private CinemachineOrbitalFollow orbit;
        private float yaw;
        private float targetYaw;
        private float yawVelocity;
        private float distance;

        public CameraScheme Scheme => config != null ? config.Scheme : CameraScheme.FreeOrbit;

        protected override void OnConstruct(IResolver resolver)
        {
            config = resolver.Resolve<ConfigService>().Load<CameraConfig>(ConfigKeys.Camera);
            input = resolver.Resolve<GameInput>();
            session = resolver.Resolve<GameSession>();
        }

        private void Start()
        {
            orbit = virtualCamera.GetComponent<CinemachineOrbitalFollow>();
            var lens = virtualCamera.Lens;
            lens.FieldOfView = config.FieldOfView;
            lens.NearClipPlane = 0.5f;
            lens.FarClipPlane = 300f;
            virtualCamera.Lens = lens;
            if (followTarget != null)
                virtualCamera.Follow = followTarget;

            yaw = targetYaw = config.StartYaw;
            distance = config.Distance;
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.VerticalAxis.Range = new Vector2(-10f, 89f);
            orbit.TrackerSettings.PositionDamping = Vector3.one * config.FollowDamping;
            Apply();
            orbit.ForceCameraPosition(virtualCamera.transform.position, virtualCamera.transform.rotation);
        }

        private void Update()
        {
            if (config == null || orbit == null)
                return;

            var control = session.PlayerControl.Value;
            var fixedAngles = config.Scheme == CameraScheme.FixedAngles;
            if (config.LockCursor)
            {
                var wanted = control && !fixedAngles && Application.isFocused ? CursorLockMode.Locked : CursorLockMode.None;
                if (Cursor.lockState != wanted)
                {
                    Cursor.lockState = wanted;
                    Cursor.visible = wanted != CursorLockMode.Locked;
                }
            }

            if (control && fixedAngles)
            {
                // Схема 2: четыре угла на LB/RB (Q/E), мышь и правый стик заняты направлением взгляда.
                if (input.Pressed(GameAction.CameraLeft))
                    targetYaw -= config.FixedAngleStep;
                if (input.Pressed(GameAction.CameraRight))
                    targetYaw += config.FixedAngleStep;
                yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, config.FixedRotateTime, Mathf.Infinity, Time.unscaledDeltaTime);
                Zoom();
            }
            else if (control)
            {
                var mouse = Mouse.current;
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                    yaw += mouse.delta.ReadValue().x * config.MouseRotateSpeed;
                var stick = input.CameraStick.ReadValue<Vector2>();
                yaw += stick.x * config.StickRotateSpeed * Time.deltaTime;

                Zoom();
                targetYaw = yaw;
            }

            if (!fixedAngles)
                yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            Apply();
        }

        private void Zoom()
        {
            var scroll = input.Zoom.ReadValue<Vector2>().y;
            if (Mathf.Abs(scroll) > 0.01f)
                distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * config.ZoomStep, config.MinDistance, config.MaxDistance);
        }

        private void Apply()
        {
            orbit.HorizontalAxis.Value = yaw;
            orbit.VerticalAxis.Value = config.Pitch;
            orbit.Radius = Mathf.Lerp(orbit.Radius <= 0 ? distance : orbit.Radius, distance, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));
        }
    }
}
