"""
Procedural field textures for the terrain (run once; outputs are committed), 2048 px tileable colour + OpenGL normal
maps in Assets/_Project/Art/Environment/Ground/<name>/ with the same naming as the Poly Haven scans:
  field_plowed   ploughed chernozem, furrows about 0.35 m apart            (terrain tile 8 m)
  field_stubble  harvested wheat: straw stubble rows 0.15 m, straw swaths  (terrain tile 6 m)
  field_wheat    young winter wheat in 0.15 m drill rows over soil         (terrain tile 4 m)
  field_burnt    burnt stubble: ash, char and surviving straw rows         (terrain tile 10 m)
and a shell-crater decal (Art/Decals/Crater.png + Crater_nor.png): a dark bowl of churned soil, a raised rim of
thrown-out earth and radial ejecta fading into the ground.
Rows run along the texture's U axis, i.e. along the long side of the fields. Row spacings follow common practice
(cereal drills ~15 cm, mouldboard plough furrow ~35 cm); colours are art direction. Usage: python Tools/ground_textures.py
"""
import os
import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "_Project", "Art", "Environment", "Ground")
N = 2048


def periodic_noise(fx, fy, seed):
    """Tileable noise: white noise filtered in frequency space (anisotropic Gaussian falloff), range 0..1."""
    r = np.random.default_rng(seed)
    f = np.fft.fft2(r.standard_normal((N, N)))
    ky, kx = np.meshgrid(np.fft.fftfreq(N), np.fft.fftfreq(N), indexing="ij")
    f *= np.exp(-((kx / fx) ** 2 + (ky / fy) ** 2))
    x = np.real(np.fft.ifft2(f))
    return (x - x.min()) / (x.max() - x.min())


V = np.arange(N)[:, None] / N   # rows along U: patterns vary with V
U = np.arange(N)[None, :] / N


def rows(count, wobble, sharp=1.0):
    """0..1 ridge pattern with `count` rows per tile, wobbling sideways."""
    phase = (V * count + wobble) % 1.0
    return (0.5 + 0.5 * np.cos(2 * np.pi * phase)) ** sharp


def lerp(a, b, t):
    return np.array(a)[None, None, :] + (np.array(b) - np.array(a))[None, None, :] * t[..., None]


