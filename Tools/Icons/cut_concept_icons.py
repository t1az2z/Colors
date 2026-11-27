"""Иконки инвентаря из концепта Art/Concepts/Items.png: каждый предмет вырезается без подписи, фон (светлый
градиент и мягкая тень под предметом) становится прозрачным, результат — PNG 256×256 в Assets/Game/Art/UI/Icons.

Запуск из корня проекта: python Tools/Icons/cut_concept_icons.py [папка_для_вывода]
Нужен только Pillow. Перезаписывает Sword/Shield/Shotgun/Key/Flask/Metal/Gears/Rivets.png (.meta остаются,
ссылки в ItemIcons.asset не ломаются). Прежние иконки из прямоугольников рисовал make_resource_icons.py и
UiPrefabBuilder — их не запускать поверх этих.
"""
import os
import sys
from collections import deque

from PIL import Image, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONCEPT = os.path.join(ROOT, "Art", "Concepts", "Items.png")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "Game", "Art", "UI", "Icons")
SIZE = 256
MARGIN = 10

# Рамки предметов на концепте 1055×1491 (без подписей).
BOXES = {
    "Sword": (55, 110, 265, 402),
    "Shield": (292, 138, 508, 402),
    "Shotgun": (522, 150, 828, 388),
    "Key": (82, 478, 322, 702),
    "Flask": (422, 452, 652, 702),
    "Metal": (722, 488, 1008, 702),
    "Gears": (92, 758, 338, 968),
    "Rivets": (402, 772, 642, 968),
    "Club": (822, 122, 1032, 402),
    "Chest": (708, 758, 1002, 976),
}
# Только эти идут в игру (дубинка и сундук — не предметы инвентаря, их иконки кладутся рядом с концептом).
INVENTORY = {"Sword", "Shield", "Shotgun", "Key", "Flask", "Metal", "Gears", "Rivets"}

EDGE = 20         # перепад к соседям, на котором заливка фона останавливается (край предмета)
HOLE_DIST = 26    # замкнутые области цвета фона — тоже прозрачные, но только у предметов с дыркой
HOLES = {"Key", "Gears"}
# Срезание теневой каймы у края предмета: (до какой яркости тени срезать, сколько пикселей вглубь).
# У шестерёнок и заклёпок свой цвет похож на тень — им срезаем осторожно.
PEEL = {"Gears": (175, 6), "Rivets": (175, 6)}
PEEL_DEFAULT = (145, 30)
EDGE_FOR = {"Rivets": 12}


def shadow_like(c):
    """Фон концепта и тени под предметами: светлые, слабо окрашенные, розово-сиреневые (зелёный канал наименьший,
    синий не больше красного). Сиреневый металл (синий заметно больше красного), золото, дерево, кожа — нет."""
    r, g, b = c
    lo, hi = min(r, g, b), max(r, g, b)
    if lo > 195 and hi - lo < 40 and b < g:
        return True  # тёплый отсвет флакона на полу
    return lo > 120 and hi - lo < 55 and b - r < 10 and r - g > 12 and g <= min(r, b) + 3


