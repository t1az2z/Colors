using System;
using ColorsAction.Configs;
using ColorsAction.Status;

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
        /// <summary>
        /// Атаку парировали: оружие отскочило. Шкала выносливости заполнилась (<see cref="EnemyBrain.ParryStunned"/>) —
        /// враг ошеломлён и стоит с опущенным оружием, пока действует оглушение; нет — удар лишь поглощён, через
        /// <see cref="EnemyConfig.DeflectDuration"/> враг продолжает серию или погоню.
        /// </summary>
        Parried,
        /// <summary>Оглушён статус-эффектом вне парирования: стоит, не атакует, пока действует оглушение.</summary>
        Stunned,
        Dead
    }

    /// <summary>Что враг хочет сделать в этом кадре: адаптер движка двигает и поворачивает тело.</summary>
    public readonly struct EnemyIntent
    {
        /// <summary>Скорость к игроку (минус — от игрока), м/с.</summary>
        public readonly float MoveSpeed;

        /// <summary>Поворачиваться к игроку.</summary>
        public readonly bool FacePlayer;

        /// <summary>
        /// Ждёт очереди: идти к точке за спиной игрока на расстоянии <see cref="FlankDistance"/>
        /// (место в очереди — <see cref="QueueIndex"/>, чтобы ждущие не толпились в одной точке).
        /// </summary>
        public readonly bool Flank;
        public readonly float FlankDistance;
        public readonly int QueueIndex;

        public EnemyIntent(float moveSpeed, bool facePlayer, bool flank = false, float flankDistance = 0f, int queueIndex = -1)
        {
            MoveSpeed = moveSpeed;
            FacePlayer = facePlayer;
            Flank = flank;
            FlankDistance = flankDistance;
            QueueIndex = queueIndex;
        }
    }

    /// <summary>
    /// Поведение врага без движка: агро → реакция "!" → погоня → одиночная атака (замах, удар, фиксация, возврат).
    /// Право атаковать выдаёт <see cref="CombatDirector"/>, поэтому игрока не бьют двое одновременно.
    /// Удар по врагу на замахе срывает атаку; в ударе и после — нет (враг "дожимает"). Стойкий враг
    /// (<see cref="EnemyConfig.HyperArmor"/>) от ударов не сбивается вовсе.
    /// Удары и парирования заполняют шкалу выносливости <see cref="Poise"/>; заполнилась — ошеломление.
    /// Атака может быть серией ударов (<see cref="EnemyAttackConfig.Combo"/>): удары идут подряд без возврата в стойку.
    /// </summary>
    public sealed class EnemyBrain
    {
        private readonly CombatDirector director;
        private readonly Random random;
        private float cooldown;
        private EnemyAttackConfig rootAttack;
        private int comboIndex;

        /// <summary>Следующий удар серии после поглощённого парированием (null — серия кончилась).</summary>
        private EnemyAttackConfig deflectNext;

        public int Id { get; }
        public EnemyConfig Config { get; }

        /// <summary>Зона агро этого врага (задаётся в уровне, у каждого врага своя), м.</summary>
        public float AggroRadius { get; }

        /// <summary>Дальше этого враг теряет игрока: радиус потери типа, но не меньше своего радиуса агро.</summary>
        public float LoseRadius => Math.Max(Config.LoseRadius, AggroRadius);

        public Health Health { get; }
        public EnemyState State { get; private set; } = EnemyState.Idle;
        public float StateTime { get; private set; }
        public EnemyAttackConfig Attack { get; private set; }

        /// <summary>Статус-эффекты врага (оглушение, уязвимость…). Тикают в <see cref="Tick"/>.</summary>
        public StatusEffects Status { get; }

        /// <summary>Шкала выносливости над головой: заполнилась — враг ошеломлён.</summary>
        public Poise Poise { get; }

        /// <summary>
        /// Последнее парирование заполнило шкалу выносливости: враг ошеломлён. false — удар лишь поглощён щитом.
        /// </summary>
        public bool ParryStunned { get; private set; }

        /// <summary>Первый удар текущей серии (null — не атакует) и номер текущего удара в ней (0 — первый).</summary>
        public EnemyAttackConfig RootAttack => rootAttack;
        public int ComboIndex => comboIndex;

        /// <summary>Враг ошеломлён: шкала выносливости заполнена, пока действует оглушение.</summary>
        public bool Staggering => (State == EnemyState.Parried && ParryStunned) || State == EnemyState.Stunned;

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

        /// <summary>
        /// Сейчас окно парирования текущей атаки (время относительно начала удара в
        /// [<see cref="EnemyAttackConfig.ParryFrom"/>, <see cref="EnemyAttackConfig.ParryTo"/>]).
        /// </summary>
        public bool InParryWindow => IsInParryWindow(0f);

        /// <summary>
        /// Окно парирования, расширенное на долю <paramref name="widen"/> своей длины поровну в обе стороны
        /// (апгрейд щита). Без расширения — то же, что <see cref="InParryWindow"/>.
        /// </summary>
        public bool IsInParryWindow(float widen)
        {
            if (Attack == null)
                return false;
            float t;
            if (State == EnemyState.Windup)
                t = StateTime - Attack.Windup;
            else if (State == EnemyState.Strike)
                t = StateTime;
            else if (State == EnemyState.Hold && widen > 0f)
                t = Attack.Strike + StateTime; // расширенное окно может заходить за конец удара
            else
                return false;
            Attack.ParryWindow(widen, out var from, out var to);
            return t >= from && t <= to;
        }

        /// <summary>Атаку ещё можно сорвать парированием, если игрок попал вовремя (замах, удар, фиксация).</summary>
        public bool CanBeParried => Attack != null &&
                                    (State == EnemyState.Windup || State == EnemyState.Strike || State == EnemyState.Hold);

        /// <summary>Смена состояния (для анимаций и звуков).</summary>
        public event Action<EnemyState> StateChanged;

        /// <param name="statusConfig">StatusEffects.json; null — встроенные значения (<see cref="StatusEffectsConfig.CreateDefault"/>).</param>
        /// <param name="aggroRadius">Радиус агро врага на карте; null — <see cref="LevelEnemy.DefaultAggroRadius"/>.</param>
        /// <param name="poiseConfig">Правила шкалы выносливости (Combat.json <c>poise</c>); null — встроенные значения.</param>
        public EnemyBrain(int id, EnemyConfig config, CombatDirector director, int seed, StatusEffectsConfig statusConfig = null,
            float? aggroRadius = null, PoiseConfig poiseConfig = null)
        {
            Id = id;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            AggroRadius = Math.Max(0f, aggroRadius ?? LevelEnemy.DefaultAggroRadius);
            this.director = director ?? throw new ArgumentNullException(nameof(director));
            random = new Random(seed);
            Health = new Health(config.MaxHealth);
            Health.Died += OnDied;
            Status = new StatusEffects(statusConfig, new HealthStatusTarget(Health));
            Poise = new Poise(config.Poise, poiseConfig);
        }

        /// <param name="distance">Расстояние до игрока по горизонтали, м.</param>
        /// <param name="playerAvailable">false — игрок недоступен (мёртв, далеко по высоте): враг остывает.</param>
        /// <param name="playerReachable">
        /// false — дойти до игрока нельзя (ящики, поднятый мост, закрытая дверь-срезка): спокойный враг его не замечает.
        /// Уже агрессивного врага не останавливает.
        /// </param>
        public EnemyIntent Tick(float deltaTime, float distance, bool playerAvailable = true, bool playerReachable = true)
        {
            StateTime += deltaTime;
            if (cooldown > 0f)
                cooldown = Math.Max(0f, cooldown - deltaTime);
            if (State != EnemyState.Dead)
                Status.Tick(deltaTime);
            if (!Staggering)
                Poise.Tick(deltaTime);
            // Оглушение вне парирования (эффект наложили со стороны): атака срывается, враг стоит.
            if (Status.IsStunned && State != EnemyState.Dead && !Staggering)
                EnterStun();

            switch (State)
            {
                case EnemyState.Dead:
                    return new EnemyIntent(0f, false);

                case EnemyState.Idle:
                    if (playerAvailable && playerReachable && distance <= AggroRadius)
                        SetState(EnemyState.Alerted);
                    return new EnemyIntent(0f, false);

                case EnemyState.Alerted:
                    if (StateTime >= Config.AlertDuration)
                        Engage(distance);
                    return new EnemyIntent(0f, true);

                case EnemyState.Chase:
                    if (!playerAvailable || distance > LoseRadius)
                    {
                        SetState(EnemyState.Idle);
                        return new EnemyIntent(0f, false);
                    }

                    if (TryStartAttack(distance))
                        return new EnemyIntent(0f, true);
                    // Подошёл на дистанцию ожидания, а право атаки занято — в очередь.
                    if (distance <= Config.WaitDistance + 0.2f && (director.Attackers.Count > 0 || director.Queue.Count > 0))
                    {
                        director.Enqueue(Id);
                        SetState(EnemyState.Waiting);
                    }

                    return new EnemyIntent(ApproachSpeed(distance), true);

                case EnemyState.Waiting:
                    if (!playerAvailable || distance > LoseRadius)
                    {
                        director.Leave(Id);
                        SetState(EnemyState.Idle);
                        return new EnemyIntent(0f, false);
                    }

                    director.Enqueue(Id);
                    if (TryStartAttack(distance))
                        return new EnemyIntent(0f, true);
                    // Очередь пуста и никто не атакует — просто догоняем.
                    if (director.Attackers.Count == 0 && director.QueueIndex(Id) == 0 && PickAttack(distance) == null)
                    {
                        director.Leave(Id);
                        SetState(EnemyState.Chase);
                        return new EnemyIntent(Config.MoveSpeed, true);
                    }

                    // Ждём за спиной игрока; следующий в очереди подходит на дистанцию своего удара,
                    // чтобы начать замах точно вовремя и ударить сразу после текущего врага.
                    var place = director.QueueIndex(Id);
                    var close = place == 0 ? Math.Max(Config.ApproachDistance, Config.MaxAttackRange * 0.8f) : Config.WaitDistance;
                    return new EnemyIntent(Config.MoveSpeed, true, true, close, place);

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
                    {
                        // Серия: сразу замах следующим ударом, без возврата в стойку.
                        var next = rootAttack?.Step(comboIndex + 1);
                        if (next != null)
                            StartComboStep(next);
                        else
                            SetState(EnemyState.Recovery);
                    }

                    return new EnemyIntent(0f, false);

                case EnemyState.Recovery:
                    if (StateTime >= Attack.Recovery)
                    {
                        director.Release(Id);
                        director.Leave(Id);
                        cooldown = Config.AttackCooldown;
                        Attack = null;
                        rootAttack = null;
                        SetState(EnemyState.Chase);
                    }

                    return new EnemyIntent(0f, false);

                case EnemyState.Staggered:
                    if (StateTime >= Config.StaggerDuration)
                        SetState(EnemyState.Chase);
                    return new EnemyIntent(0f, false);

                case EnemyState.Parried when !ParryStunned:
                    // Удар поглощён парированием: оружие отскочило, враг собрался и продолжает серию или погоню.
                    if (StateTime >= Config.DeflectDuration)
                    {
                        var next = deflectNext;
                        deflectNext = null;
                        if (next != null && rootAttack != null)
                            StartComboStep(next);
                        else
                        {
                            director.Release(Id);
                            director.Leave(Id);
                            rootAttack = null;
                            SetState(EnemyState.Chase);
                        }
                    }

                    return new EnemyIntent(0f, false);

                case EnemyState.Parried:
                case EnemyState.Stunned:
                    // Ошеломление длится, пока действует оглушение (шкала заполнилась — EnemyConfig.ParriedDuration).
                    if (!Status.IsStunned)
                    {
                        if (Poise.Broken)
                            Poise.Reset();
                        SetState(EnemyState.Chase);
                    }

                    return new EnemyIntent(0f, false);
            }

            return new EnemyIntent(0f, false);
        }

        /// <summary>
        /// Враг получил урон; <paramref name="poise"/> — сколько удар заполняет шкалу выносливости
        /// (<see cref="Poise.HitAmount"/>). Возвращает true, если атака была сорвана (удар пришёлся на замах или
        /// заполнил шкалу). Вне атаки враг отшатывается; в фазе удара и позже атака продолжается. Стойкий враг
        /// (<see cref="EnemyConfig.HyperArmor"/>) не сбивается и не отшатывается — только ошеломляется заполненной шкалой.
        /// </summary>
        public bool OnDamaged(float poise = 0f)
        {
            // Ошеломлённый враг стоит открытым: урон проходит, но шкала не копится и оглушение не сбивается.
            if (State == EnemyState.Dead || Staggering)
                return false;
            if (Poise.Add(poise))
            {
                Break();
                return true;
            }

            // Удар поглощён щитом — враг уже отскочил и собирается: не сбиваем.
            if (State == EnemyState.Parried)
                return false;
            if (Config.HyperArmor)
            {
                // Не отшатывается, но удар его будит: спокойный стойкий враг сразу идёт на игрока.
                if (State == EnemyState.Idle)
                    SetState(EnemyState.Chase);
                return false;
            }
            if (State == EnemyState.Windup)
            {
                director.Release(Id);
                Attack = null;
                rootAttack = null;
                cooldown = Config.AttackCooldown * 0.5f;
                SetState(EnemyState.Staggered);
                return true;
            }

            if (!IsAttacking)
            {
                director.Leave(Id);
                SetState(EnemyState.Staggered);
            }

            return false;
        }

        /// <summary>
        /// Игрок поставил блок в окно парирования текущей атаки: удар поглощён (урона нет), шкала выносливости
        /// заполняется на <see cref="PoiseConfig.Parry"/>. Заполнилась — атака срывается, право атаки отдаётся следующему,
        /// враг оглушён (<see cref="StatusEffectsConfig.StunId"/>) на <see cref="EnemyConfig.ParriedDuration"/> и стоит
        /// с опущенным оружием; остальные эффекты парирования — <see cref="ApplyParryEffects"/> (в момент удара о щит).
        /// Нет — оружие отскакивает, и через <see cref="EnemyConfig.DeflectDuration"/> враг продолжает серию.
        /// </summary>
        /// <returns>true — атака парирована (было окно парирования).</returns>
        public bool TryParry(float widen = 0f)
        {
            if (!IsInParryWindow(widen))
                return false;
            return Parry();
        }

        /// <summary>
        /// Парирование без проверки окна: окно проверено раньше (выстрел дробовика сделан в окно, а пули долетели
        /// чуть позже). Срабатывает, пока атака не закончилась (<see cref="CanBeParried"/>).
        /// </summary>
        public bool Parry()
        {
            if (!CanBeParried)
                return false;
            ParriedAttack = Attack;
            ParriedFromState = State;
            ParriedFromProgress = PhaseProgress;
            ParryStunned = Poise.Add(Poise.Config.Parry);
            Attack = null;
            if (ParryStunned)
            {
                director.Release(Id);
                director.Leave(Id);
                rootAttack = null;
                deflectNext = null;
                cooldown = Config.AttackCooldown;
                Status.Apply(StatusEffectsConfig.StunId, Math.Max(0.0001f, Config.ParriedDuration));
            }
            else
            {
                // Удар поглощён: серия продолжится следующим ударом (право атаки держим), иначе — пауза перед новой атакой.
                deflectNext = rootAttack?.Step(comboIndex + 1);
                if (deflectNext == null)
                    cooldown = Config.AttackCooldown;
            }

            SetState(EnemyState.Parried);
            return true;
        }

        /// <summary>
        /// Эффекты парирования кроме оглушения (<see cref="StatusEffectsConfig.OnParry"/>, по умолчанию — уязвимость).
        /// Вызывается, когда оружие врага встретило щит: сам парирующий удар их бонусом не пользуется. Только если
        /// парирование заполнило шкалу и ошеломило врага: поглощённый удар без ошеломления эффектов не даёт.
        /// </summary>
        public void ApplyParryEffects()
        {
            if (State == EnemyState.Dead || !ParryStunned)
                return;
            foreach (var application in Status.Config.OnParry)
                Status.Apply(application);
        }

        private void EnterStun()
        {
            director.Release(Id);
            director.Leave(Id);
            if (Attack != null || rootAttack != null)
                cooldown = Math.Max(cooldown, Config.AttackCooldown * 0.5f);
            Attack = null;
            rootAttack = null;
            deflectNext = null;
            SetState(EnemyState.Stunned);
        }

        /// <summary>Шкала заполнилась ударом: атака срывается, враг ошеломлён на <see cref="EnemyConfig.ParriedDuration"/>.</summary>
        private void Break()
        {
            Status.Apply(StatusEffectsConfig.StunId, Math.Max(0.0001f, Config.ParriedDuration));
            EnterStun();
        }

        /// <summary>Следующий удар серии: замах сразу из текущей позы.</summary>
        private void StartComboStep(EnemyAttackConfig step)
        {
            comboIndex++;
            Attack = step;
            SetState(EnemyState.Windup);
        }

        /// <summary>Последняя парированная атака (для анимации отскока оружия).</summary>
        public EnemyAttackConfig ParriedAttack { get; private set; }

        /// <summary>В какой фазе и на каком её прогрессе атаку парировали (для анимации недоделанного удара).</summary>
        public EnemyState ParriedFromState { get; private set; }
        public float ParriedFromProgress { get; private set; }

        /// <summary>
        /// Скорость погони: до <see cref="EnemyConfig.ApproachDistance"/> — бегом, на ней — стоит,
        /// если игрок сам подошёл заметно ближе — неспешно отступает.
        /// </summary>
        private float ApproachSpeed(float distance)
        {
            var stop = Config.ApproachDistance;
            if (distance > stop)
                return Math.Min(Config.MoveSpeed, (distance - stop) * 6f);
            if (distance < stop - 0.4f)
                return -Config.MoveSpeed * 0.35f;
            return 0f;
        }

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
            // Поглощённый парированием удар серии сдвигает следующий на DeflectDuration (не больше): держим запас,
            // чтобы продолжение серии не совпало с чужим ударом.
            if (attack == null)
                return false;
            var span = attack.StrikeSpan + (attack.Steps - 1) * Math.Max(0f, Config.DeflectDuration);
            if (!director.TryAcquire(Id, attack.Windup, span))
                return false;
            Attack = attack;
            rootAttack = attack;
            comboIndex = 0;
            deflectNext = null;
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
            Status.Clear();
            director.Release(Id);
            director.Leave(Id);
            Attack = null;
            rootAttack = null;
            deflectNext = null;
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
