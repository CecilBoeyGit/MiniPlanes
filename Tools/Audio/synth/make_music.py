"""
Composes and renders MiniPlanes' background music: lo-fi beats with arcade (chiptune) leads.

  menu     120 BPM, Bb major  general menu: Hangar, level intro, results (in the boot jingle's style)
  start     84 BPM, F major   the start page's own theme
  normal   120 BPM, F major   in-game, calm (in the boot jingle's style)
  hostile  120 BPM, F major   in-game while enemy planes are up
  uprise   120 BPM, F major   in-game build past 70% of the path, climaxing at the finish
  boss     126 BPM, E minor   zeppelin waves

normal, hostile and uprise share tempo, length, chords and bar lines, so the game plays them in lock-step and
crossfades between them without a seam. Every loop is rendered with wrap-around effects, so it joins its own
start exactly.
Run with any Python that has numpy:  python -I Tools/Audio/synth/make_music.py [names...]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

import instruments as ins
from dsp import (SR, mtof, melody, lp, hp, fft_filter, reverb, ping_pong, saturate, bitcrush, wow, crackle, pump, limiter,
                 loudness, to_ogg, pan_gains)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT = os.path.join(ROOT, "Assets", "MiniPlanes", "Audio", "Music")


class Song:
    def __init__(self, bpm, bars, swing16=0.0, seed=1):
        self.bpm = bpm
        self.spb = 60.0 / bpm
        self.bars = bars
        self.n = int(round(bars * 4 * self.spb * SR))
        self.T = self.n / SR
        self.swing16 = swing16
        self.buses = {}
        self.kicks = []
        self.rng = np.random.default_rng(seed)

    def beat_time(self, beat):
        q = beat * 4.0
        if abs(q - round(q)) < 1e-6 and int(round(q)) % 2 == 1:
            beat += self.swing16
        return beat * self.spb

    def add(self, bus, beat, sig, gain=1.0, pan=0.0, human=0.003):
        if bus not in self.buses:
            self.buses[bus] = np.zeros((self.n, 2))
        b = self.buses[bus]
        t = self.beat_time(beat) + (self.rng.normal(0.0, human) if human else 0.0)
        s = int(round(t * SR)) % self.n
        gl, gr = pan_gains(pan)
        pos = 0
        while pos < len(sig):
            k = min(len(sig) - pos, self.n - s)
            b[s:s + k, 0] += sig[pos:pos + k] * gain * gl
            b[s:s + k, 1] += sig[pos:pos + k] * gain * gr
            pos += k
            s = 0

    def note(self, bus, beat, beats, midi, inst, gain=1.0, pan=0.0, **kw):
        self.add(bus, beat, inst(float(mtof(midi)), beats * self.spb, **kw), gain, pan)

    def notes(self, bus, seq, inst, gain=1.0, pan=0.0, legato=0.95, **kw):
        for beat, d, m in seq:
            self.note(bus, beat, d * legato, m, inst, gain, pan, **kw)

    def chord(self, bus, beat, beats, midis, inst, gain=1.0, spread=0.5, strum=0.0, **kw):
        k = len(midis)
        for i, m in enumerate(midis):
            p = (i / max(1, k - 1) - 0.5) * spread
            self.note(bus, beat + strum * i / self.spb, beats, m, inst, gain, p, **kw)

    def drum(self, beat, sig, gain=1.0, pan=0.0, bus="drums"):
        self.add(bus, beat, sig, gain, pan, human=0.002)

    def kick(self, beat, sig, gain=1.0):
        self.drum(beat, sig, gain)
        self.kicks.append(self.beat_time(beat) * SR)


def step(bar, s):
    return bar * 4 + s * 0.25


def mixdown(song, buses, master):
    n = song.n
    pc = pump(n, song.kicks, depth=master.get("pump", 0.3), release=master.get("pump_release", 0.15))
    dry = np.zeros((n, 2))
    rev = np.zeros((n, 2))
    ech = np.zeros((n, 2))
    for name, x in song.buses.items():
        c = buses.get(name, {})
        if c.get("hp"):
            x = hp(x, c["hp"], periodic=True)
        if c.get("lp"):
            x = lp(x, c["lp"], periodic=True)
        if c.get("crush"):
            x = bitcrush(x, *c["crush"])
        if c.get("drive"):
            x = saturate(x, c["drive"])
        if c.get("pump", name != "drums"):
            x = x * pc[:, None]
        x = x * c.get("gain", 1.0)
        dry += x
        rev += x * c.get("rev", 0.0)
        ech += x * c.get("echo", 0.0)
    if np.any(ech):
        dry += ping_pong(ech, master.get("echo_beats", 0.75) * song.spb, feedback=0.42, taps=5, damp=3500)
    if np.any(rev):
        dry += reverb(rev, seconds=master.get("rev_seconds", 1.8), damp=master.get("rev_damp", 5500), periodic=True)
    mix = hp(dry, master.get("hp", 40), 2, periodic=True)
    air = 10 ** (master.get("air", 5.0) / 20.0)
    mix = fft_filter(mix, lambda f: 1.0 + (air - 1.0) / (1.0 + (1800.0 / np.maximum(f, 1.0)) ** 1.5), periodic=True)
    mix = lp(mix, master.get("lp", 11000), 2, periodic=True)
    if master.get("wow", 0):
        mix = wow(mix, song.T, depth_ms=1.4 * master["wow"], flutter_ms=0.25 * master["wow"])
    # Tape: bring the peaks to just under full scale first, so the saturation only rounds the loudest hits.
    mix = saturate(mix / (np.max(np.abs(mix)) + 1e-9), master.get("tape", 1.3))
    mix += crackle(n, density=6.0 * master.get("crackle", 0.5), level=0.06 * master.get("crackle", 0.5),
                   hiss=0.004 * master.get("crackle", 0.5)) * np.max(np.abs(mix))
    target = master.get("lufs", -16.0)
    for _ in range(3):
        i, _pk = loudness(mix)
        mix = limiter(mix * 10 ** ((target - i) / 20.0), -1.5, periodic=True)
    return mix


# ================================================================== shared style
# The menu and in-flight music follow the title jingle the player hears at boot: 120 BPM, a galloping
# sixteenth pulse ("da-da . da" on every beat), round marimba-like plucks and a playful whistle lead,
# with warm low-mid weight. Arcade square waves stay as sparkle.

GALLOP = (0, 1, 3)  # sixteenth steps sounded in each beat


def gallop_bass(s, bar, root, vel=0.75, bus="bass", tilt=10.0, sub=True):
    """Bass bouncing in the title's gallop: root, root, octave on every beat, with a sine under the downbeats."""
    for beat in range(4):
        for i, st in enumerate(GALLOP):
            m = root + (12 if st == 3 else 0)
            s.note(bus, bar * 4 + beat + st * 0.25, 0.2, m, ins.square_bass, vel * (1.0 if i == 0 else 0.7),
                   tilt=tilt, duty=0.5)
    if sub:
        s.note(bus, bar * 4, 1.8, root, ins.sine_bass, 0.4)
        s.note(bus, bar * 4 + 2, 1.8, root, ins.sine_bass, 0.32)