def save(name, colour, height, strength):
    d = os.path.join(OUT, name)
    os.makedirs(d, exist_ok=True)
    Image.fromarray(np.clip(colour, 0, 255).astype(np.uint8), "RGB").save(os.path.join(d, f"{name}_diff_2k.jpg"), quality=92)
    gx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * strength
    gy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * strength
    n = np.stack([-gx, gy, np.ones_like(height)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").save(os.path.join(d, f"{name}_nor_gl_2k.jpg"), quality=95)


def plowed():
    wob = (periodic_noise(0.004, 0.01, 11) - 0.5) * 0.25
    furrows = rows(23, wob, 0.7)                                   # 8 m tile / 23 = 0.35 m
    clods = periodic_noise(0.05, 0.05, 12)
    fine = periodic_noise(0.3, 0.3, 13)
    moist = periodic_noise(0.006, 0.006, 14)
    h = 0.6 * furrows + 0.3 * clods + 0.1 * fine
    col = lerp((30, 21, 14), (98, 76, 52), np.clip(0.25 + 0.55 * furrows * (0.6 + 0.6 * clods) + 0.15 * fine - 0.25 * moist, 0, 1))
    straw = (periodic_noise(0.4, 0.15, 15) > 0.83).astype(float)   # ploughed-in straw bits
    col = col * (1 - straw[..., None] * 0.5) + np.array([150, 132, 92]) * straw[..., None] * 0.5
    save("field_plowed", col, h, 5.0)


def stubble():
    wob = (periodic_noise(0.003, 0.02, 21) - 0.5) * 0.4
    r = rows(40, wob, 3.0)                                         # 6 m / 40 = 0.15 m drill rows
    swath = rows(1, wob * 0.2, 6.0)                                # one straw swath per tile
    specks = periodic_noise(0.45, 0.2, 22)
    soil = periodic_noise(0.02, 0.02, 23)
    h = 0.5 * r + 0.3 * swath + 0.2 * specks
    t = np.clip(0.35 + 0.35 * r + 0.15 * swath + 0.25 * (specks - 0.5), 0, 1)
    col = lerp((112, 92, 58), (190, 164, 108), t)
    bare = np.clip((soil - 0.62) * 4, 0, 1) * (1 - r)
    col = col * (1 - bare[..., None]) + np.array([88, 72, 56]) * bare[..., None]
    save("field_stubble", col, h, 4.0)


def wheat():
    wob = (periodic_noise(0.003, 0.02, 31) - 0.5) * 0.3
    r = rows(27, wob, 2.2)                                         # 4 m / 27 = 0.15 m drill rows
    vigour = periodic_noise(0.01, 0.01, 32)
    leaves = periodic_noise(0.35, 0.12, 33)
    cover = np.clip(r * (0.6 + 0.6 * vigour) + 0.25 * (leaves - 0.5), 0, 1)
    green = lerp((58, 84, 34), (112, 142, 58), np.clip(0.3 + 0.7 * leaves * vigour + 0.2 * vigour, 0, 1))
    soil = lerp((64, 52, 40), (98, 84, 66), periodic_noise(0.08, 0.08, 34))
    col = soil * (1 - cover[..., None]) + green * cover[..., None]
    save("field_wheat", col, 0.7 * cover + 0.3 * leaves, 3.0)


def burnt():
    wob = (periodic_noise(0.003, 0.02, 41) - 0.5) * 0.4
    r = rows(66, wob, 3.0)                                         # surviving stubble rows
    ash = periodic_noise(0.02, 0.02, 42)
    char = periodic_noise(0.1, 0.1, 43)
    patches = periodic_noise(0.004, 0.004, 44)
    t = np.clip(0.2 * r + 0.55 * ash + 0.25 * char, 0, 1)
    col = lerp((20, 19, 18), (104, 100, 94), t ** 1.4)
    unburnt = np.clip((patches - 0.7) * 5, 0, 1)                   # scorched but not burnt through
    col = col * (1 - unburnt[..., None]) + np.array([120, 100, 70]) * unburnt[..., None] * (0.6 + 0.4 * r[..., None])
    save("field_burnt", col, 0.4 * r + 0.6 * char, 3.0)


def crater():
    n = 1024
    y, x = (np.mgrid[0:n, 0:n] + 0.5) / n * 2 - 1
    r = np.hypot(x, y)
    ang = np.arctan2(y, x)
    rng = np.random.default_rng(51)
    lobes = 1 + 0.08 * np.sin(ang * 3 + 1) + 0.05 * np.sin(ang * 7 + 2)      # not a perfect circle
    rr = r / lobes
    clods = np.asarray(Image.fromarray((rng.random((n // 8, n // 8)) * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)) / 255
    fine = rng.random((n, n))
    bins = rng.random(29) ** 2                                               # uneven throw: random strength per sector
    k = (ang + np.pi) / (2 * np.pi) * len(bins)
    i0 = np.floor(k).astype(int) % len(bins)
    f = k - np.floor(k)
    rays = bins[i0] * (1 - f) + bins[(i0 + 1) % len(bins)] * f
    rays = np.clip(rays * (0.6 + 0.8 * clods), 0, 1)
    bowl = np.clip(1 - (rr / 0.34) ** 2, 0, 1)
    rim = np.exp(-((rr - 0.42) / 0.07) ** 2)
    ejecta = np.clip(1 - (rr - 0.45) / 0.5, 0, 1) * (rr > 0.45)
    h = -0.9 * bowl + 0.5 * rim + 0.25 * clods * (rim + ejecta * rays) + 0.05 * fine
    soil_dark, soil_light = np.array([46, 38, 30]), np.array([112, 94, 72])
    t = np.clip(0.25 + 0.6 * rim + 0.35 * clods - 0.1 * bowl, 0, 1)
    col = soil_dark + (soil_light - soil_dark) * t[..., None]
    col *= (0.85 + 0.3 * fine)[..., None]
    col[..., :] = np.where((bowl > 0)[..., None], col * (0.82 + 0.15 * clods[..., None]), col)  # moist, fresh soil in the bowl
    alpha = np.clip(np.where(rr < 0.47, 1.0, ejecta * rays * (0.3 + 0.9 * clods) * 1.8), 0, 1)
    alpha *= np.clip((0.98 - r) / 0.08, 0, 1)
    d = os.path.join(OUT, "..", "..", "Decals")
    rgba = np.dstack([np.clip(col, 0, 255), alpha * 255]).astype(np.uint8)
    Image.fromarray(rgba, "RGBA").save(os.path.join(d, "Crater.png"))
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 40
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 40
    nm = np.stack([-gx, gy, np.ones_like(h)], -1)
    nm /= np.linalg.norm(nm, axis=-1, keepdims=True)
    Image.fromarray(((nm * 0.5 + 0.5) * 255).astype(np.uint8), "RGB").save(os.path.join(d, "Crater_nor.png"))


if __name__ == "__main__":
    plowed(); stubble(); wheat(); burnt(); crater()
    print("written to", os.path.normpath(OUT))
