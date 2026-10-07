"""
Moves the images embedded in .glb models out to files beside them, so Unity compresses them.

glTFast imports images stored inside a .glb as raw RGBA32 textures (no block compression, no anisotropy); an image
referenced by a relative "uri" is loaded as a project texture asset instead, which the texture importer compresses
(Editor/ModelTextureImport.cs). The .glb keeps its name and .meta (GUID, sub-asset ids), so prefabs and scenes keep
their references. Images go to "<model>_tex/<model>_NN_<role>.<png|jpg>"; the role comes from how materials use them:
col (base colour), nrm (normal), mr (metallic-roughness), ao (occlusion), orm (both), emi (emissive). The binary chunk
is compacted (image bytes removed) and every bufferView reference is renumbered. Models without embedded images are
left alone, so running it again is harmless.

  python Tools/glb_extract_textures.py <folder or .glb> [...]
"""
import json, os, struct, sys

JSON_CHUNK, BIN_CHUNK = 0x4E4F534A, 0x004E4942
EXT = {"image/png": "png", "image/jpeg": "jpg"}


def read_glb(path):
    data = open(path, "rb").read()
    magic, version, length = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67 and version == 2, f"{path}: not a glTF 2.0 binary"
    js, binary, off = None, b"", 12
    while off < length:
        clen, ctype = struct.unpack_from("<II", data, off)
        chunk = data[off + 8: off + 8 + clen]
        if ctype == JSON_CHUNK:
            js = json.loads(chunk.decode("utf-8"))
        elif ctype == BIN_CHUNK:
            binary = chunk
        off += 8 + clen
    return js, binary


def write_glb(path, js, binary):
    jb = json.dumps(js, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    jb += b" " * (-len(jb) % 4)
    binary += b"\0" * (-len(binary) % 4)
    total = 12 + 8 + len(jb) + (8 + len(binary) if binary else 0)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total))
        f.write(struct.pack("<II", len(jb), JSON_CHUNK) + jb)
        if binary:
            f.write(struct.pack("<II", len(binary), BIN_CHUNK) + binary)


def roles(js):
    """Image index -> set of uses by the materials."""
    tex_src = [t.get("source") for t in js.get("textures", [])]
    use = {}

    def mark(info, role):
        if isinstance(info, dict) and "index" in info and info["index"] < len(tex_src) and tex_src[info["index"]] is not None:
            use.setdefault(tex_src[info["index"]], set()).add(role)

    for m in js.get("materials", []):
        pbr = m.get("pbrMetallicRoughness", {})
        mark(pbr.get("baseColorTexture"), "col")
        mark(pbr.get("metallicRoughnessTexture"), "mr")
        mark(m.get("normalTexture"), "nrm")
        mark(m.get("occlusionTexture"), "ao")
        mark(m.get("emissiveTexture"), "emi")
    return use


def role_name(uses):
    if not uses or "col" in uses:
        return "col"
    if "nrm" in uses:
        return "nrm"
    if "mr" in uses and "ao" in uses:
        return "orm"
    return sorted(uses)[0]


def renumber(node, remap):
    if isinstance(node, dict):
        for k, v in list(node.items()):
            if k == "bufferView" and isinstance(v, int):
                node[k] = remap[v]
            else:
                renumber(v, remap)
    elif isinstance(node, list):
        for v in node:
            renumber(v, remap)


def convert(path):
    js, binary = read_glb(path)
    images = js.get("images", [])
    embedded = [i for i, im in enumerate(images) if "bufferView" in im]
    if not embedded:
        return 0
    stem = os.path.splitext(os.path.basename(path))[0]
    folder = os.path.join(os.path.dirname(path), stem + "_tex")
    os.makedirs(folder, exist_ok=True)
    views = js["bufferViews"]
    use = roles(js)
    image_views = set()
    for i in embedded:
        im = images[i]
        bv = views[im["bufferView"]]
        start = bv.get("byteOffset", 0)
        blob = binary[start: start + bv["byteLength"]]
        ext = EXT.get(im.get("mimeType"))
        if ext is None:  # unknown format: leave it embedded
            continue
        name = f"{stem}_{i:02d}_{role_name(use.get(i))}.{ext}"
        with open(os.path.join(folder, name), "wb") as f:
            f.write(blob)
        image_views.add(im["bufferView"])
        del im["bufferView"]
        im.pop("mimeType", None)
        im["uri"] = f"{stem}_tex/{name}"

    # Compact the binary chunk: keep the other views of buffer 0, 4-byte aligned, and renumber references.
    new_views, remap, out = [], {}, bytearray()
    for idx, bv in enumerate(views):
        if idx in image_views:
            continue
        if bv.get("buffer", 0) == 0:
            start = bv.get("byteOffset", 0)
            out += b"\0" * (-len(out) % 4)
            bv["byteOffset"] = len(out)
            out += binary[start: start + bv["byteLength"]]
        remap[idx] = len(new_views)
        new_views.append(bv)
    js["bufferViews"] = new_views
    for key in ("accessors", "meshes", "images", "extensions"):
        if key in js:
            renumber(js[key], remap)
    if js.get("buffers"):
        js["buffers"][0]["byteLength"] = len(out)
    write_glb(path, js, bytes(out))
    return len(image_views)


def main(args):
    files = []
    for a in args:
        if os.path.isdir(a):
            for root, _, names in os.walk(a):
                files += [os.path.join(root, n) for n in names if n.lower().endswith(".glb")]
        else:
            files.append(a)
    for path in sorted(files):
        n = convert(path)
        if n:
            print(f"{path}: {n} images extracted")


if __name__ == "__main__":
    main(sys.argv[1:] or ["Assets/_Project/Art"])
