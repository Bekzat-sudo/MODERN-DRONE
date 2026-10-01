"""
Cuts the game's sound takes out of the downloaded source recordings (see CREDITS.md for sources and licences).

  python Tools/Audio/prepare_audio.py <folder with <freesound id>.mp3> [out = Assets/_Project/Resources/Audio]

Needs ffmpeg on PATH and numpy. Every take: mono, 48 kHz, 16-bit WAV, a few ms fade-in, a fade-out, peak-normalised
to -1 dBFS (one-shots) or RMS-normalised to -20 dBFS (loops). Loops are made seamless by cross-fading their last
seconds into their start (equal-power).
"""
import os, subprocess, sys, wave
import numpy as np

SR = 48000
SRC = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join("Assets", "_Project", "Resources", "Audio")

# name: (freesound id, start s, end s, fade-out s, loop)
TAKES = {
    # Close detonations: the near layer (crack, body and the first debris).
    "exp_close_1": (165808, 2.10, 12.0, 3.0, False),   # 4 kg TNT landmine demolition, debris rain
    "exp_close_2": (251759, 0.08, 3.0, 1.0, False),    # film pyrotechnics, close
    "exp_close_3": (530163, 0.00, 9.0, 3.0, False),    # heavy charge in a forest, falling tree
    # Distant layer: the low boom with its rolling echo off the terrain.
    "exp_far_1": (475780, 0.00, 22.0, 8.0, False),     # 70 t open-mine blast (Chile), long roll
    "exp_far_2": (438538, 2.80, 15.3, 4.0, False),     # distant bomb (archive library)
    "exp_far_3": (438538, 15.35, 25.6, 3.0, False),
    "exp_far_4": (438538, 25.65, 36.6, 3.0, False),
    # Small charges: reactive armour blocks, small drone warheads, secondary pops.
    "exp_small_1": (530162, 0.00, 5.0, 2.0, False),
    "exp_small_2": (609588, 0.00, 2.2, 0.8, False),
    "exp_small_3": (462363, 0.30, 2.0, 0.6, False),
    # Debris raining down after the blast.
    "debris_metal": (530209, 0.08, 1.6, 0.5, False),
    "debris_dirt": (179341, 0.40, 6.0, 1.5, False),
    # Fire beds (loops).
    "fire_large": (483323, 5.0, 50.0, 0.0, True),      # big fire, wood and metal crackling
    "fire_fuel": (87564, 2.0, 40.0, 0.0, True),        # gasoline fire
    "fire_car": (563765, 0.5, 18.0, 0.0, True),        # car on fire
    "flame_jet": (244230, 0.6, 15.0, 0.0, True),       # large gas torch roar: propellant venting through hatches
    "flame_burst": (395039, 2.35, 12.9, 0.4, False),   # flamethrower burst: the first seconds of a cook-off
    # Cook-off crackle: firecracker strings and clusters (no CC0 recording of real rounds cooking off exists).
    "cookoff_crackle": (381722, 84.0, 114.0, 0.0, True),
    "pop_1": (140711, 0.0, 1.1, 0.3, False),
    "pop_2": (140717, 0.0, 1.0, 0.3, False),
    # Guns: 4-round slices of 12.7 mm M2 bursts (the game resolves rounds in groups of 4), 25 mm single shots.
    "mg_1": (183656, 1.83, 2.40, 0.15, False),
    "mg_2": (183656, 4.88, 5.45, 0.15, False),
    "mg_3": (183656, 10.23, 10.80, 0.15, False),
    "mg_4": (183656, 13.38, 13.95, 0.15, False),
    "cannon_1": (854186, 0.00, 0.50, 0.12, False),
    "cannon_2": (854186, 0.50, 0.95, 0.12, False),
    "cannon_3": (854186, 0.95, 1.90, 0.40, False),
    "crack": (855248, 0.0, 0.08, 0.02, False),         # real flyby crack downrange of an M240
    "metal_1": (640204, 0.10, 0.75, 0.2, False),
    "metal_2": (530209, 0.08, 0.50, 0.15, False),
}


def load(fid):
    raw = subprocess.run(["ffmpeg", "-v", "quiet", "-i", os.path.join(SRC, f"{fid}.mp3"), "-f", "f32le", "-ac", "1", "-ar", str(SR), "-"],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.float32).astype(np.float64)


def save(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def loop(x, seconds=2.0):
    n = int(seconds * SR)
    head, tail, body = x[:n], x[-n:], x[n:-n]
    t = np.linspace(0, np.pi / 2, n)
    joint = tail * np.cos(t) + head * np.sin(t)  # end fades into the start
    return np.concatenate([body, joint])


def main():
    os.makedirs(OUT, exist_ok=True)
    cache = {}
    for name, (fid, a, b, fade, is_loop) in TAKES.items():
        x = cache.setdefault(fid, load(fid))
        seg = x[int(a * SR):int(b * SR)].copy()
        seg -= seg.mean()
        if is_loop:
            seg = loop(seg)
            seg *= 10 ** (-20 / 20) / (np.sqrt((seg ** 2).mean()) + 1e-9)
            seg *= min(1.0, 0.95 / (np.abs(seg).max() + 1e-9))  # crackles must not clip
        else:
            fi = int(0.003 * SR)
            seg[:fi] *= np.linspace(0, 1, fi)
            if fade > 0:
                fo = min(len(seg), int(fade * SR))
                seg[-fo:] *= np.linspace(1, 0, fo) ** 2
            seg *= 10 ** (-1 / 20) / (np.abs(seg).max() + 1e-9)
        save(os.path.join(OUT, name + ".wav"), seg)
        print(f"{name}: {fid} {a}-{b} s -> {len(seg) / SR:.2f} s")


if __name__ == "__main__":
    main()
