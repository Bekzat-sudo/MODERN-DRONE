"""
Prepares "UAV Hornet" by warmwork (Sketchfab, CC BY) for MODERN DRONE. The download holds two copies side by side:
an assembled airframe (fuselage bottom with the internals, wing, tail, three-blade pusher prop) with its top shell
lifted off, and an exploded spare set. This keeps the assembled one, sets the shell back on, scales it to the 2.2 m
wingspan reported for the Hornet (Defense Express, which gives ~1.4 m length; the model then measures 1.34 m),
puts the nose toward -Y (Unity +Z), the origin at the wing's centre, and exports two nodes: Body and Prop (the
pusher prop, pivoted on its hub so the game can spin it).

  blender -b --factory-startup --python Tools/Blender/hornet_import.py -- <in.glb> <out.glb>
"""
import bpy, sys
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
src, dst = a[0], a[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
bpy.context.view_layer.update()

meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
for o in meshes:
    o.data = o.data.copy()
    o.data.transform(o.matrix_world)
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH':
        bpy.data.objects.remove(o)
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='LOOSE')
bpy.ops.object.mode_set(mode='OBJECT')


def box(o):
    vs = [v.co for v in o.data.vertices]
    lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
    hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
    return (lo + hi) / 2, hi - lo, lo, hi


AXIS_X = -3.48  # the assembled copy's centreline
body, prop = [], []
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH':
        continue
    c, s, lo, hi = box(o)
    # The assembled copy: within its wingspan and below the exploded set (which sits ~2 units higher).
    if not (-10.0 < c.x < 3.0 and c.z < 7.6):
        bpy.data.objects.remove(o)
        continue
    if c.y > 3.75 and s.y < 0.3 and abs(c.x - AXIS_X) < 1.6 and 5.0 < c.z < 7.5:
        prop.append(o)  # blades and hub in the disc behind the tail
    else:
        if s.y > 5.0 and abs(c.x - AXIS_X) < 0.2 and c.z > 6.8:
            o.data.transform(Matrix.Translation((0.0, 0.0, -0.47)))  # top shell back onto the fuselage
        body.append(o)


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    j = bpy.context.view_layer.objects.active
    j.name = j.data.name = name
    return j


body = join(body, "Body")
prop = join(prop, "Prop")
c, s, lo, hi = box(body)
scale = 2.2 / s.x
# Origin: centreline, at the wing's mid-height and mid-chord (the wing panels are the widest islands).
centre = Vector((AXIS_X, -0.15, 5.96))
hub = box(prop)[0]
for o in (body, prop):
    o.data.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-centre))
# The prop's own pivot on its hub.
hub_local = (hub - centre) * scale
prop.data.transform(Matrix.Translation(-hub_local))
prop.location = hub_local
c, s, lo, hi = box(body)
print(f"[hornet_import] span {s.x:.2f} m, length {s.y:.2f} m, height {s.z:.2f} m, "
      f"{sum(len(p.vertices) - 2 for p in body.data.polygons)} + {sum(len(p.vertices) - 2 for p in prop.data.polygons)} tris, "
      f"prop hub at {tuple(round(x, 3) for x in hub_local)}", flush=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=dst, export_format='GLB', export_materials='EXPORT', use_selection=True)
