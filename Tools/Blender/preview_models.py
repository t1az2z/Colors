"""Renders a quick Workbench preview of every exported model into Art/Blender/Previews (for checking shapes)."""

import math
import os

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(PROJECT, "Art", "Blender")
OUT = os.path.join(BLEND, "Previews")

os.makedirs(OUT, exist_ok=True)
for name in ("Player", "Sword", "Shield", "Dummy", "Club"):
    bpy.ops.wm.open_mainfile(filepath=os.path.join(BLEND, name + ".blend"))
    scene = bpy.context.scene
    objs = [o for o in scene.objects if o.type == "MESH"]
    lo = Vector((min(min((o.matrix_world @ Vector(c)).x for c in o.bound_box) for o in objs),
                 min(min((o.matrix_world @ Vector(c)).y for c in o.bound_box) for o in objs),
                 min(min((o.matrix_world @ Vector(c)).z for c in o.bound_box) for o in objs)))
    hi = Vector((max(max((o.matrix_world @ Vector(c)).x for c in o.bound_box) for o in objs),
                 max(max((o.matrix_world @ Vector(c)).y for c in o.bound_box) for o in objs),
                 max(max((o.matrix_world @ Vector(c)).z for c in o.bound_box) for o in objs)))
    center = (lo + hi) / 2
    size = (hi - lo).length
    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    direction = Vector((0.9, -1.4, 0.8)).normalized()
    cam.location = center + direction * size * 2.2
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.light = "STUDIO"
    scene.render.resolution_x = 400
    scene.render.resolution_y = 400
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("preview", scene.render.filepath)
