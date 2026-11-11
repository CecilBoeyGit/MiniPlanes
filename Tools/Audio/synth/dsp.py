"""
Small numpy synthesizer used to make MiniPlanes' synthesized sound effects and music.

Everything here is vectorised numpy (no scipy). Filters work in the frequency domain; with periodic=True they
wrap around the buffer, which is what seamless music loops need: a loop rendered with periodic effects joins
its own start exactly.
"""
import os
import re
import subprocess
import tempfile
import wave

import numpy as np

SR = 44100
FF = os.environ.get("FFMPEG", "C:/Users/cecil/Tools/Faster-Whisper-XXL/ffmpeg.exe")


# ------------------------------------------------------------------ notes

_NOTE = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def mtof(m):
    return 440.0 * 2.0 ** ((np.asarray(m, dtype=float) - 69.0) / 12.0)


def note(name):
    """'C#5' -> MIDI number."""
    m = re.fullmatch(r"([A-G])([#b]?)(-?\d)", name)
    if not m:
        raise ValueError(name)
    n = _NOTE[m.group(1)] + {"#": 1, "b": -1, "": 0}[m.group(2)]
    return n + 12 * (int(m.group(3)) + 1)


def melody(text, start=0.0, shift=0):
    """'A4/1 r/.5 C5/.5' -> list of (beat, length in beats, midi). 'r' is a rest."""
    out = []
    b = start
    for tok in text.split():
        name, d = tok.split("/")
        d = float(d)
        if name != "r":
            out.append((b, d, note(name) + shift))
        b += d
    return out


# ------------------------------------------------------------------ basics

def noise(n, seed=0):
    return np.random.default_rng(seed).standard_normal(n)


def times(n):
    return np.arange(n) / SR


def phase_of(freq):
    """Per-sample frequency -> phase in radians."""
    return 2.0 * np.pi * np.cumsum(freq) / SR


def env(n, dur, attack=0.005, decay=0.0, rel=0.1):
    """Attack ramp, optional exponential decay, and a release after dur seconds (about -43 dB at rel)."""
    t = times(n)
    e = np.minimum(1.0, t / max(attack, 1e-4))
    if decay:
        e = e * np.exp(-t * decay)
    if rel is not None:
        e = e * np.where(t > dur, np.exp(-(t - dur) * 5.0 / max(rel, 1e-4)), 1.0)
    return e


def additive(phase, fmax, amp, kmax=60, tilt=0.0, t=None):
    """Sum of harmonics sin(k*phase) with amplitude amp(k), stopping below Nyquist. tilt makes upper
    harmonics die away faster (a plucked, closing-filter sound)."""
    K = int(min(kmax, (SR * 0.45) / max(fmax, 1.0)))
    out = np.zeros_like(phase)
    for k in range(1, K + 1):
        a = amp(k)
        if abs(a) < 2e-4:
            continue
        c = a * np.sin(k * phase)
        if tilt and t is not None:
            c *= np.exp(-t * tilt * (k - 1))
        out += c
    return out


def amp_pulse(duty):
    return lambda k: (2.0 / (k * np.pi)) * np.sin(k * np.pi * duty)


def amp_saw(k):
    return (2.0 / np.pi) * (1.0 if k % 2 else -1.0) / k


