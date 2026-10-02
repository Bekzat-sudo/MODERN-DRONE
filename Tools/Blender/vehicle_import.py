"""
Prepares a downloaded vehicle model for MODERN DRONE: metres, front toward Blender -Y (Unity +Z after glTF export),
wheels on the ground, centred on the chassis, one joined mesh (one draw call per material instead of hundreds of nodes),
plus the two decimated LODs VehicleKit.AttachModel looks for (<name>_lod1/_lod2.glb, 25 % / 6 %, no textures of their
own: Unity gives them the full model's materials by node name).

Run headless:
  blender -b --factory-startup --python Tools/Blender/vehicle_import.py -- <in.glb> <out_dir> <name> <rot_z_deg> <length_m>
rot_z_deg turns the model so its front points to -Y; length_m is the real overall length it is scaled to (0 = keep).
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name, rot_z, length = a[0], a[1], a[2], float(a[3]), float(a[4])
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
bpy.context.view_layer.update()

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
# Bake every node's world transform into its mesh, then drop the hierarchy.
for o in meshes:
    if o.data.users > 1:
        o.data = o.data.copy()
    o.data.transform(o.matrix_world)
for o in meshes:
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o)

# Join into one object.
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "Body"
body.data.name = "Body"

# Turn, then scale to the real length.
body.data.transform(Matrix.Rotation(math.radians(rot_z), 4, 'Z'))
if length > 0:
    ys = [v.co.y for v in body.data.vertices]
    body.data.transform(Matrix.Scale(length / (max(ys) - min(ys)), 4))
vs = [v.co for v in body.data.vertices]
zmin = min(v.z for v in vs)
# Centre across on the running gear (the lowest metre: wheels, axles), not on a ladder or mirror sticking out.
low = [v for v in vs if v.z < zmin + 1.0]
cx = (min(v.x for v in low) + max(v.x for v in low)) / 2
cy = (min(v.y for v in vs) + max(v.y for v in vs)) / 2
body.data.transform(Matrix.Translation(Vector((-cx, -cy, -zmin))))
vs = [v.co for v in body.data.vertices]
print(f"[vehicle_import] {name}: size x {max(v.x for v in vs) - min(v.x for v in vs):.2f} "
      f"y {max(v.y for v in vs) - min(v.y for v in vs):.2f} z {max(v.z for v in vs):.2f} m, "
      f"{sum(len(p.vertices) - 2 for p in body.data.polygons)} tris, {len(body.data.materials)} materials", flush=True)


def export(path, images):
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', export_image_format='AUTO' if images else 'NONE',
                              export_materials='EXPORT', use_selection=True)


bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
export(os.path.join(out_dir, name + ".glb"), True)

for suffix, ratio in (("_lod1", 0.25), ("_lod2", 0.06)):
    lod = body.copy()
    lod.data = body.data.copy()
    bpy.context.scene.collection.objects.link(lod)
    mod = lod.modifiers.new("Decimate", 'DECIMATE')
    mod.ratio = ratio
    bpy.context.view_layer.objects.active = lod
    bpy.ops.object.modifier_apply(modifier=mod.name)
    # Exported under the full model's node name (Unity matches the materials by it).
    body.name = "Body_full"
    lod.name = "Body"
    bpy.ops.object.select_all(action='DESELECT')
    lod.select_set(True)
    export(os.path.join(out_dir, name + suffix + ".glb"), False)
    print(f"[vehicle_import] {name}{suffix}: {sum(len(p.vertices) - 2 for p in lod.data.polygons)} tris", flush=True)
    bpy.data.objects.remove(lod)
    body.name = "Body"
