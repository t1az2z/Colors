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

- `ColorsActionInstaller.cs` — регистрация сервисов в DI. `GameSession.cs` — состояние игры: инвентарь, выносливость, здоровье, опыт, открытые срезки, `CompletedHints`, `LastBonfire`, `Upgrades`, схема управления.
- `Configs/` — классы JSON-конфигов: `Configs.cs` (Game, Player + выносливость/деформация, Camera, Colors, Items + `AttackStepConfig` комбо с `fxFrom/fxTo`, `upgrade`, `hop`, `charge`, Combat, Inventory), `DodgeConfig.cs` (серия отскоков), `WeaponMoveConfigs.cs` (`ChargeConfig`, `FinisherConfig`, `PlungeConfig`), `UpgradeConfigs.cs` (Upgrades.json: цены апгрейдов), `EnemyConfigs.cs`, `LevelConfig.cs` (+ `bonfires`, подсказки с `target`).
- `Combat/` — `DamageRules` (урон и цвет), `Health`/`PlayerVitals`, `Stamina`, `EnemyBrain` (решения врага), `CombatDirector` (очередь врагов на атаку), `LockOnRules` (+ `Nearest` — передача фокуса), `Shotgun` (`ShotParams` — параметры залпа, `ShotPattern`, `PelletVolley`, `GunTrigger`), `DodgeCombo` (серия отскоков), `AttackCharge` (общий заряд удержанием), `ComboFinisher` + `ComboSteps` (отложенный добивающий, удары за апгрейдами), `PlungeAttack` (удар в прыжке), `ShieldBash`.
- `Inventory/` — `PlayerInventory` (сетка, руки, быстрые слоты), `ItemDatabase`.
- `Progression/` — `WeaponUpgrades` (уровни апгрейдов 0..3, `Has(itemId, tier)`; отладка `debugWeaponUpgrades` в Game.json), `WeaponUpgradeShop` (покупка за опыт), `Bonfire` (события отдыха/респауна).
- `Level/` — воксельная модель (`VoxelLevel`, `VoxelLevelData`, `VoxelTypes`), правки и история (`LevelEditing`), `SurfaceSmoothing`, `ShortcutDoorRules`, `HintTargets` (подсказки гаснут после подбора/двери), `LevelEdgeWalls`, `Navigation/` (граф и A* для врагов).
- `States/States.cs` — FSM: Boot → Play ↔ Inventory / Bonfire. `UI/` — `InventoryScreenViewModel`, `BonfireScreenViewModel`.

## Unity (`Assets/Game/Scripts`)

- `Core/` — `GameBootstrap` (точка входа сцены, хост ядра, создаёт HUD/инвентарь/EventSystem из префабов), `GameInput` (действия клавиатура/геймпад), `Palette`, `ComponentPool` (пулинг).
- `Player/` — `PlayerController` (ходьба, бег, прыжок, серия отскоков, хуки удара в прыжке), `BoxMotor` (кинематика бокса), `CubeDeformer` (деформация куба), `PlayerDamageReceiver` (урон, блок, i-frames, смерть/респаун), `PlayerLockOn` (фиксация цели), `AimMarker`.
- `Combat/` — `PlayerCombat` (partial: `.cs` — атаки обеих рук, комбо, позы; `.Moves.cs` — заряд, добивающий, удар в прыжке, окно эффекта; `.Shotgun.cs` — выстрел пулями, заряд, позы дробовика; `.Shield.cs` — блок, парирование, удар щитом), `Pellet` (пуля из пула), `WeaponGlow`, `ShockRingFx`, `WeaponTrail`, `ParryFlash`, `TrainingDummy`, `Hittable` (`IHittable`, `HitInfo`). `ShotFx` больше не используется.
- `Enemies/` — `EnemyController` (движение, атаки, анимации, реакция на парирование), `EnemyFx` (трещины, осколки `DebrisChip` из пула, опыт, маркер тревоги).
- `Items/` — `Pickup` (предмет на земле, префаб `Resources/Items/Pickups/<Name>`), `PickupVisual`, `ItemVisual`, `Interactable`/`PlayerInteractor` (E / X).
- `Level/` — `LevelBuilder` (уровень из конфига, спавн предметов/врагов/подсказок/дверей/костров, невидимые стены, `RespawnEnemies`), `Bonfire` (костёр), `Voxel/` (`VoxelLevelView`, `VoxelMesher`), `HintMarker` (подсказки на полу), `ShortcutDoor`.
- `UI/` — `Hud`, `InventoryView`, `SlotView`, `HudItemSlot`, `ItemIcons` (id → спрайт), `BonfireView`/`BonfireCell` (меню костра), `UiFactory`, `UiSprites`. Префабы UI: `Assets/Game/Resources/UI` (Canvas + CanvasScaler 1920×1080), иконки: `Assets/Game/Art/UI/Icons`.
- `Camera/CameraRig.cs` — Cinemachine-камера.
- `Editor/` — `PrototypeSceneBuilder` (собирает сцену/префабы из кода), `UiPrefabBuilder` и `BonfireMenuPrefabBuilder` (меню ColorsAction/UI), `PrototypePlaytest` (автопрогон), `PlayModeStepper`, `ModelImportSettings`, `LevelEditor/`.

## Данные и ассеты

- Конфиги: `Assets/StreamingAssets/Configs/*.json` (Player, Items, Enemies, Combat, Camera, …), уровни — `Configs/VoxelLevels`, `Configs/Levels`.
- Префабы: `Assets/Game/Resources/Items` (оружие в руках), `Resources/Items/Pickups` (на земле), `Resources/UI`, `Assets/Game/Prefabs`. Сцена: `Assets/Game/Scenes/Prototype.unity`.
- Модели: `Assets/Game/Art/Models` (из `Tools/Blender/make_models.py`).

## Инструменты

- `Tools/ConfigEditor/ConfigEditor.html` — веб-редактор JSON-конфигов (таймлайны атак).
- `Tools/LevelEditorWeb/index.html` — веб-редактор уровней (GitHub Pages, `.github/workflows/level-editor-pages.yml`).
- `Tools/Dev/CompileCheck.ps1` — быстрая проверка компиляции Unity-кода через dotnet, без редактора (`-Root <worktree>`).
- `Tools/Blender`, `Tools/Level` — генерация моделей и тестовых уровней.
- Тесты логики: `dotnet/tests/ColorsAction.Tests`. Работа с редактором — через Unity CLI (`unity status / command / test / recompile`), не через MCP.
