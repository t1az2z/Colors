# Карта кода ColorsAction

Сначала смотреть сюда, потом искать по коду. При изменении структуры (новые папки, системы, перенос файлов) — обновить карту.

## Сборки

| Сборка | Папка | Что внутри |
|---|---|---|
| `GameCore` (+`.Newtonsoft`) | `Packages/com.template.gamecore` | Шаблонное ядро без движка: DI, конфиги, события, FSM, тики, реактивный UI. Не трогать без нужды. |
| `GameCore.Unity` | `Packages/com.template.gamecore.unity` | Мост ядра к Unity: `GameHostBehaviour`, источники конфигов, логгер. |
| `ColorsAction.Game` | `Assets/Game/Logic` | Логика игры **без UnityEngine** (`noEngineReferences`). Тестируется `dotnet test dotnet/ColorsAction.sln`. |
| `ColorsAction.Unity` | `Assets/Game/Scripts` (кроме `Editor/`) | Всё, что живёт в сцене: игрок, бой, враги, уровень, UI. |
| `ColorsAction.Unity.Editor` | `Assets/Game/Scripts/Editor` | Сборщик сцены, плейтест, импорт моделей. |
| `ColorsAction.LevelEditor` | `Assets/Game/Scripts/Editor/LevelEditor` | Unity-редактор воксельных уровней. |

## Логика (`Assets/Game/Logic`)

- `ColorsActionInstaller.cs` — регистрация сервисов в DI. `LaunchOptions.cs` — параметры запуска (уровень, стартовое оружие с апгрейдом, опыт) и `LevelKeys` (ключ `VoxelLevels/…` — воксельная карта). `GameSession.cs` — состояние игры: инвентарь, выносливость, здоровье, опыт (он же «деньги» прокачки), открытые срезки, `CompletedHints`, `BrokenCrates` (разбитые ящики), `OpenedChests` (открытые сундуки), `OpenedBridges` (опущенные разводные мосты), `LastBonfire`, `Upgrades`, схема управления; `GiveResources` (ресурсы в инвентарь, событие `ResourcesGained`).
- `Configs/` — классы JSON-конфигов: `Configs.cs` (Game, Player + выносливость/здоровье (`PlayerHealthConfig`: максимум, прирост за уровень `MaxAt`, i-frames, мигание `BlinkHidden`)/деформация, Camera, Colors, Items + `AttackStepConfig` комбо с `fxFrom/fxTo`, `upgrade`, `hop`, `chargeable` (общий заряд оружия `ItemConfig.Charge`, `ChargeFor`), Combat, Inventory), `DodgeConfig.cs` (серия отскоков), `WeaponMoveConfigs.cs` (`ChargeConfig`, `FinisherConfig`, `PlungeConfig`), `UpgradeConfigs.cs` (Upgrades.json: цены апгрейдов — опыт + ресурс оружия `resource`/`resources`), `ChestConfig.cs` (Chests.json: вид сундука, открытие, полёт ресурса), `EnemyConfigs.cs`, `CrateConfig.cs` (Crates.json: HP ящиков, `breakableBy`, вид), `BridgeConfig.cs` (Bridges.json: прочность цепи, `breakableBy` — дробовик, вид, падение створки), `LevelConfig.cs` (+ `bonfires`, `crates`, `chests` (ресурс и количество), `bridges`, подсказки с `target`).
- `Combat/` — `DamageRules` (урон и цвет), `Health`/`PlayerVitals`, `Stamina`, `EnemyBrain` (решения врага), `CombatDirector` (очередь врагов на атаку), `LockOnRules` (+ `Nearest` — передача фокуса), `Shotgun` (`ShotParams` — параметры залпа, `ShotPattern`, `PelletVolley`, `GunTrigger`), `DodgeCombo` (серия отскоков), `AttackCharge` (общий заряд удержанием: вспышка на пике, окно и спад множителя), `ComboFinisher` + `ComboSteps` (отложенный добивающий, удары за апгрейдами), `PlungeAttack` (удар в прыжке без потери инерции, удар при приземлении), `ShieldBash`, `BreakableState` (`Breakable.cs`: здоровье разрушаемого, ломается только оружием из списка).
- `Inventory/` — `PlayerInventory` (сетка, руки, быстрые слоты; `Item.Charges` — заряды расходников; стопки ресурсов `Item.Count`/`ItemConfig.MaxStack`: `CountOf`, `TryAddCount`, `TryConsume`, слияние перетаскиванием), `ItemDatabase`, `HealItems` (лечилка: использование, восполнение у костра; `GameSession.UseQuickItem`/`RefillConsumables`, событие `QuickItemUsed`).
- `Progression/` — `WeaponUpgrades` (уровни апгрейдов 0..3, `Has(itemId, tier)`; отладка `debugWeaponUpgrades` в Game.json), `WeaponUpgradeShop` (покупка за опыт + ресурс оружия из инвентаря, `Missing` — чего не хватает), `Bonfire` (события отдыха/респауна).
- `Level/` — воксельная модель (`VoxelLevel`, `VoxelLevelData`, `VoxelTypes`), `VoxelPlayLayout` (объекты воксельной карты → списки `LevelConfig` с высотой опоры `Y` для игры), `CrateStackShape` (стопка ящиков 2×1×N: клетки по yaw, объект `LevelObjectKind.CrateStack`), сундук — `LevelObjectKind.Chest` (`Item` — ресурс, `Count` — количество), `DrawBridgeShape` (разводной мост 4×2×2: клетки по yaw, стена поднятой створки, объект `LevelObjectKind.DrawBridge`), правки и история (`LevelEditing`), `SurfaceSmoothing`, `ShortcutDoorRules`, `HintTargets` (подсказки гаснут после подбора/двери), `LevelEdgeWalls`, `Navigation/` (граф и A* для врагов).
- `States/States.cs` — FSM: Boot → Play ↔ Inventory / Bonfire. `UI/` — `InventoryScreenViewModel`, `BonfireScreenViewModel`.

