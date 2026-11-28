"""
Спрайты нового интерфейса.

1. Нарезка атласа Tools/UI/ui_atlas_source.png (рамки полос, заливки, ромб, круг оружия, плашка опыта, кубики)
   в отдельные PNG Assets/Game/Art/UI/Atlas. У атласа по краям цветная кайма (артефакт генерации): цвет
   полупрозрачных пикселей заменяется цветом ближайшего непрозрачного, почти прозрачные пиксели обнуляются.
2. Колесо оружия, нарисованное по SDF в той же гамме (Assets/Game/Art/UI/Wheel): фон с рамкой, сектор (белый —
   тонируется кодом), обводка выбранного сектора, середина (заливка + рамка с орнаментом), ромбы на рамке.
   Размеры — в пикселях экрана 1920×1080 (спрайты рисуются 1:1).

Запуск: python Tools/UI/make_ui_sprites.py (нужны numpy, scipy, Pillow). Числа геометрии колеса — в WHEEL ниже,
их же повторяет WeaponWheel (константы с теми же именами).
"""
import json
import math
import os

import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Tools", "UI", "ui_atlas_source.png")
ATLAS_OUT = os.path.join(ROOT, "Assets", "Game", "Art", "UI", "Atlas")
WHEEL_OUT = os.path.join(ROOT, "Assets", "Game", "Art", "UI", "Wheel")
# Бордеры 9-slice и пивоты — для импорта в Unity (UiSpriteImporter читает их отсюда).
META_OUT = os.path.join(ROOT, "Tools", "UI")

# Цвета рамок атласа.
DARK = (98, 68, 100)
LIGHT = (250, 230, 228)
MAUVE = (166, 116, 151)
MAUVE_LOW = (140, 98, 134)
PANEL_IN = (51, 36, 63)
PANEL_OUT = (82, 60, 96)

# Куски атласа: имя → (x0, y0, x1, y1) в пикселях исходника; для 9-slice — бордеры (L, B, R, T) в borders.json.
CROPS = {
    "HealthFrame": (16, 13, 612, 100),
    "HealthFill": (17, 108, 588, 168),
    "StaminaFrame": (17, 175, 451, 228),
    "StaminaFill": (18, 235, 425, 267),
    "EnemyHealthFrame": (18, 277, 208, 321),
    "EnemyPoiseFrame": (226, 276, 370, 319),
    "EnemyHealthFill": (18, 326, 183, 348),
    "EnemyPoiseFill": (226, 327, 347, 346),
    "XpPlate": (18, 364, 412, 450),
    "Emblem": (633, 16, 779, 162),
    "Cubes": (597, 171, 795, 330),
    "WeaponCircle": (873, 13, 1153, 341),
}

# Бордеры 9-slice (L, B, R, T) в пикселях вырезанного куска: заострённые концы не тянутся.
BORDERS = {
    "HealthFrame": (52, 0, 52, 0),
    "HealthFill": (34, 0, 34, 0),
    "StaminaFrame": (34, 0, 34, 0),
    "StaminaFill": (20, 0, 20, 0),
    "EnemyHealthFrame": (24, 0, 24, 0),
    "EnemyPoiseFrame": (24, 0, 24, 0),
    "EnemyHealthFill": (10, 0, 10, 0),
    "EnemyPoiseFill": (10, 0, 10, 0),
    "XpPlate": (52, 0, 52, 0),
}


def defringe(rgba):
    """Цвет полупрозрачных пикселей — от ближайшего плотного; совсем прозрачные — в ноль."""
    a = rgba[..., 3].astype(np.float32)
    solid = a >= 235
    _, (iy, ix) = ndimage.distance_transform_edt(~solid, return_indices=True)
    out = rgba.copy()
    soft = ~solid
    out[soft, :3] = rgba[iy[soft], ix[soft], :3]
    out[a < 12] = 0
    out[a >= 235, 3] = 255
    return out


def whiten(rgba):
    """Светлая (почти белая) копия заливки с её объёмом: для «хвоста» урона и тонировки кодом."""
    rgb = rgba[..., :3].astype(np.float32)
    lum = rgb @ np.array([0.299, 0.587, 0.114], dtype=np.float32)
    mask = rgba[..., 3] > 128
    hi = np.percentile(lum[mask], 90) if mask.any() else 255.0
    v = np.clip(lum / max(hi, 1.0), 0, 1) * 0.12 + 0.88
    out = rgba.copy()
    out[..., :3] = (np.clip(v, 0, 1)[..., None] * 255).astype(np.uint8)
    return out


