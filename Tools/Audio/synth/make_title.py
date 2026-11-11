"""
Builds the title theme and the in-flight music from the Skylooper title jingle,
"Friendly UI App Jingle" by TommasoMotteran (freesound 858479, CC0): piano and kalimba, 120 BPM, B-flat major.

The recording is 47 s on an exact 120 BPM grid (bars start at 0.266 s). Bars 0-1 are the opening heard as
title.ogg; bars 2-17 are a 16-bar section that the recording repeats from bar 18. So:

  Music/bgm_title_intro   the opening (bars 0-1), played once when the start-page music starts
  Music/bgm_title         bars 2-17, a 32 s loop. Its end blends (0.25 s) into the recording's own repeat of
                          bar 2, so it joins its start with no click; the intro runs straight into it.

The in-flight mixes share the loop's key, chords, bars and length, so the game can crossfade between them at any
moment. They leave the recording itself out (its melody over new drums sounded like two tracks at once): each is
synthesized drums, the recording's low end as bass, and synthesized chords on the recording's measured changes:

  Music/bgm_normal        four-on-the-floor kick, claps, galloping hats and shaker, fills and crashes, the bass,
                          a soft pad and electric-piano chords in the title's da-da-.-da gallop, a sidechain pump
  Music/bgm_hostile       harder: driving kicks with sixteenth pickups, snare and tom fills, a distorted bass,
                          brass stabs and a gated pad in the gallop, fast crushed arpeggios, a heavier pump
  Music/bgm_uprise        the build to the finish: the biggest drums, open hats, rolls, bright pad and keys,
                          a sixteenth shimmer an octave up and climbing arpeggios Run with Blender's Python:  python -I Tools/Audio/synth/make_title.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import numpy as np

import instruments as ins
import make_music as mm
from dsp import SR, read_audio, lp, hp, bp, saturate, bitcrush, limiter, loudness, to_ogg

SRC = "D:/Unity/MiniPlanes_Docs/SkylooperSounds/Sounds/UI/Title Screen/858479__tommasomotteran__friendly-ui-app-jingle.wav"
OUT = mm.OUT

BAR = 2.0                     # seconds (120 BPM)
BAR0 = 0.266                  # first downbeat in the recording, seconds
LOOP_FROM_BAR = 2             # loop bars 2..17; the recording repeats bar 2 at bar 18
LOOP_BARS = 16
L = int(round(LOOP_BARS * BAR * SR))   # 1,411,200 samples, measured: the best match is within 1 sample
BLEND = int(0.25 * SR)


def title_parts():
    x = read_audio(SRC, mono=False)
    s2 = int(round((BAR0 + LOOP_FROM_BAR * BAR) * SR))
    first = int(np.argmax(np.abs(x.mean(axis=1)) > 10 ** (-50 / 20)))
    start = max(0, first - 256)
    # The intro runs up to s2 + BLEND, and the loop carries on from there, so intro -> loop is the recording itself.
    intro = x[start: s2 + BLEND].copy()
    fade_in = 220
    intro[:fade_in] *= np.linspace(0.0, 1.0, fade_in)[:, None]
    loop = x[s2 + BLEND: s2 + BLEND + L].copy()
    # The loop's last BLEND samples move from the recording's continuation (its repeat of bar 2) to bar 2 itself,
    # which then carries on into the loop's first sample.
    w = np.sin(np.linspace(0.0, np.pi / 2, BLEND)) ** 2
    cont = x[s2 + L: s2 + L + BLEND]
    head = x[s2: s2 + BLEND]
    loop[L - BLEND:] = cont * np.sqrt(1 - w)[:, None] + head * np.sqrt(w)[:, None]
    return intro, loop, BLEND


def loudness_gain(x, target):
    i, _ = loudness(x)
    return 10 ** ((target - i) / 20.0)


def song_coords(loop):
    """The loop starts BLEND samples after a downbeat; shift it so sample 0 is a downbeat, like the synth layers."""
    return np.roll(loop, BLEND, axis=0)


def loop_coords(x):
    return np.roll(x, -BLEND, axis=0)


# The loop's chords, two per bar (bars 2-17 of the recording), measured from its bass notes and chroma:
# Bb | Dm Gm | Ebmaj7 | Eb F7sus | Bb | Dm Bb | Gm7 Ebmaj7 | Gm7 Dm |
# Gm Bb | F7sus Bb | Fsus Bb | Gm7 Dm | Gm Fsus | Gm | Fsus Bb | Bb Fsus
# Voicings carry the colour notes the recording plays over each chord: the 9th on B-flat and E-flat, the 7th on
# G minor; its "F sus" half bars are really B-flat over F (the D is strong), so they share the B-flat voicing.
VOICING = {
    "Bb": [53, 58, 62, 65, 72], "Dm": [53, 57, 62, 65], "Gm": [53, 58, 62, 67], "Gm7": [53, 58, 62, 67],
    "Ebmaj7": [51, 55, 58, 62, 65], "F7sus": [53, 58, 60, 63], "Fsus": [53, 58, 62, 65, 72],
}
CHORDS = [
    ("Bb", "Bb"), ("Dm", "Gm"), ("Ebmaj7", "Ebmaj7"), ("Ebmaj7", "F7sus"),
    ("Bb", "Bb"), ("Dm", "Bb"), ("Gm7", "Ebmaj7"), ("Gm7", "Dm"),
    ("Gm", "Bb"), ("F7sus", "Bb"), ("Fsus", "Bb"), ("Gm7", "Dm"),
    ("Gm", "Fsus"), ("Gm", "Gm"), ("Fsus", "Bb"), ("Bb", "Fsus"),
]


def chord_bed(s):
    """A soft pad on each half bar, and quiet electric-piano chords in the title's da-da-.-da gallop."""
    for bar, halves in enumerate(CHORDS):
        for h, name in enumerate(halves):
            v = VOICING[name]
            s.chord("pad", bar * 4 + 2 * h, 1.95, v, ins.saw_pad, 0.22, spread=0.8, attack=0.06, rel=0.25,
                    cutoff=1800, voices=2)
            for beat in range(2):
                for st in mm.GALLOP:
                    s.chord("keys", bar * 4 + 2 * h + beat + st * 0.25, 0.16, v, ins.ep, 0.3 if st == 0 else 0.2,
                            spread=0.6)