def gallop_marimba(s, bar, keys, vel=0.42, bus="keys", offset=0):
    """Chord tones climbing and falling in the gallop rhythm."""
    tones = sorted(keys) + [k + 12 for k in sorted(keys)]
    order = (0, 1, 2, 3, 4, 3, 2, 1, 2, 3, 4, 5, 4, 3, 2, 1)
    i = 0
    for beat in range(4):
        for st in GALLOP:
            m = tones[order[(i + offset) % len(order)]]
            s.note(bus, bar * 4 + beat + st * 0.25, 0.22, m, ins.marimba, vel * (1.0 if st == 0 else 0.75),
                   (i % 3 - 1) * 0.35)
            i += 1


def gallop_hats(s, bar, vel=0.3, pan=0.25):
    for beat in range(4):
        for st in range(4):
            g = vel if st == 0 else vel * 0.75 if st in (1, 3) else vel * 0.35
            s.drum(bar * 4 + beat + st * 0.25, KIT["hat"] if st == 0 else KIT["hat2"], g, pan)


# ================================================================== in-game trio (F major, 120 BPM)

G_PROG = [
    (41, [53, 57, 60, 65]),  # F
    (38, [53, 57, 62, 65]),  # Dm
    (46, [53, 58, 62, 65]),  # Bb
    (48, [52, 55, 60, 64]),  # C
    (41, [53, 57, 60, 65]),  # F
    (38, [53, 57, 62, 65]),  # Dm
    (46, [53, 58, 62, 65]),  # Bb
    (48, [52, 55, 60, 64]),  # C
    (46, [53, 58, 62, 65]),  # Bb
    (48, [52, 55, 60, 64]),  # C
    (45, [52, 57, 60, 64]),  # Am
    (38, [53, 57, 62, 65]),  # Dm
    (46, [53, 58, 62, 65]),  # Bb
    (48, [52, 55, 60, 64]),  # C
    (41, [53, 57, 60, 65]),  # F
    (48, [52, 58, 60, 64]),  # C7
]

THEME_A = ("C5/.5 F5/.5 A5/.5 F5/.25 G5/.25 A5/1 r/.5 C6/.5 "
           "A5/.5 G5/.5 F5/.5 D5/.5 F5/1 r/1 "
           "D5/.5 F5/.5 Bb5/.5 A5/.25 G5/.25 F5/1 r/.5 D5/.5 "
           "E5/.5 G5/.5 C6/1 Bb5/.5 A5/.5 G5/1 "
           "C5/.5 F5/.5 A5/.5 F5/.25 G5/.25 A5/1 C6/.5 D6/.5 "
           "C6/.5 A5/.5 F5/.5 A5/.5 D6/1 r/1 "
           "D6/.5 C6/.5 Bb5/.5 A5/.5 G5/.5 F5/.5 D5/1 "
           "E5/.5 F5/.5 G5/.5 A5/.5 C6/1.5 r/.5")
