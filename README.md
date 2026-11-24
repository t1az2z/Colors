# ColorsAction

Изометрическая action-RPG с элементами метроидвании: мир из белых кубов 1×1, персонаж и враги — деформирующиеся кубы, а цветное оружие наносит бонусный урон врагам своего цвета, и открывает проходы того же цвета. Подробный дизайн — в [GDD.html](GDD.html).

- Движок: Unity 6000.6.8f1, URP, PC (клавиатура+мышь и геймпад).
- Запуск: сцена `Assets/Game/Scenes/Prototype.unity` → Play. Схемы управления из GDD: А — свободная камера, Б — 4 угла камеры (Q/E, LB/RB) и взгляд мышью/правым стиком; F1 / Start переключает их прямо в игре, стартовая — `scheme` в Camera.json.
- Параметры игры — JSON в `Assets/StreamingAssets/Configs`; править их удобно в [Tools/ConfigEditor/ConfigEditor.html](Tools/ConfigEditor/ConfigEditor.html) (открыть в Chrome или Edge).
- Уровни можно рисовать на планшете или в любом браузере: [Tools/LevelEditorWeb](Tools/LevelEditorWeb/index.html), опубликован на GitHub Pages (https://t1az2z.github.io/Colors/). Сохраняет тот же JSON, что и Unity-редактор: скачанный файл кладётся в `Assets/StreamingAssets/Configs/VoxelLevels`.
- Тесты логики: `dotnet test dotnet/ColorsAction.sln`.
