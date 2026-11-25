using System;
using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Параметры одного залпа. Обычный выстрел — <see cref="From"/>(конфиг); другие виды выстрела (например,
    /// заряженный: больше пуль, шире веер, дальше, сильнее, ярче) строятся изменением копии этих параметров.
    /// </summary>
    public struct ShotParams
    {
        /// <summary>Пуль в залпе.</summary>
        public int Pellets;

        /// <summary>Ширина веера, градусы.</summary>
        public float Spread;

        /// <summary>Случайный сдвиг угла каждой пули поверх веера, ± градусы.</summary>
        public float AngleJitter;

        /// <summary>Случайный наклон пули вверх-вниз, ± градусы.</summary>
        public float VerticalJitter;

        /// <summary>Дальность полёта пули, метры.</summary>
        public float Range;

        /// <summary>Скорость пули, м/с, и её разброс (доля).</summary>
        public float Speed;
        public float SpeedJitter;

        /// <summary>Множитель урона одной пули (к урону оружия).</summary>
        public float DamageMultiplier;

        /// <summary>Отбрасывание, если в цель попали все пули залпа.</summary>
        public float Knockback;

        /// <summary>Отдача: сдвиг стрелка назад, метры, и время сдвига.</summary>
        public float Recoil;
        public float RecoilTime;

        /// <summary>Диаметр шарика пули, радиус попадания, яркость свечения.</summary>
        public float PelletSize;
        public float HitRadius;
        public float Glow;

        public static ShotParams From(RangedConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            return new ShotParams
            {
                Pellets = config.Pellets,
                Spread = config.Spread,
                AngleJitter = config.AngleJitter,
                VerticalJitter = config.VerticalJitter,
                Range = config.Range,
                Speed = config.PelletSpeed,
                SpeedJitter = config.SpeedJitter,
                DamageMultiplier = 1f,
                Knockback = config.Knockback,
                Recoil = config.Recoil,
                RecoilTime = config.RecoilTime,
                PelletSize = config.PelletSize,
                HitRadius = config.HitRadius,
                Glow = config.PelletGlow
            };
        }
    }

    /// <summary>Как вылетает одна пуля: поворот от направления выстрела (по горизонтали и вертикали) и скорость.</summary>
    public readonly struct PelletLaunch
    {
        /// <summary>Поворот вокруг вертикали, градусы (плюс — вправо).</summary>
        public readonly float Yaw;

        /// <summary>Наклон, градусы (плюс — вверх).</summary>
        public readonly float Pitch;

        public readonly float Speed;

        public PelletLaunch(float yaw, float pitch, float speed)
        {
            Yaw = yaw;
            Pitch = pitch;
            Speed = speed;
        }
    }

    /// <summary>Веер пуль и курок стрелкового оружия: пауза между выстрелами и буфер нажатия.</summary>
    public static class ShotPattern
    {
        /// <summary>
        /// Пули залпа: веер <see cref="PelletAngles"/> плюс случайный сдвиг угла (± AngleJitter), наклона
        /// (± VerticalJitter) и скорости (± SpeedJitter) — залп неровный, пули прилетают не строем.
        /// </summary>
        /// <param name="random">Случайное число 0..1; null — без случайности (ровный веер, одна скорость).</param>
        public static PelletLaunch[] Launches(in ShotParams shot, Func<double> random = null)
        {
            var yaws = PelletAngles(shot.Pellets, shot.Spread, random);
            var result = new PelletLaunch[yaws.Length];
            for (int i = 0; i < yaws.Length; i++)
            {
                var yaw = yaws[i] + shot.AngleJitter * Signed(random);
                var pitch = shot.VerticalJitter * Signed(random);
                var speed = Math.Max(0.01f, shot.Speed * (1f + shot.SpeedJitter * Signed(random)));
                result[i] = new PelletLaunch(yaw, pitch, speed);
            }

            return result;
        }

        /// <summary>Случайное число -1..1 (0 без генератора).</summary>
        private static float Signed(Func<double> random) => random != null ? (float)random() * 2f - 1f : 0f;

        /// <summary>
        /// Углы пуль относительно направления выстрела, градусы, в пределах ±spread/2. Веер делится на равные
        /// секторы, в каждом одна пуля в случайной точке — покрытие ровное, но залп не выглядит штампом.
        /// </summary>
        /// <param name="random">Случайное число 0..1; null — пули по центрам секторов.</param>
        public static float[] PelletAngles(int pellets, float spread, Func<double> random = null)
        {
            pellets = Math.Max(1, pellets);
            var result = new float[pellets];
            var sector = spread / pellets;
            for (int i = 0; i < pellets; i++)
            {
                var u = random != null ? (float)random() : 0.5f;
                result[i] = -spread * 0.5f + sector * (i + u);
            }

            return result;
        }
    }

    /// <summary>Курок: стреляет не чаще Cooldown, раннее нажатие запоминается на InputBuffer секунд.</summary>
    public sealed class GunTrigger
    {
        private float cooldown;
        private float buffered;

        public float CooldownLeft => Math.Max(0f, cooldown);
        public bool Ready => cooldown <= 0f;

        /// <summary>Тик курка. true — пора стрелять (вызвать <see cref="Fired"/> или <see cref="Failed"/>).</summary>
        public bool Tick(float dt, bool pressed, RangedConfig config)
        {
            cooldown -= dt;
            buffered -= dt;
            if (pressed)
                buffered = Math.Max(0.0001f, config.InputBuffer);
            return buffered > 0f && cooldown <= 0f;
        }

        public void Fired(RangedConfig config)
        {
            cooldown = config.Cooldown;
            buffered = 0f;
        }

        /// <summary>Выстрел не состоялся (нет выносливости): нажатие сбрасывается.</summary>
        public void Failed() => buffered = 0f;

        public void Reset()
        {
            cooldown = 0f;
            buffered = 0f;
        }
    }

    /// <summary>Что сделать с попаданием пули (см. <see cref="PelletVolley{T}.Hit"/>).</summary>
    public readonly struct PelletHitOutcome
    {
        /// <summary>Сколько пуль нанести сейчас (0 — попадание отложено до решения о парировании).</summary>
        public readonly int Pellets;

        /// <summary>Множитель урона этих пуль (парирующий залп бьёт сильнее).</summary>
        public readonly float DamageMultiplier;

        /// <summary>Залп только что набрал нужное число пуль в окне парирования: парировать атаку цели.</summary>
        public readonly bool Parry;

        public PelletHitOutcome(int pellets, float damageMultiplier, bool parry)
        {
            Pellets = pellets;
            DamageMultiplier = damageMultiplier;
            Parry = parry;
        }
    }

    /// <summary>
    /// Учёт попаданий одного залпа. Обычная цель получает урон каждой пулей сразу.
    /// Парирование выстрелом (апгрейд): цель, чья атака была в окне парирования, копит попадания без урона
    /// (урон на замахе сорвал бы атаку, и парировать было бы нечего); как только в неё попало
    /// <c>parryPellets</c> пуль — атака парирована, накопленные и следующие пули бьют с множителем.
    /// Не набралось (или атака уже закончилась) — накопленные пули наносят обычный урон.
    /// </summary>
    public sealed class PelletVolley<T> where T : class
    {
        private readonly int parryPellets;
        private readonly float parryMultiplier;
        private readonly Dictionary<T, TargetState> targets = new Dictionary<T, TargetState>();
        private readonly List<KeyValuePair<T, int>> deferred = new List<KeyValuePair<T, int>>();

        private sealed class TargetState
        {
            public int Hits;
            public int Pending;
            public bool Candidate;
            public bool Parried;
        }

        /// <param name="pellets">Пуль в залпе.</param>
        /// <param name="parryPellets">Сколько пуль нужно для парирования; 0 — парирование выключено.</param>
        /// <param name="parryDamageMultiplier">Множитель урона пуль парирующего залпа.</param>
        public PelletVolley(int pellets, int parryPellets, float parryDamageMultiplier)
        {
            Remaining = Math.Max(0, pellets);
            this.parryPellets = Math.Max(0, parryPellets);
            parryMultiplier = Math.Max(1f, parryDamageMultiplier);
        }

        /// <summary>Пуль ещё в полёте.</summary>
        public int Remaining { get; private set; }

        /// <summary>Все пули долетели или попали.</summary>
        public bool Done => Remaining == 0;

        /// <summary>Сколько пуль залпа попало в цель.</summary>
        public int HitsOn(T target) => target != null && targets.TryGetValue(target, out var s) ? s.Hits : 0;

        /// <summary>Атаку цели парировал этот залп.</summary>
        public bool Parried(T target) => target != null && targets.TryGetValue(target, out var s) && s.Parried;

        /// <summary>Пуля попала в цель.</summary>
        /// <param name="inParryWindow">Атака цели в окне парирования (на момент выстрела или сейчас). Важно для первой пули.</param>
        /// <param name="canBeParried">Атака цели ещё идёт и её можно сорвать.</param>
        public PelletHitOutcome Hit(T target, bool inParryWindow, bool canBeParried)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            if (!targets.TryGetValue(target, out var state))
            {
                state = new TargetState { Candidate = parryPellets > 0 && inParryWindow && canBeParried };
                targets[target] = state;
            }

            state.Hits++;
            if (state.Parried)
                return new PelletHitOutcome(1, parryMultiplier, false);
            if (!state.Candidate)
                return new PelletHitOutcome(1, 1f, false);

            state.Pending++;
            if (!canBeParried)
            {
                // Атака закончилась раньше, чем набралось нужное число пуль: парирования не будет.
                state.Candidate = false;
                var pending = state.Pending;
                state.Pending = 0;
                return new PelletHitOutcome(pending, 1f, false);
            }

            if (state.Pending < parryPellets)
                return new PelletHitOutcome(0, 1f, false);

            state.Parried = true;
            var all = state.Pending;
            state.Pending = 0;
            return new PelletHitOutcome(all, parryMultiplier, true);
        }

        /// <summary>Пуля исчезла (попала или долетела до конца). Когда исчезнут все — <see cref="Done"/>.</summary>
        public void PelletFinished()
        {
            if (Remaining > 0)
                Remaining--;
        }

        /// <summary>
        /// Отложенные попадания, которым не хватило пуль до парирования: нанести обычным уроном.
        /// Вызывать, когда <see cref="Done"/>; повторный вызов ничего не возвращает.
        /// </summary>
        public IReadOnlyList<KeyValuePair<T, int>> TakeDeferred()
        {
            deferred.Clear();
            foreach (var pair in targets)
            {
                if (pair.Value.Pending <= 0)
                    continue;
                deferred.Add(new KeyValuePair<T, int>(pair.Key, pair.Value.Pending));
                pair.Value.Pending = 0;
                pair.Value.Candidate = false;
            }

            return deferred;
        }
    }
}