## Unity (`Assets/Game/Scripts`)

- `Core/` — `GameBootstrap` (точка входа сцены, хост ядра, создаёт HUD/инвентарь/EventSystem из префабов, регистрирует `LaunchOptions`), `LaunchOptionsStore` (UserSettings/ColorsActionLaunch.json), `GameInput` (действия клавиатура/геймпад), `Palette`, `ComponentPool` (пулинг), `GlowMaterials` (копии материалов с `_EMISSION` для белой вспышки неуязвимости).
- `Player/` — `PlayerController` (ходьба, бег, прыжок, серия отскоков, хуки удара в прыжке), `BoxMotor` (кинематика бокса), `CubeDeformer` (деформация куба), `PlayerDamageReceiver` (урон, блок, i-frames с белым свечением тела и оружия, смерть/респаун, быстрые слоты 1–4 — лечилка), `PlayerLockOn` (фиксация цели: враги, манекены, цепи мостов), `AimMarker`.
- `Combat/` — `PlayerCombat` (partial: `.cs` — атаки обеих рук, комбо, позы; `.Moves.cs` — заряд, добивающий, удар в прыжке, окно эффекта; `.Shotgun.cs` — выстрел пулями, заряд, позы дробовика; `.Shield.cs` — блок, парирование, удар щитом), `Pellet` (пуля из пула), `WeaponGlow`, `ShockRingFx`, `WeaponTrail`, `ParryFlash`, `TrainingDummy`, `Hittable` (`IHittable`, `HitInfo` + `WeaponId`, `IWeaponGate`/`HitGate` — цели, которые берёт не любое оружие). `ShotFx` больше не используется.
- `Enemies/` — `EnemyController` (движение, атаки, анимации, реакция на парирование; удар по игроку ждёт конца окна парирования), `EnemyFx` (трещины, осколки `DebrisChip` из пула, опыт, маркер тревоги).
- `Items/` — `Pickup` (предмет на земле, префаб `Resources/Items/Pickups/<Name>`; без модели — склянка из примитивов), `PickupVisual`, `ItemVisual` (цвет оружия + `SetBlink` — белая вспышка поверх), `Interactable`/`PlayerInteractor` (E / X).
- `Level/` — `LevelBuilder` (уровень из конфига: карта высот или воксельная карта через `VoxelLevelView`, спавн предметов/врагов/подсказок/дверей/костров, невидимые стены, `RespawnEnemies`), `Bonfire` (костёр), `Voxel/` (`VoxelLevelView`, `VoxelMesher`), `HintMarker` (подсказки на полу), `ShortcutDoor`, `Chest` (сундук: E / X, деформация при открытии, ресурс кусочками `Debris` летит к игроку), `CrateStack` (стопка ломаемых ящиков из досок: урон только мечом через `IWeaponGate`, доски отлетают, при разрушении разваливается), `DrawBridge` (разводной мост по референсу: створки из досок на шарнирах у берегов, П-образные рамы, красные шестерни, падение дальней створки с отскоком) + `BridgeChain` (две цепи — одна цель пуль и фиксации, рвутся только дробовиком).
- `UI/` — `Hud`, `InventoryView`, `SlotView`, `HudItemSlot`, `ItemIcons` (id → спрайт), `BonfireView`/`BonfireCell` (меню костра), `UiFactory`, `UiSprites`. Префабы UI: `Assets/Game/Resources/UI` (Canvas + CanvasScaler 1920×1080), иконки: `Assets/Game/Art/UI/Icons` (ресурсы — Metal/Rivets/Gears.png, генератор `Tools/Icons/make_resource_icons.py`). `SlotView` показывает число в стопке.
- `Camera/CameraRig.cs` — Cinemachine-камера.
- `Editor/` — `LaunchOptionsWindow` (окно «Параметры запуска»: карта, стартовое оружие, опыт, кнопка «Играть»), `LevelImporter` (импорт карт веб-редактора .json/.json.txt в `Configs/VoxelLevels`, меню ColorsAction/Уровни), `PrototypeSceneBuilder` (собирает сцену/префабы из кода), `UiPrefabBuilder` и `BonfireMenuPrefabBuilder` (меню ColorsAction/UI), `PrototypePlaytest` (автопрогон), `CratePlaytest` (автопрогон ящиков на VoxelLevels/Test), `BridgePlaytest` (автопрогон моста на VoxelLevels/BridgeTest, нужен дробовик), `PlayModeStepper`, `ModelImportSettings`, `LevelEditor/`.

