"""
Renders a quick preview of exported models into Art/Blender/Previews (for checking shapes against the concepts).

    blender -b --factory-startup --python Tools/Blender/preview_models.py -- Sword Shield
Without names renders every model. Flat (faceted) studio shading with the material colours, 3/4 view from the front.
"""

import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(PROJECT, "Art", "Blender")
OUT = os.path.join(BLEND, "Previews")

ALL = ["Player", "Sword", "Shield", "Shotgun", "Club", "Dummy", "Key", "Flask", "Chest", "PickupBase"]
# Per-model view direction (from the object to the camera); front of the models is -Y.
VIEWS = {"Shotgun": (1.4, -0.5, 0.7), "Key": (0.5, -1.4, 0.5), "PickupBase": (0.8, -1.2, 1.1)}

os.makedirs(OUT, exist_ok=True)
names = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ALL
for name in names:
    bpy.ops.wm.open_mainfile(filepath=os.path.join(BLEND, name + ".blend"))
    scene = bpy.context.scene
    objs = [o for o in scene.objects if o.type == "MESH"]
    corners = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    center = (lo + hi) / 2
    size = (hi - lo).length
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = size * 1.05
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    direction = Vector(VIEWS.get(name, (0.9, -1.4, 0.8))).normalized()
    cam.location = center + direction * size * 3
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError:
        pass
    shading = scene.display.shading
    shading.color_type = "MATERIAL"
    shading.light = "STUDIO"
    shading.show_object_outline = False
    shading.show_cavity = True
    shading.cavity_type = "WORLD"
    scene.render.film_transparent = False
    scene.render.resolution_x = 512
    scene.render.resolution_y = 512
    scene.render.filepath = os.path.join(OUT, name + ".png")
    bpy.ops.render.render(write_still=True)
    print("preview", scene.render.filepath)
