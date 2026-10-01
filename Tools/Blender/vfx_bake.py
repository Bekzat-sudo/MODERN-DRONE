"""
Bakes six-way lit flipbooks for MODERN DRONE's particle effects from Mantaflow gas simulations.

Run headless:
  blender -b --factory-startup --python Tools/Blender/vfx_bake.py -- <preset> <out_dir> [tile] [samples] [resolution]
Presets: explosion (fireball cooling into soot), smoke (billowing puff), flame (steady fire, flickering tongues).

Output in <out_dir>: <preset>_A.png (R right, G top, B back light, A alpha), <preset>_B.png (R left, G bottom, B front,
A emission), <preset>.json (tile count, sim frame per tile, lighting scale). Layout follows Unity's six-way lighting
convention (docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.0/manual/six-way-lighting.html); the six lights
are set up like Unity's VFXToolbox Blender add-on (a sun per axis around an orthographic camera). Each map is one
render with three coloured suns (light is additive and the smoke albedo is white, so every channel is one direction).
"""
import bpy, sys, os, json, math
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PRESET = argv[0] if argv else "smoke"
OUT = argv[1] if len(argv) > 1 else os.path.abspath("vfx_out")
TILE = int(argv[2]) if len(argv) > 2 else 256
SAMPLES = int(argv[3]) if len(argv) > 3 else 128
RES = int(argv[4]) if len(argv) > 4 else 128
GRID = 8  # 8 x 8 tiles
os.makedirs(OUT, exist_ok=True)

P = {
    # D: domain edge (m); emitter radius / height; bursts: (first, last) frame of emission; vel: outward m/s
    "explosion": dict(D=4.0, r=0.42, z=1.0, flow="BOTH", first=1, last=4, vel=9.0, fuel=2.5, temp=4.0, dens=1.6,
                      sim=48, curve=1.6, vort=0.35, flame_vort=0.9, burn=0.55, flame_smoke=1.4, smoke_col=(0.07, 0.065, 0.06),
                      alpha=0.4, beta=1.0, noise=2.2, dens_mul=14.0, emit_mul=7.0, time_scale=2.0),
    "smoke": dict(D=4.0, r=0.5, z=1.0, flow="SMOKE", first=1, last=10, vel=1.2, fuel=0.0, temp=1.6, dens=1.2,
                  sim=50, curve=1.0, vort=0.35, flame_vort=0.5, burn=0.75, flame_smoke=1.0, smoke_col=(0.7, 0.7, 0.7),
                  alpha=0.6, beta=1.2, noise=1.0, dens_mul=16.0, emit_mul=0.0, time_scale=2.0, dissolve=45),
    "flame": dict(D=2.4, r=0.38, z=0.32, flow="FIRE", first=1, last=10_000, vel=0.6, fuel=1.4, temp=2.0, dens=0.3,
                  sim=96, curve=1.0, vort=0.25, flame_vort=1.3, burn=0.6, flame_smoke=0.4, smoke_col=(0.1, 0.1, 0.1),
                  alpha=0.1, beta=2.6, noise=2.0, dens_mul=6.0, emit_mul=9.0, start=32, time_scale=2.0, dissolve=18),
}[PRESET]
# Tuning overrides: VFX_BAKE_PARAMS='{"vel": 7}'
P.update(json.loads(os.environ.get("VFX_BAKE_PARAMS", "{}")))

REPACK = bool(os.environ.get("VFX_REPACK"))  # reuse the rendered frames in <out>/tmp_<preset>, only pack again

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.frame_start, scene.frame_end = 1, P["sim"]
D = P["D"]

