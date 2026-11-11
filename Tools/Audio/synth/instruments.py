"""Instruments and drums for the MiniPlanes music. Each returns a mono note buffer."""
import numpy as np

from dsp import (SR, times, env, phase_of, additive, amp_pulse, amp_saw, amp_tri, noise, lp, hp, bp, saturate)


# ------------------------------------------------------------------ tonal

def ep(f, dur, vel=0.7, rel=0.35, bright=1.0):
    """Lo-fi electric piano: two-operator FM with a soft bell tine, warm and slightly dull."""
    n = int((dur + rel) * SR)
    t = times(n)
    idx = bright * ((0.5 + 1.4 * vel) * np.exp(-t * 4.5) + 0.45)
    car = np.sin(2 * np.pi * f * t + idx * np.sin(2 * np.pi * f * t))
    tine = 0.16 * vel * bright * np.sin(2 * np.pi * f * 4.0 * t) * np.exp(-t * 16.0)
    x = (car + tine) * env(n, dur, attack=0.003, decay=1.0, rel=rel)
    return x * vel


def pulse(f, dur, vel=0.6, duty=0.5, rel=0.06, decay=0.0, vib=0.0, vib_delay=0.18, tilt=0.0, kmax=40,
          slide_from=None, slide_time=0.03, attack=0.004):
    """Arcade pulse/square voice, band-limited. tilt closes it like a pluck; vib adds delayed vibrato."""
    n = int((dur + rel) * SR)
    t = times(n)
    fr = np.full(n, float(f))
    if slide_from is not None:
        fr = f + (slide_from - f) * np.exp(-t / slide_time)
    if vib:
        fr = fr * (1.0 + vib * np.sin(2 * np.pi * 5.6 * t) * np.clip((t - vib_delay) / 0.2, 0.0, 1.0))
    x = additive(phase_of(fr), fr.max(), amp_pulse(duty), kmax, tilt, t)
    return x * env(n, dur, attack=attack, decay=decay, rel=rel) * vel


def tri(f, dur, vel=0.7, rel=0.05, decay=0.0):
    n = int((dur + rel) * SR)
    t = times(n)
    x = additive(2 * np.pi * f * t, f, amp_tri, 15)
    return x * env(n, dur, attack=0.003, decay=decay, rel=rel) * vel


def sine_bass(f, dur, vel=0.8, rel=0.08, decay=1.2):
    """Round lo-fi bass: sine with a touch of 2nd and 3rd harmonic, softly saturated."""
    n = int((dur + rel) * SR)
    t = times(n)
    ph = 2 * np.pi * f * t
    x = np.sin(ph) + 0.4 * np.sin(2 * ph) * np.exp(-t * 1.2) + 0.18 * np.sin(3 * ph) * np.exp(-t * 2.5) + 0.06 * np.sin(4 * ph) * np.exp(-t * 4)
    x = saturate(x * 1.2, 1.4)
    return x * env(n, dur, attack=0.006, decay=decay, rel=rel) * vel


def square_bass(f, dur, vel=0.8, rel=0.04, tilt=7.0, duty=0.5):
    """Plucky arcade bass with a closing filter, plus a sine sub for weight."""
    n = int((dur + rel) * SR)
    t = times(n)
    ph = 2 * np.pi * f * t
    x = 0.55 * additive(ph, f, amp_pulse(duty), 30, tilt, t) + 0.6 * np.sin(ph)
    return x * env(n, dur, attack=0.002, decay=2.0, rel=rel) * vel


def saw_pad(f, dur, vel=0.5, rel=0.9, attack=0.35, detune=0.006, cutoff=1800.0, voices=3):
    """Detuned saw pad with a gentle low-pass made from harmonic weights."""
    n = int((dur + rel) * SR)
    t = times(n)
    out = np.zeros(n)
    for i in range(voices):
        d = 1.0 + detune * (i - (voices - 1) / 2.0)
        fi = f * d
        ph = 2 * np.pi * fi * t + i * 2.1
        out += additive(ph, fi, lambda k, fi=fi: amp_saw(k) / np.sqrt(1.0 + (k * fi / cutoff) ** 4), 40)
    return out / voices * env(n, dur, attack=attack, rel=rel) * vel


def brass_stab(f, dur, vel=0.6, rel=0.12, cutoff=2600.0):
    """Short saw stab whose brightness falls, for tense hits."""
    n = int((dur + rel) * SR)
    t = times(n)
    out = np.zeros(n)
    for d in (0.996, 1.004):
        fi = f * d
        out += additive(2 * np.pi * fi * t, fi, lambda k, fi=fi: amp_saw(k) / np.sqrt(1.0 + (k * fi / cutoff) ** 4), 40, 3.0, t)
    return out * 0.5 * env(n, dur, attack=0.006, decay=3.0, rel=rel) * vel


def bell(f, dur, vel=0.5, rel=1.2):
    """Music-box / chime: a few inharmonic partials with quick decays."""
    n = int((dur + rel) * SR)
    t = times(n)
    x = (np.sin(2 * np.pi * f * t) * np.exp(-t * 2.2)
         + 0.35 * np.sin(2 * np.pi * f * 2.0 * t) * np.exp(-t * 4.0)
         + 0.18 * np.sin(2 * np.pi * f * 3.01 * t) * np.exp(-t * 7.0)
         + 0.08 * np.sin(2 * np.pi * f * 5.43 * t) * np.exp(-t * 12.0))
    return x * env(n, dur, attack=0.002, rel=rel) * vel