def save(img, folder, name):
    os.makedirs(folder, exist_ok=True)
    Image.fromarray(img, "RGBA").save(os.path.join(folder, name + ".png"))


def slice_atlas():
    src = np.array(Image.open(SRC).convert("RGBA"))
    src = defringe(src)
    for name, (x0, y0, x1, y1) in CROPS.items():
        crop = src[y0:y1, x0:x1].copy()
        # Соседние куски в рамку не попадают: оставляем только связные области, касающиеся центра куска.
        crop = keep_main(crop, name)
        save(crop, ATLAS_OUT, name)
        if name in ("HealthFill", "StaminaFill"):
            save(whiten(crop), ATLAS_OUT, name + "White")
    save(diamond_dot(18), ATLAS_OUT, "DotDiamond")
    with open(os.path.join(META_OUT, "borders.json"), "w", encoding="utf-8") as f:
        json.dump({k: list(v) for k, v in BORDERS.items()}, f, indent=2)


def keep_main(crop, name):
    mask = crop[..., 3] > 0
    lab, n = ndimage.label(ndimage.binary_dilation(mask, iterations=3))
    if n <= 1 or name == "Cubes":
        return crop
    sizes = ndimage.sum(mask, lab, range(1, n + 1))
    keep = np.argmax(sizes) + 1
    out = crop.copy()
    out[lab != keep] = 0
    return out


# ---------------------------------------------------------------------------------------------- рисование по SDF

SS = 3  # суперсэмплинг


def grid(w, h, cx, cy):
    """Координаты центров пикселей (y вверх) относительно (cx, cy) в пикселях итоговой картинки."""
    ys, xs = np.mgrid[0:h * SS, 0:w * SS].astype(np.float32)
    x = (xs + 0.5) / SS - cx
    y = cy - (ys + 0.5) / SS
    return x, y


def finish(rgb, alpha, w, h):
    """rgb (H,W,3) 0..1, alpha (H,W) 0..1 на суперсэмпле → картинка w×h (усреднение с учётом альфы)."""
    pa = rgb * alpha[..., None]
    pa = pa.reshape(h, SS, w, SS, 3).mean(axis=(1, 3))
    a = alpha.reshape(h, SS, w, SS).mean(axis=(1, 3))
    c = np.where(a[..., None] > 1e-4, pa / np.maximum(a[..., None], 1e-4), 0)
    out = np.zeros((h, w, 4), np.uint8)
    out[..., :3] = np.clip(c * 255, 0, 255).astype(np.uint8)
    out[..., 3] = np.clip(a * 255, 0, 255).astype(np.uint8)
    return defringe(out)


def col(c):
    return np.array(c, np.float32) / 255.0


def over(dst_rgb, dst_a, rgb, a):
    """Наложение слоя (rgb (3,) или (H,W,3), a (H,W)) поверх."""
    rgb = np.broadcast_to(rgb, dst_rgb.shape)
    out_a = a + dst_a * (1 - a)
    out_rgb = (rgb * a[..., None] + dst_rgb * (dst_a * (1 - a))[..., None]) / np.maximum(out_a[..., None], 1e-5)
    return out_rgb, out_a


def cover(d):
    """Покрытие по расстоянию (d < 0 — внутри), сглаживание ~1 px итоговой картинки."""
    return np.clip(0.5 - d, 0, 1)


def frame_bands(rgb, a, dist_from_outer, width_scale=1.0):
    """Рамка в стиле атласа поперёк: тёмный край, светлая линия, лиловая полоса, светлая линия, тёмный край."""
    s = width_scale
    bands = [(0, 3, DARK), (3, 6, LIGHT), (6, 15, MAUVE), (15, 18, (236, 208, 216)), (18, 21, (88, 60, 90))]
    for b0, b1, c in bands:
        lay = cover(b0 * s - dist_from_outer) * cover(dist_from_outer - b1 * s)
        cc = col(c)
        if c == MAUVE:
            t = np.clip((dist_from_outer - 6 * s) / (9 * s), 0, 1)[..., None]
            cc = col(MAUVE) * (1 - t) + col(MAUVE_LOW) * t
        rgb, a = over(rgb, a, cc, lay)
    return rgb, a


FRAME_W = 21