# ---- domain
bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, D / 2))
dom = bpy.context.object
dom.name = "Domain"
dom.scale = (D, D, D)
fm = dom.modifiers.new("Fluid", "FLUID")
fm.fluid_type = "DOMAIN"
ds = fm.domain_settings
ds.domain_type = "GAS"
ds.resolution_max = RES
ds.use_adaptive_timesteps = True
ds.time_scale = P.get("time_scale", 1.0)
ds.vorticity = P["vort"]
ds.flame_vorticity = P["flame_vort"]
ds.burning_rate = P["burn"]
ds.flame_smoke = P["flame_smoke"]
ds.flame_smoke_color = P["smoke_col"]
ds.alpha = P["alpha"]
ds.beta = P["beta"]
ds.use_noise = True
ds.noise_scale = 2
ds.noise_strength = P["noise"]
# Dissolve: thin wisps fade (log: slowly at first) so the puff keeps a defined edge instead of a haze.
ds.use_dissolve_smoke = P.get("dissolve", 0) > 0
ds.dissolve_speed = max(1, P.get("dissolve", 0))
ds.use_dissolve_smoke_log = PRESET == "smoke"
for side in ("front", "back", "right", "left", "top"):
    setattr(ds, "use_collision_border_" + side, False)
ds.use_collision_border_bottom = True
ds.cache_type = "ALL"
ds.cache_directory = os.path.join(OUT, "cache_" + PRESET)
ds.cache_frame_start, ds.cache_frame_end = 1, P["sim"]

# ---- emitter (lumpy: a noise-displaced icosphere so the burst is not a perfect ball)
bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=P["r"], location=(0, 0, P["z"]))
em = bpy.context.object
em.name = "Emitter"
tex = bpy.data.textures.new("Lumps", "CLOUDS")
tex.noise_scale = 0.35
disp = em.modifiers.new("Disp", "DISPLACE")
disp.texture = tex
disp.strength = P["r"] * 0.8
if PRESET == "flame":
    em.scale = (1.6, 1.6, 0.35)  # a flat burning patch
fe = em.modifiers.new("Fluid", "FLUID")
fe.fluid_type = "FLOW"
fs = fe.flow_settings
fs.flow_type = P["flow"]
fs.flow_behavior = "INFLOW"
fs.flow_source = "MESH"
fs.surface_distance = 0.6
fs.use_initial_velocity = True
fs.velocity_normal = P["vel"]
fs.velocity_factor = 0.0
fs.smoke_color = P["smoke_col"] if PRESET != "smoke" else (0.75, 0.75, 0.75)
fs.use_texture = True
fs.noise_texture = tex
fs.texture_size = 0.6


fs.fuel_amount = P["fuel"]
fs.temperature = P["temp"]
fs.density = P["dens"]
# Emission stops when the emitter leaves the domain (flow-setting keyframes are not honoured by the bake).
if P["last"] < P["sim"]:
    for f, z in ((P["last"], P["z"]), (P["last"] + 1, -100.0)):
        em.location = (0, 0, z)
        em.keyframe_insert("location", frame=f)
    for fc in em.animation_data.action.fcurves if hasattr(em.animation_data.action, "fcurves") else []:
        for k in fc.keyframe_points:
            k.interpolation = "CONSTANT"
if PRESET == "flame":
    # Gusts: the patch moves a little so the tongues lean and flicker.
    for f in range(1, P["sim"] + 1, 12):
        em.location = (0.08 * math.sin(f * 0.7), 0.08 * math.cos(f * 0.45), P["z"])
        em.keyframe_insert("location", frame=f)

# ---- bake
if not REPACK:
    with bpy.context.temp_override(object=dom, active_object=dom, selected_objects=[dom]):
        bpy.ops.fluid.bake_all()
em.hide_render = True

if os.environ.get("VFX_SIMCHECK"):
    # Tuning aid: mass, centroid height and extent of the smoke every few frames, no rendering.
    import glob, openvdb
    for f in sorted(glob.glob(os.path.join(ds.cache_directory, "data", "fluid_data_*.vdb")))[::6]:
        grids, _ = openvdb.readAll(f)
        for name in ("density", "flame"):
            g = next((g for g in grids if g.name == name), None)
            if g is None:
                continue
            lo, hi = g.evalActiveVoxelBoundingBox()
            if any(h < l for h, l in zip(hi, lo)):
                continue
            a = np.zeros(tuple(np.array(hi) - np.array(lo) + 1), np.float32)
            g.copyToArray(a, ijk=lo)
            tot = float(a.sum())
            if tot <= 0:
                continue
            k = np.indices(a.shape)
            zc = float((k[2] * a).sum() / tot + lo[2])
            occ = a > 0.05 * a.max()
            ext = [int(np.ptp(np.nonzero(occ)[i])) + 1 for i in range(3)] if occ.any() else [0, 0, 0]
            res = max(hi) + 1
            print(f"[simcheck] {f[-8:-4]} {name}: mass {tot:8.1f} z {zc / RES:4.2f} extent {[round(e / RES, 2) for e in ext]}", flush=True)
    sys.exit(0)

