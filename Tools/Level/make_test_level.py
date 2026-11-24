"""
Generates Assets/StreamingAssets/Configs/Levels/Test.json: a 64x64 test level for jumps.

Height symbols (see LevelConfig): '0'..'9' = 0..9 steps, 'a'..'z' = 10..35 steps, one step = 0.25 m.
Height differences are tuned for jumpHeight 1.5 and stepHeight 0.3:
    1 step  (0.25) — walkable without a jump
    4 steps (1.0)  — only with a jump
    8 steps (2.0)  — can't be jumped

Run: python Tools/Level/make_test_level.py
This is a stopgap until the level editor exists: it writes the same format the editor will.
"""

import json
import math
import os

W = H = 64
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "StreamingAssets", "Configs", "Levels", "Test.json")

WALK, JUMP, BLOCK = 1, 4, 8   # steps of 0.25 m

g = [[0] * W for _ in range(H)]  # g[z][x], steps


def rect(x0, z0, x1, z1, h):
    """Fills cells x0..x1, z0..z1 inclusive."""
    for z in range(z0, z1 + 1):
        for x in range(x0, x1 + 1):
            g[z][x] = h


# Hills in the north half: smooth sines quantized to quarter steps (neighbours differ by <= 1 step).
for z in range(34, 63):
    for x in range(1, 63):
        v = 2.2 + 1.6 * math.sin(x * 0.21) + 1.4 * math.cos(z * 0.27 + x * 0.05) + 0.8 * math.sin((x + z) * 0.13)
        g[z][x] = max(0, int(round(v)))

# Plateau (3.75 m) with unclimbable cliffs in the hills and a walkable 0.25 ramp up to it.
rect(20, 44, 30, 54, 15)
for i in range(14):            # ramp along +z: heights 1..14
    rect(24, 30 + i, 26, 30 + i, i + 1)

# Border wall (3 m): taller walls hide the player from the iso camera near the edges.
for i in range(W):
    g[0][i] = g[H - 1][i] = 12
    g[i][0] = g[i][W - 1] = 12

# Step comparison right in front of the spawn: 0.25 / 1.0 / 2.0 blocks, 3x3 each.
rect(6, 20, 8, 22, WALK)
rect(11, 20, 13, 22, JUMP)
rect(16, 20, 18, 22, BLOCK)

# Staircase of 0.25 steps (walkable): x 26..29, z 3..18, up to 4 m.
for i in range(16):
    rect(26, 3 + i, 29, 3 + i, i + 1)
# Staircase of 1.0 steps (jump on every step): x 32..35, up to 4 m.
for i in range(4):
    rect(32, 3 + i * 4, 35, 6 + i * 4, (i + 1) * JUMP)
# Staircase of 2.0 steps (blocked): x 38..41.
for i in range(3):
    rect(38, 3 + i * 5, 41, 7 + i * 5, (i + 1) * BLOCK)
# Shared platform on top of the first two staircases (4 m).
rect(26, 19, 41, 24, 16)
# Detour next to the blocked stairs: 1.0 jumps.
for i in range(4):
    rect(43, 3 + i * 3, 45, 5 + i * 3, (i + 1) * JUMP)
rect(43, 15, 45, 24, 16)

# Parkour pillars (2x2), each 1.0 higher than the previous, no gaps.
px, pz = 50, 4
for i in range(6):
    rect(px + (i % 2) * 2, pz + i * 2, px + (i % 2) * 2 + 1, pz + i * 2 + 1, (i + 1) * JUMP)
# Same-height pillars (2 m) with gaps of 1, 1 and 2 blocks to test jump distance.
for x0 in (48, 51, 54, 58):
    rect(x0, 22, x0 + 1, 23, 2 * JUMP)
rect(46, 20, 47, 25, 2 * JUMP)   # start block 2 m
rect(46, 26, 47, 27, JUMP)       # entry: 1.0 from the ground

rows = ["".join(("0123456789abcdefghijklmnopqrstuvwxyz"[v] if v >= 0 else ".") for v in g[z]) for z in range(H)]

level = {
    "name": "Тестовый уровень",
    "step": 0.25,
    "depth": 1.0,
    "rows": rows,
    "spawn": {"x": 10, "z": 9, "yaw": 225},
    "items": [
        {"item": "sword", "x": 12, "z": 13, "yaw": 30},
        {"item": "shield", "x": 15, "z": 12, "yaw": -20},
    ],
    "dummies": [
        {"x": 18, "z": 15, "yaw": 200},
    ],
    "hints": [
        {"action": "interact", "x": 13, "z": 11},
        {"action": "jump", "x": 12, "z": 18},
        {"action": "jump", "x": 33, "z": 1},
        {"action": "roll", "x": 7, "z": 14},
        {"action": "inventory", "x": 10, "z": 11},
    ],
}

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(level, f, ensure_ascii=False, indent=1)
print("written", os.path.abspath(OUT))
for r in reversed(rows):
    print(r)