THEME_B = ("F5/.75 D5/.25 F5/.5 Bb5/.5 A5/1 F5/1 "
           "G5/.75 E5/.25 G5/.5 C6/.5 Bb5/1 G5/1 "
           "A5/.5 C6/.5 E6/.5 C6/.5 A5/1 E5/1 "
           "F5/.5 A5/.5 D6/.5 A5/.5 F5/1 D5/1 "
           "D5/.25 F5/.25 Bb5/.5 D6/1 C6/.5 Bb5/.5 A5/1 "
           "G5/.25 C6/.25 E6/.5 G6/1 F6/.5 E6/.5 D6/1 "
           "C6/.5 A5/.5 F5/.5 A5/.5 C6/1 F6/1 "
           "E6/.5 D6/.5 C6/.5 Bb5/.5 G5/.5 E5/.5 C5/1")
HB1 = "A5/.25 A5/.5 C6/.25 A5/.25 A5/.5 F5/.25 C6/.25 C6/.5 D6/.25 C6/.25 A5/.5 F5/.25 "
HB2 = "F5/.25 F5/.5 A5/.25 F5/.25 F5/.5 D5/.25 A5/.25 A5/.5 C6/.25 D6/.5 A5/.5 "
HB3 = "D6/.25 D6/.5 F6/.25 D6/.25 D6/.5 Bb5/.25 F5/.25 F5/.5 Bb5/.25 D6/.5 C6/.5 "
HB9 = "F6/.25 F6/.5 D6/.25 Bb5/.25 Bb5/.5 F5/.25 D6/.5 C6/.5 Bb5/1 "
HB10 = "G6/.25 G6/.5 E6/.25 C6/.25 C6/.5 G5/.25 E6/.5 D6/.5 C6/1 "
HOSTILE = (HB1 + HB2 + HB3 + "E6/.25 E6/.5 G6/.25 E6/.25 C6/.5 G5/.25 E5/.5 G5/.5 C6/1 " +
           HB1 + HB2 + HB3 + "G5/.25 A5/.25 Bb5/.25 C6/.25 D6/.25 E6/.25 F6/.25 G6/.25 E6/1 C6/1 " +
           HB9 + HB10 + "E6/.25 E6/.5 C6/.25 A5/.25 A5/.5 E5/.25 A5/.5 C6/.5 E6/1 " +
           "F6/.25 F6/.5 D6/.25 A5/.25 A5/.5 F5/.25 D6/.5 E6/.5 F6/1 " +
           HB9 + HB10 + "A5/.25 C6/.25 F6/.25 A6/.25 F6/.25 C6/.25 A5/.25 C6/.25 F6/1 r/1 " +
           "E6/.25 G6/.25 Bb6/.25 G6/.25 E6/.25 C6/.25 Bb5/.25 G5/.25 E5/1 C5/1")

# The three in-flight mixes must share these.
GAME_BPM, GAME_BARS, GAME_SWING = 120, 16, 0.0
BOSS_BASS = (0, 0, 12, 0, 0, 12, 0, 7, 0, 0, 12, 0, 7, 12, 7, 0)

KIT = dict(
    kick=ins.kick(1.0, tone=50, sweep=110, decay=8.0, click=0.25, seed=1),
    kick_soft=ins.kick(0.8, tone=52, sweep=90, decay=9.0, click=0.12, seed=2),
    snare=ins.snare(0.8, tone=190, snap=0.7, decay=15, seed=3),
    ghost=ins.snare(0.8, tone=200, snap=0.9, decay=30, seed=4),
    clap=ins.clap(0.9, seed=5),
    hat=ins.hat(0.5, False, seed=6),
    hat2=ins.hat(0.5, False, seed=7),
    ohat=ins.hat(0.5, True, seed=8),
    shaker=ins.shaker(0.5, seed=9),
    rim=ins.rim(0.6),
    crash=ins.crash(0.7, 2.5, seed=10),
)


def song_normal():
    """Calm flight in the title's style: four-on-the-floor bounce, galloping bass and marimba, and the theme on a
    playful whistle with a soft square double."""
    s = Song(GAME_BPM, GAME_BARS, GAME_SWING, seed=11)
    for bar, (root, keys) in enumerate(G_PROG):
        for k in (0, 4, 8, 12):
            s.kick(step(bar, k), KIT["kick"], 0.6 if k in (0, 8) else 0.48)
        for sn in (4, 12):
            s.drum(step(bar, sn), KIT["clap"], 0.5)
            s.drum(step(bar, sn), KIT["snare"], 0.4)
        gallop_hats(s, bar, 0.3)
        if bar % 4 == 3:
            for i, st in enumerate((13, 14, 15)):
                s.drum(step(bar, st), KIT["ghost"], 0.22 + 0.12 * i)
        if bar % 8 == 0:
            s.drum(step(bar, 0), KIT["crash"], 0.22, 0.3)
        gallop_bass(s, bar, root + (12 if root < 44 else 0), 0.72, sub=False)
        gallop_marimba(s, bar, keys, 0.44, offset=bar)
        s.chord("pad", bar * 4, 3.9, keys, ins.ep, 0.18, spread=0.6, strum=0.01)
    theme = melody(THEME_A + " " + THEME_B, 0)
    s.notes("lead", theme, ins.whistle, 0.5, 0.0, legato=0.9)
    s.notes("lead2", theme, ins.pulse, 0.1, 0.3, legato=0.6, duty=0.25, tilt=3.0, rel=0.04)
    buses = dict(
        drums=dict(lp=11000, drive=1.3, rev=0.05, gain=0.8),
        bass=dict(lp=1800, drive=1.15, gain=0.62),
        keys=dict(lp=6000, rev=0.18, echo=0.08, gain=0.7),
        pad=dict(lp=3500, rev=0.3, gain=0.5),
        lead=dict(lp=7000, rev=0.22, echo=0.18, gain=0.75),
        lead2=dict(lp=6000, rev=0.2, gain=0.5),
    )
    return mixdown(s, buses, dict(lufs=-16.0, hp=75, lp=12500, wow=0.25, crackle=0.2, pump=0.3, tape=1.4,
                                  echo_beats=0.75))