# ---- volume material: white, scattering only for the lighting passes; flame as emission for the emission pass
mat = bpy.data.materials.new("Gas")
mat.use_nodes = True
nt = mat.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
out = nt.nodes.new("ShaderNodeOutputMaterial")
vol = nt.nodes.new("ShaderNodeVolumePrincipled")
vol.inputs["Color"].default_value = (1, 1, 1, 1)
vol.inputs["Anisotropy"].default_value = 0.25
dens = nt.nodes.new("ShaderNodeAttribute"); dens.attribute_name = "density"
flame = nt.nodes.new("ShaderNodeAttribute"); flame.attribute_name = "flame"
dmul = nt.nodes.new("ShaderNodeMath"); dmul.operation = "MULTIPLY"; dmul.inputs[1].default_value = P["dens_mul"]
# Burning gas is thin: give the flame region some density of its own so it is not a hole in the alpha.
fadd = nt.nodes.new("ShaderNodeMath"); fadd.operation = "MULTIPLY_ADD"; fadd.inputs[1].default_value = 3.0
emul = nt.nodes.new("ShaderNodeMath"); emul.operation = "MULTIPLY"
emit_switch = nt.nodes.new("ShaderNodeValue"); emit_switch.name = "EmitSwitch"; emit_switch.outputs[0].default_value = 0.0
eamt = nt.nodes.new("ShaderNodeMath"); eamt.operation = "MULTIPLY"; eamt.inputs[1].default_value = P["emit_mul"]
nt.links.new(dens.outputs["Fac"], dmul.inputs[0])
nt.links.new(flame.outputs["Fac"], fadd.inputs[0])
nt.links.new(dmul.outputs[0], fadd.inputs[2])
nt.links.new(fadd.outputs[0], vol.inputs["Density"])
nt.links.new(flame.outputs["Fac"], eamt.inputs[0])
nt.links.new(eamt.outputs[0], emul.inputs[0])
nt.links.new(emit_switch.outputs[0], emul.inputs[1])
nt.links.new(emul.outputs[0], vol.inputs["Emission Strength"])
vol.inputs["Emission Color"].default_value = (1, 1, 1, 1)
nt.links.new(vol.outputs[0], out.inputs["Volume"])
dom.data.materials.append(mat)

# ---- camera, lights, render settings
bpy.ops.object.camera_add(location=(0, -12, D / 2), rotation=(math.radians(90), 0, 0))
cam = bpy.context.object
cam.data.type = "ORTHO"
cam.data.ortho_scale = D
scene.camera = cam

world = bpy.data.worlds.new("Black")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[1].default_value = 0.0
scene.world = world

# Sun per axis (rotation turns the sun's -Z travel direction): from +X, -X, +Z, -Z, camera side (-Y), behind (+Y).
SUN_ROT = {"right": (0, 90, 0), "left": (0, -90, 0), "top": (0, 0, 0), "bottom": (180, 0, 0),
           "front": (90, 0, 0), "back": (-90, 0, 0)}
suns = {}
for name, rot in SUN_ROT.items():
    bpy.ops.object.light_add(type="SUN", rotation=tuple(math.radians(a) for a in rot))
    s = bpy.context.object
    s.name = "Sun_" + name
    s.data.energy = 3.0
    s.data.angle = math.radians(2)
    s.data.specular_factor = 0.0
    suns[name] = s

