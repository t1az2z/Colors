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
        private PlayerLockOn lockOn;
        private float yaw;
        private float targetYaw;
        private float distance;

        // Схема Б: поворот на 90° как твин фиксированной длительности.
        private float tweenFrom;
        private float tweenTime = 1f;
        private CameraScheme lastScheme;
        private CurvedWorld curvedWorld;

        // Смерть игрока: камера остаётся смотреть на место гибели (иначе при падении за мир летит вниз за телом).
        private PlayerDamageReceiver playerDamage;
        private Transform deathAnchor;
        private bool followingDeath;

        // Кадр сцены (подбор предмета): камера стоит там, куда её ставит сцена, ввод не крутит её.
        private bool shotActive;
        private Transform shotAnchor;
        private CinemachineBrain brain;
        private bool brainIgnoredTimeScale;
        private Vector3 trackerDamping;

        /// <summary>Текущий угол камеры по горизонтали, градусы (куда смотрит камера).</summary>
        public float Yaw => yaw;

        /// <summary>Обычный наклон камеры, градусы.</summary>
        public float Pitch => config != null ? config.Pitch : 35f;

        /// <summary>Обычное расстояние до игрока (с учётом приближения колесом).</summary>
        public float Distance => distance;

        /// <summary>Точка, за которой обычно следит камера.</summary>
        public Vector3 FollowPoint => followTarget != null ? followTarget.position : transform.position;

        public bool ShotActive => shotActive;

        /// <summary>
        /// Поставить камеру кадром сцены: угол по горизонтали <paramref name="shotYaw"/>, наклон, расстояние и точка
        /// взгляда. Вызывается каждый кадр сцены; пока кадр задан, мышь, стик и фиксация камеру не двигают, а Cinemachine
        /// считает по реальному времени (мир в сцене стоит). Свой угол и приближение камера помнит — после
        /// <see cref="ClearShot"/> продолжает с них.
        /// </summary>
        public void SetShot(float shotYaw, float shotPitch, float shotDistance, Vector3 lookAt)
        {
            if (orbit == null)
                return;
            if (!shotActive)
            {
                shotActive = true;
                if (shotAnchor == null)
                    shotAnchor = new GameObject("CameraShotAnchor").transform;
                brain = brain != null ? brain : FindBrain();
                if (brain != null)
                {
                    brainIgnoredTimeScale = brain.IgnoreTimeScale;
                    brain.IgnoreTimeScale = true;
                }

                // Сцена сама ведёт точку плавно: запаздывание слежения дало бы рывки.
                trackerDamping = orbit.TrackerSettings.PositionDamping;
                orbit.TrackerSettings.PositionDamping = Vector3.zero;
                virtualCamera.Follow = shotAnchor;
            }

            shotAnchor.position = lookAt;
            orbit.HorizontalAxis.Value = Mathf.Repeat(shotYaw + 180f, 360f) - 180f;
            orbit.VerticalAxis.Value = shotPitch;
            orbit.Radius = shotDistance;
        }

        /// <summary>Вернуть обычную камеру (сцена кончилась): следит за игроком, ввод снова её крутит.</summary>
        public void ClearShot()
        {
            if (!shotActive)
                return;
            shotActive = false;
            if (brain != null)
                brain.IgnoreTimeScale = brainIgnoredTimeScale;
            if (orbit != null)
                orbit.TrackerSettings.PositionDamping = trackerDamping;
            virtualCamera.Follow = followingDeath && deathAnchor != null ? deathAnchor : followTarget;
            Apply();
        }

        private CinemachineBrain FindBrain()
        {
            var main = UnityEngine.Camera.main;
            return main != null ? main.GetComponent<CinemachineBrain>() : FindAnyObjectByType<CinemachineBrain>();
        }

        public CameraScheme Scheme => session != null ? session.ControlScheme.Value : CameraScheme.FreeOrbit;

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
            lastScheme = Scheme;
            distance = config.Distance;
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.VerticalAxis.Range = new Vector2(-10f, 89f);
            orbit.TrackerSettings.PositionDamping = Vector3.one * config.FollowDamping;
            Apply();
            orbit.ForceCameraPosition(virtualCamera.transform.position, virtualCamera.transform.rotation);

            // Искривление мира к горизонту: центр — игрок (за ним следует камера).
            curvedWorld = new CurvedWorld(config.CurvedWorld, followTarget);
            curvedWorld.Tick();
        }

        private void LateUpdate() => curvedWorld?.Tick();

        private void OnDestroy()
        {
            curvedWorld?.Dispose();
            curvedWorld = null;
            if (deathAnchor != null)
                Destroy(deathAnchor.gameObject);
            if (shotAnchor != null)
                Destroy(shotAnchor.gameObject);
        }

        private void Update()
        {
            if (config == null || orbit == null)
                return;
            if (shotActive)
                return;

            UpdateDeathFollow();

            // Колесо оружия открыто: мышь и правый стик выбирают сектор, курсор свободен.
            var control = session.PlayerControl.Value && !WeaponWheel.IsOpen;
            var fixedAngles = Scheme == CameraScheme.FixedAngles;
            if (Scheme != lastScheme)
            {
                lastScheme = Scheme;
                if (fixedAngles)
                    StartTurn(NearestFixedAngle(yaw));
            }
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
                    StartTurn(targetYaw - config.FixedAngleStep);
                if (input.Pressed(GameAction.CameraRight))
                    StartTurn(targetYaw + config.FixedAngleStep);
                Zoom();
            }
            else if (control && LockedYaw(out var lockedYaw))
            {
                // Фиксация на цели: камера плавно уходит за спину персонажу, цель — в кадре.
                // Мышь и правый стик в это время переключают цель, камеру не крутят.
                var speed = lockOn.Config.LockOnCameraSpeed;
                yaw = Mathf.LerpAngle(yaw, lockedYaw, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
                Zoom();
                targetYaw = yaw;
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

            if (fixedAngles)
            {
                // Поворот ровно за FixedRotateTime с плавным разгоном и торможением (GDD: ~0.25 с).
                tweenTime = Mathf.Min(1f, tweenTime + Time.unscaledDeltaTime / Mathf.Max(0.01f, config.FixedRotateTime));
                var k = tweenTime * tweenTime * (3f - 2f * tweenTime);
                yaw = tweenFrom + Mathf.DeltaAngle(tweenFrom, targetYaw) * k;
            }
            else
            {
                yaw = Mathf.Repeat(yaw + 180f, 360f) - 180f;
            }

            Apply();
        }

        /// <summary>
        /// Пока игрок мёртв, камера следит за неподвижной точкой, где он погиб; после возрождения — снова за игроком.
        /// Приёмник урона ищется лениво: <see cref="PlayerController"/> добавляет его в своём Start.
        /// </summary>
        private void UpdateDeathFollow()
        {
            if (followTarget == null)
                return;
            if (playerDamage == null)
                playerDamage = followTarget.GetComponentInParent<PlayerDamageReceiver>();
            var dead = playerDamage != null && playerDamage.Dead;
            if (dead == followingDeath)
                return;
            followingDeath = dead;
            if (dead)
            {
                if (deathAnchor == null)
                    deathAnchor = new GameObject("CameraDeathAnchor").transform;
                deathAnchor.SetPositionAndRotation(followTarget.position, followTarget.rotation);
                virtualCamera.Follow = deathAnchor;
            }
            else
            {
                virtualCamera.Follow = followTarget;
            }
        }

        private bool LockedYaw(out float value)
        {
            value = 0f;
            if (lockOn == null && followTarget != null)
                lockOn = followTarget.GetComponentInParent<PlayerLockOn>();
            if (lockOn == null || !lockOn.Active || lockOn.Config.LockOnCameraSpeed <= 0f || followTarget == null)
                return false;
            var to = lockOn.TargetPoint - followTarget.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.25f)
                return false;
            value = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>Текущий целевой угол схемы Б (для автотестов и HUD).</summary>
        public float TargetYaw => targetYaw;

        private void StartTurn(float to)
        {
            tweenFrom = yaw;
            targetYaw = to;
            tweenTime = 0f;
        }

        /// <summary>Ближайший из четырёх изометрических углов: StartYaw + k·шаг.</summary>
        private float NearestFixedAngle(float current)
        {
            var step = Mathf.Max(1f, config.FixedAngleStep);
            var k = Mathf.Round(Mathf.DeltaAngle(config.StartYaw, current) / step);
            return config.StartYaw + k * step;
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
