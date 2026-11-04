"""
Prepares a downloaded vehicle model for MODERN DRONE: metres, front toward Blender -Y (Unity +Z after glTF export),
wheels on the ground, centred on the chassis, one joined mesh (one draw call per material instead of hundreds of nodes),
plus the two decimated LODs VehicleKit.AttachModel looks for (<name>_lod1/_lod2.glb, 25 % / 6 %, no textures of their
own: Unity gives them the full model's materials by node name).

Run headless:
  blender -b --factory-startup --python Tools/Blender/vehicle_import.py -- <in.glb> <out_dir> <name> <rot_z_deg> <length_m> [paint...]
rot_z_deg turns the model so its front points to -Y; length_m is the real overall length it is scaled to (0 = keep).
paint: optional "Material=r,g,b" (sRGB 0-1) repaints a material, matte (e.g. a civilian body in olive).
bake: optional flag for models with flat colours only: a new UV unwrap and one 2048 texture baked from the material
colours with ambient occlusion in the crevices, road dust rising from the wheels and blotches of grime; the opaque
materials become one textured "<name>_Paint" (glass and lamps keep their own).
"""
import bpy, sys, os, math
from mathutils import Vector, Matrix

a = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name, rot_z, length = a[0], a[1], a[2], float(a[3]), float(a[4])
bake_flag = "bake" in a[5:]
paint = dict((k, tuple(float(c) for c in v.split(","))) for k, v in (x.split("=") for x in a[5:] if "=" in x))
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

def linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


for m in bpy.data.materials:
    if m.name in paint and m.node_tree and m.node_tree.nodes.get("Principled BSDF"):
        bsdf = m.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = tuple(linear(c) for c in paint[m.name]) + (1.0,)
        bsdf.inputs["Roughness"].default_value = 0.75
        print(f"[vehicle_import] painted {m.name} {paint[m.name]}", flush=True)

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


def principled(m):
    return m.node_tree.nodes.get("Principled BSDF") if m and m.use_nodes else None


def rgba_in(node, label):
    return next(x for x in node.inputs if x.name == label and x.type == 'RGBA')


def rgba_out(node):
    return next(x for x in node.outputs if x.type == 'RGBA')