r = scene.render
r.engine = "CYCLES"
r.resolution_x = r.resolution_y = TILE
r.resolution_percentage = 100
r.film_transparent = True
r.image_settings.file_format = "OPEN_EXR"
r.image_settings.color_depth = "16"
r.image_settings.color_mode = "RGBA"
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"
c = scene.cycles
c.device = "GPU"
prefs = bpy.context.preferences.addons["cycles"].preferences
prefs.compute_device_type = "OPTIX"
prefs.get_devices()
for dv in prefs.devices:
    dv.use = dv.type == "OPTIX"
c.samples = SAMPLES
c.use_denoising = False
c.volume_bounces = 4
c.transparent_max_bounces = 16
c.max_bounces = 8
c.volume_step_rate = 1.0
c.use_adaptive_sampling = True

start = P.get("start", 2)
n = GRID * GRID
frames = [round(start + (P["sim"] - start) * (i / (n - 1)) ** P["curve"]) for i in range(n)]

PASSES = {"A": {"right": (1, 0, 0), "top": (0, 1, 0), "back": (0, 0, 1)},
          "B": {"left": (1, 0, 0), "bottom": (0, 1, 0), "front": (0, 0, 1)},
          "E": {}}
tmp = os.path.join(OUT, "tmp_" + PRESET)
os.makedirs(tmp, exist_ok=True)


def render(path):
    r.filepath = path
    bpy.ops.render.render(write_still=True)


def load(path):
    img = bpy.data.images.load(path)
    px = np.empty(TILE * TILE * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(TILE, TILE, 4)  # rows bottom-up


if not REPACK:
    for i, f in enumerate(frames):
        scene.frame_set(f)
        for key, lights in PASSES.items():
            for name, s in suns.items():
                s.hide_render = name not in lights
                if name in lights:
                    s.data.color = lights[name]
            emit_switch.outputs[0].default_value = 1.0 if key == "E" else 0.0
            render(os.path.join(tmp, f"{key}_{i:02d}.exr"))
        print(f"[vfx_bake] {PRESET} tile {i + 1}/{n} (sim frame {f})", flush=True)

frames_px = {key: [load(os.path.join(tmp, f"{key}_{i:02d}.exr")) for i in range(n)] for key in PASSES}


def blur(img, sigma):
    """Separable Gaussian: path-traced volumes at this sample count are grainy; light and opacity vary smoothly."""
    r = int(sigma * 3)
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2)
    k /= k.sum()
    extra = ((0, 0),) * (img.ndim - 2)
    pad = np.pad(img, ((r, r), (0, 0)) + extra, mode="edge")
    img = sum(k[i] * pad[i:i + img.shape[0]] for i in range(2 * r + 1))
    pad = np.pad(img, ((0, 0), (r, r)) + extra, mode="edge")
    return sum(k[i] * pad[:, i:i + img.shape[1]] for i in range(2 * r + 1))


# Denoise: premultiplied light (so edges do not bleed dark), stronger on light than on opacity.
for key in PASSES:
    for i, px in enumerate(frames_px[key]):
        a = px[..., 3:4]
        rgb = blur(px[..., :3] * (a if key != "E" else 1.0), 1.6)
        a2 = blur(a[..., 0], 0.9)[..., None] if key == "A" else a
        if key != "E":
            # Thin edges carry no reliable light (division by a tiny opacity would make them glow): floor it.
            rgb = rgb / np.maximum(blur(a[..., 0], 1.6)[..., None], 0.15)
        frames_px[key][i] = np.concatenate([rgb, a2], axis=2)

# ---- crop: the square around everything any frame covers (the gas never fills the whole domain), so the
# particle's quad is mostly gas, not empty margin that costs fill rate.
cover = np.zeros((TILE, TILE), bool)
emax = max(float(np.percentile(px[..., 0], 99.5)) for px in frames_px["E"]) or 1.0
if PRESET == "flame":
    # Flames are too thin to hide much by density alone; their glow is what shows: opacity follows it.
    for px, ex in zip(frames_px["A"], frames_px["E"]):
        px[..., 3] = np.maximum(px[..., 3], np.clip(ex[..., 0] / emax * 0.85, 0, 1))
