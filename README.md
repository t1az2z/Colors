# ColorsAction — прототип

Unity 6000.6.8f1, URP, PC. Кодовая база — шаблон `D:\Projects\ProjectTemplate` (ядро скопировано в `Packages/`, сам шаблон не менялся).

## Запуск

Откройте сцену `Assets/Game/Scenes/Prototype.unity` и нажмите Play.
Сцена собирается кодом: меню **ColorsAction → Build Prototype Scene** пересоздаёт материалы, префабы и сцену
(ручные правки в Prototype.unity при этом теряются — правьте конфиги или `PrototypeSceneBuilder`).

## Управление

| Действие | Клавиатура + мышь | Геймпад |
|---|---|---|
| Ходьба / бег | WASD / LShift | левый стик / L3 |
| Прыжок | Пробел | A |
| Перекат | Alt | B |
| Правая рука (атака) | ЛКМ | RT |
| Левая рука (атака, блок щитом) | ПКМ | LT |
| Подобрать | F | X |
| Инвентарь | TAB | View |
| Камера, схема FreeOrbit | мышь (курсор спрятан), колесо — зум | правый стик |
| Камера, схема FixedAngles | Q / E — поворот на 90°, мышь — взгляд | LB / RB, правый стик — взгляд |

В инвентаре: перетаскивание мышью, ПКМ или двойной щелчок — надеть/снять; на геймпаде D-pad/стик — выбор, A — надеть/снять, B — закрыть.
Слоты, куда можно надеть выбранное оружие, подсвечиваются зелёным.

## Конфиги

Все числа — в `Assets/StreamingAssets/Configs/*.json` (читаются `ConfigService` шаблона):

- `Player.json` — скорость (`walkSpeed` 6.4, `runMultiplier` 2, разгон и торможение), прыжок (`jumpHeight` 1.5), перекат (`rollDistance` 4, пауза "на голове" `rollMidPause`, окно неуязвимости), шаг ступени (`stepHeight` 0.3), выносливость, пружины деформации.
- `Camera.json` — наклон, FOV, дистанция, `scheme`: `FreeOrbit` или `FixedAngles`.
- `Items.json` — оружие: цвет, тип и рука (`hand`: меч — Right, щит — Left), урон, серии ударов (`combo`), расход выносливости.
- `Combat.json` — бонус цвета, хит-стоп, тряска камеры. `Dummy.json` — манекен. `Colors.json` — палитра.
- `Levels/Test.json` — карта высот 64×64 (символ = высота в шагах по 0.25). Перепады под прыжок 1.5: 0.25 — шагом, 1.0 — прыжком, 2.0 — непреодолимо. Генератор: `Tools/Level/make_test_level.py`.

## Структура

```
Packages/com.template.gamecore(.unity)  ядро шаблона (без изменений)
Packages/com.colorsaction.game           логика игры без движка: конфиги, инвентарь, выносливость, урон, состояния FSM, модель экрана инвентаря
Assets/Game/Scripts                      адаптер Unity: бутстрап, ввод, контроллер куба, бой, камера, уровень, HUD, инвентарь
Assets/Game/Scripts/Editor               сборщик сцены, импорт моделей, автопрогон (ColorsAction → Debug)
Assets/Game/Art/Models                   FBX из Blender
Tools/Blender/make_models.py             модели игрока, меча, щита, манекена (источник правды для мешей)
dotnet/                                  тесты логики: dotnet test dotnet/ColorsAction.sln
```

Модели пересобираются так:

```
"C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" -b --factory-startup --python Tools/Blender/make_models.py
```

## Проверка

- `dotnet test dotnet/ColorsAction.sln` — инвентарь, выносливость, конфиги, переходы состояний.
- В Play Mode: **ColorsAction → Debug → Run Prototype Playtest** — автопрогон через скриптовый ввод
  (ступени 0.25/1.0/2.0, высота прыжка, перекат, подбор, инвентарь, удары по манекену, блок, вторая схема камеры).
  Результат — в консоли.
