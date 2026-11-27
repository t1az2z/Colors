"""
Воксельная карта ×2 по горизонтали: каждая клетка → 2×2, высоты те же.
Пологий слоп (L/T, подъём на куб за 2 клетки) становится лестницей из ступеней по 0.25 (S, H, Q, F) на 4 клетки;
крутой слоп X — X в нижней половине и полный куб в верхней. Объекты встают в свою клетку 2×2 «спиной» по yaw,
точки — в угол. Ручные правки (мосты, ящики, двери — у них размер в клетках не растёт) — отдельным файлом.

  python Tools/Level/scale_voxel_level_x2.py <исходная.json> <новая.json> [правки.py]

Пример: VerticalSlice3 → Tools/Level/vertical_slice3_x2_edits.py.
"""
import json, sys
from collections import OrderedDict

SRC, DST = sys.argv[1], sys.argv[2]
d = json.load(open(SRC, encoding='utf-8'), object_pairs_hook=OrderedDict)

SLOPE_DIR = {0: (0, 1), 1: (1, 0), 2: (0, -1), 3: (-1, 0)}  # куда поднимается слоп

def half(i, j, r):
    """'low' или 'high' — половина клетки вдоль подъёма слопа."""
    dx, dz = SLOPE_DIR[r & 3]
    if dz == 1: return 'low' if j == 0 else 'high'
    if dz == -1: return 'low' if j == 1 else 'high'
    if dx == 1: return 'low' if i == 0 else 'high'
    return 'low' if i == 1 else 'high'

blocks = {}
for b in d['blocks']:
    p = b.split(',')
    x, y, z, s = int(p[0]), int(p[1]), int(p[2]), p[3]
    r = int(p[4]) if len(p) > 4 else 0
    c = p[5] if len(p) > 5 else None
    for i in (0, 1):
        for j in (0, 1):
            ns, nr = s, r
            if s in 'LTX':
                h = half(i, j, r)
                if s == 'L': ns, nr = ('S' if h == 'low' else 'H'), 0
                elif s == 'T': ns, nr = ('Q' if h == 'low' else 'F'), 0
                else: ns, nr = ('X' if h == 'low' else 'F'), (r if h == 'low' else 0)
            blocks[(2 * x + i, y, 2 * z + j)] = (ns, nr, c)

def put(x, y, z, s='F', r=0, c=None): blocks[(x, y, z)] = (s, r, c)
def clear(x, y, z): blocks.pop((x, y, z), None)
def column_clear(x, z):
    for k in [k for k in blocks if k[0] == x and k[2] == z]:
        del blocks[k]
def fill(x0, x1, z0, z1, ytop, ybot=-1):
    """Столбы полных кубов от ybot до ytop-1 (верх = ytop)."""
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            column_clear(x, z)
            for y in range(ybot, ytop):
                put(x, y, z)
def carve(x0, x1, z0, z1):
    for x in range(x0, x1 + 1):
        for z in range(z0, z1 + 1):
            column_clear(x, z)

# ------------------------------------------------------------------ объекты: по умолчанию в подклетку «за спиной» по yaw
def facing(yaw):
    a = int(round((yaw or 0) / 90.0)) % 4
    return {0: (0, 1), 1: (1, 0), 2: (0, -1), 3: (-1, 0)}[a]

def back_sub(x, z, yaw):
    fx, fz = facing(yaw)
    return 2 * x + (0 if fx > 0 else 1 if fx < 0 else 0), 2 * z + (0 if fz > 0 else 1 if fz < 0 else 0)

objects = []
for o in d['objects']:
    o = OrderedDict(o)
    x, y, z = map(int, o['cell'].split(','))
    nx, nz = back_sub(x, z, o.get('yaw', 0))
    o['cell'] = f'{nx},{y},{nz}'
    objects.append(o)
points = []
for p in d.get('points', []):
    p = OrderedDict(p)
    x, y, z = map(int, p['cell'].split(','))
    p['cell'] = f'{2 * x},{y},{2 * z}'
    points.append(p)

def obj(oid):
    return next(o for o in objects if o['id'] == oid)

out = OrderedDict(d)
out['_blocks'] = blocks
out['_objects'] = objects
out['_points'] = points

# ручные правки подключаются отдельным файлом (edits.py), чтобы этот шаг оставался чистым масштабированием
import runpy
ns = runpy.run_path(sys.argv[3], init_globals=dict(blocks=blocks, objects=objects, points=points, put=put, clear=clear,
                                                     column_clear=column_clear, fill=fill, carve=carve, obj=obj,
                                                     OrderedDict=OrderedDict)) if len(sys.argv) > 3 else {}

res = OrderedDict()
res['format'] = d['format']
res['name'] = ns.get('NAME', d['name'])
res['floors'] = d['floors']
res['blocks'] = [f'{x},{y},{z},{s},{r}' + (f',{c}' if c else '') for (x, y, z), (s, r, c) in
                 sorted(blocks.items(), key=lambda kv: (kv[0][1], kv[0][2], kv[0][0]))]
res['objects'] = objects
res['points'] = points
with open(DST, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(res, f, ensure_ascii=False, indent=2)
    f.write('\n')
print('blocks', len(res['blocks']), 'objects', len(objects))
