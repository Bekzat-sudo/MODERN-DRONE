"""
Cuts the hatch lids of the T-72B3 model (Sketchfab, CC BY 4.0; see CREDITS.md) out of its one-piece meshes so the game
can blow them open and tear them off: commander's cupola lid and gunner's lid (turret), driver's lid (hull).

  blender -b --factory-startup --python Tools/Blender/t72_hatches.py -- <in.glb> <out.glb> [preview.png]

Run on t72b3.glb, t72b3_lod1.glb and t72b3_lod2.glb. Each lid becomes its own node ("Hatch_Commander",
"Hatch_Gunner" under Turret, "Hatch_Driver" under Hull) with its origin on the hinge (front edge: on the T-72 both
turret lids hinge forward). A lid is everything above its roof level inside the hatch ring: whole loose parts of the
mesh that lie inside the ring, plus every face whose centre does (the lid plate is welded into larger parts).
Positions were measured on orthographic renders of the model (Blender coordinates: x right of the image = vehicle left,
-y forward, z up).
"""
import bpy, bmesh, math, sys

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, DST = argv[0], argv[1]
PREVIEW = argv[2] if len(argv) > 2 else None

# name, mesh, centre x, y, radius x, radius y (ellipse), lowest z of the lid, faces above this z only,
# exclusion (min y, min z) for parts that stay (the commander's sight head at the back of the cupola)
HATCHES = [
    ("Hatch_Commander", "Turret", -0.60, 0.38, 0.31, 0.31, 1.95, 2.10, (0.60, 2.33)),
    ("Hatch_Gunner", "Turret", 0.50, 0.46, 0.27, 0.27, 1.95, 2.05, None),
    ("Hatch_Driver", "Hull", 0.00, -1.46, 0.27, 0.19, None, None, None),
]


def inside(p, h, scale=1.0):
    _, _, cx, cy, rx, ry = h[:6]
    return ((p.x - cx) / (rx * scale)) ** 2 + ((p.y - cy) / (ry * scale)) ** 2 < 1.0


def islands(bm):
    seen, out = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, comp = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            comp.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y.index not in seen:
                    seen.add(y.index)
                    stack.append(y)
        out.append(comp)
    return out


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)

for h in HATCHES:
    name, parent, cx, cy, rx, ry, zmin, zface, exclude = h
    obj = bpy.data.objects[parent]
    mw = obj.matrix_world
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    if zface is None:
        # Hull lid: top surface of the glacis inside the ellipse, measured on this mesh.
        zs = [(mw @ f.calc_center_median()).z for f in bm.faces if inside(mw @ f.calc_center_median(), h)]
        top = max(zs)
        zface, zmin = top - 0.09, top - 0.09
    selected = set()
    for comp in islands(bm):
        if all(inside(mw @ v.co, h, 1.07) and (mw @ v.co).z > zmin for v in comp):
            selected.update(v.index for v in comp)
    for f in bm.faces:
        c = mw @ f.calc_center_median()
        if inside(c, h) and c.z > zface:
            selected.update(v.index for v in f.verts)
    if exclude:
        for f in bm.faces:
            c = mw @ f.calc_center_median()
            if c.y > exclude[0] and c.z > exclude[1]:
                selected.difference_update(v.index for v in f.verts)
    bm.free()

    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    for v in obj.data.vertices:
        v.select = v.index in selected
    for e in obj.data.edges:
        e.select = all(obj.data.vertices[i].select for i in e.vertices)
    for poly in obj.data.polygons:
        poly.select = all(obj.data.vertices[i].select for i in poly.vertices)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(type="FACE")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    lid = next(o for o in bpy.context.selected_objects if o != obj)
    lid.name = name
    lid.data.name = name
    # Keep the lid under the part it belongs to (separate() leaves it under that part's parent).
    world = lid.matrix_world.copy()
    lid.parent = obj
    lid.matrix_world = world

    # Origin on the hinge: front (lowest y) edge of the lid, at its base.
    pts = [lid.matrix_world @ v.co for v in lid.data.vertices]
    ymin = min(p.y for p in pts)
    zlow = min(p.z for p in pts)
    hinge = (cx, ymin, zlow) if name != "Hatch_Driver" else (cx, cy, zlow)
    bpy.context.scene.cursor.location = hinge
    bpy.ops.object.select_all(action="DESELECT")
    lid.select_set(True)
    bpy.context.view_layer.objects.active = lid
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    print(f"[hatches] {name}: {len(lid.data.vertices)} verts, hinge {tuple(round(x, 3) for x in hinge)}", flush=True)

if PREVIEW:
    # Lift the lids for a look at the cut.
    for o in bpy.data.objects:
        if o.name.startswith("Hatch_"):
            o.matrix_world.translation.z += 0.6
            o.color = (1, 0.2, 0.2, 1)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.show_cavity = True
    sc.display.shading.color_type = "OBJECT"
    sc.render.resolution_x, sc.render.resolution_y = 1600, 1000
    import mathutils
    bpy.ops.object.camera_add(location=(-4.0, -3.2, 5.0))
    cam = bpy.context.object
    cam.rotation_euler = (mathutils.Vector((0, -0.2, 1.8)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    sc.camera = cam
    sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
    for o in bpy.data.objects:
        if o.name.startswith("Hatch_"):
            o.matrix_world.translation.z -= 0.6

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.gltf(filepath=DST, export_format="GLB", use_selection=False, export_cameras=False, export_lights=False,
                          export_image_format="AUTO", export_apply=False)
print("[hatches] wrote", DST, flush=True)
