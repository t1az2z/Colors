using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    public enum EnemyState
    {
        Idle,
        /// <summary>Заметил игрока: сжатие, подпрыгивание, "!" над головой.</summary>
        Alerted,
        Chase,
        /// <summary>Другой враг атакует — этот держит дистанцию и ждёт права атаки.</summary>
        Waiting,
        Windup,
        Strike,
        Hold,
        Recovery,
        /// <summary>Отшатнулся от удара.</summary>
        Staggered,
        Dead
    }

    /// <summary>Что враг хочет сделать в этом кадре: адаптер движка двигает и поворачивает тело.</summary>
    public readonly struct EnemyIntent
    {
        /// <summary>Скорость к игроку (минус — от игрока), м/с.</summary>
        public readonly float MoveSpeed;

        /// <summary>Поворачиваться к игроку.</summary>
        public readonly bool FacePlayer;

        public EnemyIntent(float moveSpeed, bool facePlayer)
        {
            MoveSpeed = moveSpeed;
            FacePlayer = facePlayer;
        }
    }

    /// <summary>
    /// Поведение врага без движка: агро → реакция "!" → погоня → одиночная атака (замах, удар, фиксация, возврат).
    /// Право атаковать выдаёт <see cref="CombatDirector"/>, поэтому игрока не бьют двое одновременно.
    /// Удар по врагу на замахе срывает атаку; в ударе и после — нет (враг "дожимает").
    /// </summary>
    public sealed class EnemyBrain
    {
        private readonly CombatDirector director;
        private readonly Random random;
        private float cooldown;

        public int Id { get; }
        public EnemyConfig Config { get; }
        public Health Health { get; }
        public EnemyState State { get; private set; } = EnemyState.Idle;
        public float StateTime { get; private set; }
        public EnemyAttackConfig Attack { get; private set; }

        /// <summary>Прогресс текущей фазы атаки 0..1 (замах, удар, фиксация, возврат).</summary>
        public float PhaseProgress
        {
            get
            {
                if (Attack == null)
                    return 0f;
                float length;
                switch (State)
                {
                    case EnemyState.Windup: length = Attack.Windup; break;
                    case EnemyState.Strike: length = Attack.Strike; break;
                    case EnemyState.Hold: length = Attack.Hold; break;
                    case EnemyState.Recovery: length = Attack.Recovery; break;
                    default: return 0f;
                }

                return length <= 0f ? 1f : Math.Min(1f, StateTime / length);
            }
        }

        public bool IsAttacking => State == EnemyState.Windup || State == EnemyState.Strike
                                   || State == EnemyState.Hold || State == EnemyState.Recovery;

        public bool IsAggro => State != EnemyState.Idle && State != EnemyState.Dead;

        /// <summary>Смена состояния (для анимаций и звуков).</summary>
        public event Action<EnemyState> StateChanged;

        public EnemyBrain(int id, EnemyConfig config, CombatDirector director, int seed)
        {
            Id = id;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            this.director = director ?? throw new ArgumentNullException(nameof(director));
            random = new Random(seed);
            Health = new Health(config.MaxHealth);
            Health.Died += OnDied;
        }

        /// <param name="distance">Расстояние до игрока по горизонтали, м.</param>
        /// <param name="playerAvailable">false — игрок недоступен (мёртв, далеко по высоте): враг остывает.</param>
        public EnemyIntent Tick(float deltaTime, float distance, bool playerAvailable = true)
        {
            StateTime += deltaTime;
            if (cooldown > 0f)
                cooldown = Math.Max(0f, cooldown - deltaTime);

            switch (State)
            {
                case EnemyState.Dead:
                    return new EnemyIntent(0f, false);

                case EnemyState.Idle:
                    if (playerAvailable && distance <= Config.AggroRadius)
                        SetState(EnemyState.Alerted);
                    return new EnemyIntent(0f, false);

                case EnemyState.Alerted:
                    if (StateTime >= Config.AlertDuration)
                        Engage(distance);
                    return new EnemyIntent(0f, true);

                case EnemyState.Chase:
                    if (!playerAvailable || distance > Config.LoseRadius)
                    {
                        SetState(EnemyState.Idle);
                        return new EnemyIntent(0f, false);
                    }

                    if (TryStartAttack(distance))
                        return new EnemyIntent(0f, true);
                    if (!CanAcquireNow() && distance <= Config.WaitDistance + 0.2f)
                        SetState(EnemyState.Waiting);
                    return new EnemyIntent(Config.MoveSpeed, true);

                case EnemyState.Waiting:
                    if (!playerAvailable || distance > Config.LoseRadius)
                    {
                        SetState(EnemyState.Idle);
                        return new EnemyIntent(0f, false);
                    }

                    if (TryStartAttack(distance))
                        return new EnemyIntent(0f, true);
                    if (CanAcquireNow())
                    {
                        SetState(EnemyState.Chase);
                        return new EnemyIntent(Config.MoveSpeed, true);
                    }

                    // Держим дистанцию: подходим, если далеко, отступаем, если вплотную.
                    if (distance > Config.WaitDistance + 0.6f)
                        return new EnemyIntent(Config.MoveSpeed * 0.5f, true);
                    if (distance < Config.WaitDistance - 0.6f)
                        return new EnemyIntent(-Config.MoveSpeed * 0.4f, true);
                    return new EnemyIntent(0f, true);

                case EnemyState.Windup:
                    if (StateTime >= Attack.Windup)
                        SetState(EnemyState.Strike);
                    // Следит за игроком почти весь замах, в конце фиксирует направление — от удара можно уйти.
                    return new EnemyIntent(0f, StateTime < Attack.Windup * 0.75f);

                case EnemyState.Strike:
                    if (StateTime >= Attack.Strike)
                        SetState(EnemyState.Hold);
                    return new EnemyIntent(0f, false);

                case EnemyState.Hold:
                    if (StateTime >= Attack.Hold)
                        SetState(EnemyState.Recovery);
                    return new EnemyIntent(0f, false);

                case EnemyState.Recovery:
                    if (StateTime >= Attack.Recovery)
                    {
                        director.Release(Id);
                        cooldown = Config.AttackCooldown;
                        Attack = null;
                        SetState(EnemyState.Chase);
                    }

                    return new EnemyIntent(0f, false);

                case EnemyState.Staggered:
                    if (StateTime >= Config.StaggerDuration)
                        SetState(EnemyState.Chase);
                    return new EnemyIntent(0f, false);
            }

            return new EnemyIntent(0f, false);
        }

        /// <summary>
        /// Враг получил урон. Возвращает true, если атака была сорвана (удар пришёлся на замах).
        /// Вне атаки враг отшатывается; в фазе удара и позже атака продолжается.
        /// </summary>
        public bool OnDamaged()
        {
            if (State == EnemyState.Dead)
                return false;
            if (State == EnemyState.Windup)
            {
                director.Release(Id);
                Attack = null;
                cooldown = Config.AttackCooldown * 0.5f;
                SetState(EnemyState.Staggered);
                return true;
            }

            if (!IsAttacking)
                SetState(EnemyState.Staggered);
            return false;
        }

        private bool CanAcquireNow() => cooldown <= 0f && (director.Attackers.Count == 0 || director.IsAttacking(Id));

        private void Engage(float distance)
        {
            if (!TryStartAttack(distance))
                SetState(EnemyState.Chase);
        }

        private bool TryStartAttack(float distance)
        {
            if (cooldown > 0f)
                return false;
            var attack = PickAttack(distance);
            if (attack == null || !director.TryAcquire(Id))
                return false;
            Attack = attack;
            SetState(EnemyState.Windup);
            return true;
        }

        /// <summary>Случайная (по весам) атака из тех, что достают с этого расстояния.</summary>
        private EnemyAttackConfig PickAttack(float distance)
        {
            var total = 0f;
            foreach (var a in Config.Attacks)
                if (a.Range >= distance)
                    total += Math.Max(0.0001f, a.Weight);
            if (total <= 0f)
                return null;
            var roll = random.NextDouble() * total;
            foreach (var a in Config.Attacks)
            {
                if (a.Range < distance)
                    continue;
                roll -= Math.Max(0.0001f, a.Weight);
                if (roll <= 0)
                    return a;
            }

            return null;
        }

        private void OnDied()
        {
            director.Release(Id);
            Attack = null;
            SetState(EnemyState.Dead);
        }

        private void SetState(EnemyState state)
        {
            State = state;
            StateTime = 0f;
            StateChanged?.Invoke(state);
        }
    }
}