def bake(body, size=2048):
    """Bakes colour x occlusion + dust + grime into one texture on a fresh unwrap (see the header)."""
    me = body.data
    src_uv = me.uv_layers.active.name if me.uv_layers.active else None
    uv = me.uv_layers.new(name="Bake")
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    me.uv_layers.active = uv
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.002)
    bpy.ops.object.mode_set(mode='OBJECT')
    if src_uv:  # source textures (number plates, lamps) still sample their own map while baking
        me.uv_layers[src_uv].active_render = True

    img = bpy.data.images.new(name + "_albedo", size, size, alpha=False)
    opaque = [m for m in me.materials if principled(m) and principled(m).inputs["Alpha"].default_value >= 0.99]
    for m in opaque:
        nt = m.node_tree
        bsdf = principled(m)
        base = bsdf.inputs["Base Color"]
        colour = base.links[0].from_socket if base.links else None
        tc = nt.nodes.new("ShaderNodeTexCoord")
        ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
        ao.samples = 16
        ao.inputs["Distance"].default_value = 0.35
        if colour is not None:
            nt.links.new(colour, ao.inputs["Color"])
        else:
            ao.inputs["Color"].default_value = base.default_value
        # Occlusion darkens the crevices (never fully black: sky light gets in).
        occ = nt.nodes.new("ShaderNodeMath"); occ.operation = 'MULTIPLY_ADD'
        occ.inputs[1].default_value = 0.7; occ.inputs[2].default_value = 0.3
        nt.links.new(ao.outputs["AO"], occ.inputs[0])
        shaded = nt.nodes.new("ShaderNodeMix"); shaded.data_type = 'RGBA'; shaded.blend_type = 'MULTIPLY'
        shaded.inputs["Factor"].default_value = 1.0
        nt.links.new(ao.outputs["Color"], rgba_in(shaded, "A"))
        nt.links.new(occ.outputs[0], rgba_in(shaded, "B"))
        # Paint that is not one flat colour: large, faint patches of fading and touch-up.
        patches = nt.nodes.new("ShaderNodeTexNoise")
        patches.inputs["Scale"].default_value = 0.8; patches.inputs["Detail"].default_value = 3.0
        nt.links.new(tc.outputs["Object"], patches.inputs["Vector"])
        tone = nt.nodes.new("ShaderNodeMapRange")
        tone.inputs["To Min"].default_value = 0.82; tone.inputs["To Max"].default_value = 1.15
        nt.links.new(patches.outputs["Fac"], tone.inputs["Value"])
        toned = nt.nodes.new("ShaderNodeMix"); toned.data_type = 'RGBA'; toned.blend_type = 'MULTIPLY'
        toned.inputs["Factor"].default_value = 1.0
        nt.links.new(rgba_out(shaded), rgba_in(toned, "A"))
        nt.links.new(tone.outputs[0], rgba_in(toned, "B"))
        # Worn edges: convex corners rubbed lighter.
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        wear = nt.nodes.new("ShaderNodeMapRange")
        wear.inputs["From Min"].default_value = 0.52; wear.inputs["From Max"].default_value = 0.62
        wear.inputs["To Min"].default_value = 0.0; wear.inputs["To Max"].default_value = 0.4
        nt.links.new(geo.outputs["Pointiness"], wear.inputs["Value"])
        bright = nt.nodes.new("ShaderNodeMix"); bright.data_type = 'RGBA'; bright.blend_type = 'SCREEN'
        nt.links.new(wear.outputs[0], bright.inputs["Factor"])
        nt.links.new(rgba_out(toned), rgba_in(bright, "A"))
        rgba_in(bright, "B").default_value = (0.35, 0.33, 0.28, 1.0)
        shaded = bright
        # Road dust: thick at the wheels and sills, thinning out by ~1.8 m, broken up by noise.
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        nt.links.new(tc.outputs["Object"], sep.inputs[0])
        height = nt.nodes.new("ShaderNodeMapRange")
        height.inputs["From Min"].default_value = 0.0; height.inputs["From Max"].default_value = 1.8
        height.inputs["To Min"].default_value = 0.9; height.inputs["To Max"].default_value = 0.0
        nt.links.new(sep.outputs["Z"], height.inputs["Value"])
        noise = nt.nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 3.0; noise.inputs["Detail"].default_value = 8.0
        nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
        dustmask = nt.nodes.new("ShaderNodeMath"); dustmask.operation = 'MULTIPLY'
        nt.links.new(height.outputs[0], dustmask.inputs[0])
        nt.links.new(noise.outputs["Fac"], dustmask.inputs[1])
        dusty = nt.nodes.new("ShaderNodeMix"); dusty.data_type = 'RGBA'
        rgba_in(dusty, "B").default_value = (0.26, 0.205, 0.14, 1.0)  # dry loam, linear
        nt.links.new(dustmask.outputs[0], dusty.inputs["Factor"])
        nt.links.new(rgba_out(shaded), rgba_in(dusty, "A"))
        # Grime blotches: dark, oily, anywhere.
        noise2 = nt.nodes.new("ShaderNodeTexNoise")
        noise2.inputs["Scale"].default_value = 9.0; noise2.inputs["Detail"].default_value = 4.0
        nt.links.new(tc.outputs["Object"], noise2.inputs["Vector"])
        grime = nt.nodes.new("ShaderNodeMapRange")
        grime.inputs["From Min"].default_value = 0.52; grime.inputs["From Max"].default_value = 0.68
        grime.inputs["To Min"].default_value = 0.0; grime.inputs["To Max"].default_value = 0.5
        nt.links.new(noise2.outputs["Fac"], grime.inputs["Value"])
        dirty = nt.nodes.new("ShaderNodeMix"); dirty.data_type = 'RGBA'
        rgba_in(dirty, "B").default_value = (0.03, 0.028, 0.022, 1.0)
        nt.links.new(grime.outputs[0], dirty.inputs["Factor"])
        nt.links.new(rgba_out(dusty), rgba_in(dirty, "A"))
        emit = nt.nodes.new("ShaderNodeEmission")
        nt.links.new(rgba_out(dirty), emit.inputs["Color"])
        out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL')
        nt.links.new(emit.outputs[0], out.inputs["Surface"])
        target = nt.nodes.new("ShaderNodeTexImage"); target.image = img
        uvn = nt.nodes.new("ShaderNodeUVMap"); uvn.uv_map = "Bake"
        nt.links.new(uvn.outputs[0], target.inputs["Vector"])
        nt.nodes.active = target

    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.samples = 64
    sc.cycles.device = 'GPU'
    try:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        prefs.compute_device_type = "OPTIX"
        prefs.get_devices()
        for dv in prefs.devices:
            dv.use = dv.type == "OPTIX"
    except Exception:
        sc.cycles.device = 'CPU'
    sc.render.bake.margin = 6
    bpy.ops.object.bake(type='EMIT')
    import tempfile
    img.filepath_raw = os.path.join(tempfile.gettempdir(), name + "_albedo.png")  # packed into the .glb, not an asset
    img.file_format = 'PNG'
    img.save()
    img.pack()

    # One textured paint material for every opaque face; glass and lamps keep theirs.
    paint_mat = bpy.data.materials.new(name + "_Paint")
    paint_mat.use_nodes = True
    pb = principled(paint_mat)
    pb.inputs["Roughness"].default_value = 0.72
    tex = paint_mat.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = img
    uvn = paint_mat.node_tree.nodes.new("ShaderNodeUVMap"); uvn.uv_map = "Bake"
    paint_mat.node_tree.links.new(uvn.outputs[0], tex.inputs["Vector"])
    paint_mat.node_tree.links.new(tex.outputs["Color"], pb.inputs["Base Color"])
    keep = [m for m in me.materials if m not in opaque]
    old = list(me.materials)
    me.materials.clear()
    me.materials.append(paint_mat)
    for m in keep:
        me.materials.append(m)
    index = {i: (1 + keep.index(m) if m in keep else 0) for i, m in enumerate(old)}
    for poly in me.polygons:
        poly.material_index = index.get(poly.material_index, 0)
    for layer in list(me.uv_layers):
        if layer.name != "Bake":
            me.uv_layers.remove(layer)
    me.uv_layers["Bake"].active = True
    me.uv_layers["Bake"].active_render = True
    print(f"[vehicle_import] baked {name}_albedo.png ({size}), {len(opaque)} opaque materials -> 1, kept {len(keep)}", flush=True)


if bake_flag:
    bake(body)


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
