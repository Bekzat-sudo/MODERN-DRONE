"""
Procedural foliage textures for the windbreak trees (run once; outputs are committed):
  LeafAtlas.png  2048x512 RGBA, four 512 px leaf-spray cards: poplar, black locust (acacia), Siberian elm, oak
  Bark.png / Bark_nor.png  512x512 tileable bark colour and tangent-space normal map

Leaf shapes follow the species: poplar (Populus nigra) deltoid leaves on long stalks, black locust (Robinia
pseudoacacia) pinnate leaves of small oval leaflets, Siberian elm (Ulmus pumila) small elliptic leaves along the
twigs, pedunculate oak (Quercus robur) lobed leaves clustered at twig ends. Sizes are relative to the card, colours
are art direction. Usage: python Tools/foliage_textures.py
"""
import colorsys, math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Project", "Art", "Environment", "Plants", "Trees")
TILE, SS = 512, 4           # tile size and supersampling
S = TILE * SS
rng = np.random.default_rng(7)


def outline(kind, length, width):
    """Leaf outline in leaf space: base at (0, 0), tip at (0, length)."""
    ts = np.linspace(0.0, 1.0, 24)
    if kind == "deltoid":      # poplar: broad base, straight sides to a pointed tip
        w = width / 2 * np.minimum(1.0, ts * 5.0) * (1.0 - ts) ** 0.85
    elif kind == "lobed":      # oak: obovate with rounded lobes
        w = width / 2 * np.sin(np.pi * ts) ** 0.7 * (0.55 + 0.6 * ts) * (1.0 + 0.3 * np.sin(2 * np.pi * 4.5 * ts))
    elif kind == "oval":       # locust leaflet: rounded both ends
        w = width / 2 * np.sin(np.pi * ts) ** 0.5
    else:                      # elm: elliptic, pointed tip
        w = width / 2 * np.sin(np.pi * ts ** 0.8) ** 0.9
    right = list(zip(w, ts * length))
    left = [(-x, y) for x, y in reversed(right)]
    return right + left


class Card:
    def __init__(self):
        self.leaves = []   # (depth, polygon, colour)
        self.stems = []    # (points, width)

    def leaf(self, base, angle, kind, length, width, colour, depth):
        c, s = math.cos(angle), math.sin(angle)
        pts = [(base[0] + x * c - y * s, base[1] + x * s + y * c) for x, y in outline(kind, length, width)]
        self.leaves.append((depth, pts, colour, base, angle, length))

    def stem(self, pts, width):
        self.stems.append((pts, width))

    def draw(self):
        img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        px = lambda p: (p[0] * S, (1.0 - p[1]) * S)
        for pts, w in self.stems:
            d.line([px(p) for p in pts], fill=(74, 62, 48, 255), width=max(1, int(w * S)))
        for depth, pts, col, base, angle, length in sorted(self.leaves, key=lambda l: l[0]):
            shade = 0.72 + 0.28 * depth                   # deeper leaves darker (fake occlusion)
            r, g, b = [int(min(255, v * shade)) for v in col]
            d.polygon([px(p) for p in pts], fill=(r, g, b, 255))
            tip = (base[0] - math.sin(angle) * length * 0.9, base[1] + math.cos(angle) * length * 0.9)
            d.line([px(base), px(tip)], fill=(int(r * 0.8), int(g * 0.85), int(b * 0.8), 255), width=max(1, SS))
        return img.resize((TILE, TILE), Image.LANCZOS)


def jitter(rgb, dl=0.12, dh=0.025):
    h, l, s = colorsys.rgb_to_hls(*[v / 255 for v in rgb])
    h += rng.uniform(-dh, dh); l *= 1 + rng.uniform(-dl, dl); s *= 1 + rng.uniform(-0.15, 0.1)
    return [int(255 * min(1, max(0, v))) for v in colorsys.hls_to_rgb(h, min(1, l), min(1, s))]


def curve(a, b, bend, n=8):
    ax, ay = a; bx, by = b
    nx, ny = -(by - ay), bx - ax
    return [(ax + (bx - ax) * t + nx * bend * math.sin(math.pi * t), ay + (by - ay) * t + ny * bend * math.sin(math.pi * t)) for t in np.linspace(0, 1, n)]


def along(pts, t):
    i = min(len(pts) - 2, int(t * (len(pts) - 1)))
    f = t * (len(pts) - 1) - i
    (x0, y0), (x1, y1) = pts[i], pts[i + 1]
    return (x0 + (x1 - x0) * f, y0 + (y1 - y0) * f), math.atan2(y1 - y0, x1 - x0)