# ------------------------------------------------------------------ drums

def kick(vel=1.0, tone=48.0, sweep=120.0, decay=7.0, click=0.25, seed=0):
    n = int(0.6 * SR)
    t = times(n)
    f = tone + sweep * np.exp(-t * 32.0)
    body = np.sin(phase_of(f)) * np.exp(-t * decay)
    cl = hp(noise(n, seed), 2000) * np.exp(-t * 260.0) * click
    return saturate(1.5 * (body + cl), 1.3) * vel


def snare(vel=0.8, tone=185.0, snap=0.8, decay=16.0, seed=1):
    n = int(0.45 * SR)
    t = times(n)
    body = np.sin(phase_of(tone + 60.0 * np.exp(-t * 40.0))) * np.exp(-t * 22.0) * 0.6
    nz = bp(noise(n, seed), 1200, 9000) * np.exp(-t * decay) * snap * 0.5
    return saturate(body + nz, 1.2) * vel


def clap(vel=0.8, seed=2):
    n = int(0.4 * SR)
    t = times(n)
    nz = bp(noise(n, seed), 900, 5000)
    e = np.zeros(n)
    for k, dt in enumerate((0.0, 0.011, 0.022, 0.033)):
        tt = t - dt
        e += np.where(tt >= 0, np.exp(-np.maximum(tt, 0) * 170.0), 0.0) * (0.8 if k < 3 else 1.0)
    e += np.where(t > 0.033, np.exp(-(t - 0.033) * 14.0), 0.0) * 0.5
    return nz * e * 0.5 * vel


_METAL = (205.3, 304.4, 369.6, 522.7, 540.0, 800.0)


def _metal(n, scale=1.0):
    t = times(n)
    return sum(np.sign(np.sin(2 * np.pi * f * scale * t + i)) for i, f in enumerate(_METAL)) / 6.0


def hat(vel=0.4, open_=False, seed=4):
    n = int((0.6 if open_ else 0.12) * SR)
    t = times(n)
    x = 0.6 * _metal(n, 1.0) + 0.5 * noise(n, seed)
    x = hp(x, 7000, 3)
    return x * np.exp(-t * (7.0 if open_ else 55.0)) * env(n, 99, attack=0.001, rel=None) * vel * 0.6


def shaker(vel=0.3, seed=5):
    n = int(0.09 * SR)
    t = times(n)
    x = bp(noise(n, seed), 5000, 12000)
    e = np.minimum(1.0, t / 0.012) * np.exp(-t * 45.0)
    return x * e * vel * 0.5


def rim(vel=0.5):
    n = int(0.12 * SR)
    t = times(n)
    x = np.sin(2 * np.pi * 1700 * t) * np.exp(-t * 90) + 0.6 * np.sin(2 * np.pi * 520 * t) * np.exp(-t * 60)
    return x * vel * 0.6


def tom(vel=0.7, f=110.0, seed=6):
    n = int(0.5 * SR)
    t = times(n)
    body = np.sin(phase_of(f + f * 0.6 * np.exp(-t * 20.0))) * np.exp(-t * 9.0)
    nz = bp(noise(n, seed), 300, 3000) * np.exp(-t * 40.0) * 0.2
    return saturate(body + nz, 1.2) * vel


def crash(vel=0.6, dur=3.0, seed=8):
    n = int(dur * SR)
    t = times(n)
    x = 0.5 * _metal(n, 1.7) + 0.7 * noise(n, seed)
    x = hp(x, 4500, 2)
    return x * np.exp(-t * 1.6) * np.minimum(1.0, t / 0.002) * vel * 0.5


def reverse_cymbal(dur=1.5, vel=0.5, seed=9):
    c = crash(1.0, dur, seed)
    return c[::-1] * vel


def marimba(f, dur, vel=0.7, rel=0.12, decay=7.0):
    """Round, woody pluck: a near-sine fundamental with a quick 4th-partial knock, like a soft marimba."""
    n = int((dur + rel) * SR)
    t = times(n)
    ph = 2 * np.pi * f * t
    x = (np.sin(ph) + 0.22 * np.sin(2 * ph) * np.exp(-t * 10.0) + 0.16 * np.sin(4 * ph) * np.exp(-t * 35.0)
         + 0.05 * np.sin(9.9 * ph) * np.exp(-t * 80.0))
    return x * env(n, dur, attack=0.002, decay=decay, rel=rel) * vel


def whistle(f, dur, vel=0.6, rel=0.08, vib=0.006, slide=0.0):
    """Round playful lead: sine with a little 2nd harmonic, a breathy edge and delayed vibrato."""
    n = int((dur + rel) * SR)
    t = times(n)
    fr = np.full(n, float(f))
    if slide:
        fr = f * (1.0 + slide * np.exp(-t / 0.03))
    fr = fr * (1.0 + vib * np.sin(2 * np.pi * 6.0 * t) * np.clip((t - 0.12) / 0.15, 0.0, 1.0))
    ph = phase_of(fr)
    x = np.sin(ph) + 0.25 * np.sin(2 * ph) + 0.06 * np.sin(3 * ph)
    breath = bp(noise(n, int(f) % 997), f * 0.8, f * 3.0) * 0.04 * np.exp(-t * 18.0)
    return (x + breath) * env(n, dur, attack=0.008, decay=0.3, rel=rel) * vel
