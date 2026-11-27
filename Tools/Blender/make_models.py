"""
Generates the prototype meshes for ColorsAction and exports them as FBX into the Unity project.

Run headless (from the project root):
    "C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" -b --factory-startup --python Tools/Blender/make_models.py

Or open Blender and run it from the Text Editor. Every model is built from code, so the
script is the source of truth: tweak the numbers here and run it again.

Conventions (Blender, Z up): the model's front looks along -Y, the origin is where Unity
should hold or place it. Exported with Y up / -Z forward and baked axes, so in Unity the
front looks along +Z and no extra rotation is needed.

Material slots are named; Unity maps them by submesh order:
    Player: 0 Body, 1 Eyes
    Sword:  0 Blade (weapon colour), 1 Handle
    Shield: 0 Face (weapon colour), 1 Rim
    Dummy:  0 Body (red), 1 Base
    Club:   0 Head, 1 Grip
    Key:    0 Metal

Only some models: pass their names after "--", e.g. ... --python Tools/Blender/make_models.py -- key
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

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


# --------------------------------------------------------------------------- sword

def make_sword():
    """Origin in the middle of the grip; the blade points up (+Z). Big and readable from the iso camera."""
    bm = bmesh.new()
    # Blade with a tapered tip.
    blade_w, blade_t = 0.17, 0.05
    z0, z1, tip = 0.2, 1.12, 1.34
    faces = box(bm, (0, 0, (z0 + z1) / 2), (blade_w, blade_t, z1 - z0), mat_index=0)
    top_face = next(f for f in faces if all(abs(v.co.z - z1) < 1e-4 for v in f.verts))
    ext = bmesh.ops.extrude_face_region(bm, geom=[top_face])
    new_verts = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
    for v in new_verts:
        v.co.z = tip
        v.co.x *= 0.08
    bmesh.ops.delete(bm, geom=[top_face], context="FACES_ONLY")
    box(bm, (0, 0, 0.165), (0.46, 0.1, 0.075), mat_index=1)   # crossguard
    box(bm, (0, 0, 0.0), (0.07, 0.07, 0.28), mat_index=1)      # grip
    box(bm, (0, 0, -0.175), (0.12, 0.12, 0.08), mat_index=1)   # pommel
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = new_object("Sword", bm, [material("Blade", (0.18, 0.42, 1.0, 1)), material("Handle", (0.2, 0.2, 0.23, 1))])
    bevel_all(obj, 0.012, 2)
    return obj


# --------------------------------------------------------------------------- shield

def heater_outline(width, height, steps=10):
    """Heater shield outline in XZ, top edge flat, pointed bottom. Origin at the centre."""
    w = width / 2
    top = height * 0.45
    pts = [(-w, top), (w, top)]
    side_bottom = -height * 0.05
    pts.append((w, side_bottom))
    # Right curve down to the tip.
    tip = -height * 0.55
    for i in range(1, steps):
        t = i / steps
        ang = t * math.pi / 2
        pts.append((w * math.cos(ang), side_bottom + (tip - side_bottom) * math.sin(ang)))
    pts.append((0, tip))
    for i in range(steps - 1, 0, -1):
        t = i / steps
        ang = t * math.pi / 2
        pts.append((-w * math.cos(ang), side_bottom + (tip - side_bottom) * math.sin(ang)))
    pts.append((-w, side_bottom))
    return pts


def make_shield():
    """Origin at the handle on the back; the face looks along -Y (front)."""
    bm = bmesh.new()
    outline = heater_outline(0.86, 1.04)
    thickness = 0.08
    verts = [bm.verts.new((x, 0.0, z)) for x, z in outline]
    face = bm.faces.new(verts)
    face.material_index = 1
    # Extrude to the back (thickness), the original face is the front.
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    for e in ext["geom"]:
        if isinstance(e, bmesh.types.BMVert):
            e.co.y += thickness
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    # Front face: inset to get a rim, the inner part is coloured and pushed forward a bit.
    front = min(bm.faces, key=lambda f: f.calc_center_median().y)
    res = bmesh.ops.inset_region(bm, faces=[front], thickness=0.07, depth=0.0)
    front.material_index = 0
    for v in front.verts:
        v.co.y -= 0.015
    for f in res["faces"]:
        f.material_index = 1
    # Boss in the middle.
    box(bm, (0, -0.025, 0.06), (0.17, 0.05, 0.17), mat_index=1)
    # Handle on the back.
    box(bm, (0, thickness + 0.035, 0.0), (0.05, 0.04, 0.26), mat_index=1)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = new_object("Shield", bm, [material("Face", (0.69, 0.49, 1.0, 1)), material("Rim", (0.32, 0.31, 0.36, 1))])
    # Shift so the origin is at the handle.
    for v in obj.data.vertices:
        v.co.y -= thickness + 0.035
    bevel_all(obj, 0.006, 2)
    smooth(obj)
    return obj


# --------------------------------------------------------------------------- club

def make_club():
    """Enemy club: grey, smaller than the sword. Origin in the middle of the grip; the head points up (+Z)."""
    bm = bmesh.new()
    seg = 10
    # Grip.
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=0.035, radius2=0.035, depth=0.26)
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = 1
    # Pommel.
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=0.05, radius2=0.05, depth=0.05)
    for v in res["verts"]:
        v.co.z -= 0.15
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = 1
    # Head: thickens towards the top.
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=0.055, radius2=0.11, depth=0.5)
    for v in res["verts"]:
        v.co.z += 0.13 + 0.25
    for f in {f for v in res["verts"] for f in v.link_faces}:
        f.material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = new_object("Club", bm, [material("ClubHead", (0.5, 0.5, 0.52, 1)), material("ClubGrip", (0.25, 0.24, 0.26, 1))])
    bevel_all(obj, 0.012, 2)
    smooth(obj, 40)
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


# --------------------------------------------------------------------------- key

def ring(bm, center, outer, inner, thickness, sides=8, mat_index=0):
    """Flat polygonal ring in the XZ plane (faces look along -Y), one flat side on top."""
    cx, cy, cz = center
    half = thickness / 2
    offset = math.pi / sides  # a flat edge on top, like the octagonal bow of the reference
    rows = []
    for i in range(sides):
        a = offset + 2 * math.pi * i / sides
        ca, sa = math.cos(a), math.sin(a)
        rows.append([bm.verts.new((cx + r * ca, cy + y, cz + r * sa))
                     for r, y in ((outer, -half), (outer, half), (inner, half), (inner, -half))])
    for i in range(sides):
        a, b = rows[i], rows[(i + 1) % sides]
        for k in range(4):
            f = bm.faces.new((a[k], b[k], b[(k + 1) % 4], a[(k + 1) % 4]))
            f.material_index = mat_index


def make_key():
    """
    Door key after the user's reference: chunky octagonal bow with a hole, a collar under it, a square shaft and
    a notched bit to the side. Points up (+Z), flat side faces the camera (-Y). Origin in the middle of the shaft.
    """
    bm = bmesh.new()
    t = 0.07  # thickness of the bow and the bit
    ring(bm, (0.0, 0.0, 0.25), 0.165, 0.09, t)
    box(bm, (0.0, 0.0, 0.08), (0.11, 0.095, 0.05))       # collar
    box(bm, (0.0, 0.0, 0.035), (0.085, 0.08, 0.04))      # neck under the collar
    box(bm, (0.0, 0.0, -0.13), (0.06, 0.06, 0.32))       # shaft
    box(bm, (0.0, 0.0, -0.3), (0.075, 0.075, 0.03))      # tip
    # Bit: top bar, outer bar and a short lower tooth leave a notch open towards the shaft.
    box(bm, (0.105, 0.0, -0.165), (0.16, t * 0.8, 0.05))
    box(bm, (0.16, 0.0, -0.235), (0.05, t * 0.8, 0.14))
    box(bm, (0.06, 0.0, -0.285), (0.06, t * 0.8, 0.04))
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    obj = new_object("Key", bm, [material("KeyMetal", (0.72, 0.55, 0.33, 1))])
    bevel_all(obj, 0.008, 2)
    smooth(obj, 35)
    return obj


# --------------------------------------------------------------------------- main

def main():
    os.makedirs(OUT_BLEND, exist_ok=True)
    only = [a.lower() for a in sys.argv[sys.argv.index("--") + 1:]] if "--" in sys.argv else []
    if only:
        builders = {"key": lambda: [make_key()]}
        for name in only:
            reset_scene()
            objs = builders[name]()
            title = name.capitalize()
            export(objs, title + ".fbx")
            bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, title + ".blend"))
        print("done")
        return

    reset_scene()
    player = make_player()
    export([player], "Player.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, "Player.blend"))

    reset_scene()
    sword = make_sword()
    export([sword], "Sword.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, "Sword.blend"))

    reset_scene()
    shield = make_shield()
    export([shield], "Shield.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, "Shield.blend"))

    reset_scene()
    club = make_club()
    export([club], "Club.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, "Club.blend"))

    reset_scene()
    body, base = make_dummy()
    export([body, base], "Dummy.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, "Dummy.blend"))

    reset_scene()
    key = make_key()
    export([key], "Key.fbx")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, "Key.blend"))

    print("done")


if __name__ == "__main__":
    main()