def hard_bed(s):
    """Enemy planes up: dark sustained pad, brass stabs and a gated pad in the gallop, fast crushed arpeggios."""
    for bar, halves in enumerate(CHORDS):
        for h, name in enumerate(halves):
            v = VOICING[name]
            t0 = bar * 4 + 2 * h
            s.chord("pad", t0, 1.95, v, ins.saw_pad, 0.16, spread=0.8, attack=0.04, rel=0.2, cutoff=1300, voices=2)
            for beat in range(2):
                for st in mm.GALLOP:
                    s.chord("stab", t0 + beat + st * 0.25, 0.16, v, ins.brass_stab, 0.2 if st == 0 else 0.13,
                            spread=0.7, cutoff=3400)
                for st in range(4):
                    if st == 2:
                        continue
                    s.chord("gpad", t0 + beat + st * 0.25, 0.14, [m + 12 for m in v], ins.saw_pad, 0.1, spread=0.9,
                            attack=0.004, rel=0.04, cutoff=2800, voices=2)
            tones = sorted(set(v)) + [m + 12 for m in sorted(set(v))]
            for i in range(8):
                idx = (0, 2, 4, 6, 7, 5, 3, 1)[i] % len(tones)
                s.note("arp", t0 + i * 0.25, 0.14, tones[idx] + 12, ins.pulse, 0.22, (i % 2) * 0.8 - 0.4,
                       duty=0.125, tilt=5.0, rel=0.03)


def build_bed(s):
    """The build: bright pad, electric-piano chords in the gallop, a sixteenth shimmer an octave up and
    arpeggios climbing through each chord."""
    for bar, halves in enumerate(CHORDS):
        for h, name in enumerate(halves):
            v = VOICING[name]
            t0 = bar * 4 + 2 * h
            s.chord("pad", t0, 1.95, [m + 12 for m in v], ins.saw_pad, 0.16, spread=0.9, attack=0.08, rel=0.3,
                    cutoff=3200, voices=2)
            for beat in range(2):
                for st in mm.GALLOP:
                    s.chord("keys", t0 + beat + st * 0.25, 0.16, v, ins.ep, 0.32 if st == 0 else 0.22, spread=0.6,
                            bright=1.4)
                for st in range(4):
                    s.chord("gpad", t0 + beat + st * 0.25, 0.12, [m + 24 for m in v[1:]], ins.saw_pad, 0.07,
                            spread=0.9, attack=0.003, rel=0.04, cutoff=5000, voices=2)
            tones = sorted(set(v)) + [m + 12 for m in sorted(set(v))]
            for i in range(8):
                s.note("arp", t0 + i * 0.25, 0.14, tones[i % len(tones)] + 12, ins.pulse, 0.17 + 0.012 * i,
                       i / 7.0 - 0.5, duty=0.5, tilt=6.0, rel=0.03)