## Данные и ассеты

- Конфиги: `Assets/StreamingAssets/Configs/*.json` (Player, Items (+ ресурсы kind `Resource`: metal, rivets, gears), Upgrades, Chests, Enemies, Combat, Camera, …), уровни — `Configs/VoxelLevels` (`BridgeTest` — коридор с мостом над пропастью для автопрогона), `Configs/Levels`.
- Префабы: `Assets/Game/Resources/Items` (оружие в руках), `Resources/Items/Pickups` (на земле), `Resources/UI`, `Assets/Game/Prefabs`. Сцена: `Assets/Game/Scenes/Prototype.unity`.
- Модели: `Assets/Game/Art/Models` (из `Tools/Blender/make_models.py`).

## Инструменты

- `Tools/ConfigEditor/ConfigEditor.html` — веб-редактор JSON-конфигов (таймлайны атак, цены апгрейдов, сундуки, ресурсы, мосты).
- `Tools/LevelEditorWeb/index.html` — веб-редактор уровней (сундук: ресурс и количество, подпись на значке; разводной мост с направлением) (GitHub Pages, `.github/workflows/level-editor-pages.yml`).
- `Tools/Dev/CompileCheck.ps1` — быстрая проверка компиляции Unity-кода через dotnet, без редактора (`-Root <worktree>`).
- `Tools/Blender`, `Tools/Level` — генерация моделей и тестовых уровней.
- Тесты логики: `dotnet/tests/ColorsAction.Tests`. Работа с редактором — через Unity CLI (`unity status / command / test / recompile`), не через MCP.