def dist(a, b):
    return ((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2) ** 0.5


def cut(img, holes, edge=EDGE, peel=PEEL_DEFAULT):
    w, h = img.size
    px = img.load()
    border = [px[x, 0] for x in range(w)] + [px[x, h - 1] for x in range(w)] + \
             [px[0, y] for y in range(h)] + [px[w - 1, y] for y in range(h)]
    bg = tuple(sorted(c[i] for c in border)[len(border) // 2] for i in range(3))

    # Фон по строкам: градиент концепта идёт сверху вниз, берём края строки.
    row_bg = []
    for y in range(h):
        l, r = px[0, y], px[w - 1, y]
        row_bg.append(tuple((l[i] + r[i]) / 2 for i in range(3)))

    def grad(x, y):
        c = px[x, y]
        g = 0.0
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h:
                g = max(g, dist(c, px[nx, ny]))
        return g

    passable = [[shadow_like(px[x, y]) and grad(x, y) < edge for x in range(w)] for y in range(h)]
    background = [[False] * w for _ in range(h)]
    queue = deque()
    for x in range(w):
        for y in (0, h - 1):
            if passable[y][x] and not background[y][x]:
                background[y][x] = True
                queue.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if passable[y][x] and not background[y][x]:
                background[y][x] = True
                queue.append((x, y))
    while queue:
        x, y = queue.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h and not background[ny][nx] and passable[ny][nx]:
                background[ny][nx] = True
                queue.append((nx, ny))

    # Замкнутые «окна» фона внутри предмета.
    seen = [[False] * w for _ in range(h)]
    for y in range(h if holes else 0):
        for x in range(w):
            if background[y][x] or seen[y][x] or dist(px[x, y], row_bg[y]) >= HOLE_DIST                     or not shadow_like(px[x, y]):
                continue
            comp, q = [], deque([(x, y)])
            seen[y][x] = True
            while q:
                cx, cy = q.popleft()
                comp.append((cx, cy))
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    nx, ny = cx + dx, cy + dy
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and not background[ny][nx] \
                            and dist(px[nx, ny], row_bg[ny]) < HOLE_DIST and shadow_like(px[nx, ny]):
                        seen[ny][nx] = True
                        q.append((nx, ny))
            if len(comp) > 60:
                for cx, cy in comp:
                    background[cy][cx] = True

    # Остатки тени: оторванные пятна из «теневых» пикселей и светлая теневая кайма у края предмета.
    def neighbours(x, y):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h:
                yield nx, ny

    peel_min, peel_steps = peel
    for _ in range(peel_steps):
        ring = [(x, y) for y in range(h) for x in range(w)
                if not background[y][x] and shadow_like(px[x, y]) and min(px[x, y]) > peel_min
                and sum(px[x, y]) < sum(row_bg[y]) - 15  # тень темнее фона, блики светлее
                and any(background[ny][nx] for nx, ny in neighbours(x, y))]
        if not ring:
            break
        for x, y in ring:
            background[y][x] = True
    seen = [[False] * w for _ in range(h)]
    comps = []
    for y in range(h):
        for x in range(w):
            if background[y][x] or seen[y][x]:
                continue
            comp, q = [], deque([(x, y)])
            seen[y][x] = True
            while q:
                cx, cy = q.popleft()
                comp.append((cx, cy))
                for nx, ny in neighbours(cx, cy):
                    if not seen[ny][nx] and not background[ny][nx]:
                        seen[ny][nx] = True
                        q.append((nx, ny))
            comps.append(comp)
    largest = max((len(c) for c in comps), default=0)
    for comp in comps:
        shadowy = sum(1 for cx, cy in comp if shadow_like(px[cx, cy]))
        if len(comp) < 0.2 * largest or shadowy > 0.5 * len(comp):
            for cx, cy in comp:
                background[cy][cx] = True

    mask = Image.new("L", (w, h), 0)
    mp = mask.load()
    for y in range(h):
        for x in range(w):
            mp[x, y] = 0 if background[y][x] else 255
    # Убрать крошки, чуть сжать край (там смесь с фоном) и смягчить его.
    mask = mask.filter(ImageFilter.MedianFilter(3)).filter(ImageFilter.MinFilter(3))
    mask = mask.filter(ImageFilter.GaussianBlur(0.8))
    out = img.convert("RGBA")
    out.putalpha(mask)
    return out


def fit(icon):
    box = icon.getchannel("A").point(lambda a: 255 if a > 8 else 0).getbbox()
    icon = icon.crop(box)
    k = (SIZE - 2 * MARGIN) / max(icon.size)
    icon = icon.resize((max(1, round(icon.width * k)), max(1, round(icon.height * k))), Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.paste(icon, ((SIZE - icon.width) // 2, (SIZE - icon.height) // 2), icon)
    return canvas


def main():
    concept = Image.open(CONCEPT).convert("RGB")
    extra = os.path.join(ROOT, "Art", "Concepts", "Icons")
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(extra, exist_ok=True)
    for name, box in BOXES.items():
        icon = fit(cut(concept.crop(box), name in HOLES, EDGE_FOR.get(name, EDGE), PEEL.get(name, PEEL_DEFAULT)))
        path = os.path.join(OUT if name in INVENTORY else extra, name + ".png")
        icon.save(path)
        print("icon", path)


if __name__ == "__main__":
    main()