def amp_tri(k):
    if k % 2 == 0:
        return 0.0
    return (8.0 / np.pi ** 2) * (1.0 if (k // 2) % 2 == 0 else -1.0) / (k * k)


# ------------------------------------------------------------------ filters

def _nfft(n, periodic):
    if periodic:
        return n
    return 1 << int(np.ceil(np.log2(n + 8192)))


def fft_filter(x, resp, periodic=False):
    x = np.asarray(x, dtype=float)
    n = x.shape[0]
    N = _nfft(n, periodic)
    X = np.fft.rfft(x, N, axis=0)
    f = np.fft.rfftfreq(N, 1.0 / SR)
    g = resp(f)
    X = X * (g[:, None] if X.ndim == 2 else g)
    return np.fft.irfft(X, N, axis=0)[:n]


def lp(x, fc, order=2, periodic=False):
    return fft_filter(x, lambda f: 1.0 / np.sqrt(1.0 + (f / fc) ** (2 * order)), periodic)


def hp(x, fc, order=2, periodic=False):
    return fft_filter(x, lambda f: 1.0 / np.sqrt(1.0 + (fc / np.maximum(f, 1e-3)) ** (2 * order)), periodic)


def bp(x, lo, hi, order=2, periodic=False):
    def r(f):
        a = 1.0 / np.sqrt(1.0 + (f / hi) ** (2 * order))
        b = 1.0 / np.sqrt(1.0 + (lo / np.maximum(f, 1e-3)) ** (2 * order))
        return a * b
    return fft_filter(x, r, periodic)


def peak_eq(x, fc, gain_db, q=1.0, periodic=False):
    g = 10 ** (gain_db / 20.0)
    def r(f):
        bw = fc / q
        return 1.0 + (g - 1.0) / (1.0 + ((f - fc) / (bw * 0.5)) ** 2)
    return fft_filter(x, r, periodic)


def shaped_noise(n, resp, seed=0):
    """Noise with a given magnitude spectrum, exactly periodic over n samples."""
    rng = np.random.default_rng(seed)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    spec = resp(f) * np.exp(1j * rng.uniform(0, 2 * np.pi, f.shape[0]))
    spec[0] = 0.0
    x = np.fft.irfft(spec, n)
    return x / (np.std(x) + 1e-12)


# ------------------------------------------------------------------ effects

def conv(x, ir, periodic=False):
    """Convolution of mono or stereo x with a mono ir."""
    x = np.asarray(x, dtype=float)
    n = x.shape[0]
    N = n if periodic else 1 << int(np.ceil(np.log2(n + len(ir))))
    H = np.fft.rfft(ir, N)
    X = np.fft.rfft(x, N, axis=0)
    Y = X * (H[:, None] if X.ndim == 2 else H)
    y = np.fft.irfft(Y, N, axis=0)
    return y[:n] if periodic else y[: n + len(ir) - 1]


def reverb_ir(seconds=1.8, damp=6000.0, predelay=0.012, seed=0):
    n = int(seconds * SR)
    t = times(n)
    ir = noise(n, seed) * np.exp(-6.9 * t / seconds)
    ir = lp(ir, damp)
    ir[: int(predelay * SR)] = 0.0
    return ir / np.sqrt(np.sum(ir ** 2))


def reverb(x, seconds=1.8, damp=6000.0, predelay=0.012, periodic=False, seed=7):
    """Stereo reverb (wet only). Input mono or stereo; output stereo."""
    x = np.asarray(x, dtype=float)
    if x.ndim == 1:
        x = np.stack([x, x], axis=1)
    irl = reverb_ir(seconds, damp, predelay, seed)
    irr = reverb_ir(seconds, damp, predelay, seed + 1)
    yl = conv(x[:, 0], irl, periodic)
    yr = conv(x[:, 1], irr, periodic)
    return np.stack([yl, yr], axis=1)


def echo(x, delay_s, feedback=0.4, taps=4, damp=4000.0, periodic=True):
    """Feedback-style echo (wet only). Periodic version rolls around the loop."""
    d = int(round(delay_s * SR))
    y = np.zeros_like(x)
    g = 1.0
    for i in range(1, taps + 1):
        g *= feedback
        if periodic:
            y += g * np.roll(x, i * d, axis=0)
        else:
            sh = np.zeros_like(x)
            sh[i * d:] = x[: x.shape[0] - i * d]
            y += g * sh
    return lp(y, damp, periodic=periodic)


def ping_pong(x, delay_s, feedback=0.45, taps=4, damp=4000.0):
    """Stereo echo bouncing left and right (periodic). x stereo."""
    mono = x.mean(axis=1)
    d = int(round(delay_s * SR))
    y = np.zeros_like(x)
    g = 1.0
    for i in range(1, taps + 1):
        g *= feedback
        ch = (i - 1) % 2
        y[:, ch] += g * np.roll(mono, i * d)
    return lp(y, damp, periodic=True)


def saturate(x, drive=1.5):
    return np.tanh(drive * x) / np.tanh(drive)


def bitcrush(x, bits=8, hold=2):
    q = 2.0 ** (bits - 1)
    y = np.round(x * q) / q
    if hold > 1:
        idx = (np.arange(y.shape[0]) // hold) * hold
        y = y[idx]
    return y


def wow(x, loop_seconds, depth_ms=1.2, flutter_ms=0.25, base_ms=4.0):
    """Tape wow and flutter as a moving delay. The wobble rates are whole cycles per loop, so loops stay seamless."""
    n = x.shape[0]
    k1 = max(1, int(round(loop_seconds * 0.45)))
    k2 = max(1, int(round(loop_seconds * 5.3)))
    t = times(n)
    T = n / SR
    d = (base_ms + depth_ms * np.sin(2 * np.pi * k1 * t / T) + flutter_ms * np.sin(2 * np.pi * k2 * t / T + 1.3)) * 1e-3
    pos = np.arange(n) - d * SR
    idx = np.arange(n)
    if x.ndim == 1:
        return np.interp(pos, idx, x, period=n)
    return np.stack([np.interp(pos, idx, x[:, c], period=n) for c in range(x.shape[1])], axis=1)


def crackle(n, density=6.0, level=0.03, hiss=0.004, seed=3):
    """Vinyl crackle and hiss, stereo."""
    rng = np.random.default_rng(seed)
    out = np.zeros((n, 2))
    count = int(density * n / SR)
    pos = rng.integers(0, n - 200, count)
    amp = rng.exponential(1.0, count) * level
    click = np.exp(-np.arange(60) / 6.0) * np.sign(np.sin(np.arange(60) * 1.7))
    for p, a in zip(pos, amp):
        ch = rng.integers(0, 2)
        out[p:p + 60, ch] += a * click * rng.choice([-1, 1])
        out[p:p + 60, 1 - ch] += 0.4 * a * click
    out = hp(out, 1500)
    out += hp(np.stack([noise(n, seed + 1), noise(n, seed + 2)], axis=1), 3000) * hiss
    return out


def pump(n, kick_samples, depth=0.35, release=0.16):
    """Sidechain gain curve: dips at each kick and recovers. Periodic."""
    imp = np.zeros(n)
    for s in kick_samples:
        imp[int(s) % n] = 1.0
    k = np.exp(-times(int(release * 5 * SR)) / release)
    c = conv(imp, k, periodic=True)
    return 1.0 - depth * np.clip(c, 0.0, 1.0)


def limiter(x, ceiling_db=-1.0, window=96, periodic=True):
    """Look-ahead peak limiter. The gain is a running minimum smoothed over half its window, so it is never
    above what any sample in reach needs, and |x| stays under the ceiling without hard clipping."""
    thr = 10 ** (ceiling_db / 20.0)
    a = np.abs(x).max(axis=1) if x.ndim == 2 else np.abs(x)
    g = np.minimum(1.0, thr / np.maximum(a, 1e-9))
    w = window
    h = w // 2

    def padded(v, r):
        if periodic:
            return np.concatenate([v[-r:], v, v[:r]])
        return np.concatenate([np.ones(r), v, np.ones(r)])

    mins = np.lib.stride_tricks.sliding_window_view(padded(g, w), 2 * w + 1).min(axis=1)
    cs = np.cumsum(np.concatenate([[0.0], padded(mins, h)]))
    sm = (cs[2 * h + 1:] - cs[: -(2 * h + 1)]) / (2 * h + 1)
    y = x * (sm[:, None] if x.ndim == 2 else sm)
    return np.clip(y, -thr, thr)


def pan_gains(pan):
    th = (pan + 1.0) * np.pi / 4.0
    return np.cos(th) * np.sqrt(2.0), np.sin(th) * np.sqrt(2.0)


def normalize_peak(x, db=-1.0):
    p = np.max(np.abs(x))
    return x if p <= 0 else x * (10 ** (db / 20.0) / p)


# ------------------------------------------------------------------ files

def write_wav(path, x):
    x = np.asarray(x, dtype=float)
    if x.ndim == 1:
        x = x[:, None]
    pcm = (np.clip(x, -1.0, 1.0) * 32767.0).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(pcm.shape[1])
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def read_audio(path, mono=True):
    """Any file ffmpeg reads -> float array at SR."""
    fd, tmp = tempfile.mkstemp(suffix=".wav")
    os.close(fd)
    try:
        subprocess.run([FF, "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", path,
                        "-ac", "1" if mono else "2", "-ar", str(SR), "-c:a", "pcm_s16le", tmp], check=True)
        with wave.open(tmp, "rb") as w:
            data = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(float) / 32768.0
            ch = w.getnchannels()
        return data if ch == 1 else data.reshape(-1, ch)
    finally:
        os.remove(tmp)


def loudness(x):
    """Integrated loudness (LUFS) and true peak (dBTP), measured by ffmpeg's ebur128."""
    fd, tmp = tempfile.mkstemp(suffix=".wav")
    os.close(fd)
    try:
        write_wav(tmp, np.clip(x, -1, 1))
        out = subprocess.run([FF, "-hide_banner", "-nostdin", "-i", tmp, "-af", "ebur128=peak=true", "-f", "null", "-"],
                             capture_output=True, text=True).stderr
        tail = out[out.rfind("Summary:"):]
        i = float(re.search(r"I:\s*(-?[\d.]+) LUFS", tail).group(1))
        pk = re.search(r"Peak:\s*(-?[\d.inf]+) dBFS", tail)
        return i, float(pk.group(1)) if pk else 0.0
    finally:
        os.remove(tmp)


def momentary_max(x):
    """Loudest 400 ms window (LUFS): the fair level measure for clips shorter than a second."""
    fd, tmp = tempfile.mkstemp(suffix=".wav")
    os.close(fd)
    try:
        pad = np.concatenate([x, np.zeros((int(0.5 * SR),) + x.shape[1:])])
        write_wav(tmp, np.clip(pad, -1, 1))
        out = subprocess.run([FF, "-hide_banner", "-nostdin", "-i", tmp, "-af", "ebur128", "-f", "null", "-"],
                             capture_output=True, text=True).stderr
        vals = [float(v) for v in re.findall(r"M:\s*(-?[\d.]+)", out)]
        return max(vals) if vals else -70.0
    finally:
        os.remove(tmp)


def to_ogg(x, path, quality=5):
    fd, tmp = tempfile.mkstemp(suffix=".wav")
    os.close(fd)
    try:
        write_wav(tmp, x)
        subprocess.run([FF, "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", tmp,
                        "-c:a", "libvorbis", "-q:a", str(quality), path], check=True)
    finally:
        os.remove(tmp)
