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
    }

    /// <summary>Configs/Game.json — что загружать при старте.</summary>
    [Preserve]
    public sealed class GameConfig : IConfigPostLoad
    {
        public string Title { get; set; }

        /// <summary>Ключ уровня, например "Levels/Test".</summary>
        public string Level { get; set; }

        public void OnConfigLoaded()
        {
            if (string.IsNullOrEmpty(Level))
                throw new InvalidOperationException("level is required");
        }
    }

    /// <summary>Configs/Player.json — движение, прыжок, перекат и деформация куба игрока.</summary>
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

        // Перекат: два переворота по 180°
        public float RollDistance { get; set; } = 1.5f;
        public float RollDuration { get; set; } = 0.45f;
        public float RollCooldown { get; set; } = 0.15f;
        public float RollHopHeight { get; set; } = 0.32f;

        /// <summary>Окно неуязвимости переката, доли длительности (0..1).</summary>
        public float RollInvulnerableFrom { get; set; } = 0.1f;
        public float RollInvulnerableTo { get; set; } = 0.8f;

        /// <summary>Доля длительности переката, которую куб стоит "на голове" между двумя переворотами.</summary>
        public float RollMidPause { get; set; } = 0.16f;

        /// <summary>Скорость скольжения в этой паузе относительно полёта (0 — стоит на месте).</summary>
        public float RollMidSpeed { get; set; } = 0.25f;

        public StaminaConfig Stamina { get; set; } = new StaminaConfig();

        // Взаимодействие
        public float InteractRadius { get; set; } = 1.4f;

        public DeformationConfig Deformation { get; set; } = new DeformationConfig();

        public void OnConfigLoaded()
        {
            if (WalkSpeed <= 0 || JumpHeight < 0 || Gravity <= 0 || RollDuration <= 0)
                throw new InvalidOperationException("walkSpeed, gravity and rollDuration must be positive, jumpHeight not negative");
            if (StepHeight >= ColliderHeight)
                throw new InvalidOperationException("stepHeight must be less than colliderHeight");
            Deformation ??= new DeformationConfig();
            Stamina ??= new StaminaConfig();
            if (RollMidPause < 0 || RollMidPause > 0.6f)
                throw new InvalidOperationException("rollMidPause must be in 0..0.6");
            if (RollInvulnerableFrom < 0 || RollInvulnerableTo > 1 || RollInvulnerableFrom > RollInvulnerableTo)
                throw new InvalidOperationException("roll invulnerability window must be inside 0..1");
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
        public float RollCost { get; set; } = 22f;
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

        public float RollBend { get; set; } = 0.18f;
        public float RollLandSquash { get; set; } = -0.25f;

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
    }

    public static class AttackStepTiming
    {
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

        public List<AttackStepConfig> Combo { get; set; } = new List<AttackStepConfig>();

        /// <summary>Щит: угол перед игроком, в котором блок гасит удар.</summary>
        public float BlockAngle { get; set; } = 120f;

        public bool IsWeapon => Kind == ItemKind.OneHanded || Kind == ItemKind.TwoHanded || Kind == ItemKind.Shield;
        public bool IsTwoHanded => Kind == ItemKind.TwoHanded;

        public void OnConfigLoaded()
        {
            Combo ??= new List<AttackStepConfig>();
            // Окно попадания должно идти вперёд по времени: перепутанные границы меняем местами,
            // иначе удар молча перестаёт наносить урон.
            foreach (var step in Combo)
                step?.Normalize();
            if (Kind != ItemKind.Shield && Kind != ItemKind.Consumable && Combo.Count == 0)
                throw new InvalidOperationException($"weapon '{Id}' needs at least one combo step");
        }
    }

    /// <summary>Configs/Combat.json — общие правила боя.</summary>
    [Preserve]
    public sealed class CombatConfig
    {
        /// <summary>Множитель урона по врагу того же цвета, что и оружие.</summary>
        public float SameColorMultiplier { get; set; } = 1.5f;

        /// <summary>Предмет для пустой руки (кулаки).</summary>
        public string UnarmedItem { get; set; } = "fists";

        /// <summary>Фриз-кадры при попадании, секунды (0 — выключено). Умножается на отбрасывание удара.</summary>
        public float HitStop { get; set; } = 0.07f;

        /// <summary>Сила толчка камеры при попадании (0 — выключено).</summary>
        public float HitShake { get; set; } = 0.3f;

        /// <summary>Свечение цвета оружия в руках (доля эмиссии), чтобы оружие читалось издалека.</summary>
        public float HeldWeaponGlow { get; set; } = 0.25f;
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
