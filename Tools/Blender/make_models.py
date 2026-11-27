"""
Generates the prototype meshes for ColorsAction and exports them as FBX into the Unity project.

Run headless (from the project root):
    "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" -b --factory-startup --python Tools/Blender/make_models.py

Or open Blender and run it from the Text Editor. Every model is built from code, so the
script is the source of truth: tweak the numbers here and run it again.

Conventions (Blender, Z up): the model's front looks along -Y, the origin is where Unity
should hold or place it. Exported with Y up / -Z forward and baked axes, so in Unity the
front looks along +Z and no extra rotation is needed.

Items follow the concept sheet Art/Concepts/Items.png (low-poly, flat faces, chamfered edges).
Material slots are named; Unity maps them by submesh order (ItemPrefabBuilder assigns the materials):
    Player:     0 Body, 1 Eyes
    Sword:      0 Blade (weapon colour), 1 Gold, 2 Grip, 3 Gem
    Shield:     0 Face (weapon colour), 1 Rim, 2 Emblem
    Shotgun:    0 Stock (weapon colour), 1 Metal, 2 Emblem, 3 Bore, 4 Barrel   (the concept's musket)
    Club:       0 Head, 1 Grip, 2 Wrap
    Dummy:      0 Body (red), 1 Base
    Key:        0 Metal
    Flask:      0 Liquid (item colour), 1 Glass, 2 Collar, 3 Cork, 4 Rope, 5 Tag, 6 Cross, 7 Bubble
    Chest:      meshes ChestBody / ChestLid (origin on the hinge): 0 Wood, 1 Metal, 2 Emblem, 3 Keyhole, 4 Gap
    PickupBase: 0 Stone, 1 Grass, 2 Petal, 3 Flower centre, 4 Red leaf, 5 Orange leaf

Only some models: pass their names after "--", e.g. ... --python Tools/Blender/make_models.py -- key chest
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_FBX = os.path.join(PROJECT, "Assets", "Game", "Art", "Models")
OUT_BLEND = os.path.join(PROJECT, "Art", "Blender")


# --------------------------------------------------------------------------- helpers

def reset_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def material(name, rgba):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Roughness"].default_value = 0.6
    mat.diffuse_color = rgba
    return mat


def new_object(name, bm, materials):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    for mat in materials:
        mesh.materials.append(mat)
    return obj


def box(bm, center, size, mat_index=0):
    """Adds an axis-aligned box to bm and returns its faces."""
    geom = bmesh.ops.create_cube(bm, size=1.0)
    verts = geom["verts"]
    for v in verts:
        v.co = Vector((v.co.x * size[0] + center[0], v.co.y * size[1] + center[1], v.co.z * size[2] + center[2]))
    faces = list({f for v in verts for f in v.link_faces})
    for f in faces:
        f.material_index = mat_index
    return faces


def bevel_all(obj, width, segments=2):
    mod = obj.modifiers.new("Bevel", "BEVEL")
    mod.width = width
    mod.segments = segments
    mod.limit_method = "ANGLE"
    mod.angle_limit = math.radians(40)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.select_set(False)


def smooth(obj, angle_deg=35):
    for poly in obj.data.polygons:
        poly.use_smooth = True
    # Sharp edges by angle, so flat faces stay flat and only rounded parts are smooth.
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle_deg))
    except Exception:  # older/newer API differences: plain smooth is fine
        pass
    obj.select_set(False)


def export(objs, filename):
    os.makedirs(OUT_FBX, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
        if not any(m.type == "TRIANGULATE" for m in o.modifiers):
            tri = o.modifiers.new("Triangulate", "TRIANGULATE")  # concave n-gons import safely
            tri.quad_method = "BEAUTY"
            tri.ngon_method = "BEAUTY"
    bpy.context.view_layer.objects.active = objs[0]
    path = os.path.join(OUT_FBX, filename)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"MESH"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=False,
        use_tspace=False,
    )
    print("exported", path)


# --------------------------------------------------------------------------- player

def make_player():
    """1x1x1 rounded cube, origin at the bottom centre, a dense even grid so it bends smoothly."""
    cuts = 11          # 12 segments per edge
    radius = 0.09      # corner rounding
    half = 0.5

    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=cuts, use_grid_fill=True)
    inner = half - radius
    for v in bm.verts:
        p = v.co.copy()
        q = Vector((max(-inner, min(inner, p.x)), max(-inner, min(inner, p.y)), max(-inner, min(inner, p.z))))
        d = p - q
        if d.length > 1e-6:
            v.co = q + d.normalized() * radius
    for v in bm.verts:
        v.co.z += half

    # Eyes: two thin dark slabs on the front (-Y) face. They are part of the same mesh,
    # so the deformation moves them together with the body.
    for x in (-0.17, 0.17):
        box(bm, (x, -half - 0.004, 0.64), (0.1, 0.02, 0.17), mat_index=1)

    obj = new_object("Player", bm, [material("Body", (0.62, 0.63, 0.65, 1)), material("Eyes", (0.08, 0.08, 0.1, 1))])
    for poly in obj.data.polygons:
        poly.use_smooth = poly.material_index == 0
    return obj



# --------------------------------------------------------------------------- dummy

def make_dummy():
    """Training dummy: a red cylinder on a flat base. Origin at the bottom centre."""
    bm = bmesh.new()
    radius, height, base_h = 0.36, 1.5, 0.08
    seg = 32
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=radius, radius2=radius, depth=height)
    for v in res["verts"]:
        v.co.z += height / 2
    # Horizontal loops so the body can be bent later if needed.
    side_edges = [e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > height * 0.9]
    bmesh.ops.subdivide_edges(bm, edges=side_edges, cuts=5, use_grid_fill=False)
    for f in bm.faces:
        f.material_index = 0
    body = new_object("DummyBody", bm, [material("DummyRed", (0.91, 0.25, 0.24, 1))])
    bevel_all(body, 0.04, 3)
    smooth(body, 40)

    bm = bmesh.new()
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=0.52, radius2=0.48, depth=base_h)
    for v in res["verts"]:
        v.co.z += base_h / 2 - 0.0
    base = new_object("DummyBase", bm, [material("DummyBaseMat", (0.35, 0.35, 0.38, 1))])
    bevel_all(base, 0.015, 2)
    smooth(base, 40)
    return body, base




# --------------------------------------------------------------------------- concept helpers
#
# Items after the concept sheet "Игровые предметы" (Art/Concepts/Items.png): chunky low-poly shapes with flat
# faces and chamfered edges. All parts of one model are a single mesh with named material slots.

def add_part(bm, mat_index, build, matrix=None):
    """Runs build() (which adds geometry to bm), sets the material of the new faces, optionally moves the new verts."""
    verts_before = set(bm.verts)
    faces_before = set(bm.faces)
    build()
    new_faces = [f for f in bm.faces if f not in faces_before]
    for f in new_faces:
        f.material_index = mat_index
    if matrix is not None:
        for v in bm.verts:
            if v not in verts_before:
                v.co = matrix @ v.co
    return new_faces


def obox(bm, center, size, mat_index=0, rot=(0, 0, 0)):
    """Box with a rotation (Euler XYZ in degrees) around its centre."""
    m = (Matrix.Translation(Vector(center))
         @ Euler([math.radians(a) for a in rot], "XYZ").to_matrix().to_4x4()
         @ Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
    return add_part(bm, mat_index, lambda: bmesh.ops.create_cube(bm, size=1.0, matrix=m))


def bar(bm, a, b, thick, mat_index=0):
    """Square bar from point a to point b (thick × thick section)."""
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = d.to_track_quat("Z", "Y").to_matrix().to_4x4()
    m = Matrix.Translation((a + b) / 2) @ rot @ Matrix.Diagonal((thick, thick, d.length, 1.0))
    return add_part(bm, mat_index, lambda: bmesh.ops.create_cube(bm, size=1.0, matrix=m))


def lathe(bm, rings, sides, mat_index=0, center=(0, 0, 0), axis="Z", offset=0.0, twist=0.0, jitter=0.0, seed=0,
          cap_start=True, cap_end=True):
    """
    Low-poly body of revolution: rings = [(h, r), ...] along the axis ("Z" up, "-Y" forward, "X" side).
    r == 0 makes a single apex vertex. twist turns every next ring by twist * step (faceted, crystal-like look).
    """
    import random
    rnd = random.Random(seed)
    cx, cy, cz = center

    def point(h, r, a):
        ca, sa = math.cos(a) * r, math.sin(a) * r
        if axis == "Z":
            return (cx + ca, cy + sa, cz + h)
        if axis == "-Y":
            return (cx + ca, cy - h, cz + sa)
        return (cx + h, cy + ca, cz + sa)  # "X"

    def build():
        step = 2 * math.pi / sides
        rows = []
        for k, (h, r) in enumerate(rings):
            if r <= 1e-6:
                rows.append([bm.verts.new(point(h, 0, 0))])
                continue
            row = []
            for i in range(sides):
                rr = r * (1 + (rnd.uniform(-jitter, jitter) if jitter else 0))
                row.append(bm.verts.new(point(h, rr, offset + step * i + twist * step * k)))
            rows.append(row)
        for a, b in zip(rows, rows[1:]):
            if len(a) == 1 and len(b) == 1:
                continue
            if len(a) == 1:
                for i in range(sides):
                    bm.faces.new((a[0], b[i], b[(i + 1) % sides]))
            elif len(b) == 1:
                for i in range(sides):
                    bm.faces.new((a[i], b[0], a[(i + 1) % sides]))
            else:
                for i in range(sides):
                    bm.faces.new((a[i], b[i], b[(i + 1) % sides], a[(i + 1) % sides]))
        if cap_start and len(rows[0]) > 2:
            bm.faces.new(rows[0])
        if cap_end and len(rows[-1]) > 2:
            bm.faces.new(rows[-1])

    return add_part(bm, mat_index, build)


def prism(bm, outline, depth, mat_index=0, plane="XZ", at=0.0, matrix=None):
    """
    Flat polygon extruded to a slab. plane "XZ": outline in (x, z), slab from y = at to at + depth (front = -Y).
    plane "UV": outline in (forward u, up v) on the side of a model, slab from x = at to at + depth.
    """
    def build():
        def p(a, b, d):
            if plane == "XZ":
                return (a, at + d, b)
            return (at + d, -a, b)
        front = [bm.verts.new(p(a, b, 0.0)) for a, b in outline]
        back = [bm.verts.new(p(a, b, depth)) for a, b in outline]
        n = len(outline)
        bm.faces.new(front)
        bm.faces.new(list(reversed(back)))
        for i in range(n):
            bm.faces.new((front[i], front[(i + 1) % n], back[(i + 1) % n], back[i]))

    return add_part(bm, mat_index, build, matrix)


def square_frame(bm, side, width, depth, mat_index, matrix, inner=0.0):
    """Square outline in the local XZ plane (front -Y, slab from y = 0 to depth), a small square in the middle."""
    h = side / 2
    root2 = math.sqrt(2)
    add_part(bm, mat_index, lambda: ring(bm, (0, depth / 2, 0), h * root2, (h - width) * root2, depth,
                                         sides=4, offset=math.pi / 4), matrix)
    if inner > 0:
        m = matrix @ Matrix.Translation((0, depth / 2, 0)) @ Matrix.Diagonal((inner, depth, inner, 1.0))
        add_part(bm, mat_index, lambda: bmesh.ops.create_cube(bm, size=1.0, matrix=m))


def ring(bm, center, outer, inner, thickness, sides=8, offset=None):
    """Flat polygonal ring in the XZ plane (faces look along -Y). offset None: a flat edge on top for even sides."""
    cx, cy, cz = center
    half = thickness / 2
    if offset is None:
        offset = math.pi / sides if sides % 4 == 0 else 0.0
    rows = []
    for i in range(sides):
        a = offset + 2 * math.pi * i / sides
        ca, sa = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((cx + r * ca, cy + y, cz + r * sa))
                     for r, y in ((outer, -half), (outer, half), (inner, half), (inner, -half))])
    for i in range(sides):
        a, b = rows[i], rows[(i + 1) % sides]
        for k in range(4):
            bm.faces.new((a[k], b[k], b[(k + 1) % 4], a[(k + 1) % 4]))


def finish(bm, name, mats, bevel=0.0, angle=40):
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = new_object(name, bm, mats)
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 1
        mod.limit_method = "ANGLE"
        mod.angle_limit = math.radians(angle)
        mod.harden_normals = False
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.modifier_apply(modifier=mod.name)
        obj.select_set(False)
    # Unity builds submeshes in the order materials first appear in the face list: sort faces by slot so the
    # submesh index equals the slot index (the prefab builder assigns materials by it).
    sorted_bm = bmesh.new()
    sorted_bm.from_mesh(obj.data)
    sorted_bm.faces.sort(key=lambda f: f.material_index)
    sorted_bm.faces.index_update()
    sorted_bm.to_mesh(obj.data)
    sorted_bm.free()
    for poly in obj.data.polygons:
        poly.use_smooth = False  # faceted low-poly look of the concept
    return obj


def srgb(hex_str):
    h = hex_str.lstrip("#")
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
    return (lin[0], lin[1], lin[2], 1.0)


# Colours of the concept (sRGB). Unity materials get the same values in ItemPrefabBuilder.
C_GOLD = "#F0A63A"
C_GRIP = "#5A3A38"
C_WHITE = "#F4F1F4"
C_METAL = "#A6A2BC"
C_METAL_DARK = "#6F6D84"
C_DARK = "#2E2A36"


# --------------------------------------------------------------------------- sword

def make_sword():
    """
    Crystal blade (hexagonal section with a ridge), gold crossguard with a white gem, wrapped grip, gold pommel.
    Origin in the middle of the grip; the blade points up (+Z), the flat side faces the camera (-Y).
    Slots: 0 Blade (weapon colour), 1 Gold, 2 Grip, 3 Gem.
    """
    bm = bmesh.new()
    w, e, t = 0.125, 0.014, 0.045
    section = [(-w, -e), (-w, e), (0.0, t), (w, e), (w, -e), (0.0, -t)]
    z0, z1, tip = 0.2, 1.1, 1.36

    def blade():
        r0 = [bm.verts.new((x, y, z0)) for x, y in section]
        r1 = [bm.verts.new((x * 0.92, y * 0.95, z1)) for x, y in section]
        apex = bm.verts.new((w * 0.7, 0.0, tip))  # chisel tip, as on the concept
        n = len(section)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
            bm.faces.new((r1[i], r1[j], apex))
        bm.faces.new(list(reversed(r0)))

    add_part(bm, 0, blade)
    # Crossguard: a bar with thicker square ends and a block in the middle with the gem on both sides.
    obox(bm, (0, 0, 0.155), (0.5, 0.095, 0.085), 1)
    for s in (-1, 1):
        obox(bm, (s * 0.245, 0, 0.155), (0.085, 0.12, 0.115), 1)
    obox(bm, (0, 0, 0.16), (0.17, 0.13, 0.17), 1)
    for s in (-1, 1):
        obox(bm, (0, s * 0.07, 0.16), (0.07, 0.02, 0.07), 3)
    # Grip with wraps, pommel.
    obox(bm, (0, 0, -0.01), (0.08, 0.08, 0.26), 2)
    for z in (-0.1, -0.035, 0.03, 0.095):
        obox(bm, (0, 0, z), (0.096, 0.096, 0.05), 2)
    obox(bm, (0, 0, -0.185), (0.145, 0.145, 0.1), 1)
    return finish(bm, "Sword", [material("Blade", srgb("#3AA0F0")), material("Gold", srgb(C_GOLD)),
                                material("Grip", srgb(C_GRIP)), material("Gem", srgb(C_WHITE))], bevel=0.011)


# --------------------------------------------------------------------------- shield

SHIELD_OUTLINE = [(-0.33, 0.52), (0.33, 0.52), (0.43, 0.42), (0.43, 0.06), (0.36, -0.22), (0.2, -0.42),
                  (0.0, -0.53), (-0.2, -0.42), (-0.36, -0.22), (-0.43, 0.06), (-0.43, 0.42)]


def make_shield():
    """
    Heater shield with a thick faceted rim, a ridged face and a white square emblem with a notch (speech bubble).
    Origin at the handle on the back; the face looks along -Y. Slots: 0 Face (weapon colour), 1 Rim, 2 Emblem.
    """
    bm = bmesh.new()
    thickness = 0.09
    verts = [bm.verts.new((x, 0.0, z)) for x, z in SHIELD_OUTLINE]
    face = bm.faces.new(verts)
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    for g in ext["geom"]:
        if isinstance(g, bmesh.types.BMVert):
            g.co.y += thickness
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.material_index = 1
    front = min(bm.faces, key=lambda f: f.calc_center_median().y)
    res = bmesh.ops.inset_region(bm, faces=[front], thickness=0.095, depth=0.0)
    for f in res["faces"]:
        f.material_index = 1
    front.material_index = 0
    for v in front.verts:
        v.co.y += 0.022  # face sits behind the rim
    # Vertical ridge down the middle of the face.
    cut = bmesh.ops.bisect_plane(bm, geom=list(front.verts) + list(front.edges) + [front],
                                 plane_co=(0, 0, 0), plane_no=(1, 0, 0))
    for g in cut["geom_cut"]:
        if isinstance(g, bmesh.types.BMVert):
            g.co.y -= 0.02
    # Emblem: square frame, small square inside, a notch under the frame.
    c = 0.06
    m = Matrix.Translation((0, -0.012, c))
    square_frame(bm, 0.37, 0.075, 0.03, 2, m, inner=0.11)
    prism(bm, [(-0.07, c - 0.18), (0.0, c - 0.18), (-0.035, c - 0.235)], 0.03, 2, at=-0.012)
    # Handle on the back.
    obox(bm, (0, thickness + 0.035, 0.0), (0.05, 0.04, 0.26), 1)
    for v in bm.verts:
        v.co.y -= thickness + 0.035
    return finish(bm, "Shield", [material("Face", srgb("#2C86E8")), material("Rim", srgb(C_METAL)),
                                 material("Emblem", srgb(C_WHITE))], bevel=0.013)


# --------------------------------------------------------------------------- musket (shotgun)

def make_shotgun():
    """
    Flintlock blunderbuss (the "Мушкет" of the concept, the game's shotgun). Barrel along -Y (Unity +Z), up +Z.
    Same frame as the old primitive gun: origin at the wrist (where the hand holds it), muzzle near u = 0.86.
    Slots: 0 Stock (weapon colour), 1 Metal, 2 Emblem, 3 Dark (bore), 4 Barrel (dark metal).
    """
    bm = bmesh.new()
    half = 0.042
    stock = [(-0.46, -0.2), (-0.43, 0.03), (-0.14, 0.045), (-0.04, 0.07), (0.54, 0.062), (0.58, 0.035),
             (0.55, 0.005), (0.1, 0.005), (0.02, -0.02), (-0.08, -0.05)]
    prism(bm, stock, 2 * half, 0, plane="UV", at=-half)
    # Barrel: thin tube, a thicker band before the bell, the bell muzzle; dark bore.
    v = 0.087
    lathe(bm, [(-0.08, 0.03), (0.56, 0.033), (0.56, 0.042), (0.68, 0.042), (0.68, 0.034), (0.72, 0.034),
               (0.82, 0.06), (0.86, 0.062)], 8, 4, center=(0, 0, v), axis="-Y", offset=math.pi / 8)
    lathe(bm, [(0.861, 0.044), (0.862, 0.044)], 8, 3, center=(0, 0, v), axis="-Y", offset=math.pi / 8)
    # Lock plate with two screws on the right side, hammer with a ball, frizzen claw.
    side = half + 0.004
    obox(bm, (side + 0.01, -0.03, 0.075), (0.02, 0.21, 0.075), 1)
    for u, z in ((-0.03, 0.06), (0.09, 0.085)):
        lathe(bm, [(0.0, 0.019), (0.014, 0.017)], 8, 1, center=(side + 0.02, -u, z), axis="X")
    bar(bm, (0.012, 0.05, 0.1), (0.012, 0.075, 0.18), 0.03, 1)
    lathe(bm, [(-0.036, 0.0), (-0.026, 0.026), (0.0, 0.037), (0.026, 0.026), (0.036, 0.0)], 8, 1,
          center=(0.012, 0.085, 0.205))
    bar(bm, (0.012, -0.02, 0.1), (0.012, -0.03, 0.165), 0.026, 1)
    bar(bm, (0.012, -0.03, 0.165), (0.012, 0.0, 0.19), 0.024, 1)
    # Trigger guard loop and the trigger.
    loop = [(-0.05, -0.035), (-0.035, -0.085), (0.02, -0.11), (0.08, -0.095), (0.11, -0.055), (0.11, -0.0)]
    for (u0, v0), (u1, v1) in zip(loop, loop[1:]):
        bar(bm, (0, -u0, v0), (0, -u1, v1), 0.02, 1)
    bar(bm, (0, -0.03, -0.02), (0, -0.045, -0.07), 0.018, 1)
    # The concept gun is chunky: thicken everything across the barrel line (length stays, the muzzle stays put).
    for vert in bm.verts:
        vert.co.x *= 1.3
        vert.co.z = 0.03 + (vert.co.z - 0.03) * 1.3
    # Emblem on both sides of the butt: a diamond outline with a small diamond inside.
    for s in (-1, 1):
        m = (Matrix.Translation((s * (half + 0.001), 0.3, -0.05))
             @ Matrix.Rotation(math.radians(90), 4, "Z") @ Matrix.Rotation(math.radians(45), 4, "Y"))
        if s < 0:
            m = m @ Matrix.Rotation(math.radians(180), 4, "Z")
        square_frame(bm, 0.1, 0.02, 0.008, 2, m @ Matrix.Translation((0, -0.008, 0)), inner=0.03)
    return finish(bm, "Shotgun", [material("Stock", srgb("#D2483C")), material("GunMetal", srgb(C_METAL)),
                                  material("GunEmblem", srgb(C_WHITE)), material("Bore", srgb(C_DARK)),
                                  material("Barrel", srgb(C_METAL_DARK))], bevel=0.008)


# --------------------------------------------------------------------------- club

def make_club():
    """
    Enemy club: faceted wooden head, dark grip with two cream wraps. Origin in the middle of the grip, head up (+Z).
    Slots: 0 Head, 1 Grip, 2 Wrap.
    """
    bm = bmesh.new()
    lathe(bm, [(0.1, 0.055), (0.24, 0.09), (0.42, 0.14), (0.56, 0.16), (0.66, 0.14), (0.73, 0.08), (0.76, 0.0)],
          7, 0, twist=0.5, jitter=0.06, seed=3)
    lathe(bm, [(-0.19, 0.05), (0.12, 0.05)], 8, 1, offset=math.pi / 8)
    for z in (-0.005, 0.07):
        lathe(bm, [(z, 0.07), (z + 0.05, 0.07)], 8, 2, offset=math.pi / 8)
    return finish(bm, "Club", [material("ClubHead", srgb("#7E6068")), material("ClubGrip", srgb("#45302F")),
                               material("ClubWrap", srgb("#E6D0AE"))], bevel=0.008)


# --------------------------------------------------------------------------- key

def make_key():
    """
    Door key of the concept: hexagonal bow with a hexagonal hole, square shaft, two teeth at the end.
    Points up (+Z), flat side faces the camera (-Y). Origin in the middle of the shaft. Slot: 0 Metal.
    """
    bm = bmesh.new()
    # Hexagon with points left and right, flat top and bottom.
    add_part(bm, 0, lambda: ring(bm, (0.0, 0.0, 0.24), 0.17, 0.085, 0.085, sides=6, offset=0.0))
    obox(bm, (0, 0, -0.065), (0.076, 0.076, 0.35), 0)
    obox(bm, (0.075, 0, -0.105), (0.1, 0.07, 0.07), 0)
    obox(bm, (0.075, 0, -0.205), (0.1, 0.07, 0.07), 0)
    return finish(bm, "Key", [material("KeyMetal", srgb(C_METAL))], bevel=0.02)


# --------------------------------------------------------------------------- flask

def make_flask():
    """
    Healing flask: faceted round bottle with green liquid, glass top, collar, cork, rope and a tag with a cross.
    Stands on its bottom (origin), up +Z. Slots: 0 Liquid (item colour), 1 Glass, 2 Collar, 3 Cork, 4 Rope,
    5 Tag, 6 Cross, 7 Bubble.
    """
    bm = bmesh.new()
    off = math.pi / 8
    lathe(bm, [(0.0, 0.1), (0.035, 0.165), (0.17, 0.175), (0.2, 0.17)], 8, 0, offset=off)
    lathe(bm, [(0.2, 0.17), (0.265, 0.13), (0.3, 0.075), (0.36, 0.06)], 8, 1, offset=off)
    lathe(bm, [(0.335, 0.08), (0.385, 0.08)], 8, 2, offset=off)
    lathe(bm, [(0.385, 0.068), (0.455, 0.074)], 8, 3, offset=off)
    # Rope: a ring of short bars around the neck.
    n = 12
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        r = 0.08
        z = 0.318 + (0.006 if i % 2 else -0.006)  # twisted look
        bar(bm, (r * math.cos(a0), r * math.sin(a0), z), (r * math.cos(a1), r * math.sin(a1), 0.318), 0.034, 4)
    # Bubbles on the front facet (it faces -Y).
    front_y = -0.172 * math.cos(off)
    for x, z, r in ((-0.04, 0.085, 0.02), (0.005, 0.125, 0.028), (0.045, 0.07, 0.017), (-0.01, 0.045, 0.013)):
        lathe(bm, [(0.0, r), (0.006, r)], 8, 7, center=(x, front_y - 0.004, z), axis="-Y", offset=off)
    # Tag hanging from the rope on the right, turned to the camera, with a cross.
    tag = (Matrix.Translation((0.13, -0.12, 0.14)) @ Matrix.Rotation(math.radians(25), 4, "Z")
           @ Matrix.Rotation(math.radians(28), 4, "Y"))
    prism(bm, [(-0.05, 0.0), (0.05, 0.0), (0.05, 0.08), (0.0, 0.115), (-0.05, 0.08)], 0.01, 5, matrix=tag)
    for sx, sz in ((0.055, 0.016), (0.016, 0.055)):
        m = tag @ Matrix.Translation((0, -0.004, 0.045)) @ Matrix.Diagonal((sx, 0.006, sz, 1.0))
        add_part(bm, 6, lambda m=m: bmesh.ops.create_cube(bm, size=1.0, matrix=m))
    bar(bm, (0.07, -0.05, 0.31), tag @ Vector((0.0, 0.005, 0.105)), 0.012, 4)
    return finish(bm, "Flask", [material("Liquid", srgb("#6CCB2E")), material("Glass", srgb("#CDEBDD")),
                                material("Collar", srgb("#7FA0A3")), material("Cork", srgb("#B5643A")),
                                material("Rope", srgb("#C28A52")), material("Tag", srgb("#EBCFA5")),
                                material("Cross", srgb("#6FB43A")), material("Bubble", srgb("#B8F07A"))],
                  bevel=0.006)


# --------------------------------------------------------------------------- chest

CHEST_W, CHEST_D, CHEST_H, CHEST_LID = 0.86, 0.62, 0.42, 0.2  # = Configs/Chests.json width/depth/bodyHeight/lidHeight


def make_chest():
    """
    Red plank chest with metal ends, a band, corner plates, rivets, a lock plate with a keyhole and a white emblem.
    Two objects: ChestBody (origin at the bottom centre) and ChestLid (origin on the back top edge = hinge).
    Front faces -Y (Unity +Z). Slots: 0 Wood, 1 Metal, 2 Emblem, 3 Keyhole, 4 Wood gaps.
    """
    W, D, H, LH = CHEST_W, CHEST_D, CHEST_H, CHEST_LID
    mats = [material("ChestWood", srgb("#C8473A")), material("ChestMetal", srgb(C_METAL)),
            material("ChestEmblem", srgb(C_WHITE)), material("ChestKeyhole", srgb(C_DARK)),
            material("ChestGap", srgb("#9A3C30"))]
    rivet = 0.028

    # ---- body
    bm = bmesh.new()
    obox(bm, (0, 0, H / 2), (W - 0.03, D - 0.03, H), 4)
    plank = H / 3
    for i in range(3):
        obox(bm, (0, 0, plank * (i + 0.5)), (W - 0.02, D - 0.005, plank - 0.014), 0)
    for s in (-1, 1):
        for f in (-1, 1):
            obox(bm, (s * (W / 2 - 0.05), f * (D / 2 - 0.045), H / 2), (0.11, 0.12, H), 1)  # corner posts
            obox(bm, (s * (W / 2 - 0.08), f * (D / 2 - 0.07), 0.06), (0.17, 0.16, 0.12), 1)  # corner plates
    obox(bm, (0, 0, H - 0.05), (W - 0.02, D + 0.02, 0.09), 1)                     # top band
    obox(bm, (0, 0, H + 0.002), (W - 0.12, D - 0.1, 0.006), 4)                    # inside, seen when open
    for x in (-0.3, -0.2, 0.2, 0.3):
        obox(bm, (x, -(D / 2 + 0.012), H - 0.05), (rivet, 0.02, rivet), 1)
    for s in (-1, 1):
        for z in (0.07, 0.2, 0.3):
            obox(bm, (s * (W / 2 - 0.05), -(D / 2 + 0.017), z), (rivet, 0.02, rivet), 1)
    # Lock plate (octagon) and keyhole.
    lock = [(-0.1, H - 0.02), (0.1, H - 0.02), (0.11, H - 0.06), (0.11, H - 0.17), (0.07, H - 0.22),
            (-0.07, H - 0.22), (-0.11, H - 0.17), (-0.11, H - 0.06)]
    prism(bm, lock, 0.035, 1, at=-(D / 2 + 0.03))
    lathe(bm, [(0.0, 0.03), (0.004, 0.03)], 8, 3, center=(0, -(D / 2 + 0.031), H - 0.09), axis="-Y",
          offset=math.pi / 8)
    prism(bm, [(-0.014, H - 0.1), (0.014, H - 0.1), (0.024, H - 0.17), (-0.024, H - 0.17)], 0.004, 3,
          at=-(D / 2 + 0.035))
    body = finish(bm, "ChestBody", mats, bevel=0.01)

    # ---- lid (built in body space, then moved so the origin is the hinge)
    bm = bmesh.new()
    seg = 5

    def arch(r_scale, x0, x1, mat, gap=0.0, thick=0.05):
        for i in range(seg):
            t0 = math.pi * i / seg + gap
            t1 = math.pi * (i + 1) / seg - gap
            pts = []
            for t, k in ((t0, 1.0), (t1, 1.0), (t1, 0.0), (t0, 0.0)):
                ry = D / 2 * r_scale - thick * (1 - k)
                rz = LH * r_scale - thick * (1 - k)
                pts.append((-ry * math.cos(t), H + max(0.0, rz) * math.sin(t)))

            def build(pts=pts):
                a = [bm.verts.new((x0, y, z)) for y, z in pts]
                b = [bm.verts.new((x1, y, z)) for y, z in pts]
                bm.faces.new(a)
                bm.faces.new(list(reversed(b)))
                for j in range(4):
                    bm.faces.new((a[j], a[(j + 1) % 4], b[(j + 1) % 4], b[j]))

            add_part(bm, mat, build)

    arch(0.97, -(W / 2 - 0.02), W / 2 - 0.02, 4, thick=0.12)                       # gap filler
    arch(1.0, -(W / 2 - 0.01), W / 2 - 0.01, 0, gap=0.012)                       # planks
    for s in (-1, 1):
        arch(1.06, s * (W / 2 - 0.105), s * (W / 2 + 0.005), 1, thick=0.07)       # metal arches
        for i in range(1, seg):
            t = math.pi * i / seg
            y = -(D / 2 * 1.06 + 0.004) * math.cos(t)
            z = H + (LH * 1.06 + 0.004) * math.sin(t)
            obox(bm, (s * (W / 2 - 0.05), y, z), (rivet, rivet, rivet), 1, rot=(math.degrees(-t) + 90, 0, 0))
    obox(bm, (0, -(D / 2 + 0.01), H + 0.025), (W - 0.2, 0.03, 0.05), 1)           # front lip
    cap = [(-(D / 2) * math.cos(math.pi * i / 10), H + LH * math.sin(math.pi * i / 10)) for i in range(11)]
    for s in (-1, 1):                                                                # wooden ends of the lid
        prism(bm, [(-y, z) for y, z in cap], 0.012, 0, plane="UV", at=s * (W / 2 - 0.002) - 0.006)
    # Emblem on the front slope of the lid: a diamond frame with a small diamond.
    t = math.radians(52)
    pos = Vector((0.0, -(D / 2) * math.cos(t), H + LH * math.sin(t)))
    tilt = -math.atan2(math.sin(t) / LH, math.cos(t) / (D / 2))  # turn -Y onto the arch normal
    m = (Matrix.Translation(pos) @ Matrix.Rotation(tilt, 4, "X")
         @ Matrix.Rotation(math.radians(45), 4, "Y") @ Matrix.Translation((0, -0.012, 0)))
    square_frame(bm, 0.12, 0.028, 0.016, 2, m, inner=0.035)
    for v in bm.verts:
        v.co -= Vector((0.0, D / 2, H))
    lid = finish(bm, "ChestLid", mats, bevel=0.008)
    lid.location = (0.0, D / 2, H)
    return body, lid


# --------------------------------------------------------------------------- pickup base

def make_pickup_base():
    """
    Ground under items lying in the world: a patch of flagstones with grass tufts, daisies and autumn leaves.
    Origin at the floor level in the middle; about 1.3 × 1.2 cells. Slots: 0 Stone, 1 Grass, 2 Petal, 3 Flower centre,
    4 Red leaf, 5 Orange leaf.
    """
    import random
    rnd = random.Random(11)
    bm = bmesh.new()
    rows = [(-0.4, 0.35), (-0.02, 0.36), (0.37, 0.34)]
    for ri, (yc, depth) in enumerate(rows):
        x = -0.66 + (0.12 if ri == 1 else 0.0)
        while x < 0.62:
            w = rnd.uniform(0.3, 0.46)
            x1 = min(x + w, 0.66)
            cx, cy = (x + x1) / 2, yc + rnd.uniform(-0.015, 0.015)
            corner = abs(cx) > 0.42 and ri != 1
            if not (corner and rnd.random() < 0.45):
                h = rnd.uniform(0.035, 0.06)
                obox(bm, (cx, cy, h / 2 - 0.005), (x1 - x - 0.035, depth - 0.035, h), 0,
                     rot=(0, 0, rnd.uniform(-3, 3)))
            x = x1

    def tuft(px, py, count, height):
        for i in range(count):
            a = 2 * math.pi * i / count + rnd.uniform(-0.3, 0.3)
            lean = rnd.uniform(0.25, 0.55)
            h = height * rnd.uniform(0.7, 1.1)
            bx, by = px + math.cos(a) * 0.025, py + math.sin(a) * 0.025
            w = 0.06
            tipp = (bx + math.cos(a) * h * lean, by + math.sin(a) * h * lean, h)
            mid = (bx + math.cos(a) * h * lean * 0.35, by + math.sin(a) * h * lean * 0.35, h * 0.55)
            ca, sa = -math.sin(a), math.cos(a)

            def build(bx=bx, by=by, tipp=tipp, mid=mid, ca=ca, sa=sa):
                l0 = bm.verts.new((bx - ca * w / 2, by - sa * w / 2, 0.0))
                r0 = bm.verts.new((bx + ca * w / 2, by + sa * w / 2, 0.0))
                b0 = bm.verts.new((bx - math.cos(a) * 0.01, by - math.sin(a) * 0.01, 0.0))
                l1 = bm.verts.new((mid[0] - ca * w * 0.35, mid[1] - sa * w * 0.35, mid[2]))
                r1 = bm.verts.new((mid[0] + ca * w * 0.35, mid[1] + sa * w * 0.35, mid[2]))
                b1 = bm.verts.new((mid[0] - math.cos(a) * 0.008, mid[1] - math.sin(a) * 0.008, mid[2]))
                tp = bm.verts.new(tipp)
                bm.faces.new((l0, r0, r1, l1))
                bm.faces.new((r0, b0, b1, r1))
                bm.faces.new((b0, l0, l1, b1))
                bm.faces.new((l1, r1, tp))
                bm.faces.new((r1, b1, tp))
                bm.faces.new((b1, l1, tp))

            add_part(bm, 1, build)

    def daisy(px, py, size):
        z = 0.07 + rnd.uniform(-0.01, 0.01)
        bar(bm, (px, py, 0.0), (px, py, z), 0.012, 1)
        for i in range(6):
            a = 2 * math.pi * i / 6
            obox(bm, (px + math.cos(a) * size * 0.55, py + math.sin(a) * size * 0.55, z),
                 (size * 0.75, size * 0.42, 0.01), 2, rot=(0, 0, math.degrees(a)))
        lathe(bm, [(0.0, size * 0.3), (0.016, size * 0.3)], 8, 3, center=(px, py, z - 0.004))

    def leaf(px, py, size, mat, turn):
        pts = []
        for i in range(10):
            a = math.radians(turn) + 2 * math.pi * i / 10
            r = size if i % 2 == 0 else size * 0.45
            pts.append((px + math.cos(a) * r, py + math.sin(a) * r))

        def build(pts=pts):
            top = [bm.verts.new((x, y, 0.012)) for x, y in pts]
            bot = [bm.verts.new((x, y, 0.004)) for x, y in pts]
            c0 = bm.verts.new((px, py, 0.012))
            c1 = bm.verts.new((px, py, 0.004))
            n = len(pts)
            for i in range(n):
                j = (i + 1) % n
                bm.faces.new((c0, top[i], top[j]))
                bm.faces.new((c1, bot[j], bot[i]))
                bm.faces.new((top[i], bot[i], bot[j], top[j]))

        add_part(bm, mat, build, Matrix.Translation((0, 0, 0.04)) if abs(px) < 0.6 and abs(py) < 0.55 else None)

    tuft(-0.66, 0.18, 8, 0.36)
    tuft(0.64, 0.36, 7, 0.32)
    tuft(0.62, -0.4, 6, 0.24)
    tuft(-0.2, 0.6, 5, 0.18)
    daisy(-0.5, -0.52, 0.075)
    daisy(-0.37, -0.6, 0.065)
    daisy(0.42, 0.58, 0.07)
    leaf(-0.62, -0.44, 0.14, 4, 10)
    leaf(0.42, -0.64, 0.12, 5, 40)
    leaf(0.74, 0.04, 0.09, 4, 70)
    leaf(-0.12, 0.64, 0.08, 5, 0)
    return finish(bm, "PickupBase", [material("BaseStone", srgb("#B9AEB6")), material("BaseGrass", srgb("#B7BE3A")),
                                     material("BasePetal", srgb("#FFFFFF")), material("BaseFlower", srgb("#F2C230")),
                                     material("BaseLeafRed", srgb("#E2452E")),
                                     material("BaseLeafOrange", srgb("#F29A2A"))], bevel=0.01)


# --------------------------------------------------------------------------- main

BUILDERS = {
    "player": lambda: [make_player()],
    "sword": lambda: [make_sword()],
    "shield": lambda: [make_shield()],
    "shotgun": lambda: [make_shotgun()],
    "club": lambda: [make_club()],
    "dummy": lambda: list(make_dummy()),
    "key": lambda: [make_key()],
    "flask": lambda: [make_flask()],
    "chest": lambda: list(make_chest()),
    "pickupbase": lambda: [make_pickup_base()],
}

FILE_NAMES = {"pickupbase": "PickupBase"}


def main():
    os.makedirs(OUT_BLEND, exist_ok=True)
    only = [a.lower() for a in sys.argv[sys.argv.index("--") + 1:]] if "--" in sys.argv else []
    for name in only or list(BUILDERS):
        reset_scene()
        objs = BUILDERS[name]()
        title = FILE_NAMES.get(name, name.capitalize())
        export(objs, title + ".fbx")
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, title + ".blend"))
    print("done")


if __name__ == "__main__":
    main()
