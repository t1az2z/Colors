# ColorsAction

Изометрическая action-RPG с элементами метроидвании: мир из белых кубов 1×1, персонаж и враги — деформирующиеся кубы, а цветное оружие наносит бонусный урон врагам своего цвета, и открывает проходы того же цвета. Подробный дизайн — в [GDD.html](GDD.html).

- Движок: Unity 6000.6.8f1, URP, PC (клавиатура+мышь и геймпад).
- Запуск: сцена `Assets/Game/Scenes/Prototype.unity` → Play. Камера — 4 фиксированных угла: поворот на 90° на Q/E (геймпад: LB — вправо, RB — влево), взгляд и атака — курсор мыши по земле / правый стик. Схемы со свободной камерой и переключения F1 больше нет.
- Параметры игры — JSON в `Assets/StreamingAssets/Configs`; править их удобно в [Tools/ConfigEditor/ConfigEditor.html](Tools/ConfigEditor/ConfigEditor.html) (открыть в Chrome или Edge).
- Уровни рисуются только в веб-редакторе (на планшете или в любом браузере): [Tools/LevelEditorWeb](Tools/LevelEditorWeb/index.html), опубликован на GitHub Pages (https://t1az2z.github.io/Colors/). Карта — JSON в `Assets/StreamingAssets/Configs/VoxelLevels`; в Chrome/Edge «Сохранить» перезаписывает открытый файл.
- Тесты логики: `dotnet test dotnet/ColorsAction.sln`.
