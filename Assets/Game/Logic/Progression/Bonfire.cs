namespace ColorsAction
{
    /// <summary>Игрок сел у костра (F / X). <see cref="States.PlayState"/> переводит игру в <see cref="States.BonfireState"/>.</summary>
    public readonly struct BonfireRestRequested
    {
        public readonly string BonfireId;

        public BonfireRestRequested(string bonfireId)
        {
            BonfireId = bonfireId;
        }
    }

    /// <summary>
    /// Отдых у костра: здоровье и выносливость восстановлены, костёр стал точкой возрождения.
    /// Уровень по этому событию возрождает всех врагов.
    /// </summary>
    public readonly struct BonfireRested
    {
        public readonly string BonfireId;

        public BonfireRested(string bonfireId)
        {
            BonfireId = bonfireId;
        }
    }

    /// <summary>Игрок появился после смерти (у последнего костра или на старте уровня).</summary>
    public readonly struct PlayerRespawned
    {
    }
}
