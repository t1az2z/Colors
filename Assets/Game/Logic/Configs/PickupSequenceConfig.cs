using System;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/PickupSequence.json — сцена подбора нового оружия или лечилки (ключ <see cref="Key"/>).
    /// Мир застывает, камера подъезжает к персонажу, предмет плавно поднимается, персонаж следит за ним глазами,
    /// затем предмет с панчем влетает в персонажа и сам надевается; камера показывает персонажа совсем близко,
    /// он радуется, вокруг — эффект как у лечения, без плюсиков и в цвет предмета; камера возвращается.
    /// Все времена — секунды от начала сцены (реальное время, мир стоит), сцена длится <see cref="Duration"/>.
    /// </summary>
    [Preserve]
    public sealed class PickupSequenceConfig : IConfigPostLoad
    {
        public const string Key = "PickupSequence";

        /// <summary>Выключено — предметы подбираются как раньше, без сцены.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Длина всей сцены: в конце камера на месте, мир снова идёт, управление возвращается.</summary>
        public float Duration { get; set; } = 5f;

        public PickupItemMotion Item { get; set; } = new PickupItemMotion();

        public PickupPlayerMotion Player { get; set; } = new PickupPlayerMotion();

        public PickupCameraMotion Camera { get; set; } = new PickupCameraMotion();

        public PickupFxConfig Fx { get; set; } = new PickupFxConfig();

        /// <summary>Играть ли сцену для предмета: новое оружие (его ещё нет на колесе) или новая лечилка.</summary>
        public bool Plays(ItemConfig item, bool alreadyOwned)
        {
            if (!Enabled || item == null || alreadyOwned || item.IsKey)
                return false;
            return item.IsWeapon || (item.Kind == ItemKind.Consumable && item.Heal != null);
        }

        // ------------------------------------------------------------------ кривые по времени сцены

        /// <summary>Доля пути по отрезку [from, to] в момент <paramref name="t"/>: 0 до него, 1 после.</summary>
        public static float Window(float t, float from, float to)
        {
            if (to <= from)
                return t >= to ? 1f : 0f;
            var k = (t - from) / (to - from);
            return k < 0f ? 0f : k > 1f ? 1f : k;
        }

        /// <summary>Плавный разгон и торможение (smoothstep).</summary>
        public static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>Подъём предмета 0..1: плавно трогается и плавно зависает.</summary>
        public float Rise(float t) => Smooth(Window(t, Item.RiseStart, Item.RiseEnd));

        /// <summary>Замах предмета перед броском 0..1..0: чуть отходит назад-вверх и возвращается к началу полёта.</summary>
        public float Windup(float t)
        {
            var k = Window(t, Item.ThrowStart, Item.FlyStart);
            return k <= 0f || k >= 1f ? 0f : (float)Math.Sin(k * Math.PI);
        }

        /// <summary>Полёт в персонажа 0..1 с разгоном (кубическое ускорение) — удар приходится на <see cref="PickupItemMotion.HitTime"/>.</summary>
        public float Flight(float t)
        {
            var k = Window(t, Item.FlyStart, Item.HitTime);
            return k * k * k;
        }

        /// <summary>Сколько персонаж смотрит вверх на предмет 0..1: пока предмет поднимается и висит, до удара.</summary>
        public float LookUp(float t)
        {
            if (t >= Item.HitTime)
                return 0f;
            return Smooth(Window(t, Item.RiseStart, Item.RiseEnd)) * (1f - Smooth(Window(t, Item.FlyStart, Item.HitTime)));
        }

        /// <summary>
        /// Радость 0..1 (поднятое оружие, прищуренные глаза): быстро набирается после удара и уходит к концу
        /// <see cref="PickupPlayerMotion.CheerEnd"/>.
        /// </summary>
        public float Cheer(float t)
        {
            var p = Player;
            var up = Smooth(Window(t, p.CheerStart, p.CheerStart + 0.2f));
            var down = Smooth(Window(t, p.CheerEnd - 0.35f, p.CheerEnd));
            return up * (1f - down);
        }

        /// <summary>
        /// Прыжок радости: номер прыжка и его фаза 0..1 (или -1, если не прыгает). Прыжки делят отрезок радости поровну,
        /// последний оставляет время на возврат позы.
        /// </summary>
        public float HopPhase(float t, out int index)
        {
            index = -1;
            var p = Player;
            if (p.Hops <= 0 || p.HopTime <= 0f || t < p.CheerStart)
                return -1f;
            var k = (t - p.CheerStart) / p.HopTime;
            index = (int)Math.Floor(k);
            if (index >= p.Hops)
            {
                index = -1;
                return -1f;
            }

            return k - index;
        }

        /// <summary>Высота прыжка радости (доля <see cref="PickupPlayerMotion.HopHeight"/>) на фазе прыжка: присед, полёт, приземление.</summary>
        public float HopHeight(float phase)
        {
            var crouch = Player.HopCrouch;
            if (phase < crouch || phase >= 1f)
                return 0f;
            var u = (phase - crouch) / (1f - crouch);
            return 4f * u * (1f - u);
        }

        /// <summary>Сила свечения тела и эффекта в цвет предмета 0..1: вспыхивает к удару и плавно гаснет.</summary>
        public float Glow(float t)
        {
            var f = Fx;
            if (t < f.GlowStart || t >= f.GlowEnd)
                return 0f;
            var k = Window(t, f.GlowStart, f.GlowEnd);
            if (k < 0.12f)
                return k / 0.12f;
            var d = 1f - (k - 0.12f) / 0.88f;
            return Smooth(d);
        }

        /// <summary>
        /// Веса камеры в момент <paramref name="t"/>: <paramref name="approach"/> — от обычного вида к плану подъёма,
        /// <paramref name="closeUp"/> — от плана подъёма к крупному плану, <paramref name="hold"/> — вся сцена
        /// (1 — кадр сцены, 0 — обычная камера; уходит в 0 на возврате к концу).
        /// </summary>
        public void CameraWeights(float t, out float approach, out float closeUp, out float hold)
        {
            var c = Camera;
            approach = Smooth(Window(t, 0f, c.ApproachEnd));
            closeUp = Smooth(Window(t, c.CloseUpStart, c.CloseUpEnd));
            hold = 1f - Smooth(Window(t, c.ReturnStart, Duration));
        }

        public void OnConfigLoaded()
        {
            if (Duration <= 0f)
                throw new InvalidOperationException("PickupSequence.duration must be positive");
            var i = Item;
            if (!(0f <= i.RiseStart && i.RiseStart <= i.RiseEnd && i.RiseEnd <= i.ThrowStart && i.ThrowStart <= i.FlyStart
                  && i.FlyStart < i.HitTime && i.HitTime <= Duration))
                throw new InvalidOperationException(
                    "PickupSequence.item: expected 0 <= riseStart <= riseEnd <= throwStart <= flyStart < hitTime <= duration");
            var p = Player;
            if (p.CheerStart < i.HitTime || p.CheerEnd < p.CheerStart || p.CheerEnd > Duration)
                throw new InvalidOperationException("PickupSequence.player: expected hitTime <= cheerStart <= cheerEnd <= duration");
            if (p.Hops < 0 || p.HopTime < 0f || p.HopCrouch < 0f || p.HopCrouch >= 1f || p.TurnTime < 0f)
                throw new InvalidOperationException("PickupSequence.player: hops, hopTime, turnTime >= 0, hopCrouch within 0..1");
            var c = Camera;
            if (c.ApproachEnd < 0f || c.CloseUpStart > c.CloseUpEnd || c.CloseUpEnd > c.ReturnStart || c.ReturnStart > Duration)
                throw new InvalidOperationException(
                    "PickupSequence.camera: expected approachEnd >= 0, closeUpStart <= closeUpEnd <= returnStart <= duration");
            if (c.RiseDistance <= 0f || c.CloseUpDistance <= 0f)
                throw new InvalidOperationException("PickupSequence.camera: distances must be positive");
            var f = Fx;
            if (f.GlowEnd < f.GlowStart || f.GlowEnd > Duration || f.BurstTime < 0f || f.BurstTime > Duration)
                throw new InvalidOperationException("PickupSequence.fx: expected glowStart <= glowEnd <= duration, burstTime within the scene");
            if (f.GlowTint < 0f || f.GlowTint > 1f || f.GlowEmission < 0f || f.TintToWhite < 0f || f.TintToWhite > 1f)
                throw new InvalidOperationException("PickupSequence.fx: glowTint and tintToWhite within 0..1, glowEmission >= 0");
        }
    }

    /// <summary>Движение подбираемого предмета.</summary>
    [Preserve]
    public sealed class PickupItemMotion
    {
        /// <summary>Подъём над местом, где лежал: начало и конец, секунды.</summary>
        public float RiseStart { get; set; } = 0.15f;

        public float RiseEnd { get; set; } = 1.5f;

        /// <summary>Высота подъёма над исходной позицией предмета, метры.</summary>
        public float RiseHeight { get; set; } = 1.5f;

        /// <summary>Насколько предмет при подъёме смещается к персонажу (доля расстояния по горизонтали).</summary>
        public float RiseApproach { get; set; } = 0.35f;

        /// <summary>Покачивание в воздухе, метры, и его частота, рад/с.</summary>
        public float Bob { get; set; } = 0.06f;

        public float BobSpeed { get; set; } = 5f;

        /// <summary>Вращение вокруг вертикали при подъёме, град/с (к броску замедляется до нуля).</summary>
        public float Spin { get; set; } = 220f;

        /// <summary>Замах перед броском: с <see cref="ThrowStart"/> до <see cref="FlyStart"/> предмет отходит назад-вверх.</summary>
        public float ThrowStart { get; set; } = 1.8f;

        public float FlyStart { get; set; } = 2.05f;

        /// <summary>Насколько далеко отходит на замахе, метры.</summary>
        public float WindupDistance { get; set; } = 0.35f;

        /// <summary>Удар о персонажа (конец полёта): предмет исчезает в нём и надевается.</summary>
        public float HitTime { get; set; } = 2.25f;

        /// <summary>Масштаб предмета в момент удара (доля исходного): к персонажу он «входит» в него.</summary>
        public float HitScale { get; set; } = 0.6f;

        /// <summary>Высота точки удара над ногами персонажа, метры.</summary>
        public float HitHeight { get; set; } = 0.55f;
    }

    /// <summary>Персонаж: поворот к предмету, взгляд, панч от удара и радость.</summary>
    [Preserve]
    public sealed class PickupPlayerMotion
    {
        /// <summary>Поворот лицом к предмету в начале, секунды.</summary>
        public float TurnTime { get; set; } = 0.3f;

        /// <summary>Глаза за предметом: сдвиг по грани вверх и в стороны (доля куба).</summary>
        public float EyeUp { get; set; } = 0.14f;

        public float EyeSide { get; set; } = 0.12f;

        /// <summary>Персонаж запрокидывается, глядя вверх: поклон назад, градусы у верха.</summary>
        public float LookBack { get; set; } = 10f;

        /// <summary>Панч при ударе: сжатие (минус — сплющен), толчок пружины и прогиб назад.</summary>
        public float PunchSquash { get; set; } = -0.4f;

        public float PunchImpulse { get; set; } = -3f;

        public float PunchBend { get; set; } = 3.5f;

        /// <summary>Толчок камеры при ударе.</summary>
        public float PunchShake { get; set; } = 0.5f;

        /// <summary>Радость: начало и конец, секунды.</summary>
        public float CheerStart { get; set; } = 2.4f;

        public float CheerEnd { get; set; } = 4.2f;

        /// <summary>Сколько раз подпрыгивает, длительность одного прыжка, высота, метры, и доля приседа перед ним.</summary>
        public int Hops { get; set; } = 2;

        public float HopTime { get; set; } = 0.55f;

        public float HopHeight { get; set; } = 0.3f;

        public float HopCrouch { get; set; } = 0.25f;

        /// <summary>Вытягивание в прыжке и сплющивание на присед/приземлении.</summary>
        public float HopStretch { get; set; } = 0.12f;

        public float HopSquash { get; set; } = -0.22f;

        /// <summary>Толчок пружины сжатия при отрыве от земли (вытягивание на взлёте).</summary>
        public float HopKick { get; set; } = 1.2f;

        /// <summary>Покачивание от радости: скручивание, градусы, и частота, рад/с.</summary>
        public float Wiggle { get; set; } = 12f;

        public float WiggleSpeed { get; set; } = 14f;

        /// <summary>Прищур от радости: 0 — глаза открыты, 1 — закрыты.</summary>
        public float HappyEyes { get; set; } = 0.55f;

        /// <summary>Поворот лицом к камере на радости: доля поворота (0 — смотрит, куда смотрел, 1 — прямо в камеру).</summary>
        public float FaceCamera { get; set; } = 0.6f;
    }

    /// <summary>Камера сцены: план подъёма (средний), крупный план после удара и возврат.</summary>
    [Preserve]
    public sealed class PickupCameraMotion
    {
        /// <summary>За сколько секунд камера подъезжает к плану подъёма.</summary>
        public float ApproachEnd { get; set; } = 1.3f;

        /// <summary>План подъёма: расстояние, наклон, угол от «прямо в лицо» (камера сбоку-спереди), высота точки взгляда.</summary>
        public float RiseDistance { get; set; } = 9f;

        public float RisePitch { get; set; } = 24f;

        public float RiseYaw { get; set; } = 40f;

        public float RiseLookHeight { get; set; } = 1.2f;

        /// <summary>Крупный план: начало и конец подъезда, расстояние, наклон, угол, высота точки взгляда.</summary>
        public float CloseUpStart { get; set; } = 2.2f;

        public float CloseUpEnd { get; set; } = 2.6f;

        public float CloseUpDistance { get; set; } = 4.4f;

        public float CloseUpPitch { get; set; } = 16f;

        public float CloseUpYaw { get; set; } = 25f;

        public float CloseUpLookHeight { get; set; } = 0.95f;

        /// <summary>Возврат к обычной камере: с этого момента до конца сцены.</summary>
        public float ReturnStart { get; set; } = 4.1f;
    }

    /// <summary>Эффект вокруг персонажа (как у лечения, без плюсиков, в цвет предмета) и свечение тела.</summary>
    [Preserve]
    public sealed class PickupFxConfig
    {
        /// <summary>Когда вспыхивает эффект (префаб Resources/Fx/PickupBurst), секунды, и его масштаб.</summary>
        public float BurstTime { get; set; } = 2.3f;

        public float BurstScale { get; set; } = 1.1f;

        /// <summary>Насколько цвет эффекта светлее цвета предмета (0 — цвет как есть, 1 — белый).</summary>
        public float TintToWhite { get; set; } = 0.15f;

        /// <summary>Свечение тела в цвет предмета: начало, конец, сила подкраски (0..1) и яркость emission.</summary>
        public float GlowStart { get; set; } = 2.25f;

        public float GlowEnd { get; set; } = 4.2f;

        public float GlowTint { get; set; } = 0.12f;

        public float GlowEmission { get; set; } = 0.25f;

        /// <summary>Белая вспышка в точке удара предмета о персонажа (размер; 0 — без неё).</summary>
        public float HitFlash { get; set; } = 0.6f;
    }
}
