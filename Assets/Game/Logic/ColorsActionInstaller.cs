using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Progression;
using ColorsAction.States;
using GameCore.Config;
using GameCore.DI;
using GameCore.Events;
using GameCore.Fsm;
using GameCore.Hosting;
using GameCore.Ticking;
using GameCore.UI;

namespace ColorsAction
{
    /// <summary>
    /// Сервисы и состояния игры поверх ядра. Платформа (Unity) регистрирует логгер, источник конфигов и сериализатор.
    /// Регистрация фабриками, без рефлексии: безопасно для IL2CPP.
    /// </summary>
    public sealed class ColorsActionInstaller : IInstaller
    {
        public void Install(Container container)
        {
            container
                .Register(r => new ItemDatabase(r.Resolve<ConfigService>()))
                .Register(r => new DamageRules(r.Resolve<ConfigService>().Load<CombatConfig>(ConfigKeys.Combat)))
                .Register(r =>
                {
                    var session = new GameSession(r.Resolve<ConfigService>(), r.Resolve<ItemDatabase>(), r.Resolve<IEventBus>());
                    r.Resolve<TickScheduler>().Add(session.Stamina);
                    r.Resolve<TickScheduler>().Add(session.Vitals);
                    // Стартовое оружие и опыт из окна «Параметры запуска» (платформа регистрирует LaunchOptions сама).
                    if (r.TryResolve<LaunchOptions>(out var launch))
                        launch.Apply(session);
                    return session;
                })
                .Register(r =>
                {
                    var director = new CombatDirector(r.Resolve<ConfigService>().Load<CombatConfig>(ConfigKeys.Combat));
                    r.Resolve<TickScheduler>().Add(director);
                    return director;
                })
                .Register(r => new BootState(r.Resolve<ConfigService>(), r.Resolve<StateMachine>(),
                    r.TryResolve<LaunchOptions>(out var launch) ? launch : LaunchOptions.None))
                .Register(r => new PlayState(r.Resolve<GameSession>(), r.Resolve<StateMachine>(), r.Resolve<IEventBus>()))
                .Register(r => new InventoryState(r.Resolve<GameSession>(), r.Resolve<StateMachine>(),
                    r.Resolve<IEventBus>(), r.Resolve<ScreenStack>()))
                .Register(r =>
                {
                    var session = r.Resolve<GameSession>();
                    var items = r.Resolve<ItemDatabase>();
                    return new WeaponUpgradeShop(session.Upgrades, session.Experience,
                        r.Resolve<ConfigService>().LoadTable<WeaponUpgradeConfig>(ConfigKeys.Upgrades), session.Inventory,
                        id => items.Exists(id) ? items.Get(id).Name ?? id : id);
                })
                .Register(r => new BonfireState(r.Resolve<GameSession>(), r.Resolve<StateMachine>(),
                    r.Resolve<IEventBus>(), r.Resolve<ScreenStack>(), r.Resolve<WeaponUpgradeShop>()));
        }
    }
}