def diamond_dot(size):
    """Маленький светлый ромб с тёмной каймой (точки на плашках)."""
    x, y = grid(size, size, size / 2, size / 2)
    d = (np.abs(x) + np.abs(y)) / math.sqrt(2) - size * 0.34
    rgb = np.zeros(x.shape + (3,), np.float32)
    a = np.zeros(x.shape, np.float32)
    rgb, a = over(rgb, a, col(DARK), cover(d - 1.5))
    rgb, a = over(rgb, a, col(LIGHT), cover(d + 0.5))
    return finish(rgb, a, size, size)


def ornament_diamond(size):
    """Ромб на рамке колеса: тёмный край, светлая кайма, лиловое поле, внутри маленький тёмный ромб в светлой обводке."""
    x, y = grid(size, size, size / 2, size / 2)
    r = (np.abs(x) + np.abs(y)) / math.sqrt(2)
    half = size * 0.33
    rgb = np.zeros(x.shape + (3,), np.float32)
    a = np.zeros(x.shape, np.float32)
    rgb, a = over(rgb, a, col(DARK), cover(r - half))
    rgb, a = over(rgb, a, col(LIGHT), cover(r - (half - 2.5)))
    t = np.clip((y + half) / (2 * half), 0, 1)[..., None]
    rgb, a = over(rgb, a, col(MAUVE) * t + col(MAUVE_LOW) * (1 - t), cover(r - (half - 5)))
    rgb, a = over(rgb, a, col(LIGHT), cover(r - half * 0.42))
    rgb, a = over(rgb, a, col((112, 78, 118)), cover(r - (half * 0.42 - 2.5)))
    return finish(rgb, a, size, size)


def hollow_diamond(size, stroke=2.2):
    """Контур ромба светлой линией (орнамент середины колеса)."""
    x, y = grid(size, size, size / 2, size / 2)
    r = (np.abs(x) + np.abs(y)) / math.sqrt(2)
    half = size * 0.3
    d = np.abs(r - half) - stroke / 2
    rgb = np.zeros(x.shape + (3,), np.float32)
    a = np.zeros(x.shape, np.float32)
    rgb, a = over(rgb, a, col(LIGHT), cover(d))
    return finish(rgb, a, size, size)


# Геометрия колеса (пиксели экрана 1080p). Повторена в WeaponWheel.cs.
WHEEL = {
    "outer": 392,      # внешний край рамки
    "sector_in": 168,  # сектор: внутренний радиус
    "sector_out": 350,  # сектор: внешний радиус (с обводкой ×1.03 не заходит на рамку)
    "gap": 8,          # зазор между секторами
    "corner": 14,      # скругление углов сектора
    "hub": 156,        # внешний край рамки середины
    "sectors": 6,
}


def wheel_back():
    R = WHEEL["outer"]
    size = 2 * R + 8
    c = size / 2
    x, y = grid(size, size, c, c)
    r = np.sqrt(x * x + y * y)
    rgb = np.zeros(x.shape + (3,), np.float32)
    a = np.zeros(x.shape, np.float32)
    # Тёмный фон внутри рамки: светлее к краю.
    t = np.clip(r / R, 0, 1)[..., None]
    back = col(PANEL_IN) * (1 - t) + col(PANEL_OUT) * t
    rgb, a = over(rgb, a, back, cover(r - (R - 10)) * 0.94)
    rgb, a = frame_bands(rgb, a, R - r)
    return finish(rgb, a, size, size)


def sector_sdf(x, y, inner, outer, gap, corner, half_angle):
    """SDF сектора кольца с центром вверху (угол от +y по часовой), зазор gap, скруглённые углы."""
    ri, ro, g = inner + corner, outer - corner, gap / 2 + corner
    r = np.sqrt(x * x + y * y)
    d_in = ri - r
    d_out = r - ro
    # Боковые стороны: прямые через центр под углами ±half_angle, сдвинутые внутрь сектора на g.
    s, c = math.sin(half_angle), math.cos(half_angle)
    d_right = x * c - y * s + g    # правая сторона (по часовой)
    d_left = -x * c - y * s + g
    ds = np.stack([d_in, d_out, d_right, d_left])
    outside = np.sqrt(np.sum(np.maximum(ds, 0) ** 2, axis=0))
    inside = np.minimum(ds.max(axis=0), 0)
    return outside + inside - corner


def sector_box(pad):
    half = math.pi / WHEEL["sectors"]
    xo = WHEEL["sector_out"] * math.sin(half) + pad
    y0 = WHEEL["sector_in"] * math.cos(half) - pad
    y1 = WHEEL["sector_out"] + pad
    return xo, y0, y1


