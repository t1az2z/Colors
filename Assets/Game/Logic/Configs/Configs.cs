using System;
using System.Collections.Generic;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>Ключи конфигов: StreamingAssets/Configs/{key}.json.</summary>
    public static class ConfigKeys
    {
        public const string Game = "Game";
        public const string Player = "Player";
        public const string Camera = "Camera";
        public const string Colors = "Colors";
        public const string Items = "Items";
        public const string Combat = "Combat";
        public const string Dummy = "Dummy";
        public const string Inventory = "Inventory";
        public const string Enemies = "Enemies";
        public const string Upgrades = "Upgrades";
    }

    /// <summary>Configs/Game.json — что загружать при старте.</summary>
    [Preserve]
    public sealed class GameConfig : IConfigPostLoad
    {
        public string Title { get; set; }

        /// <summary>Ключ уровня, например "Levels/Test".</summary>
        public string Level { get; set; }

        /// <summary>
        /// Отладка прокачки: уровни апгрейдов оружия на старте, например { "sword": 3 } (0..3).
        /// Позволяет проверять способности апгрейдов без костра.
        /// </summary>
        public Dictionary<string, int> DebugWeaponUpgrades { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Смерть игрока возрождает всех врагов уровня, как отдых у костра (Dark Souls).
        /// false — враги остаются как были, игрок просто появляется у последнего костра.
        /// </summary>
        public bool RespawnEnemiesOnDeath { get; set; } = true;

        public void OnConfigLoaded()
        {
            if (string.IsNullOrEmpty(Level))
                throw new InvalidOperationException("level is required");
        }
    }

    /// <summary>Configs/Player.json — движение, прыжок, отскоки и деформация куба игрока.</summary>
    [Preserve]
    public sealed class PlayerConfig : IConfigPostLoad
    {
        // Движение
        public float WalkSpeed { get; set; } = 3.2f;
        public float RunMultiplier { get; set; } = 1.6f;

        /// <summary>Кнопка бега переключает бег (сбрасывается при остановке); false — бег, пока кнопка зажата.</summary>
        public bool RunToggle { get; set; } = true;
        public float Acceleration { get; set; } = 30f;
        public float Deceleration { get; set; } = 40f;
        public float AirControl { get; set; } = 0.6f;
        public float TurnSpeed { get; set; } = 900f;
        public float BlockMoveMultiplier { get; set; } = 0.45f;
        public float AttackMoveMultiplier { get; set; } = 0.25f;

        // Коллизия: куб 1×1, шаг на ступень без прыжка
        public float ColliderWidth { get; set; } = 0.9f;
        public float ColliderHeight { get; set; } = 1f;
        public float StepHeight { get; set; } = 0.3f;

        // Прыжок
        public float JumpHeight { get; set; } = 0.7f;
        public float JumpWindup { get; set; } = 0.05f;
        public float Gravity { get; set; } = 30f;
        public float FallGravityMultiplier { get; set; } = 1.4f;
        public float MaxFallSpeed { get; set; } = 25f;
        public float CoyoteTime { get; set; } = 0.1f;
        public float JumpBuffer { get; set; } = 0.12f;

        /// <summary>Отскоки: серия-комбо (короткие прыжки и перекат через верх), у каждого шага свои параметры.</summary>
        public DodgeConfig Dodge { get; set; } = new DodgeConfig();

        public StaminaConfig Stamina { get; set; } = new StaminaConfig();

        // Взаимодействие
        public float InteractRadius { get; set; } = 1.4f;

        // Здоровье и урон
        public float MaxHealth { get; set; } = 100f;

        /// <summary>Неуязвимость после получения урона, секунды (игрок мигает).</summary>
        public float HitInvulnerability { get; set; } = 2f;

        /// <summary>Отбрасывание игрока при получении урона, метры.</summary>
        public float HitKnockback { get; set; } = 0.8f;

        /// <summary>Частота мигания во время неуязвимости, раз в секунду.</summary>
        public float BlinkRate { get; set; } = 12f;

        /// <summary>После смерти: через сколько секунд игрок появляется снова на точке старта.</summary>
        public float RespawnDelay { get; set; } = 2.5f;

        public DeformationConfig Deformation { get; set; } = new DeformationConfig();

        public void OnConfigLoaded()
        {
            if (WalkSpeed <= 0 || JumpHeight < 0 || Gravity <= 0)
                throw new InvalidOperationException("walkSpeed and gravity must be positive, jumpHeight not negative");
            if (StepHeight >= ColliderHeight)
                throw new InvalidOperationException("stepHeight must be less than colliderHeight");
            Deformation ??= new DeformationConfig();
            Stamina ??= new StaminaConfig();
            Dodge ??= new DodgeConfig();
            Dodge.Validate();
        }

        /// <summary>Начальная скорость прыжка для высоты JumpHeight: v = sqrt(2gh).</summary>
        public float JumpVelocity => (float)Math.Sqrt(2.0 * Gravity * JumpHeight);
    }

    /// <summary>Выносливость: тратится сразу, восстанавливается медленно.</summary>
    [Preserve]
    public sealed class StaminaConfig
    {
        public float Max { get; set; } = 100f;
        public float RegenPerSecond { get; set; } = 22f;
        public float RegenDelay { get; set; } = 0.9f;
        public float BlockStartCost { get; set; } = 6f;
        public float BlockRegenMultiplier { get; set; } = 0.3f;
        public float JumpCost { get; set; } = 0f;
    }

    /// <summary>Параметры пружин деформации (сжатие, изгиб) и анимаций куба.</summary>
    [Preserve]
    public sealed class DeformationConfig
    {
        public float Stiffness { get; set; } = 220f;
        public float Damping { get; set; } = 9f;

        public float WalkBend { get; set; } = 0.08f;
        public float WalkSwayPerMeter { get; set; } = 1.6f;
        public float WalkBob { get; set; } = 0.04f;
        public float MoveLean { get; set; } = 0.06f;

        public float JumpWindupSquash { get; set; } = -0.3f;
        public float JumpTakeoffStretch { get; set; } = 0.25f;
        public float LandSquashPerSpeed { get; set; } = -0.035f;
        public float MaxLandSquash { get; set; } = -0.4f;

        public float HitSquash { get; set; } = -0.15f;

        /// <summary>Скручивание куба при взмахе, градусы у верха: замах — против взмаха, удар — по взмаху.</summary>
        public float AttackTwist { get; set; } = 24f;

        /// <summary>Поклон куба вперёд-вниз при рубящем ударе, градусы у верха.</summary>
        public float ChopPitch { get; set; } = 30f;

        /// <summary>Отклонение назад на замахе рубящего удара, градусы.</summary>
        public float ChopPitchBack { get; set; } = 14f;

        /// <summary>Отклонение назад на замахе горизонтальных взмахов, градусы.</summary>
        public float SwingPitchBack { get; set; } = 8f;

        /// <summary>Поклон вперёд в момент горизонтального взмаха, градусы.</summary>
        public float SwingPitchForward { get; set; } = 10f;
    }

    /// <summary>Схема управления камерой и направлением взгляда.</summary>
    public enum CameraScheme
    {
        /// <summary>Камера свободно вращается мышью / правым стиком, персонаж смотрит туда, куда идёт.</summary>
        FreeOrbit,

        /// <summary>
        /// Четыре фиксированных угла камеры (LB/RB, Q/E), правый стик / мышь задают направление взгляда и атаки
        /// независимо от движения.
        /// </summary>
        FixedAngles
    }

    /// <summary>Configs/Camera.json — изометрическая орбитальная камера.</summary>
    [Preserve]
    public sealed class CameraConfig
    {
        public CameraScheme Scheme { get; set; } = CameraScheme.FreeOrbit;

        /// <summary>FixedAngles: шаг поворота (90° — четыре угла) и длительность плавного поворота, секунды.</summary>
        public float FixedAngleStep { get; set; } = 90f;
        public float FixedRotateTime { get; set; } = 0.25f;

        /// <summary>FixedAngles: расстояние маркера прицела от игрока при прицеливании стиком.</summary>
        public float StickAimMarkerDistance { get; set; } = 2.5f;

        /// <summary>FixedAngles: мёртвая зона правого стика для направления взгляда.</summary>
        public float AimDeadzone { get; set; } = 0.3f;

        public float Pitch { get; set; } = 35f;
        public float StartYaw { get; set; } = 45f;
        public float FieldOfView { get; set; } = 25f;
        public float Distance { get; set; } = 26f;
        public float MinDistance { get; set; } = 14f;
        public float MaxDistance { get; set; } = 40f;
        public float ZoomStep { get; set; } = 2f;
        /// <summary>Градусов поворота на пиксель движения мыши (камера вращается мышью без зажатых кнопок).</summary>
        public float MouseRotateSpeed { get; set; } = 0.15f;

        /// <summary>Прятать и запирать курсор во время игры (в инвентаре он свободен).</summary>
        public bool LockCursor { get; set; } = true;
        public float StickRotateSpeed { get; set; } = 140f;
        public float SnapStep { get; set; } = 45f;
        public float FollowDamping { get; set; } = 0.15f;
    }

    /// <summary>Configs/Colors.json — палитра: id цвета → HEX.</summary>
    [Preserve]
    public sealed class ColorConfig : IIdentified
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Hex { get; set; }
    }

    public enum ItemKind
    {
        OneHanded,
        TwoHanded,
        Shield,
        Consumable
    }

    /// <summary>В какую руку можно надеть предмет.</summary>
    public enum HandRestriction
    {
        Any,
        Left,
        Right
    }

    public enum AttackMotion
    {
        /// <summary>Горизонтальный взмах справа налево.</summary>
        SlashRight,

        /// <summary>Горизонтальный взмах слева направо.</summary>
        SlashLeft,
        Chop,
        Thrust,
        Punch
    }

    /// <summary>Один удар серии (комбо).</summary>
    [Preserve]
    public sealed class AttackStepConfig
    {
        public AttackMotion Motion { get; set; } = AttackMotion.SlashRight;

        /// <summary>Замах, секунды: рука уходит из стойки в начало дуги, куб отклоняется назад.</summary>
        public float Windup { get; set; } = -1f;

        /// <summary>Удар, секунды: клинок идёт по дуге, в это время засчитываются попадания.</summary>
        public float Strike { get; set; } = -1f;

        /// <summary>Возврат в стойку после фиксации (Hold), секунды.</summary>
        public float Recovery { get; set; } = -1f;

        /// <summary>
        /// Окно урона внутри фазы удара, доли Strike (0..1). Например 0.6–1 — урон только в конце спуска клинка.
        /// </summary>
        public float HitFrom { get; set; }
        public float HitTo { get; set; } = 1f;

        /// <summary>
        /// Устаревшее описание тайминга (вся длительность и окно попадания долями). Используется, только если
        /// фазы Windup/Strike/Recovery не заданы; после загрузки всегда пересчитано из фаз.
        /// </summary>
        public float Duration { get; set; } = 0.38f;
        public float ActiveFrom { get; set; } = 0.3f;
        public float ActiveTo { get; set; } = 0.65f;
        public float DamageMultiplier { get; set; } = 1f;
        /// <summary>Рывок куба при ударе, блоки: больше нуля — вперёд, меньше нуля — шаг назад.</summary>
        public float Lunge { get; set; } = 0.25f;
        public float Range { get; set; } = 1.3f;
        public float Arc { get; set; } = 150f;
        public float Knockback { get; set; } = 1f;

        /// <summary>
        /// Наклон плоскости горизонтального взмаха, градусы: 0 — строго горизонтально,
        /// плюс — взмах начинается выше и заканчивается ниже (диагональ).
        /// </summary>
        public float Tilt { get; set; }
        public float StaminaCost { get; set; } = 12f;

        /// <summary>
        /// Фиксация финальной позы удара, секунды: акцент на позе и окно комбо —
        /// нажатие в это время сразу запускает следующий удар серии.
        /// </summary>
        public float Hold { get; set; } = 0.22f;

        /// <summary>
        /// Окно, в котором перекат запрещён, секунды от начала удара (замах + удар + фиксация + возврат).
        /// <see cref="RollLockTo"/> &lt; 0 — до конца удара. Нажатие переката в окне запоминается на короткое время.
        /// </summary>
        public float RollLockFrom { get; set; }
        public float RollLockTo { get; set; } = -1f;

        /// <summary>
        /// Окно эффекта удара (след клинка), доли всего удара (замах + удар + фиксация + возврат, 0..1).
        /// Меньше нуля — автоматически: чуть до удара и до конца фиксации позы.
        /// </summary>
        public float FxFrom { get; set; } = -1f;
        public float FxTo { get; set; } = -1f;

        /// <summary>Удар доступен только с этим апгрейдом оружия (1..3); 0 — всегда. Закрытые удары серия пропускает.</summary>
        public int Upgrade { get; set; }

        /// <summary>Подскок в начале удара, блоки высоты (0 — без подскока). Вместе с отрицательным выпадом — отскок назад.</summary>
        public float Hop { get; set; }

        /// <summary>
        /// Удар заряжается удержанием кнопки атаки с общими параметрами заряда оружия (<see cref="ItemConfig.Charge"/>).
        /// </summary>
        public bool Chargeable { get; set; }

        /// <summary>Собственные параметры заряда этого удара (поверх <see cref="ItemConfig.Charge"/>; null — общие).</summary>
        public ChargeConfig Charge { get; set; }
    }

    /// <summary>
    /// Лечилка: <see cref="Charges"/> использований, каждое восстанавливает <see cref="HealFraction"/> максимального
    /// здоровья. Заряды восполняются отдыхом у костра (и возрождением у него).
    /// </summary>
    [Preserve]
    public sealed class HealConfig
    {
        /// <summary>Сколько использований между отдыхами у костра.</summary>
        public int Charges { get; set; } = 2;

        /// <summary>Доля максимального здоровья за одно использование (0.25 — четверть).</summary>
        public float HealFraction { get; set; } = 0.25f;

        /// <summary>Пауза после использования, секунды: повторное нажатие раньше не сработает.</summary>
        public float Cooldown { get; set; } = 0.6f;

        public void Validate(string id)
        {
            if (Charges < 1 || HealFraction <= 0f || Cooldown < 0f)
                throw new InvalidOperationException($"item '{id}': heal needs charges >= 1, healFraction > 0, cooldown >= 0");
        }
    }

    public static class AttackStepTiming
    {
        /// <summary>Полная длительность удара с фиксацией позы, секунды.</summary>
        public static float TotalTime(this AttackStepConfig step) => step.Duration + step.Hold;

        /// <summary>
        /// Перекат запрещён в момент <paramref name="time"/> (секунды от начала удара):
        /// внутри окна <see cref="AttackStepConfig.RollLockFrom"/>..<see cref="AttackStepConfig.RollLockTo"/>.
        /// </summary>
        public static bool RollLocked(this AttackStepConfig step, float time)
        {
            var end = step.RollLockTo < 0f ? step.TotalTime() : step.RollLockTo;
            return time >= step.RollLockFrom && time <= end;
        }

        /// <summary>
        /// Приводит тайминг удара к одному виду: фазы в секундах (Windup, Strike, Recovery) и согласованные с ними
        /// Duration/ActiveFrom/ActiveTo. Если фазы не заданы, они считаются из старых полей (перепутанные границы
        /// окна попадания меняются местами).
        /// </summary>
        public static void Normalize(this AttackStepConfig step)
        {
            if (step.Windup < 0f || step.Strike < 0f || step.Recovery < 0f)
            {
                var from = Math.Max(0f, Math.Min(1f, step.ActiveFrom));
                var to = Math.Max(0f, Math.Min(1f, step.ActiveTo));
                if (from > to)
                {
                    var t = from;
                    from = to;
                    to = t;
                }

                var duration = Math.Max(0.01f, step.Duration);
                if (step.Windup < 0f) step.Windup = duration * from;
                if (step.Strike < 0f) step.Strike = duration * (to - from);
                if (step.Recovery < 0f) step.Recovery = duration * (1f - to);
            }

            step.Windup = Math.Max(0f, step.Windup);
            step.Strike = Math.Max(0.01f, step.Strike);
            step.Recovery = Math.Max(0f, step.Recovery);
            step.Hold = Math.Max(0f, step.Hold);
            step.HitFrom = Math.Max(0f, Math.Min(1f, step.HitFrom));
            step.HitTo = Math.Max(step.HitFrom, Math.Min(1f, step.HitTo));
            step.Duration = step.Windup + step.Strike + step.Recovery;
            step.ActiveFrom = step.Windup / step.Duration;
            step.ActiveTo = (step.Windup + step.Strike) / step.Duration;
            if (step.FxFrom >= 0f && step.FxTo >= 0f)
            {
                step.FxFrom = Math.Min(1f, step.FxFrom);
                step.FxTo = Math.Max(step.FxFrom, Math.Min(1f, step.FxTo));
            }
        }

        /// <summary>
        /// Окно эффекта (след клинка) в секундах от начала удара. Без FxFrom/FxTo — как раньше:
        /// с 0.1 длительности до удара и до 0.12 длительности после фиксации позы.
        /// </summary>
        public static void FxWindow(this AttackStepConfig step, out float from, out float to)
        {
            var total = step.TotalTime();
            if (step.FxFrom < 0f || step.FxTo < 0f)
            {
                from = Math.Max(0f, step.Windup - 0.1f * step.Duration);
                to = Math.Min(total, step.Windup + step.Strike + step.Hold + 0.12f * step.Duration);
                return;
            }

            from = step.FxFrom * total;
            to = step.FxTo * total;
        }

        /// <summary>Эффект удара включён в момент <paramref name="time"/> (секунды от начала удара).</summary>
        public static bool FxActive(this AttackStepConfig step, float time)
        {
            step.FxWindow(out var from, out var to);
            return time >= from && time <= to;
        }
    }

    /// <summary>Configs/Items.json — предметы и оружие.</summary>
    [Preserve]
    public sealed class ItemConfig : IIdentified, IConfigPostLoad
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public ItemKind Kind { get; set; }

        /// <summary>Рука для экипировки: щит — только левая, меч — только правая. Any — любая.</summary>
        public HandRestriction Hand { get; set; } = HandRestriction.Any;

        public float Damage { get; set; }

        /// <summary>Путь к префабу в Resources, например "Items/Sword".</summary>
        public string Prefab { get; set; }

        /// <summary>
        /// Дополнительное время после возврата в стойку, в которое нажатие ещё продолжает серию.
        /// 0 — серию продолжает только нажатие во время фиксации финальной позы (Hold удара).
        /// </summary>
        public float ComboWindow { get; set; } = 0f;

        /// <summary>Поворот модели в руке вокруг оси клинка, градусы (меч — рубящей кромкой вниз).</summary>
        public float GripRoll { get; set; }

        /// <summary>
        /// Дальность эффекта атаки (след клинка) от центра куба, метры. 0 — как дальность поражения удара (range),
        /// чтобы след не был длиннее зоны, где засчитывается урон.
        /// </summary>
        public float TrailRange { get; set; }

        public List<AttackStepConfig> Combo { get; set; } = new List<AttackStepConfig>();

        /// <summary>Добивающий удар после паузы в серии (null — нет).</summary>
        public FinisherConfig Finisher { get; set; }

        /// <summary>Удар в прыжке: падение вниз и удар по площади при приземлении (null — в воздухе обычная серия).</summary>
        public PlungeConfig Plunge { get; set; }

        /// <summary>Общие параметры заряда ударов ближнего боя, отмеченных <see cref="AttackStepConfig.Chargeable"/>.</summary>
        public ChargeConfig Charge { get; set; }

        /// <summary>Заряд удара: собственный у удара, иначе общий оружия, если удар отмечен заряжаемым; null — не заряжается.</summary>
        public ChargeConfig ChargeFor(AttackStepConfig step)
        {
            if (step == null)
                return null;
            return step.Charge ?? (step.Chargeable ? Charge : null);
        }

        /// <summary>
        /// Стрелковое оружие (дробовик): если задано, кнопка атаки стреляет вместо серии ударов,
        /// урон одной пули — <see cref="Damage"/>.
        /// </summary>
        public RangedConfig Ranged { get; set; }

        /// <summary>Щит: угол перед игроком, в котором блок гасит удар.</summary>
        public float BlockAngle { get; set; } = 120f;

        /// <summary>Щит: параметры апгрейдов (дешевле блок, шире окно парирования, удар щитом).</summary>
        public ShieldConfig Shield { get; set; }

        /// <summary>Лечилка (расходуемый предмет в быстром слоте 1–4): заряды и сила лечения. null — не лечит.</summary>
        public HealConfig Heal { get; set; }

        public bool IsWeapon => Kind == ItemKind.OneHanded || Kind == ItemKind.TwoHanded || Kind == ItemKind.Shield;
        public bool IsTwoHanded => Kind == ItemKind.TwoHanded;
        public bool IsRanged => Ranged != null;

        public void OnConfigLoaded()
        {
            Combo ??= new List<AttackStepConfig>();
            // Окно попадания должно идти вперёд по времени: перепутанные границы меняем местами,
            // иначе удар молча перестаёт наносить урон.
            foreach (var step in Combo)
            {
                step?.Normalize();
                step?.Charge?.Validate(Id);
            }

            Charge?.Validate(Id);
            Finisher?.Validate(Id);
            Plunge?.Validate(Id);
            Ranged?.Validate(Id);
            if (Kind == ItemKind.Shield)
                Shield ??= new ShieldConfig();
            Shield?.Validate(Id);
            Heal?.Validate(Id);
            if (Kind != ItemKind.Shield && Kind != ItemKind.Consumable && Ranged == null && Combo.Count == 0)
                throw new InvalidOperationException($"weapon '{Id}' needs at least one combo step");
        }
    }

    /// <summary>
    /// Выстрел стрелкового оружия: залп пуль веером. Цель — из фиксации (СКМ/R3), без неё — направление
    /// взгляда (правый стик/мышь в схеме Б, иначе куда смотрит персонаж).
    /// </summary>
    [Preserve]
    public sealed class RangedConfig
    {
        /// <summary>Пуль за выстрел.</summary>
        public int Pellets { get; set; } = 8;

        /// <summary>Разброс: ширина веера пуль, градусы (пули распределены по вееру равномерно со случайным сдвигом).</summary>
        public float Spread { get; set; } = 30f;

        /// <summary>Дальность пули, метры.</summary>
        public float Range { get; set; } = 7f;

        /// <summary>Пауза между выстрелами, секунды.</summary>
        public float Cooldown { get; set; } = 0.3f;

        /// <summary>Выносливость за выстрел.</summary>
        public float StaminaCost { get; set; } = 20f;

        /// <summary>Отбрасывание врага, если в него попали все пули (меньше пуль — пропорционально меньше).</summary>
        public float Knockback { get; set; } = 1.2f;

        /// <summary>Отдача: на сколько метров выстрел сдвигает персонажа назад.</summary>
        public float Recoil { get; set; } = 0.35f;

        /// <summary>За сколько секунд проходит сдвиг отдачи.</summary>
        public float RecoilTime { get; set; } = 0.12f;

        /// <summary>Нажатие раньше конца паузы запоминается на столько секунд и стреляет, как только можно.</summary>
        public float InputBuffer { get; set; } = 0.12f;

        // Пули — настоящие снаряды: летят со скоростью, попадают при касании, останавливаются о стены.

        /// <summary>Скорость пули, м/с.</summary>
        public float PelletSpeed { get; set; } = 45f;

        /// <summary>Разброс скорости пуль, доля: 0.15 — каждая пуля от 85% до 115% скорости.</summary>
        public float SpeedJitter { get; set; } = 0.15f;

        /// <summary>Случайный сдвиг угла каждой пули поверх веера, ± градусы: залп не выглядит ровным веером.</summary>
        public float AngleJitter { get; set; } = 3f;

        /// <summary>Случайный наклон пули вверх-вниз, ± градусы.</summary>
        public float VerticalJitter { get; set; } = 1f;

        /// <summary>Диаметр шарика пули (визуал), метры.</summary>
        public float PelletSize { get; set; } = 0.1f;

        /// <summary>Радиус попадания пули (толщина проверки касания), метры.</summary>
        public float HitRadius { get; set; } = 0.08f;

        /// <summary>Яркость свечения пули (HDR-множитель цвета оружия).</summary>
        public float PelletGlow { get; set; } = 2.2f;

        /// <summary>Поза в руках: в покое — перед кубом стволом чуть вниз; после выстрела — на прицеле.</summary>
        public GunHoldConfig IdleHold { get; set; } = new GunHoldConfig();
        public GunHoldConfig AimHold { get; set; } = new GunHoldConfig { Height = 0.5f, Forward = 0.62f, Pitch = 0f };

        /// <summary>Апгрейды на костре.</summary>
        public RangedUpgradesConfig Upgrades { get; set; } = new RangedUpgradesConfig();

        /// <summary>
        /// Заряд выстрела удержанием кнопки (общий механизм заряда, апгрейд <see cref="ChargeConfig.Upgrade"/>):
        /// время, выносливость за полный заряд, множители урона и отбрасывания. null — выстрел не заряжается.
        /// С зарядом выстрел уходит при отпускании кнопки (короткое нажатие — обычный выстрел).
        /// </summary>
        public ChargeConfig Charge { get; set; }

        /// <summary>Каким становится залп при полном заряде (пуль больше, веер шире, дальше, ярче).</summary>
        public ChargedShotConfig ChargedShot { get; set; } = new ChargedShotConfig();

        public void Validate(string id)
        {
            IdleHold ??= new GunHoldConfig();
            AimHold ??= new GunHoldConfig { Height = 0.5f, Forward = 0.62f, Pitch = 0f };
            Upgrades ??= new RangedUpgradesConfig();
            ChargedShot ??= new ChargedShotConfig();
            Charge?.Validate(id);
            ChargedShot.Validate(id);
            if (PelletSpeed <= 0f || SpeedJitter < 0f || SpeedJitter >= 1f || AngleJitter < 0f || VerticalJitter < 0f)
                throw new InvalidOperationException($"ranged weapon '{id}': pelletSpeed > 0, speedJitter in 0..1, jitters not negative");
            if (PelletSize <= 0f || HitRadius < 0f)
                throw new InvalidOperationException($"ranged weapon '{id}': pelletSize > 0, hitRadius >= 0");
            if (Upgrades.StaminaCostMultiplier < 0f || Upgrades.ParryPellets < 1 || Upgrades.ParryDamageMultiplier < 1f)
                throw new InvalidOperationException($"ranged weapon '{id}': upgrades staminaCostMultiplier >= 0, parryPellets >= 1, parryDamageMultiplier >= 1");
            if (Pellets < 1)
                throw new InvalidOperationException($"ranged weapon '{id}': pellets must be at least 1");
            if (Range <= 0f || Cooldown < 0f || Spread < 0f || Spread > 180f)
                throw new InvalidOperationException($"ranged weapon '{id}': range > 0, cooldown >= 0, spread in 0..180");
            if (Recoil < 0f || RecoilTime <= 0f)
                throw new InvalidOperationException($"ranged weapon '{id}': recoil >= 0, recoilTime > 0");
            if (StaminaCost < 0f || Knockback < 0f || InputBuffer < 0f)
                throw new InvalidOperationException($"ranged weapon '{id}': staminaCost, knockback and inputBuffer must not be negative");
        }
    }

    /// <summary>Поза стрелкового оружия в руке (координаты меша куба: +X вправо, +Y вверх, +Z вперёд).</summary>
    [Preserve]
    public sealed class GunHoldConfig
    {
        /// <summary>Сдвиг вбок от центра куба (для правой руки вправо), метры.</summary>
        public float Side { get; set; } = 0.22f;

        /// <summary>Высота хвата над низом куба, метры.</summary>
        public float Height { get; set; } = 0.42f;

        /// <summary>Вынос вперёд от центра куба (передняя грань — 0.5), метры.</summary>
        public float Forward { get; set; } = 0.58f;

        /// <summary>Наклон ствола вниз, градусы.</summary>
        public float Pitch { get; set; } = 16f;

        /// <summary>
        /// Положение модели оружия относительно руки [x, y, z] (например, в покое — вдоль тела).
        /// null — модель в руке без сдвига.
        /// </summary>
        public float[] ModelPosition { get; set; }

        /// <summary>Поворот модели оружия относительно руки [x, y, z], градусы (Euler). null — без поворота (только хват).</summary>
        public float[] ModelRotation { get; set; }
    }

    /// <summary>
    /// Заряженный выстрел: изменения залпа относительно обычного. Урон и отбрасывание задаёт заряд
    /// (<see cref="ChargeConfig.DamageMultiplier"/>, <see cref="ChargeConfig.KnockbackMultiplier"/>).
    /// </summary>
    [Preserve]
    public sealed class ChargedShotConfig
    {
        /// <summary>Пуль в заряженном залпе.</summary>
        public int Pellets { get; set; } = 12;

        /// <summary>Множитель ширины веера (1.15 — на 15% шире).</summary>
        public float SpreadMultiplier { get; set; } = 1.15f;

        /// <summary>Множители дальности и скорости пуль.</summary>
        public float RangeMultiplier { get; set; } = 1.5f;
        public float SpeedMultiplier { get; set; } = 1.15f;

        /// <summary>Множитель размера шарика пули и яркости её свечения.</summary>
        public float PelletSizeMultiplier { get; set; } = 1.4f;
        public float GlowMultiplier { get; set; } = 2f;

        /// <summary>Множитель отдачи.</summary>
        public float RecoilMultiplier { get; set; } = 1.6f;

        public void Validate(string id)
        {
            if (Pellets < 1 || SpreadMultiplier <= 0f || RangeMultiplier <= 0f || SpeedMultiplier <= 0f ||
                PelletSizeMultiplier <= 0f || GlowMultiplier < 0f || RecoilMultiplier < 0f)
                throw new InvalidOperationException($"ranged weapon '{id}': chargedShot pellets >= 1, multipliers positive");
        }
    }

    /// <summary>
    /// Апгрейды стрелкового оружия (id предмета в WeaponUpgrades). 1 — дешевле выстрел; 2 — парирование выстрелом:
    /// залп в окно парирования вражеской атаки парирует её, если в врага попало не меньше <see cref="ParryPellets"/> пуль,
    /// и урон этих пуль умножается на <see cref="ParryDamageMultiplier"/>. 3 — заряженный выстрел (<see cref="RangedConfig.Charge"/>, <see cref="RangedConfig.ChargedShot"/>).
    /// </summary>
    [Preserve]
    public sealed class RangedUpgradesConfig
    {
        /// <summary>Апгрейд 1: множитель выносливости за выстрел.</summary>
        public float StaminaCostMultiplier { get; set; } = 0.6f;

        /// <summary>Апгрейд 2: сколько пуль залпа должно попасть во врага, чтобы парировать.</summary>
        public int ParryPellets { get; set; } = 3;

        /// <summary>Апгрейд 2: множитель урона пуль парирующего залпа.</summary>
        public float ParryDamageMultiplier { get; set; } = 3f;
    }

    /// <summary>
    /// Щит и его апгрейды (id предмета в WeaponUpgrades). 1 — дешевле блок; 2 — шире окно парирования;
    /// 3 — удар щитом: отпустить блок и сразу нажать снова.
    /// </summary>
    [Preserve]
    public sealed class ShieldConfig
    {
        /// <summary>Апгрейд 1: множитель выносливости на блок (поднять щит и принять удар).</summary>
        public float BlockCostMultiplier { get; set; } = 0.6f;

        /// <summary>
        /// Апгрейд 2: окно парирования шире на эту долю своей длины, поровну в обе стороны
        /// (0.5 — окно в 1.5 раза длиннее: по 25% длины раньше и позже).
        /// </summary>
        public float ParryWindowBonus { get; set; } = 0.5f;

        /// <summary>Апгрейд 3: удар щитом, если блок отпущен и нажат снова не позже чем через столько секунд.</summary>
        public float BashMaxGap { get; set; } = 0.25f;

        public float BashStaminaCost { get; set; } = 12f;
        public float BashDamage { get; set; } = 8f;

        /// <summary>Отбрасывание врага ударом щитом (как knockback удара).</summary>
        public float BashKnockback { get; set; } = 2.5f;

        /// <summary>Дальность и ширина сектора удара щитом.</summary>
        public float BashRange { get; set; } = 1.7f;
        public float BashArc { get; set; } = 110f;

        /// <summary>Фазы удара щитом, секунды: замах, толчок (в конце — попадание), возврат.</summary>
        public float BashWindup { get; set; } = 0.06f;
        public float BashStrike { get; set; } = 0.08f;
        public float BashRecovery { get; set; } = 0.22f;

        /// <summary>Рывок вперёд при ударе щитом, метры.</summary>
        public float BashLunge { get; set; } = 0.4f;

        /// <summary>Удар щитом в окно парирования вражеской атаки: множитель урона и отбрасывания (и атака парирована).</summary>
        public float BashParryMultiplier { get; set; } = 2f;

        public float BashDuration => BashWindup + BashStrike + BashRecovery;

        public void Validate(string id)
        {
            if (BlockCostMultiplier < 0f || ParryWindowBonus < 0f || BashMaxGap < 0f || BashStaminaCost < 0f)
                throw new InvalidOperationException($"shield '{id}': blockCostMultiplier, parryWindowBonus, bashMaxGap, bashStaminaCost must not be negative");
            if (BashWindup < 0f || BashStrike <= 0f || BashRecovery < 0f || BashRange <= 0f || BashParryMultiplier < 1f)
                throw new InvalidOperationException($"shield '{id}': bash phases (strike > 0), bashRange > 0, bashParryMultiplier >= 1");
        }
    }

    /// <summary>Configs/Combat.json — общие правила боя.</summary>
    [Preserve]
    public sealed class CombatConfig
    {
        /// <summary>Множитель урона по врагу того же цвета, что и оружие.</summary>
        public float SameColorMultiplier { get; set; } = 1.5f;

        /// <summary>Предмет для пустой руки (например "fists"). Пусто — без оружия рука не атакует.</summary>
        public string UnarmedItem { get; set; } = "";

        /// <summary>Фриз-кадры при попадании, секунды (0 — выключено). Умножается на отбрасывание удара.</summary>
        public float HitStop { get; set; } = 0.07f;

        /// <summary>Сила толчка камеры при попадании (0 — выключено).</summary>
        public float HitShake { get; set; } = 0.3f;

        /// <summary>Свечение цвета оружия в руках (доля эмиссии), чтобы оружие читалось издалека.</summary>
        public float HeldWeaponGlow { get; set; } = 0.25f;

        /// <summary>
        /// Сколько врагов одновременно в атаке (замах/удар). Удары при этом не совпадают никогда: следующий в очереди
        /// замахивается заранее и бьёт сразу после текущего.
        /// </summary>
        public int MaxAttackers { get; set; } = 3;

        /// <summary>Пауза между концом удара одного врага и ударом следующего, секунды.</summary>
        public float AttackerGap { get; set; } = 0.25f;

        /// <summary>Фиксация на цели: в каком радиусе ищется цель, метры.</summary>
        public float LockOnRange { get; set; } = 12f;

        /// <summary>Фиксация снимается, если цель дальше этого, метры.</summary>
        public float LockOnBreakRange { get; set; } = 16f;

        /// <summary>Цель в стороне от взгляда камеры считается дальше на столько метров за каждые 90°.</summary>
        public float LockOnAnglePenalty { get; set; } = 4f;

        /// <summary>Скорость поворота персонажа к цели, градусов в секунду.</summary>
        public float LockOnTurnSpeed { get; set; } = 900f;

        /// <summary>Свободная камера (схема А) доворачивается за спину персонажу, чтобы цель была в кадре; 0 — не доворачивать.</summary>
        public float LockOnCameraSpeed { get; set; } = 4f;

        /// <summary>Порог отклонения правого стика для переключения цели (0..1).</summary>
        public float LockOnSwitchStick { get; set; } = 0.7f;

        /// <summary>Рывок мышью для переключения цели, пикселей (схема А).</summary>
        public float LockOnSwitchMouse { get; set; } = 120f;

        /// <summary>Успешное парирование: фриз-кадры, секунды.</summary>
        public float ParryHitStop { get; set; } = 0.12f;

        /// <summary>Успешное парирование: толчок камеры.</summary>
        public float ParryShake { get; set; } = 0.35f;
    }

    /// <summary>Configs/Dummy.json — тренировочный манекен.</summary>
    [Preserve]
    public sealed class DummyConfig
    {
        public string Color { get; set; } = "red";
        public float TiltPerHit { get; set; } = 9f;
        public float MaxTilt { get; set; } = 20f;
        public float Stiffness { get; set; } = 120f;
        public float Damping { get; set; } = 7f;
        public float FlashDuration { get; set; } = 0.18f;
        public string FlashColor { get; set; } = "#FF2A1F";
        public float FlashIntensity { get; set; } = 2.5f;
    }

    /// <summary>Configs/Inventory.json.</summary>
    [Preserve]
    public sealed class InventoryConfig
    {
        public int Columns { get; set; } = 5;
        public int Rows { get; set; } = 5;
        public int QuickSlots { get; set; } = 4;

        /// <summary>Подобранное оружие сразу надевается, если его рука свободна; иначе ложится в сетку.</summary>
        public bool AutoEquipOnPickup { get; set; } = true;
    }
}
