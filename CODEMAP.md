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

- `ColorsActionInstaller.cs` — регистрация сервисов в DI. `GameSession.cs` — состояние игры: инвентарь, выносливость, здоровье, опыт, открытые срезки, `Upgrades`, схема управления.
- `Configs/` — классы JSON-конфигов: `Configs.cs` (Game, Player + выносливость/деформация, Camera, Colors, Items + `AttackStepConfig` комбо, Combat, Inventory), `EnemyConfigs.cs`, `LevelConfig.cs`.
- `Combat/` — `DamageRules` (урон и цвет), `Health`/`PlayerVitals`, `Stamina`, `EnemyBrain` (решения врага), `CombatDirector` (очередь врагов на атаку), `LockOnRules`, `Shotgun` (`ShotPattern`, `GunTrigger`).
- `Inventory/` — `PlayerInventory` (сетка, руки, быстрые слоты), `ItemDatabase`.
- `Progression/WeaponUpgrades.cs` — уровни апгрейдов оружия (0..3), `Has(itemId, tier)`. Отладка: `debugWeaponUpgrades` в Game.json.
- `Level/` — воксельная модель (`VoxelLevel`, `VoxelLevelData`, `VoxelTypes`), правки и история (`LevelEditing`), `SurfaceSmoothing`, `ShortcutDoorRules`, `Navigation/` (граф и A* для врагов).
- `States/States.cs` — FSM: Boot → Play ↔ Inventory. `UI/InventoryScreenViewModel.cs` — модель экрана инвентаря.

## Unity (`Assets/Game/Scripts`)

- `Core/` — `GameBootstrap` (точка входа сцены, хост ядра, загрузка уровня), `GameInput` (действия клавиатура/геймпад), `Palette`.
- `Player/` — `PlayerController` (ходьба, бег, прыжок, перекат), `BoxMotor` (кинематика бокса), `CubeDeformer` (деформация куба), `PlayerDamageReceiver` (урон, блок, i-frames, смерть/респаун), `PlayerLockOn` (фиксация цели), `AimMarker`.
- `Combat/` — `PlayerCombat` (атаки обеих рук, комбо, блок/парирование, дробовик, позы оружия в руках), `WeaponTrail`, `ShotFx`, `ParryFlash`, `TrainingDummy`, `Hittable` (`IHittable`, `HitInfo`).
- `Enemies/` — `EnemyController` (движение, атаки, анимации врага), `EnemyFx` (трещины, осколки, опыт, маркер тревоги).
- `Items/` — `Pickup` (предмет на земле), `ItemVisual`, `Interactable`/`PlayerInteractor` (E / X).
- `Level/` — `LevelBuilder` (уровень из конфига, спавн предметов/врагов/подсказок/дверей), `Voxel/` (`VoxelLevelView`, `VoxelMesher`), `HintMarker` (подсказки на полу), `ShortcutDoor`.
- `UI/` — `Hud`, `InventoryView`, `UiFactory`, `UiSprites`.
- `Camera/CameraRig.cs` — Cinemachine-камера.
- `Editor/` — `PrototypeSceneBuilder` (собирает сцену/префабы из кода), `PrototypePlaytest` (автопрогон), `PlayModeStepper`, `ModelImportSettings`, `LevelEditor/`.

## Данные и ассеты

- Конфиги: `Assets/StreamingAssets/Configs/*.json` (Player, Items, Enemies, Combat, Camera, …), уровни — `Configs/VoxelLevels`, `Configs/Levels`.
- Префабы: `Assets/Game/Resources/Items` (оружие), `Assets/Game/Prefabs`. Сцена: `Assets/Game/Scenes/Prototype.unity`.
- Модели: `Assets/Game/Art/Models` (из `Tools/Blender/make_models.py`).

## Инструменты

- `Tools/ConfigEditor/ConfigEditor.html` — веб-редактор JSON-конфигов (таймлайны атак).
- `Tools/LevelEditorWeb/index.html` — веб-редактор уровней (GitHub Pages, `.github/workflows/level-editor-pages.yml`).
- `Tools/Dev/CompileCheck.ps1` — быстрая проверка компиляции Unity-кода через dotnet, без редактора (`-Root <worktree>`).
- `Tools/Blender`, `Tools/Level` — генерация моделей и тестовых уровней.
- Тесты логики: `dotnet/tests/ColorsAction.Tests`. Работа с редактором — через Unity CLI (`unity status / command / test / recompile`), не через MCP.