def wheel_sector(outline):
    """Сектор (белый с объёмом — тонируется кодом) или его обводка. Пивот (центр колеса) — в sprites.json."""
    pad = 12 if outline else 3
    xo, y0, y1 = sector_box(pad)
    w = int(math.ceil(2 * xo))
    h = int(math.ceil(y1 - y0))
    x, y = grid(w, h, w / 2, 0)
    y = y + y1  # y вверх от центра колеса
    half = math.pi / WHEEL["sectors"]
    d = sector_sdf(x, y, WHEEL["sector_in"], WHEEL["sector_out"], WHEEL["gap"], WHEEL["corner"], half)
    rgb = np.zeros(x.shape + (3,), np.float32)
    a = np.zeros(x.shape, np.float32)
    if outline:
        # Обводка снаружи сектора: тёмный край и кремовая полоса.
        rgb, a = over(rgb, a, col(DARK), cover(d - 9) * cover(-d - 1))
        rgb, a = over(rgb, a, col((255, 240, 236)), cover(d - 7) * cover(-d - 1))
    else:
        r = np.sqrt(x * x + y * y)
        t = np.clip((r - WHEEL["sector_in"]) / (WHEEL["sector_out"] - WHEEL["sector_in"]), 0, 1)
        body = (0.70 + 0.16 * t)[..., None] * np.ones(3, np.float32)
        rgb, a = over(rgb, a, body, cover(d))
        # Светлая кромка по краю сектора, как на макете.
        rim = cover(d) * cover(-d - 3.5)
        rgb, a = over(rgb, a, np.ones(3, np.float32), rim * 0.9)
        # Тёмная тонкая граница снаружи кромки.
        rgb, a = over(rgb, a, col((40, 30, 50)), cover(d - 1.2) * cover(-d) * 0.6)
    pivot = (0.5, (0 - (y0)) / (y1 - y0))
    return finish(rgb, a, w, h), pivot


def hub():
    R = WHEEL["hub"]
    size = 2 * R + 6
    c = size / 2
    x, y = grid(size, size, c, c)
    r = np.sqrt(x * x + y * y)
    # Заливка (белая с радиальным градиентом — тонируется кодом).
    rgb = np.zeros(x.shape + (3,), np.float32)
    a = np.zeros(x.shape, np.float32)
    t = np.clip(r / R, 0, 1)
    shade = (1.0 - 0.22 * t * t)[..., None] * np.ones(3, np.float32)
    rgb, a = over(rgb, a, shade, cover(r - (R - 16)))
    fill = finish(rgb, a, size, size)

    # Рамка: полосы атласа, сверху — контур ромба, снизу — разделитель «—◇—».
    rgb = np.zeros(x.shape + (3,), np.float32)
    a = np.zeros(x.shape, np.float32)
    rgb, a = frame_bands(rgb, a, R - r, 0.85)
    rd = (np.abs(x) + np.abs(y - 82)) / math.sqrt(2)
    rgb, a = over(rgb, a, col(LIGHT), cover(np.abs(rd - 7.5) - 1.2))
    # Разделитель: линия, сужающаяся к концам, ромб в середине.
    yl = y + 84
    lw = np.clip(1.6 * (1 - np.abs(x) / 70), 0, 1.6)
    line = cover(np.abs(yl) - lw) * (np.abs(x) < 70) * (np.abs(x) > 9)
    rgb, a = over(rgb, a, col(LIGHT), line.astype(np.float32))
    rd2 = (np.abs(x) + np.abs(yl)) / math.sqrt(2)
    rgb, a = over(rgb, a, col(LIGHT), cover(np.abs(rd2 - 4.2) - 1.1))
    frame = finish(rgb, a, size, size)
    return fill, frame


def make_wheel():
    os.makedirs(WHEEL_OUT, exist_ok=True)
    save(wheel_back(), WHEEL_OUT, "WheelBack")
    sector, pivot = wheel_sector(False)
    save(sector, WHEEL_OUT, "WheelSector")
    outline, pivot_o = wheel_sector(True)
    save(outline, WHEEL_OUT, "WheelSectorOutline")
    fill, frame = hub()
    save(fill, WHEEL_OUT, "WheelHubFill")
    save(frame, WHEEL_OUT, "WheelHubFrame")
    save(ornament_diamond(56), WHEEL_OUT, "WheelDiamond")
    save(ornament_diamond(34), WHEEL_OUT, "WheelDiamondSmall")
    with open(os.path.join(META_OUT, "wheel.json"), "w", encoding="utf-8") as f:
        json.dump({"WheelSector": pivot, "WheelSectorOutline": pivot_o, "geometry": WHEEL}, f, indent=2)