for px, ex in zip(frames_px["A"], frames_px["E"]):
    cover |= px[..., 3] > 0.2  # thin haze filling the domain does not count
    cover |= ex[..., 0] > 0.08 * emax  # flames are thin gas but bright
ys, xs = np.nonzero(cover)
cx, cy = (xs.min() + xs.max() + 1) / 2, (ys.min() + ys.max() + 1) / 2
half = max(xs.max() + 1 - xs.min(), ys.max() + 1 - ys.min()) / 2 * 1.04
x0, y0c = int(max(0, round(cx - half))), int(max(0, round(cy - half)))
side = int(min(TILE - x0, TILE - y0c, round(2 * half)))
# Where the gas starts (first frame's opacity centroid), in tile units from the centre, +y up: the particle's pivot.
a0 = frames_px["A"][0][y0c:y0c + side, x0:x0 + side, 3]
gy_, gx_ = np.indices(a0.shape)
tot = a0.sum() + 1e-9
pivot = (float((gx_ * a0).sum() / tot / side - 0.5), float((gy_ * a0).sum() / tot / side - 0.5))


def crop(px):
    c = px[y0c:y0c + side, x0:x0 + side]
    # Nearest-neighbour index map back to TILE x TILE (box-filtered by the mip chain in the engine).
    idx = (np.arange(TILE) * side / TILE).astype(int)
    c = c[idx][:, idx].copy()
    # Fade the opacity to nothing at the tile's edge: cropped haze must not end in a straight line.
    e = np.minimum(np.arange(TILE), np.arange(TILE)[::-1]) / (TILE * 0.08)
    edge = np.clip(np.minimum(e[:, None], e[None, :]), 0, 1)
    c[..., 3] *= edge * edge * (3 - 2 * edge)
    return c


atlas = {k: np.zeros((GRID * TILE, GRID * TILE, 4), np.float32) for k in PASSES}
for i in range(n):
    gx, gy = i % GRID, i // GRID
    ya = (GRID - 1 - gy) * TILE  # tile 0 at the top-left, as Unity's texture sheet animation reads it
    for key in PASSES:
        atlas[key][ya:ya + TILE, gx * TILE:(gx + 1) * TILE] = crop(frames_px[key][i])

# ---- pack: lighting normalised to its 99.7th percentile, emission to its max
light = np.concatenate([atlas["A"][..., :3].ravel(), atlas["B"][..., :3].ravel()])
lscale = float(np.percentile(light[light > 1e-4], 99.7)) if np.any(light > 1e-4) else 1.0
e = atlas["E"][..., 0]
# Emission to its 99.8th percentile (a few hot voxels would otherwise leave the rest of the flame black).
escale = float(np.percentile(e[e > 1e-4], 99.8)) if np.any(e > 1e-4) else 1.0
alpha = atlas["A"][..., 3]
emission = atlas["E"][..., 0] / escale if P["emit_mul"] > 0 else np.zeros_like(alpha)
outs = {"A": np.dstack([np.clip(atlas["A"][..., :3] / lscale, 0, 1), alpha]),
        "B": np.dstack([np.clip(atlas["B"][..., :3] / lscale, 0, 1), np.clip(emission, 0, 1)])}
for key, data in outs.items():
    img = bpy.data.images.new(f"{PRESET}_{key}", GRID * TILE, GRID * TILE, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "Non-Color"
    img.alpha_mode = "STRAIGHT"
    img.pixels.foreach_set(np.ascontiguousarray(data, np.float32).ravel())
    img.filepath_raw = os.path.join(OUT, f"{PRESET}_{key}.png")
    img.file_format = "PNG"
    img.save()
json.dump({"preset": PRESET, "grid": GRID, "tile": TILE, "frames": frames, "fps": scene.render.fps, "domain_m": D,
           "light_scale": lscale, "emission_scale": escale, "crop_px": [x0, y0c, side], "pivot": pivot, "sim": P}, open(os.path.join(OUT, PRESET + ".json"), "w"), indent=1)
print("[vfx_bake] done", PRESET, OUT, flush=True)