def spray(base_col, twigs, leaves_per, kind, length, width, spread, stalk=0.0, cluster=False, compound=False):
    card = Card()
    main = curve((0.5 + rng.uniform(-0.05, 0.05), 0.04), (0.5 + rng.uniform(-0.06, 0.06), 0.84), rng.uniform(-0.08, 0.08))
    card.stem(main, 0.012)
    for k in range(twigs):
        t = 0.12 + 0.8 * k / max(1, twigs - 1)
        p, a = along(main, t)
        side = 1 if k % 2 else -1
        ta = a + side * rng.uniform(0.6, 1.0) * spread
        tl = rng.uniform(0.2, 0.3) * (1.1 - 0.4 * t)
        twig = curve(p, (p[0] + math.cos(ta) * tl, p[1] + math.sin(ta) * tl), rng.uniform(-0.15, 0.15))
        card.stem(twig, 0.006)
        if compound:   # locust: the twig is a rachis with paired leaflets and a terminal one
            n = leaves_per
            for j in range(n):
                q, qa = along(twig, 0.12 + 0.8 * j / n)
                for sgn in (-1, 1):
                    card.leaf(q, qa - math.pi / 2 + sgn * 1.25, kind, length, width, jitter(base_col), rng.uniform(0.3, 1))
            q, qa = along(twig, 1.0)
            card.leaf(q, qa - math.pi / 2, kind, length, width, jitter(base_col), rng.uniform(0.5, 1))
            continue
        spots = [1.0] * leaves_per if cluster else [0.15 + 0.85 * j / max(1, leaves_per - 1) for j in range(leaves_per)]
        for j, u in enumerate(spots):
            q, qa = along(twig, u)
            la = qa - math.pi / 2 + (rng.uniform(-1.3, 1.3) if cluster else (1 if j % 2 else -1) * rng.uniform(0.5, 1.0))
            if stalk:
                sq = (q[0] - math.sin(la) * stalk, q[1] + math.cos(la) * stalk)
                card.stem([q, sq], 0.003)
                q = sq
            s = rng.uniform(0.8, 1.15)
            card.leaf(q, la, kind, length * s, width * s, jitter(base_col), rng.uniform(0, 1))
    return card.draw()


def dilate(img, passes=24):
    """Bleed leaf colour into transparent texels so mipmaps and alpha clipping show no dark fringes."""
    a = np.asarray(img).astype(np.float32) / 255
    rgb, alpha = a[..., :3].copy(), a[..., 3:4]
    mask = (alpha > 0.02).astype(np.float32)
    col = rgb * mask
    for _ in range(passes):
        blur = lambda x: (np.roll(x, 1, 0) + np.roll(x, -1, 0) + np.roll(x, 1, 1) + np.roll(x, -1, 1) + x) / 5
        c2, m2 = blur(col), blur(mask)
        fill = (mask < 0.5) & (m2 > 1e-4)
        col = np.where(fill, c2 / np.maximum(m2, 1e-4) * np.maximum(m2, 0), col)
        mask = np.maximum(mask, (m2 > 1e-4).astype(np.float32))
        col = np.where(mask > 0, col, 0)
    rgb = np.where(alpha > 0.02, rgb, col / np.maximum(mask, 1e-4))
    out = np.concatenate([np.clip(rgb, 0, 1), alpha], axis=-1)
    return Image.fromarray((out * 255).astype(np.uint8), "RGBA")


def periodic_noise(n, fx, fy, seed):
    """Tileable noise: white noise filtered in frequency space (anisotropic Gaussian falloff)."""
    r = np.random.default_rng(seed)
    f = np.fft.fft2(r.standard_normal((n, n)))
    ky, kx = np.meshgrid(np.fft.fftfreq(n), np.fft.fftfreq(n), indexing="ij")
    f *= np.exp(-((kx / fx) ** 2 + (ky / fy) ** 2))
    x = np.real(np.fft.ifft2(f))
    return (x - x.min()) / (x.max() - x.min())


def bark():
    n = 512
    furrows = periodic_noise(n, 0.03, 0.006, 1)          # long vertical ridges
    plates = periodic_noise(n, 0.05, 0.02, 2)
    fine = periodic_noise(n, 0.3, 0.3, 3)
    h = np.clip(0.65 * furrows + 0.25 * plates + 0.1 * fine, 0, 1)
    h = np.clip((h - 0.35) * 1.8, 0, 1) ** 0.8
    dark, light = np.array([46, 40, 34]), np.array([122, 114, 100])
    col = dark + (light - dark) * h[..., None]
    col *= (0.9 + 0.2 * fine)[..., None]
    Image.fromarray(np.clip(col, 0, 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "Bark.png"))
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 4
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 4
    nrm = np.stack([-gx, gy, np.ones_like(h)], -1)
    nrm /= np.linalg.norm(nrm, axis=-1, keepdims=True)
    Image.fromarray(((nrm * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, "Bark_nor.png"))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    tiles = [
        spray((82, 102, 50), 11, 6, "deltoid", 0.12, 0.11, 0.9, stalk=0.035),               # poplar
        spray((104, 124, 60), 11, 7, "oval", 0.052, 0.03, 0.8, compound=True),              # black locust
        spray((72, 94, 46), 12, 12, "ellipse", 0.062, 0.036, 1.0),                         # Siberian elm
        spray((66, 88, 44), 11, 7, "lobed", 0.15, 0.08, 1.0, cluster=True),                # oak
    ]
    atlas = Image.new("RGBA", (TILE * 4, TILE))
    for i, t in enumerate(tiles):
        atlas.paste(dilate(t), (i * TILE, 0))
    atlas.save(os.path.join(OUT, "LeafAtlas.png"))
    bark()
    print("written to", os.path.normpath(OUT))