# ---------------------------------------------------------------------------------------------- атлас 2 (инвентарь)

SRC2 = os.path.join(ROOT, "Tools", "UI", "ui_atlas2_source.png")

# Куски второго атласа (RGB на чёрном фоне): имя → (x0, y0, x1, y1).
CROPS2 = {
    "Panel": (33, 44, 641, 498),
    "PortraitFrame": (37, 518, 385, 872),
    "TitlePlate": (413, 545, 898, 637),
    "CaptionPlate": (419, 653, 822, 716),
    "Slot": (996, 537, 1140, 682),
    "LongPlate": (21, 895, 1233, 986),
    "SlotCursor": (508, 1029, 690, 1185),
    "DiamondLarge": (62, 1049, 176, 1163),
    "DiamondMedium": (201, 1066, 279, 1145),
    "DiamondSmall": (308, 1078, 364, 1134),
    "DiamondTiny": (403, 1084, 447, 1127),
    "Divider": (747, 1050, 1194, 1098),
    "DividerShort": (842, 1116, 1082, 1154),
}

BORDERS2 = {
    "Panel": (96, 96, 96, 96),
    "TitlePlate": (60, 0, 60, 0),
    "CaptionPlate": (44, 0, 44, 0),
    "LongPlate": (56, 0, 56, 0),
    "Slot": (34, 34, 34, 34),
    "SlotCursor": (64, 60, 64, 60),
}


def alpha_from_black(rgb):
    """
    Атлас без альфы, детали на чёрном. Фон — чёрное, связанное с краем картинки; плотные пиксели — внутри деталей;
    на кромке (и в свечении) альфа = яркость пикселя / яркость ближайшего плотного, цвет — от плотного.
    """
    rgb = rgb.astype(np.float32)
    lum = rgb.max(axis=2)
    dark = lum < 14
    lab, _ = ndimage.label(dark)
    border = set(np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))) - {0}
    background = np.isin(lab, list(border))
    obj = ~background
    solid = ndimage.binary_erosion(obj, iterations=2)
    _, (iy, ix) = ndimage.distance_transform_edt(~solid, return_indices=True)
    near = rgb[iy, ix]
    near_lum = np.maximum(near.max(axis=2), 1.0)
    alpha = np.clip(lum / near_lum, 0, 1)
    alpha[solid] = 1.0
    # Фон и свечение далеко от детали: альфа по яркости (мягкое свечение разделителей), цвет — ближайший.
    alpha[background] = np.clip((lum[background] - 6) / 120.0, 0, 1)
    out = np.zeros(rgb.shape[:2] + (4,), np.uint8)
    out[..., :3] = np.clip(np.where(alpha[..., None] < 1, near, rgb), 0, 255).astype(np.uint8)
    out[..., 3] = (alpha * 255).astype(np.uint8)
    out[alpha < 0.03] = 0
    return out


def erase_center_diamond(panel, top, bottom):
    """С 9-slice панели убрать ромбы посередине верхней и нижней кромки: кромку в этих столбцах копируем с соседних."""
    h, w = panel.shape[:2]
    cx = w // 2
    src = cx - 90
    out = panel.copy()
    for y0, y1 in (top, bottom):
        for x in range(cx - 40, cx + 41):
            out[y0:y1, x] = panel[y0:y1, src]
    return out


def slice_atlas2():
    src = np.array(Image.open(SRC2).convert("RGB"))
    rgba = alpha_from_black(src)
    for name, (x0, y0, x1, y1) in CROPS2.items():
        crop = rgba[y0:y1, x0:x1].copy()
        if name == "Panel":
            crop = erase_center_diamond(crop, (0, 60), (crop.shape[0] - 60, crop.shape[0]))
        save(crop, ATLAS_OUT, name)
    with open(os.path.join(META_OUT, "borders.json"), "r", encoding="utf-8") as f:
        borders = json.load(f)
    borders.update({k: list(v) for k, v in BORDERS2.items()})
    with open(os.path.join(META_OUT, "borders.json"), "w", encoding="utf-8") as f:
        json.dump(borders, f, indent=2)


if __name__ == "__main__":
    slice_atlas()
    make_wheel()
    slice_atlas2()
    print("ok")
