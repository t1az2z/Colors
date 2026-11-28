using System;
using System.Collections.Generic;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>Движение атаки врага (анимация оружия).</summary>
    public enum EnemyAttackMotion
    {
        /// <summary>Размашистый горизонтальный удар.</summary>
        Sweep,

        /// <summary>Удар сверху вниз: дальше, но уже.</summary>
        Overhead,

        /// <summary>Тяжёлый удар сверху в землю с высоким замахом над головой (как 4-й удар меча игрока).</summary>
        Slam,

        /// <summary>Косой удар из-за правого плеча вниз влево (первый удар «накрест»).</summary>
        DiagonalRight,

        /// <summary>Косой удар из-за левого плеча вниз вправо (второй удар «накрест»).</summary>
        DiagonalLeft
    }

    /// <summary>Одиночная атака врага (без комбо): долгий читаемый замах и резкий удар.</summary>
    [Preserve]
    public sealed class EnemyAttackConfig
    {
        public EnemyAttackMotion Motion { get; set; } = EnemyAttackMotion.Sweep;

        /// <summary>Замах, секунды. Долгий, чтобы игрок успел среагировать.</summary>
        public float Windup { get; set; } = 1.5f;

        /// <summary>Удар, секунды — резкий; в это время наносится урон.</summary>
        public float Strike { get; set; } = 0.1f;

        /// <summary>Фиксация финальной позы, секунды.</summary>
        public float Hold { get; set; } = 0.3f;

        /// <summary>Возврат в стойку, секунды.</summary>
        public float Recovery { get; set; } = 0.5f;

        public float Damage { get; set; } = 15f;

        /// <summary>Дальность от центра врага до цели, метры. С какого расстояния враг начинает эту атаку.</summary>
        public float Range { get; set; } = 1.5f;

        /// <summary>Ширина сектора поражения, градусы.</summary>
        public float Arc { get; set; } = 140f;

        public float Knockback { get; set; } = 1f;

        /// <summary>Рывок в момент удара, метры: больше нуля — вперёд, меньше нуля — шаг назад.</summary>
        public float Lunge { get; set; } = 0.3f;

        /// <summary>Вес при случайном выборе атаки.</summary>
        public float Weight { get; set; } = 1f;

        /// <summary>
        /// Окно парирования щитом, секунды относительно начала удара (минус — до удара, в конце замаха).
        /// Блок, поставленный в это окно, — парирование. К открытию окна цепляются звук и VFX-подсказка.
        /// </summary>
        public float ParryFrom { get; set; } = -0.18f;
        public float ParryTo { get; set; } = 0.06f;

        /// <summary>
        /// Удар по области: радиус круга поражения, метры (0 — обычный удар по сектору <see cref="Arc"/>).
        /// Центр круга — на земле в <see cref="AreaOffset"/> перед врагом; там же волна по земле, как у удара игрока в прыжке.
        /// </summary>
        public float AreaRadius { get; set; }

        /// <summary>Где падает удар по области: метры от центра врага вперёд.</summary>
        public float AreaOffset { get; set; } = 1f;

        /// <summary>Удар по области (<see cref="AreaRadius"/> больше нуля).</summary>
        public bool IsArea => AreaRadius > 0f;

        /// <summary>
        /// Продолжение комбо: удары после этого, по порядку. После фиксации позы враг сразу замахивается следующим
        /// (без возврата в стойку). Парировать можно каждый удар; поглощённый парированием удар урона не наносит,
        /// а серия продолжается, если шкала выносливости врага не заполнилась. Вес и дальность выбора — у первого удара.
        /// </summary>
        public List<EnemyAttackConfig> Combo { get; set; } = new List<EnemyAttackConfig>();

        /// <summary>Удар в серии комбо № <paramref name="index"/> (0 — сам этот удар); null — серия кончилась.</summary>
        public EnemyAttackConfig Step(int index)
        {
            if (index == 0)
                return this;
            return Combo != null && index - 1 < Combo.Count ? Combo[index - 1] : null;
        }

        /// <summary>Ударов в серии (с этим).</summary>
        public int Steps => 1 + (Combo?.Count ?? 0);

        /// <summary>
        /// От начала первого удара до конца последнего удара серии, секунды: столько оркестратор держит право атаки
        /// занятым для чужих ударов.
        /// </summary>
        public float StrikeSpan
        {
            get
            {
                var span = Strike;
                if (Combo == null)
                    return span;
                var previous = this;
                foreach (var next in Combo)
                {
                    span += previous.Hold + next.Windup + next.Strike;
                    previous = next;
                }

                return span;
            }
        }

        /// <summary>
        /// Окно парирования, расширенное на долю <paramref name="widen"/> своей длины поровну в обе стороны
        /// (апгрейд щита: 0.5 — по четверти длины раньше и позже).
        /// </summary>
        public void ParryWindow(float widen, out float from, out float to)
        {
            var extra = Math.Max(0f, ParryTo - ParryFrom) * Math.Max(0f, widen) * 0.5f;
            from = ParryFrom - extra;
            to = ParryTo + extra;
        }

        public float Total => Windup + Strike + Hold + Recovery;
    }

    /// <summary>Configs/Enemies.json — типы врагов.</summary>
    [Preserve]
    public sealed class EnemyConfig : IIdentified, IConfigPostLoad
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Цвет врага этого типа по умолчанию (круг Combat.json <c>colorWheel</c>): бонус урона оружием того же цвета.
        /// Каждому поставленному врагу цвет задаётся в карте (<see cref="LevelEnemy.Color"/>), этот — когда там пусто.
        /// </summary>
        public string Color { get; set; } = "blue";

        /// <summary>Размер куба в блоках (куб размера 1 — как игрок по высоте).</summary>
        public float Size { get; set; } = 0.9f;

        public float MaxHealth { get; set; } = 40f;

        /// <summary>Элитный враг: в редакторах уровней помечен звездой.</summary>
        public bool Elite { get; set; }

        /// <summary>
        /// Объём шкалы выносливости (poise). Удары и парирования заполняют её одинаково у всех врагов
        /// (<see cref="PoiseConfig"/>), заполнилась — враг ошеломлён на <see cref="ParriedDuration"/>.
        /// 0 — шкалы нет, враг не ошеломляется.
        /// </summary>
        public float Poise { get; set; } = 100f;

        /// <summary>
        /// Стойкость: удары не срывают замах и не заставляют отшатнуться — остановить врага может только
        /// заполненная шкала выносливости.
        /// </summary>
        public bool HyperArmor { get; set; }

        /// <summary>Оружие в руке: модель из Resources (Items/Club).</summary>
        public string WeaponPrefab { get; set; } = "Items/Club";

        // Радиус агро — у каждого врага на карте свой: LevelEnemy.AggroRadius (VoxelLevels, редакторы уровней).

        /// <summary>
        /// Дальше этого враг теряет игрока и возвращается в покой. Если радиус агро врага на карте больше —
        /// берётся он (<see cref="ColorsAction.Combat.EnemyBrain.LoseRadius"/>).
        /// </summary>
        public float LoseRadius { get; set; } = 13f;

        /// <summary>
        /// Профиль перемещения из Movement.json: по нему проверяется, может ли враг дойти до игрока
        /// (за ящиками, поднятым мостом, закрытой дверью-срезкой враг не замечает игрока).
        /// </summary>
        public string Movement { get; set; } = "walker";

        /// <summary>Реакция "заметил!" (сжатие, подпрыгивание, знак "!"), секунды. Потом бег или атака.</summary>
        public float AlertDuration { get; set; } = 2f;

        public float MoveSpeed { get; set; } = 4.5f;
        public float TurnSpeed { get; set; } = 540f;

        /// <summary>Пока атакует другой враг, этот держится на такой дистанции от игрока (заходя за спину).</summary>
        public float WaitDistance { get; set; } = 3.2f;

        /// <summary>
        /// Погоня: враг подходит к игроку на это расстояние и дальше не напирает (не впритык).
        /// Если игрок подошёл ближе — отступает. Атакует ударами, которые достают с этой дистанции.
        /// </summary>
        public float ApproachDistance { get; set; } = 1.3f;

        /// <summary>
        /// Ошеломление: шкала выносливости заполнилась (парированием или ударами) — враг стоит с опущенным оружием
        /// столько секунд.
        /// </summary>
        public float ParriedDuration { get; set; } = 1f;

        /// <summary>
        /// Парирование при незаполненной шкале поглощает удар: оружие отскакивает, враг приходит в себя за столько
        /// секунд и продолжает серию (или возвращается к погоне).
        /// </summary>
        public float DeflectDuration { get; set; } = 0.4f;

        /// <summary>
        /// Подача парирования: враг ещё успевает начать удар и встречает щит на полпути — через столько секунд
        /// после парирования оружие "сталкивается" (вспышка, фриз-кадры) и отскакивает.
        /// </summary>
        public float ParryContactTime { get; set; } = 0.07f;

        /// <summary>Докуда по дуге удара (0..1) оружие успевает дойти до столкновения при парировании.</summary>
        public float ParryContactArc { get; set; } = 0.45f;

        /// <summary>Насколько стремится зайти игроку за спину, пока ждёт очереди: 0 — где стоит, 1 — точно сзади.</summary>
        public float FlankBias { get; set; } = 1f;

        /// <summary>Пауза после своей атаки, прежде чем снова попросить право атаковать.</summary>
        public float AttackCooldown { get; set; } = 0.8f;

        /// <summary>Отшатывание при получении урона, секунды (враг не двигается).</summary>
        public float StaggerDuration { get; set; } = 0.35f;

        /// <summary>Сила отталкивания при попадании по врагу, метры.</summary>
        public float HitPushback { get; set; } = 0.45f;

        /// <summary>Опыт за победу.</summary>
        public int Experience { get; set; } = 10;

        /// <summary>На сколько кусков раскалывается при смерти и сколько из них летят к игроку как опыт.</summary>
        public int DeathPieces { get; set; } = 14;
        public int ExperiencePieces { get; set; } = 6;

        public List<EnemyAttackConfig> Attacks { get; set; } = new List<EnemyAttackConfig>();

        public float MaxAttackRange
        {
            get
            {
                var r = 0f;
                foreach (var a in Attacks)
                    r = Math.Max(r, a.Range);
                return r;
            }
        }

        public void OnConfigLoaded()
        {
            Attacks ??= new List<EnemyAttackConfig>();
            if (Attacks.Count == 0)
                throw new InvalidOperationException($"enemy '{Id}' needs at least one attack");
            if (MaxHealth <= 0 || Size <= 0)
                throw new InvalidOperationException($"enemy '{Id}': maxHealth and size must be positive");
            if (Poise < 0f || DeflectDuration < 0f)
                throw new InvalidOperationException($"enemy '{Id}': poise and deflectDuration must not be negative");
            foreach (var attack in Attacks)
            {
                attack.Combo ??= new List<EnemyAttackConfig>();
                foreach (var step in attack.Combo)
                    step.Combo = new List<EnemyAttackConfig>(); // серия — только у первого удара
            }
        }
    }
}