def gate_env(n, pattern=(1, 1, 0, 1), duty=0.6):
    """Sixteenth-note gate in the title's gallop (da-da . da), smooth edges. Periodic."""
    step = n / (LOOP_BARS * 16)
    t = np.arange(n) / step
    k = t.astype(int)
    frac = t - k
    on = np.array(pattern)[k % 4] > 0
    shape = np.clip(np.minimum(frac / 0.08, (duty - frac) / 0.08), 0.0, 1.0)
    return on * shape


def layered(rec, kind):
    s = mm.Song(120, LOOP_BARS, 0.0, seed={"normal": 51, "hostile": 52, "uprise": 53}[kind])
    assert s.n == L
    K = mm.KIT
    toms = (ins.tom(0.85, 200), ins.tom(0.85, 160), ins.tom(0.85, 125), ins.tom(0.85, 95))
    for bar in range(LOOP_BARS):
        if kind == "normal":
            for k in (0, 4, 8, 12):
                s.kick(mm.step(bar, k), K["kick"], 0.85 if k in (0, 8) else 0.72)
            if bar % 2:
                s.kick(mm.step(bar, 14), K["kick_soft"], 0.45)
            for sn in (4, 12):
                s.drum(mm.step(bar, sn), K["clap"], 0.55)
                s.drum(mm.step(bar, sn), K["snare"], 0.45)
            mm.gallop_hats(s, bar, 0.32)
            for st in range(1, 16, 2):
                s.drum(mm.step(bar, st), K["shaker"], 0.14, -0.3)
            if bar % 4 == 3:
                for i, st in enumerate((13, 14, 15)):
                    s.drum(mm.step(bar, st), K["ghost"], 0.25 + 0.15 * i)
            if bar % 8 == 0:
                s.drum(mm.step(bar, 0), K["crash"], 0.3, 0.3)
        elif kind == "hostile":
            for k in (0, 3, 4, 8, 11, 12):
                s.kick(mm.step(bar, k), K["kick"], 1.0 if k in (0, 4, 8, 12) else 0.7)
            if bar % 2:
                s.kick(mm.step(bar, 14), K["kick"], 0.65)
                s.kick(mm.step(bar, 15), K["kick_soft"], 0.55)
            for sn in (4, 12):
                s.drum(mm.step(bar, sn), K["snare"], 0.95)
                s.drum(mm.step(bar, sn), K["clap"], 0.6)
            for st in (7, 10, 15):
                s.drum(mm.step(bar, st), K["ghost"], 0.22)
            mm.gallop_hats(s, bar, 0.38, 0.3)
            for st in (2, 6, 10, 14):
                s.drum(mm.step(bar, st), K["ohat"], 0.2, -0.25)
            if bar % 4 == 3:
                if bar % 8 == 7:
                    for i, st in enumerate(range(8, 16)):
                        s.drum(mm.step(bar, st), toms[i // 2], 0.5 + 0.04 * i, 0.4 - 0.1 * i)
                else:
                    for st in range(12, 16):
                        s.drum(mm.step(bar, st), K["snare"], 0.45 + 0.1 * (st - 12))
            if bar % 4 == 0:
                s.drum(mm.step(bar, 0), K["crash"], 0.45, 0.3 if bar % 8 else -0.3)
        else:  # uprise
            for k in (0, 4, 8, 12):
                s.kick(mm.step(bar, k), K["kick"], 1.0)
            if bar % 2:
                s.kick(mm.step(bar, 14), K["kick_soft"], 0.6)
            for sn in (4, 12):
                s.drum(mm.step(bar, sn), K["snare"], 0.8)
                s.drum(mm.step(bar, sn), K["clap"], 0.65)
            mm.gallop_hats(s, bar, 0.38)
            for st in range(16):
                s.drum(mm.step(bar, st), K["shaker"], 0.2 if st % 2 else 0.1, -0.35)
            for st in (2, 6, 10, 14):
                s.drum(mm.step(bar, st), K["ohat"], 0.24, -0.2)
            if bar % 4 == 0:
                s.drum(mm.step(bar, 0), K["crash"], 0.42, -0.3 if bar % 8 else 0.3)
            if bar % 8 == 7:
                first = 8 if bar == 15 else 12
                for st in range(first, 16):
                    s.drum(mm.step(bar, st), K["snare"], 0.3 + 0.5 * (st - first) / (16 - first))

    # No recording on top: the title melody over the new drums sounded like two tracks at once. The harmony comes
    # from synthesized chords on the recording's own changes, and the bass from its low end, cut steeply at 200 Hz
    # so no melody leaks in.
    r = song_coords(rec)
    low = saturate(lp(r, 200, 4, periodic=True) * 4.0, 1.8)
    s.buses["low"] = low / (np.max(np.abs(low)) + 1e-9) * np.max(np.abs(r))
    {"normal": chord_bed, "hostile": hard_bed, "uprise": build_bed}[kind](s)

    cfg = {
        "normal": (dict(
            drums=dict(lp=12000, drive=1.3, rev=0.04, gain=0.75),
            low=dict(gain=0.42, lp=900),
            pad=dict(gain=0.5, lp=2600, rev=0.3),
            keys=dict(gain=0.45, lp=5000, rev=0.18, echo=0.06),
        ), dict(lufs=-15.5, hp=35, lp=16000, air=2.0, wow=0.0, crackle=0.0, pump=0.3, tape=1.3)),
        "hostile": (dict(
            drums=dict(lp=13000, drive=1.55, rev=0.05, gain=0.9),
            low=dict(gain=0.62, lp=1200, drive=1.6),
            pad=dict(gain=0.5, lp=2000, rev=0.25),
            stab=dict(gain=0.55, lp=4500, drive=1.2, rev=0.15),
            gpad=dict(gain=0.5, lp=4000, rev=0.2),
            arp=dict(gain=0.5, lp=8000, crush=(7, 2), rev=0.1, echo=0.12),
        ), dict(lufs=-14.5, hp=35, lp=15000, air=3.0, wow=0.0, crackle=0.0, pump=0.42, tape=1.55)),
        "uprise": (dict(
            drums=dict(lp=14000, drive=1.4, rev=0.06, gain=0.88),
            low=dict(gain=0.5, lp=1000, drive=1.3),
            pad=dict(gain=0.5, lp=4500, rev=0.32),
            keys=dict(gain=0.5, lp=7000, rev=0.18, echo=0.06),
            gpad=dict(gain=0.4, lp=9000, rev=0.25, echo=0.12),
            arp=dict(gain=0.5, lp=8000, rev=0.15, echo=0.15),
        ), dict(lufs=-14.0, hp=35, lp=17000, air=4.0, wow=0.0, crackle=0.0, pump=0.38, tape=1.45)),
    }[kind]
    return loop_coords(mm.mixdown(s, cfg[0], cfg[1]))


def main():
    os.makedirs(OUT, exist_ok=True)
    intro, loop, _ = title_parts()
    # The title loop and its intro share one gain, set from the loop.
    g = loudness_gain(loop, -16.0)
    loop_t = limiter(loop * g, -1.5, periodic=True)
    intro_t = limiter(intro * g, -1.5, periodic=False)
    to_ogg(intro_t, os.path.join(OUT, "bgm_title_intro.ogg"), quality=6)
    to_ogg(loop_t, os.path.join(OUT, "bgm_title.ogg"), quality=6)
    print("bgm_title_intro  %.3f s" % (len(intro_t) / SR))
    print("bgm_title        %.3f s  %d samples  %.1f LUFS" % (len(loop_t) / SR, len(loop_t), loudness(loop_t)[0]))
    names = sys.argv[1:] or ["normal", "hostile", "uprise"]
    for kind in names:
        y = layered(loop, kind)
        to_ogg(y, os.path.join(OUT, "bgm_%s.ogg" % kind), quality=5)
        i, pk = loudness(y)
        print("bgm_%-12s %.3f s  %d samples  %.1f LUFS  peak %.1f dB" % (kind, len(y) / SR, len(y), i, pk))


if __name__ == "__main__":
    main()
