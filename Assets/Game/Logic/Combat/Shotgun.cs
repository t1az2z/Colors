using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>Веер пуль и курок стрелкового оружия: пауза между выстрелами и буфер нажатия.</summary>
    public static class ShotPattern
    {
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
}
