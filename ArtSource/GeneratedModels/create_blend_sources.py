"""Create five editable .blend source files in Blender 4.x or later.
Run: blender --background --python ArtSource/GeneratedModels/create_blend_sources.py
"""
from pathlib import Path
import bpy
root = Path(__file__).resolve().parents[2]
models = root / "Assets" / "Models"
out = root / "ArtSource" / "GeneratedModels"
for model in ("SF_ScoutDrone", "SF_FlankerDrone", "SF_GuardDrone", "SF_InterceptorDrone", "SF_SlidingLabDoor"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.wm.obj_import(filepath=str(models / (model + ".obj")))
    mesh_objects = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if not mesh_objects or any(not obj.data.uv_layers for obj in mesh_objects):
        raise RuntimeError(f"Missing mesh / UVs: {model}")
    bpy.ops.wm.save_as_mainfile(filepath=str(out / (model + ".blend")))
    print("Wrote", model + ".blend")
