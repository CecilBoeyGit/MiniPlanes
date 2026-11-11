"""
Synthesizes MiniPlanes' made-from-scratch sound effects:
  ring_pass, ring_big_o     bubbly ring scores (the big O is lower, fuller and longer)
  enemy_engine_jet          fighter jet engine loop (seamless)
  enemy_engine_prop         bomber heavy twin propeller loop (seamless)
  enemy_explode             enemy plane destroyed (Skylooper small blast + synthesized boom and debris)
  Music/riser               endless rising Shepard swell, looped and pitched up as the plane nears the end
  Music/climax              the hit when the plane reaches the end of the path
  streak_reveal             three rings in a row bring the path back
  enemy_warning             the first enemy plane into a clear sky
  life_up                   an extra life
Run with any Python that has numpy:  python -I Tools/Audio/synth/make_sfx.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

import instruments as ins
from dsp import (SR, times, noise, lp, hp, bp, reverb, shaped_noise, saturate, limiter, normalize_peak,
                 read_audio, to_ogg, momentary_max, loudness, mtof)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "MiniPlanes", "Audio")
MUSIC = os.path.join(OUT, "Music")
SRC = "D:/Unity/MiniPlanes_Docs/SkylooperSounds/Sounds"


def place(buf, at, sig, gain=1.0):
    s = int(at * SR)
    e = min(len(buf), s + len(sig))
    buf[s:e] += sig[: e - s] * gain


def bloop(f0, length=0.25, rise=1.5, rise_time=0.025, decay=18.0, body=0.25):
    """A cartoon bubble: a sine that jumps up in pitch as it forms, then rings away."""
    n = int(length * SR)
    t = times(n)
    f = f0 * (1.0 + (rise - 1.0) * (1.0 - np.exp(-t / rise_time)))
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = np.sin(ph) + body * np.sin(2 * ph) * np.exp(-t * decay * 2)
    e = np.minimum(1.0, t / 0.002) * np.exp(-t * decay)
    return x * e


def fizz(length, count, lo, hi, level, seed):
    """Tiny high bubbles fizzing after the main ones."""
    rng = np.random.default_rng(seed)
    out = np.zeros(int(length * SR))
    for _ in range(count):
        at = rng.uniform(0.02, length * 0.7)
        f = np.exp(rng.uniform(np.log(lo), np.log(hi)))
        place(out, at, bloop(f, 0.08, rise=1.3, rise_time=0.01, decay=45, body=0.0), level * rng.uniform(0.4, 1.0))
    return out


def pop_click(length=0.006, seed=11):
    n = int(length * SR)
    return bp(noise(n, seed), 1500, 6000) * np.exp(-times(n) * 900)


def finish_sfx(x, wet=0.12, seconds=0.6, fade=0.08):
    """Light room, fade the tail, peak to -1 dB."""
    if wet > 0:
        r = reverb(x, seconds=seconds, damp=7000, predelay=0.008)[: len(x)].mean(axis=1)
        x = x + wet * r
    n = int(fade * SR)
    x[-n:] *= np.linspace(1.0, 0.0, n) ** 2
    return normalize_peak(limiter(x, -1.0, periodic=False), -1.2)


def ring_pass():
    """Four quick bubbles climbing a major arpeggio, with a fizz on top."""
    L = 0.75
    x = np.zeros(int(L * SR))
    place(x, 0.0, pop_click(), 0.25)
    for i, (m, at) in enumerate(zip((72, 76, 79, 84), (0.0, 0.05, 0.10, 0.16))):
        place(x, at, bloop(mtof(m), 0.35, rise=1.45, rise_time=0.022, decay=16 - 2 * i, body=0.2), 0.55 + 0.12 * i)
    x += fizz(L, 9, 1800, 4200, 0.12, 21)
    return finish_sfx(x, wet=0.14)


def ring_big_o():
    """A big gloop with a sub thump, then a longer cascade of larger, lower bubbles."""
    L = 1.3
    x = np.zeros(int(L * SR))
    t = times(int(0.5 * SR))
    gloop = bloop(130.0, 0.5, rise=2.3, rise_time=0.06, decay=8.0, body=0.45)
    thump = np.sin(2 * np.pi * np.cumsum(55 + 40 * np.exp(-t * 25)) / SR) * np.exp(-t * 11)
    place(x, 0.0, pop_click(0.01, 12), 0.35)
    place(x, 0.0, saturate(gloop * 1.4, 1.5), 0.9)
    place(x, 0.0, thump, 0.7)
    notes = (60, 64, 67, 72, 76, 79, 84)
    for i, m in enumerate(notes):
        at = 0.08 + 0.055 * i + 0.004 * i * i
        place(x, at, bloop(mtof(m), 0.5, rise=1.6, rise_time=0.035, decay=11 - 0.6 * i, body=0.35), 0.5 + 0.06 * i)
    x += fizz(L, 18, 900, 3500, 0.13, 22)
    x = lp(x, 9000)
    return finish_sfx(x, wet=0.22, seconds=1.0, fade=0.15)


def loop_seconds(T):
    return int(round(T * SR))


def fighter_jet(T=2.0):
    """Small jet: turbine whine over a broad roar. Every component repeats a whole number of times per loop."""
    n = loop_seconds(T)
    t = times(n)
    roar = shaped_noise(n, lambda f: 1.0 / (1.0 + ((f - 1100.0) / 1300.0) ** 2) / np.sqrt(1 + (60 / np.maximum(f, 1)) ** 4), 31)
    rumble = shaped_noise(n, lambda f: 1.0 / (1.0 + (f / 160.0) ** 4) / np.sqrt(1 + (35 / np.maximum(f, 1)) ** 4), 32)
    hiss = shaped_noise(n, lambda f: 1.0 / np.sqrt(1 + (4500 / np.maximum(f, 1)) ** 4) / (1 + (f / 12000) ** 2), 33)
    fm = 1.5  # whole cycles per loop: 3
    def whine(f, dev, phase0=0.0):
        return np.sin(2 * np.pi * f * t + (dev / fm) * np.sin(2 * np.pi * fm * t + phase0))
    w = 0.6 * whine(2400.0, 18.0) + 0.3 * whine(4800.0, 36.0, 1.0) + 0.18 * whine(3150.0, 22.0, 2.0)
    am = 1.0 + 0.12 * np.sin(2 * np.pi * 1.0 * t) + 0.06 * np.sin(2 * np.pi * 3.5 * t + 0.4)
    x = (0.62 * roar * am + 0.32 * rumble + 0.10 * hiss + 0.17 * w)
    return normalize_peak(saturate(x / np.max(np.abs(x)) * 1.3, 1.2), -2.0)


def bomber_prop(T=2.0):
    """Heavy twin propeller: rough engine harmonics and blade thumps, the two engines slightly apart so they beat."""
    n = loop_seconds(T)
    t = times(n)
    rng = np.random.default_rng(41)
    def engine(fe, fb, seed):
        ph = rng.uniform(0, 2 * np.pi, 40)
        tone = sum(np.sin(2 * np.pi * k * fe * t + ph[k]) / k / (1 + (k * fe / 320.0) ** 2) for k in range(1, 40))
        rough = shaped_noise(n, lambda f: 1.0 / (1.0 + (f / 450.0) ** 4) / np.sqrt(1 + (40 / np.maximum(f, 1)) ** 4), seed)
        fire = 0.55 + 0.45 * np.abs(np.sin(np.pi * fe * t)) ** 3
        blades = np.abs(np.sin(np.pi * fb * t)) ** 6
        chop = shaped_noise(n, lambda f: 1.0 / (1.0 + ((f - 500.0) / 600.0) ** 2), seed + 1)
        return 0.9 * tone + 0.5 * rough * fire + 0.55 * chop * blades
    x = engine(54.0, 18.0, 51) + 0.85 * engine(55.5, 18.5, 61)
    x = lp(x, 2600, 2, periodic=True)
    return normalize_peak(saturate(x / np.max(np.abs(x)) * 1.4, 1.3), -2.0)


def enemy_explode():
    """A small toy-plane blast: the Skylooper small blast for crack, a synthesized low boom, and debris."""
    src = read_audio(SRC + "/Death/584119__robinhood76__10167-small-explode-blast.wav")
    on = np.argmax(np.abs(src) > 0.02)
    crack = src[on: on + int(1.4 * SR)]
    L = 1.6
    x = np.zeros(int(L * SR))
    place(x, 0.0, crack / (np.max(np.abs(crack)) + 1e-9), 0.85)
    t = times(int(1.0 * SR))
    boom = np.sin(2 * np.pi * np.cumsum(42 + 70 * np.exp(-t * 18)) / SR) * np.exp(-t * 5.5)
    rumble = lp(noise(len(t), 71), 260) * np.exp(-t * 4.0)
    rumble /= np.max(np.abs(rumble))
    place(x, 0.0, saturate(boom * 1.5, 1.6), 0.75)
    place(x, 0.0, rumble, 0.35)
    rng = np.random.default_rng(72)
    for _ in range(16):
        at = rng.uniform(0.12, 1.0)
        n = int(0.02 * SR)
        d = bp(noise(n, int(rng.integers(1e6))), 1500, 7000) * np.exp(-times(n) * 300)
        place(x, at, d, rng.uniform(0.04, 0.16) * (1.1 - at))
    return finish_sfx(x, wet=0.1, seconds=0.8, fade=0.3)


def riser(T=4.0, octaves=6):
    """Shepard glissando: it always seems to rise and loops seamlessly. Playing it faster (higher pitch) climbs faster."""
    n = loop_seconds(T)
    N = n * octaves
    t = times(N)
    f = 55.0 * 2.0 ** (t / T)
    ph = 2 * np.pi * np.cumsum(f) / SR
    w = np.sin(np.pi * t / (octaves * T)) ** 2
    g = (np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.15 * np.sin(3 * ph)) * w
    tone = g.reshape(octaves, n).sum(axis=0)
    air = shaped_noise(n, lambda fr: 1.0 / (1.0 + ((fr - 2500.0) / 2200.0) ** 2), 81)
    x = tone / np.max(np.abs(tone)) * 0.8 + 0.18 * air
    return normalize_peak(x, -2.0)


def climax():
    """The finish hit, in the in-flight key (B-flat, the title theme's): kick, crash, a bright power chord with the ninth, and a bubble arpeggio flying up."""
    L = 3.2
    x = np.zeros((int(L * SR), 2))
    def put(sig, at=0.0, g=1.0, pan=0.0):
        s = int(at * SR)
        e = min(x.shape[0], s + len(sig))
        th = (pan + 1) * np.pi / 4
        x[s:e, 0] += sig[: e - s] * g * np.cos(th) * 1.414
        x[s:e, 1] += sig[: e - s] * g * np.sin(th) * 1.414
    put(ins.kick(1.0, tone=45, sweep=140, decay=5.0, click=0.4), 0, 0.9)
    put(ins.crash(0.9, 3.0, 91), 0, 0.55, -0.3)
    put(ins.crash(0.9, 3.0, 92), 0, 0.55, 0.3)
    put(ins.clap(1.0, 93), 0, 0.5)
    put(ins.sine_bass(mtof(46), 1.6, 0.9, rel=0.6, decay=0.8), 0, 0.7)
    for i, m in enumerate((46, 53, 58, 60, 65, 70)):
        put(ins.saw_pad(mtof(m), 1.2, 0.5, rel=1.2, attack=0.004, cutoff=3200), 0.0, 0.32, (i - 2.5) * 0.15)
        put(ins.ep(mtof(m + 12), 1.4, 0.7, rel=0.8), 0.006 * i, 0.22, (2.5 - i) * 0.12)
    for i, m in enumerate((77, 82, 84, 89, 94)):
        put(bloop(mtof(m), 0.4, rise=1.4, rise_time=0.02, decay=10), 0.08 + 0.06 * i, 0.28, -0.6 + 0.3 * i)
    wet = reverb(x, seconds=1.6, damp=6500, predelay=0.02)[: x.shape[0]]
    x = x + 0.22 * wet
    # Kept to two seconds so the jingle after it (next wave, level complete) doesn't wait long.
    x = x[: int(2.0 * SR)]
    n = int(0.7 * SR)
    x[-n:] *= (np.linspace(1, 0, n) ** 2)[:, None]
    return normalize_peak(limiter(x, -1.0, periodic=False), -1.2)


def streak_reveal():
    """Three rings in a row: three quick pops (one per ring), then a bubble cascade racing up two octaves
    over a rising whoosh, ending in a shimmering bubbly chord as the path appears."""
    L = 1.5
    x = np.zeros(int(L * SR))
    for i, m in enumerate((67, 72, 76)):
        place(x, 0.0 + 0.075 * i, pop_click(0.008, 30 + i), 0.3)
        place(x, 0.0 + 0.075 * i, bloop(mtof(m), 0.2, rise=1.3, rise_time=0.012, decay=26, body=0.1), 0.6)
    steps = (72, 74, 76, 79, 81, 84, 86, 88, 91, 93, 96)
    for i, m in enumerate(steps):
        at = 0.25 + 0.032 * i
        place(x, at, bloop(mtof(m), 0.25, rise=1.25, rise_time=0.01, decay=20, body=0.0), 0.32 + 0.025 * i)
    n = int(0.75 * SR)
    t = times(n)
    sweep = shaped_noise(n, lambda f: 1.0, 41)
    sweep = sum(bp(sweep * (np.abs(t - c) < 0.1), 500 * 2 ** (6 * c), 1200 * 2 ** (6 * c)) for c in np.arange(0.05, 0.75, 0.1))
    sweep *= np.sin(np.pi * t / t[-1]) ** 2
    place(x, 0.2, sweep / (np.max(np.abs(sweep)) + 1e-9), 0.18)
    tt = times(int(0.95 * SR))
    trem = 0.75 + 0.25 * np.sin(2 * np.pi * 11.0 * tt)
    for m in (84, 88, 91, 96):
        b = ins.bell(float(mtof(m)), 0.5, 0.5, rel=0.45)
        k = min(len(b), len(trem))
        place(x, 0.6, b[:k] * trem[:k], 0.22)
    x += fizz(L, 22, 2000, 6000, 0.08, 42)
    return finish_sfx(x, wet=0.2, seconds=0.9, fade=0.25)


def enemy_warning():
    """Arcade alert: a crushed two-tone square bleep, three times, over a low radar ping."""
    L = 1.1
    x = np.zeros(int(L * SR))
    for i in range(3):
        at = 0.27 * i
        place(x, at, ins.pulse(float(mtof(83)), 0.1, 0.8, duty=0.5, rel=0.01, kmax=25), 0.5)
        place(x, at + 0.12, ins.pulse(float(mtof(77)), 0.1, 0.8, duty=0.5, rel=0.01, kmax=25), 0.5)
    t = times(int(0.6 * SR))
    ping = np.sin(2 * np.pi * np.cumsum(330 - 120 * t) / SR) * np.exp(-t * 7)
    place(x, 0.0, ping, 0.35)
    q = 2.0 ** 5
    x = np.round(x * q) / q
    x = lp(x, 7000)
    return finish_sfx(x, wet=0.1, seconds=0.5, fade=0.15)


def life_up():
    """Extra life: a bright square arpeggio racing up with bubbles riding it, then a little bubbly sparkle."""
    L = 1.2
    x = np.zeros(int(L * SR))
    seq = (72, 76, 79, 84, 88, 91)
    for i, m in enumerate(seq):
        at = 0.06 * i
        place(x, at, ins.pulse(float(mtof(m)), 0.07 if i < len(seq) - 1 else 0.35, 0.7, duty=0.25, rel=0.05, vib=0.01,
                               vib_delay=0.05), 0.42)
        place(x, at + 0.01, bloop(mtof(m + 12), 0.15, rise=1.3, rise_time=0.01, decay=30, body=0.0), 0.18)
    for i, m in enumerate((91, 96, 100)):
        place(x, 0.45 + 0.05 * i, ins.bell(float(mtof(m)), 0.3, 0.45, rel=0.4), 0.2)
    x += fizz(L, 14, 2200, 5000, 0.07, 43)
    return finish_sfx(x, wet=0.15, seconds=0.7, fade=0.2)


def main():
    os.makedirs(MUSIC, exist_ok=True)
    jobs = [
        (os.path.join(OUT, "ring_pass.ogg"), ring_pass),
        (os.path.join(OUT, "ring_big_o.ogg"), ring_big_o),
        (os.path.join(OUT, "enemy_engine_jet.ogg"), fighter_jet),
        (os.path.join(OUT, "enemy_engine_prop.ogg"), bomber_prop),
        (os.path.join(OUT, "enemy_explode.ogg"), enemy_explode),
        (os.path.join(MUSIC, "riser.ogg"), riser),
        (os.path.join(MUSIC, "climax.ogg"), climax),
        (os.path.join(OUT, "streak_reveal.ogg"), streak_reveal),
        (os.path.join(OUT, "enemy_warning.ogg"), enemy_warning),
        (os.path.join(OUT, "life_up.ogg"), life_up),
    ]
    only = set(sys.argv[1:])
    for path, fn in jobs:
        name = os.path.splitext(os.path.basename(path))[0]
        if only and name not in only:
            continue
        x = fn()
        to_ogg(x, path, quality=5)
        print("%-18s %5.2f s  momentary max %6.1f LUFS" % (name, x.shape[0] / SR, momentary_max(x)))


if __name__ == "__main__":
    main()