def song_hostile():
    """Enemy planes up: the same chords and length as normal, hard and fast. Driving kicks with sixteenth pickups,
    fills, a sixteenth distorted bass, galloping alarm stabs, a gated pad, fast arpeggios and a crunchy riff."""
    s = Song(GAME_BPM, GAME_BARS, GAME_SWING, seed=12)
    toms = (ins.tom(0.85, 200), ins.tom(0.85, 160), ins.tom(0.85, 125), ins.tom(0.85, 95))
    for bar, (root, keys) in enumerate(G_PROG):
        for k in (0, 3, 4, 8, 11, 12):
            s.kick(step(bar, k), KIT["kick"], 1.0 if k in (0, 4, 8, 12) else 0.7)
        if bar % 2:
            s.kick(step(bar, 14), KIT["kick"], 0.65)
            s.kick(step(bar, 15), KIT["kick_soft"], 0.55)
        for sn in (4, 12):
            s.drum(step(bar, sn), KIT["snare"], 0.95)
            s.drum(step(bar, sn), KIT["clap"], 0.6)
        for st in (7, 10, 15):
            s.drum(step(bar, st), KIT["ghost"], 0.2)
        gallop_hats(s, bar, 0.36, 0.3)
        for st in (2, 6, 10, 14):
            s.drum(step(bar, st), KIT["ohat"], 0.2, -0.25)
        if bar % 4 == 3:
            if bar % 8 == 7:
                for i, st in enumerate(range(8, 16)):
                    s.drum(step(bar, st), toms[i // 2], 0.5 + 0.04 * i, 0.4 - 0.1 * i)
            else:
                for st in range(12, 16):
                    s.drum(step(bar, st), KIT["snare"], 0.45 + 0.1 * (st - 12))
        if bar % 4 == 0:
            s.drum(step(bar, 0), KIT["crash"], 0.45, 0.3 if bar % 8 else -0.3)
        for st in range(16):
            s.note("bass", step(bar, st), 0.18, root + BOSS_BASS[st], ins.square_bass,
                   0.85 if st % 4 == 0 else 0.6, tilt=7.0, duty=0.5)
        for beat in range(4):
            for st in GALLOP:
                s.chord("stab", bar * 4 + beat + st * 0.25, 0.18, keys, ins.brass_stab, 0.2 if st == 0 else 0.13,
                        spread=0.7, cutoff=3400)
        for st in range(16):
            if st % 4 == 2:
                continue
            s.chord("pad", step(bar, st), 0.14, [k + 12 for k in keys], ins.saw_pad, 0.1, spread=0.9, attack=0.004,
                    rel=0.04, cutoff=2800, voices=2)
        tones = sorted(keys) + [k + 12 for k in sorted(keys)]
        for st in range(16):
            idx = (0, 2, 4, 6, 7, 5, 3, 1)[st % 8]
            s.note("arp", step(bar, st), 0.14, tones[idx] + 12, ins.pulse, 0.24, (st % 2) * 0.8 - 0.4,
                   duty=0.125, tilt=5.0, rel=0.03)
    lead = melody(HOSTILE, 0, shift=-12)
    s.notes("lead", lead, ins.pulse, 0.5, 0.0, legato=0.88, duty=0.125, vib=0.006, tilt=0.5, rel=0.04)
    s.notes("lead2", [(b, d, m + 12) for b, d, m in lead], ins.pulse, 0.16, 0.25, legato=0.7, duty=0.25, tilt=2.0,
            rel=0.03)
    buses = dict(
        drums=dict(lp=13000, drive=1.55, rev=0.05, gain=0.9),
        bass=dict(lp=3200, drive=1.5, gain=0.7),
        stab=dict(lp=4500, drive=1.2, rev=0.15, gain=0.6),
        pad=dict(lp=4000, rev=0.2, gain=0.5),
        arp=dict(lp=8000, crush=(7, 2), rev=0.1, echo=0.12, gain=0.55),
        lead=dict(lp=8000, crush=(8, 2), rev=0.15, echo=0.15, gain=0.8),
        lead2=dict(lp=9000, rev=0.2, gain=0.5),
    )
    return mixdown(s, buses, dict(lufs=-14.5, lp=14000, wow=0.15, crackle=0.15, pump=0.45, tape=1.6, echo_beats=0.75))


def song_uprise():
    """The build to the finish: everything from normal at full tilt, with claps, open hats, rising arpeggios,
    crashes, snare rolls and the theme doubled an octave down."""
    s = Song(GAME_BPM, GAME_BARS, GAME_SWING, seed=13)
    for bar, (root, keys) in enumerate(G_PROG):
        for k in (0, 4, 8, 12):
            s.kick(step(bar, k), KIT["kick"], 1.0)
        if bar % 2:
            s.kick(step(bar, 14), KIT["kick_soft"], 0.6)
        for sn in (4, 12):
            s.drum(step(bar, sn), KIT["snare"], 0.8)
            s.drum(step(bar, sn), KIT["clap"], 0.65)
        gallop_hats(s, bar, 0.36)
        for st in range(16):
            s.drum(step(bar, st), KIT["shaker"], 0.2 if st % 2 else 0.1, -0.35)
        for st in (2, 6, 10, 14):
            s.drum(step(bar, st), KIT["ohat"], 0.22, -0.2)
        if bar % 4 == 0:
            s.drum(step(bar, 0), KIT["crash"], 0.4, -0.3 if bar % 8 else 0.3)
        if bar % 8 == 7:
            first = 8 if bar == 15 else 12
            for st in range(first, 16):
                s.drum(step(bar, st), KIT["snare"], 0.3 + 0.5 * (st - first) / (16 - first))
        gallop_bass(s, bar, root, 0.8, tilt=7.0)
        gallop_marimba(s, bar, keys, 0.42, offset=bar)
        s.chord("pad", bar * 4, 3.9, [k + 12 for k in keys], ins.saw_pad, 0.18, spread=0.9, attack=0.15, cutoff=3200)
        tones = sorted(keys) + [k + 12 for k in sorted(keys)]
        for st in range(16):
            s.note("arp", step(bar, st), 0.14, tones[st % 8] + 12, ins.pulse, 0.18 + 0.012 * (st % 8),
                   (st % 8) / 7.0 - 0.5, duty=0.5, tilt=6.0, rel=0.03)
    theme = melody(THEME_A + " " + THEME_B, 0)
    s.notes("lead", theme, ins.whistle, 0.55, 0.0, legato=0.9)
    s.notes("lead2", theme, ins.pulse, 0.2, 0.25, legato=0.8, duty=0.25, tilt=1.5, rel=0.05)
    s.notes("lead3", [(b, d, m - 12) for b, d, m in theme], ins.saw_pad, 0.22, 0.0, legato=0.9, attack=0.01,
            rel=0.08, cutoff=2500, voices=2)
    buses = dict(
        drums=dict(lp=13000, drive=1.4, rev=0.06, gain=0.88),
        bass=dict(lp=2600, drive=1.25, gain=0.68),
        keys=dict(lp=7000, rev=0.18, gain=0.62),
        pad=dict(lp=4500, rev=0.3, gain=0.5),
        arp=dict(lp=8000, rev=0.15, echo=0.15, gain=0.55),
        lead=dict(lp=8000, rev=0.22, echo=0.15, gain=0.75),
        lead2=dict(lp=8000, rev=0.2, gain=0.55),
        lead3=dict(lp=3000, rev=0.25, gain=0.45),
    )
    return mixdown(s, buses, dict(lufs=-14.0, lp=14500, wow=0.15, crackle=0.15, pump=0.38, tape=1.5, echo_beats=0.75))


# ================================================================== menus

M_PROG = [
    (46, [53, 58, 62, 65]),  # Bb
    (39, [55, 58, 63, 67]),  # Eb
    (43, [55, 58, 62, 67]),  # Gm
    (41, [53, 57, 60, 65]),  # F
    (46, [53, 58, 62, 65]),  # Bb
    (39, [55, 58, 63, 67]),  # Eb
    (48, [55, 58, 60, 63]),  # Cm7
    (41, [53, 57, 63, 65]),  # F7
    (39, [55, 58, 63, 67]),  # Eb
    (41, [53, 57, 60, 65]),  # F
    (38, [53, 57, 62, 65]),  # Dm
    (43, [55, 58, 62, 67]),  # Gm
    (39, [55, 58, 63, 67]),  # Eb
    (41, [53, 57, 60, 65]),  # F
    (46, [53, 58, 62, 65]),  # Bb
    (41, [53, 57, 63, 65]),  # F7
]
MENU_A = ("F4/.5 Bb4/.5 D5/.5 Bb4/.25 C5/.25 D5/1 r/.5 F5/.5 "
          "Eb5/.5 C5/.5 Bb4/.5 G4/.5 Bb4/1 r/1 "
          "D5/.5 Bb4/.5 G4/.5 Bb4/.25 C5/.25 D5/1 r/.5 G5/.5 "
          "F5/.5 Eb5/.5 D5/.5 C5/.5 A4/1 C5/1 "
          "F4/.5 Bb4/.5 D5/.5 Bb4/.25 C5/.25 D5/1 r/.5 F5/.5 "
          "G5/.5 F5/.5 Eb5/.5 Bb4/.5 G4/1 Bb4/1 "
          "C5/.5 Eb5/.5 G5/.5 Bb5/.5 G5/1 Eb5/1 "
          "F5/.5 A5/.5 C6/.5 A5/.5 F5/1.5 r/.5")
MENU_B = ("r/1 G5/.5 Bb5/.5 G5/.5 Eb5/.5 Bb4/1 "
          "r/1 A5/.5 C6/.5 A5/.5 F5/.5 C5/1 "
          "D5/.5 F5/.5 A5/1 G5/.5 F5/.5 D5/1 "
          "G5/.5 Bb5/.5 D6/1 C6/.5 Bb5/.5 G5/1 "
          "Bb5/.75 G5/.25 Eb5/.5 G5/.5 Bb5/1 Eb6/1 "
          "C6/.75 A5/.25 F5/.5 A5/.5 C6/1 F6/1 "
          "D6/.5 C6/.5 Bb5/.5 F5/.5 D5/1 Bb4/1 "
          "A4/.5 C5/.5 Eb5/.5 F5/.5 A5/1 C5/1")


def song_menu():
    """Menus in the title's style: a lighter version of the in-flight bounce, marimba carrying the tune in the
    jingle's own register, galloping bass and hats, B-flat major like the jingle."""
    s = Song(120, 16, 0.0, seed=21)
    for bar, (root, keys) in enumerate(M_PROG):
        for k in (0, 8):
            s.kick(step(bar, k), KIT["kick"], 0.55)
        for k in (4, 12):
            s.kick(step(bar, k), KIT["kick_soft"], 0.35)
        for sn in (4, 12):
            s.drum(step(bar, sn), KIT["clap"], 0.42)
            s.drum(step(bar, sn), KIT["rim"], 0.25, 0.2)
        gallop_hats(s, bar, 0.22)
        for st in (2, 6, 10, 14):
            s.drum(step(bar, st), KIT["shaker"], 0.14, -0.3)
        if bar % 8 == 7:
            for i, st in enumerate((13, 14, 15)):
                s.drum(step(bar, st), KIT["ghost"], 0.2 + 0.12 * i)
        gallop_bass(s, bar, root + (12 if root < 44 else 0), 0.62, tilt=12.0, sub=False)
        gallop_marimba(s, bar, keys, 0.3, offset=bar * 3)
        s.chord("pad", bar * 4, 3.9, keys, ins.ep, 0.2, spread=0.6, strum=0.012)
    tune = melody(MENU_A + " " + MENU_B, 0)
    s.notes("lead", tune, ins.marimba, 0.6, 0.0, legato=1.0, decay=4.0)
    s.notes("lead2", tune, ins.whistle, 0.22, 0.2, legato=0.85, vib=0.004)
    buses = dict(
        drums=dict(lp=10000, drive=1.25, rev=0.05, gain=0.72),
        bass=dict(lp=1600, drive=1.1, gain=0.6),
        keys=dict(lp=5500, rev=0.2, echo=0.1, gain=0.65),
        pad=dict(lp=3500, rev=0.3, gain=0.5),
        lead=dict(lp=7000, rev=0.25, echo=0.2, gain=0.8),
        lead2=dict(lp=6000, rev=0.25, gain=0.5),
    )
    return mixdown(s, buses, dict(lufs=-17.0, hp=75, lp=11500, wow=0.3, crackle=0.3, pump=0.25, tape=1.4,
                                  echo_beats=0.75))


S_PROG = [
    (46, [57, 62, 65, 69]),  # Bbmaj7
    (48, [55, 60, 64, 69]),  # C6
    (45, [55, 60, 64, 67]),  # Am7
    (38, [53, 57, 60, 64]),  # Dm9
    (43, [53, 57, 58, 62]),  # Gm9
    (48, [52, 58, 62, 67]),  # C9
    (41, [57, 60, 64, 67]),  # Fmaj9
    (41, [57, 63, 65, 69]),  # F7
    (46, [57, 62, 65, 69]),  # Bbmaj7
    (45, [55, 60, 64, 67]),  # Am7
    (43, [53, 57, 58, 62]),  # Gm9
    (48, [53, 58, 62, 67]),  # C7sus
    (46, [57, 62, 65, 69]),  # Bbmaj7
    (45, [55, 60, 64, 67]),  # Am7
    (43, [53, 57, 58, 62]),  # Gm9
    (48, [52, 58, 62, 67]),  # C9
]
START_A = ("D5/.5 F5/.5 A5/1 G5/.5 F5/.5 D5/1 "
           "E5/.5 G5/.5 C6/1.5 A5/.5 G5/1 "
           "A5/.75 G5/.25 E5/1 C5/.5 D5/.5 E5/1 "
           "F5/1.5 E5/.5 D5/2 "
           "D5/.5 F5/.5 Bb5/1 A5/.5 G5/.5 F5/1 "
           "E5/.5 G5/.5 Bb5/1 C6/1 Bb5/.5 A5/.5 "
           "A5/2.5 G5/.5 F5/.5 E5/.5 "
           "F5/2 r/1 A4/.5 C5/.5")
START_B = ("r/1 D5/.5 E5/.5 F5/1 A5/1 "
           "G5/1 E5/1 C5/2 "
           "r/1 Bb4/.5 C5/.5 D5/1 F5/1 "
           "F5/1.5 D5/.5 C5/2 "
           "r/1 D5/.5 F5/.5 A5/1 C6/1 "
           "C6/1 A5/1 E5/1 G5/1 "
           "F5/1 D5/1 Bb4/1 D5/1 "
           "E5/1 G5/1 C6/1 Bb5/1")


def song_start():
    s = Song(84, 16, 0.04, seed=31)
    for bar, (root, keys) in enumerate(S_PROG):
        for k in [0, 10] + ([7] if bar % 2 else [3]):
            s.kick(step(bar, k), KIT["kick"] if k == 0 else KIT["kick_soft"], 0.85 if k == 0 else 0.6)
        for sn in (4, 12):
            s.drum(step(bar, sn), KIT["snare"], 0.62)
        for st in range(0, 16, 2):
            s.drum(step(bar, st), KIT["hat"] if st % 4 == 0 else KIT["hat2"], 0.26 if st % 4 == 0 else 0.17, 0.25)
        for st in (3, 7, 11, 15):
            s.drum(step(bar, st), KIT["shaker"], 0.16, -0.3)
        if bar % 8 == 7:
            for i, st in enumerate((13, 14, 15)):
                s.drum(step(bar, st), KIT["ghost"], 0.2 + 0.15 * i)
        s.chord("keys", bar * 4, 2.4, keys, ins.ep, 0.46, spread=0.5, strum=0.012)
        s.chord("keys", bar * 4 + 2.5, 1.3, keys, ins.ep, 0.32, spread=0.5, strum=0.008)
        s.note("bass", bar * 4, 1.4, root, ins.sine_bass, 0.85)
        s.note("bass", bar * 4 + 1.75, 0.45, root, ins.sine_bass, 0.5)
        s.note("bass", bar * 4 + 2.5, 1.2, root + 7, ins.sine_bass, 0.65)
    s.notes("lead", melody(START_A, 0), ins.pulse, 0.36, 0.0, legato=0.9, duty=0.5, tilt=1.5, vib=0.005, rel=0.1)
    s.notes("lead", melody(START_B, 32), ins.pulse, 0.26, 0.0, legato=0.9, duty=0.25, tilt=2.0, vib=0.004, rel=0.1)
    s.notes("bell", melody(START_B, 32, shift=12), ins.bell, 0.16, 0.35)
    buses = dict(
        drums=dict(lp=8000, drive=1.3, rev=0.05, gain=0.75),
        bass=dict(lp=1500, gain=0.6),
        keys=dict(lp=4200, drive=1.1, rev=0.26, gain=0.75),
        lead=dict(lp=4500, rev=0.22, echo=0.25, gain=0.75),
        bell=dict(rev=0.45, echo=0.2, gain=0.6),
    )
    return mixdown(s, buses, dict(lufs=-17.0, lp=9500, wow=0.55, crackle=0.6, pump=0.2, tape=1.4))


# ================================================================== boss

B_PROG = [
    (40, [52, 55, 59, 64]),  # Em
    (40, [52, 55, 59, 64]),
    (36, [52, 55, 60, 64]),  # C
    (38, [54, 57, 62, 66]),  # D
    (40, [52, 55, 59, 64]),
    (40, [52, 55, 59, 64]),
    (36, [52, 55, 60, 64]),
    (35, [51, 57, 59, 63]),  # B7
    (45, [52, 57, 60, 64]),  # Am
    (40, [52, 55, 59, 64]),  # Em
    (45, [52, 57, 60, 64]),  # Am
    (35, [51, 57, 59, 63]),  # B7
    (36, [52, 55, 60, 64]),  # C
    (38, [54, 57, 62, 66]),  # D
    (35, [52, 57, 59, 64]),  # B7sus4
    (35, [51, 57, 59, 63]),  # B7
]
BOSS_1 = "E5/.25 E5/.25 r/.25 E5/.25 G5/.5 F#5/.5 E5/.5 D#5/.5 E5/1 "
BOSS_A = (BOSS_1 +
          "B5/.5 A5/.5 G5/.5 F#5/.5 G5/.5 A5/.5 B5/1 "
          "C6/.75 B5/.25 G5/.5 E5/.5 C6/.5 B5/.5 G5/1 "
          "D6/.75 C6/.25 A5/.5 F#5/.5 D6/.5 C6/.5 A5/1 " +
          BOSS_1 +
          "B5/.5 C6/.5 B5/.5 A5/.5 G5/.5 F#5/.5 E5/1 "
          "E6/.5 D6/.5 C6/.5 B5/.5 C6/.5 D6/.5 E6/1 "
          "D#6/1 F#6/1 B5/1 A5/1")
BOSS_B = ("G5/.5 C6/.5 E6/1 D6/.5 C6/.5 B5/1 "
          "A5/.5 D6/.5 F#6/1 E6/.5 D6/.5 C6/1 "
          "E6/2 F#6/2 "
          "D#6/1 C6/1 B5/2")


def song_boss():
    s = Song(126, 16, 0.0, seed=41)
    kick = ins.kick(1.0, tone=46, sweep=130, decay=9.0, click=0.35, seed=42)
    toms = (ins.tom(0.9, 160), ins.tom(0.9, 120), ins.tom(0.9, 90))
    for bar, (root, keys) in enumerate(B_PROG):
        for k in (0, 4, 8, 12):
            s.kick(step(bar, k), kick, 1.0)
        if bar % 2:
            s.kick(step(bar, 14), kick, 0.6)
        if bar >= 8:
            s.kick(step(bar, 6), kick, 0.5)
        for sn in (4, 12):
            s.drum(step(bar, sn), KIT["snare"], 0.9)
            s.drum(step(bar, sn), KIT["clap"], 0.55)
        for st in range(16):
            s.drum(step(bar, st), KIT["hat"] if st % 2 == 0 else KIT["hat2"], 0.24 if st % 4 == 2 else 0.15, 0.3)
        if 8 <= bar < 12:
            for i, st in enumerate((3, 7, 11)):
                s.drum(step(bar, st), toms[(i + bar) % 3], 0.45, 0.4 - 0.4 * i)
        if bar in (0, 8):
            s.drum(step(bar, 0), KIT["crash"], 0.4, 0.25)
        if bar == 15:
            for st in range(8, 16):
                s.drum(step(bar, st), KIT["snare"], 0.3 + 0.07 * (st - 8))
        for st in range(16):
            s.note("bass", step(bar, st), 0.2, root + BOSS_BASS[st], ins.square_bass, 0.75 if st % 4 == 0 else 0.55,
                   tilt=10.0, duty=0.5)
        s.chord("stab", step(bar, 0), 0.6, keys, ins.brass_stab, 0.35, spread=0.6)
        s.chord("stab", step(bar, 6), 0.35, keys, ins.brass_stab, 0.25, spread=0.6)
        s.chord("pad", bar * 4, 3.9, keys, ins.saw_pad, 0.2, spread=0.8, attack=0.2, cutoff=1000)
        if 8 <= bar < 12:
            tones = sorted(keys) + [k + 12 for k in sorted(keys)]
            for st in range(16):
                idx = (0, 1, 2, 3, 4, 3, 2, 1)[st % 8] + (2 if st >= 8 else 0)
                s.note("arp", step(bar, st), 0.18, tones[idx] + 12, ins.pulse, 0.3, (st % 2) * 0.7 - 0.35,
                       duty=0.125, tilt=4.0, rel=0.03)
    s.notes("lead", melody(BOSS_A, 0, shift=-12), ins.pulse, 0.45, 0.0, legato=0.88, duty=0.125, vib=0.007,
            tilt=0.5, rel=0.05)
    s.notes("lead", melody(BOSS_B, 48, shift=-12), ins.pulse, 0.45, 0.0, legato=0.88, duty=0.125, vib=0.007,
            tilt=0.5, rel=0.05)
    s.notes("lead2", melody(BOSS_A, 0), ins.pulse, 0.12, 0.3, legato=0.6, duty=0.5, tilt=3.0, rel=0.04)
    buses = dict(
        drums=dict(lp=11000, drive=1.5, rev=0.05, gain=0.9),
        bass=dict(lp=3000, drive=1.3, gain=0.7),
        stab=dict(lp=4000, drive=1.2, rev=0.2, gain=0.6),
        pad=dict(lp=1800, rev=0.3, gain=0.6),
        arp=dict(lp=7000, crush=(7, 2), rev=0.12, echo=0.15, gain=0.5),
        lead=dict(lp=7000, crush=(8, 2), rev=0.15, echo=0.18, gain=0.75),
        lead2=dict(lp=6000, crush=(6, 3), rev=0.2, gain=0.5),
    )
    return mixdown(s, buses, dict(lufs=-15.0, lp=12000, wow=0.25, crackle=0.3, pump=0.3, tape=1.6, echo_beats=0.5))


SONGS = dict(menu=song_menu, start=song_start, normal=song_normal, hostile=song_hostile, uprise=song_uprise,
             boss=song_boss)


def main():
    os.makedirs(OUT, exist_ok=True)
    # normal, hostile and uprise now come from make_title.py (built on the title recording); the synthesized
    # versions here are kept for reference and only made when named.
    names = sys.argv[1:] or ["menu", "start", "boss"]
    for name in names:
        x = SONGS[name]()
        path = os.path.join(OUT, "bgm_" + name + ".ogg")
        to_ogg(x, path, quality=5)
        i, pk = loudness(x)
        print("bgm_%-8s %6.2f s  %d samples  %5.1f LUFS  peak %5.1f dB" % (name, x.shape[0] / SR, x.shape[0], i, pk))


if __name__ == "__main__":
    main()
