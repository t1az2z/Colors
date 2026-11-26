"""Иконки ресурсов 256x256 (металл, заклёпки, шестерёнки) в стиле Art/UI/Icons: плоский цвет + тёмный контур.
Запуск из корня проекта: python Tools/Icons/make_resource_icons.py  (перезапишет Metal/Rivets/Gears.png).
Иконки можно и просто перерисовать в любом редакторе: важно только имя файла (и .meta рядом)."""
import math, struct, sys, zlib

SIZE = 256
SS = 3  # суперсэмплинг
DARK = (41, 41, 46)
OUT = 9.0  # толщина контура, px


def hexc(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c)


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)
    t = max(0.0, min(1.0, t))
    qx, qy = ax + dx * t, ay + dy * t
    return math.hypot(px - qx, py - qy)


def in_poly(px, py, pts):
    inside = False
    n = len(pts)
    for i in range(n):
        ax, ay = pts[i]
        bx, by = pts[(i + 1) % n]
        if (ay > py) != (by > py) and px < (bx - ax) * (py - ay) / (by - ay) + ax:
            inside = not inside
    return inside


def poly_sd(px, py, pts):
    """Знаковое расстояние до многоугольника: < 0 внутри."""
    n = len(pts)
    d = min(seg_dist(px, py, *pts[i], *pts[(i + 1) % n]) for i in range(n))
    return -d if in_poly(px, py, pts) else d


def circle_sd(px, py, cx, cy, r):
    return math.hypot(px - cx, py - cy) - r


class Canvas:
    def __init__(self):
        self.layers = []  # (sd(x,y), color)

    def add(self, sd, color, outline=True):
        if outline:
            self.layers.append((lambda x, y, sd=sd: sd(x, y) - OUT, DARK))
        self.layers.append((sd, color))

    def render(self):
        rows = []
        for y in range(SIZE):
            row = bytearray()
            for x in range(SIZE):
                acc = [0.0, 0.0, 0.0, 0.0]
                for sy in range(SS):
                    for sx in range(SS):
                        px = x + (sx + 0.5) / SS
                        py = y + (sy + 0.5) / SS
                        col = None
                        for sd, c in self.layers:
                            if sd(px, py) <= 0:
                                col = c
                        if col is not None:
                            acc[0] += col[0]; acc[1] += col[1]; acc[2] += col[2]; acc[3] += 1
                n = SS * SS
                a = acc[3] / n
                if acc[3] > 0:
                    row += bytes((int(acc[0] / acc[3]), int(acc[1] / acc[3]), int(acc[2] / acc[3]), int(round(a * 255))))
                else:
                    row += bytes((0, 0, 0, 0))
            rows.append(bytes(row))
        return rows


def write_png(path, rows):
    raw = b''.join(b'\x00' + r for r in rows)

    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)

    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', SIZE, SIZE, 8, 6, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(png)


def ingot(c, cx, cy, w, h, color):
    """Слиток: трапеция (верх уже низа) + светлая верхняя грань."""
    top = w * 0.62
    pts = [(cx - w / 2, cy + h / 2), (cx + w / 2, cy + h / 2), (cx + top / 2, cy - h / 2), (cx - top / 2, cy - h / 2)]
    c.add(lambda x, y: poly_sd(x, y, pts), color)
    face = [(cx - top / 2 + 10, cy - h / 2 + 9), (cx + top / 2 - 10, cy - h / 2 + 9), (cx + top / 2 - 4, cy - h / 2 + 22), (cx - top / 2 + 4, cy - h / 2 + 22)]
    c.add(lambda x, y: poly_sd(x, y, face), shade(color, 1.35), outline=False)


def metal(color):
    c = Canvas()
    ingot(c, 86, 176, 120, 52, color)
    ingot(c, 170, 176, 120, 52, color)
    ingot(c, 128, 118, 120, 52, color)
    return c


def rivet(c, cx, cy, color):
    """Заклёпка: стержень и полукруглая шляпка."""
    shaft = [(cx - 13, cy - 4), (cx + 13, cy - 4), (cx + 13, cy + 56), (cx - 13, cy + 56)]
    c.add(lambda x, y: poly_sd(x, y, shaft), shade(color, 0.8))
    # шляпка: круг, срезанный снизу
    c.add(lambda x, y: max(circle_sd(x, y, cx, cy, 36), y - cy), color)
    c.add(lambda x, y: circle_sd(x, y, cx - 10, cy - 14, 11), shade(color, 1.4), outline=False)


def rivets(color):
    c = Canvas()
    rivet(c, 76, 112, color)
    rivet(c, 180, 112, color)
    rivet(c, 128, 150, color)
    return c


def gear_sd(x, y, cx, cy, r_out, r_in, teeth, tooth_w):
    dx, dy = x - cx, y - cy
    r = math.hypot(dx, dy)
    a = math.atan2(dy, dx)
    step = 2 * math.pi / teeth
    k = (a % step) - step / 2  # угол от центра ближайшего зуба
    # ширина зуба по дуге: tooth_w (доля шага)
    in_tooth = abs(k) < step * tooth_w / 2
    body = r - r_in
    tooth = max(r - r_out, abs(k) * r - step * tooth_w / 2 * r_in)
    return min(body, tooth)


def gears(color):
    c = Canvas()
    big = (112, 140, 82, 64, 10, 0.5)
    small = (190, 78, 50, 38, 8, 0.5)
    c.add(lambda x, y: max(gear_sd(x, y, *small), -circle_sd(x, y, small[0], small[1], 13)), shade(color, 0.85))
    c.add(lambda x, y: max(gear_sd(x, y, *big), -circle_sd(x, y, big[0], big[1], 22)), color)
    return c


if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/Art/UI/Icons"
    write_png(out + '/Metal.png', metal(hexc('#2F6BFF')).render())
    write_png(out + '/Rivets.png', rivets(hexc('#B07CFF')).render())
    write_png(out + '/Gears.png', gears(hexc('#E8413C')).render())
    print('ok')
