namespace ColorsAction.Combat
{
    /// <summary>
    /// Удар щитом (апгрейд 3): блок отпущен и сразу нажат снова — не позже чем через maxGap секунд после отпускания.
    /// Считает по удержанию кнопки блока, кадр за кадром.
    /// </summary>
    public sealed class ShieldBashTrigger
    {
        private bool wasHeld;
        private float sinceRelease = float.MaxValue;

        /// <param name="held">Кнопка блока зажата в этом кадре.</param>
        /// <param name="maxGap">Наибольшая пауза между отпусканием и новым нажатием, секунды.</param>
        /// <returns>true — в этом кадре нажатие после быстрого отпускания: удар щитом.</returns>
        public bool Tick(float dt, bool held, float maxGap)
        {
            if (sinceRelease < float.MaxValue)
                sinceRelease += dt;
            var pressed = held && !wasHeld;
            var released = !held && wasHeld;
            wasHeld = held;
            if (released)
            {
                sinceRelease = 0f;
                return false;
            }

            if (!pressed || sinceRelease > maxGap)
                return false;
            sinceRelease = float.MaxValue;
            return true;
        }

        /// <summary>Забыть отпускание (нет контроля, щит снят): следующее нажатие — обычный блок.</summary>
        public void Reset(bool held = false)
        {
            wasHeld = held;
            sinceRelease = float.MaxValue;
        }
    }
}
