#!/usr/bin/env python3
"""Add a voiceover and a soft music bed to the KafkaLens demo video.

The voice reads one line per caption (see NARRATION), starting when the caption appears.
A line that would run into the next caption is sped up a little; if it still does not fit,
the next line waits for it. Music is synthesized here (no licensing), ducks under the voice,
and fades in and out with the video.

Voice: Microsoft neural TTS via `edge-tts` (online; only the narration text below is sent).
Clips are cached in the work dir, so re-runs with unchanged text need no network.

Usage: narrate.py <timeline.json> <in.mp4> <out.mp4> [work-dir]

timeline.json (written by make_video.py):
  {"duration": 83.12, "segments": [{"start": 0.0, "end": 4.08, "caption": "__intro__"}, ...]}
"""
import asyncio
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np

SR = 48000
VOICE = os.environ.get("KL_VOICE", "en-US-AndrewNeural")
BASE_RATE = 4           # percent faster than the voice's default
MAX_RATE = 22           # cap when squeezing a line into its caption slot
LEAD = 0.25             # seconds after a caption appears before the voice starts
GAP = 0.2               # minimum silence between lines
MUSIC_DB = -21.0        # music level relative to full-scale voice
DUCK_DB = -9.0          # extra attenuation of the music while the voice speaks

# Caption (exactly as in VideoScript.cs) → spoken line. Spelled for the ear: "Kafka Lens",
# "dot com", no symbols. A caption without an entry is read as-is.
NARRATION = {
    "__intro__": "Meet Kafka Lens: a fast desktop browser for Apache Kafka.",
    "Open any configured Kafka cluster from the Cluster menu":
        "Open any configured cluster from the Cluster menu.",
    "Pick a topic — the latest messages are fetched instantly":
        "Pick a topic, and the latest messages load instantly.",
    "Select a message to read it as pretty-printed, syntax-highlighted JSON":
        "Select one to read it as highlighted JSON.",
    "Headers are one click away":
        "Headers are one click away.",
    "Filter loaded messages with boolean expressions: &&  ||  !  ( )  \"phrases\"":
        "Filter loaded messages with boolean expressions: and, or, not, parentheses, and quoted phrases.",
    "Zoom into big payloads: the object filter keeps only matching fields":
        "For big payloads, the object filter zooms in, and keeps only the fields that match.",
    "Drill into a single partition and start from any timestamp or offset":
        "Drill into a single partition, and start reading from any timestamp or offset.",
    "Binary payload? Pick a formatter per topic: JSON, Text, numeric types, or plugins":
        "Binary payload? Pick a formatter per topic: JSON, text, numeric types, or your own plugins.",
    "…values are decoded immediately, and the choice is remembered for the topic":
        "Values are decoded right away, and the choice is remembered.",
    "Keep several clusters open side by side, and find topics fast":
        "Keep several clusters open side by side, and find any topic in seconds.",
    "Copy keys and values, or save messages to disk and reopen them offline":
        "Copy keys and values, or save messages to reopen them offline.",
    "Light or dark — your choice":
        "And light, or dark? Your choice.",
    "__outro__": "Try Kafka Lens for free, at kafkalens dot com.",
}


# ── voice ────────────────────────────────────────────────────────────────
def decode(path):
    """Any audio file → mono float32 at SR."""
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-f", "f32le", "-ac", "1", "-ar", str(SR), "-"],
                         check=True, capture_output=True).stdout
    return np.frombuffer(raw, np.float32).copy()


def trim(x, thresh=0.003):
    idx = np.where(np.abs(x) > thresh)[0]
    return x[max(0, idx[0] - 240): idx[-1] + 2400] if len(idx) else x


def tts(text, rate, cache):
    import edge_tts  # imported lazily so --help etc. work without it
    key = hashlib.sha1(f"{VOICE}|{rate}|{text}".encode()).hexdigest()[:16]
    mp3 = cache / f"{key}.mp3"
    if not mp3.exists():
        tmp = mp3.with_suffix(".part")
        asyncio.run(edge_tts.Communicate(text, VOICE, rate=f"{rate:+d}%").save(str(tmp)))
        tmp.replace(mp3)
    return trim(decode(mp3))


def build_voice(segments, duration, cache):
    track = np.zeros(int(duration * SR) + SR, np.float32)
    texts = [NARRATION.get(s["caption"], s["caption"]) for s in segments]
    base = [tts(t, BASE_RATE, cache) for t in texts]
    cursor = 0.0
    for i, seg in enumerate(segments):
        text = texts[i]
        lead = 0.4 if seg["caption"] == "__intro__" else LEAD
        start = max(seg["start"] + lead, cursor + GAP)
        if i + 1 < len(segments):
            # may borrow whatever the next caption's slot doesn't need for its own line
            nxt = segments[i + 1]
            spare = (nxt["end"] - nxt["start"]) - LEAD - len(base[i + 1]) / SR - GAP - 0.4
            limit = nxt["start"] + LEAD + max(0.0, spare)
        else:
            limit = duration - 1.2
        rate = BASE_RATE
        clip = base[i]
        need = len(clip) / SR
        if start + need > limit:
            # speed up just enough to fit (speech length scales ~1/(1+rate))
            want = int(np.ceil(((1 + BASE_RATE / 100) * need / max(limit - start, 0.1) - 1) * 100))
            rate = min(MAX_RATE, max(BASE_RATE + 1, want))
            clip = tts(text, rate, cache)
        end = start + len(clip) / SR
        note = "" if end <= limit else f"  (runs {end - limit:.2f}s into next)"
        print(f"  {start:6.2f}–{end:6.2f}s rate {rate:+d}%  {text}{note}")
        a = int(start * SR)
        track[a:a + len(clip)] += clip[: len(track) - a]
        cursor = end
    track = track[: int(duration * SR)]
    peak = np.abs(track).max() or 1.0
    return track * (0.89 / peak)  # ≈ -1 dBFS


# ── music ────────────────────────────────────────────────────────────────
def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def onepole(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    # vectorised enough for 80 s of audio via chunked lfilter-free recursion
    for i in range(0, len(x), 4096):
        seg = x[i:i + 4096]
        out = np.empty_like(seg)
        for j, v in enumerate(seg):
            acc = (1 - a) * v + a * acc
            out[j] = acc
        y[i:i + 4096] = out
    return y


def build_music(duration, seed=7):
    """Calm ambient bed: slow pad chords, soft bass, sparse echoing plucks. Deterministic."""
    rng = np.random.default_rng(seed)
    n = int(duration * SR)
    t = np.arange(n) / SR
    bpm = 84
    beat = 60 / bpm
    bar = 4 * beat
    # D major-ish: Dmaj9 · Bm9 · Gmaj9 · A6sus — two bars each
    chords = [[50, 57, 61, 64, 66], [47, 54, 57, 61, 64], [43, 50, 54, 57, 59], [45, 52, 54, 57, 62]]
    chord_len = 2 * bar

    pad = np.zeros(n, np.float32)
    bass = np.zeros(n, np.float32)
    xfade = 1.6
    k = 0
    while k * chord_len < duration:
        c0 = k * chord_len
        notes = chords[k % len(chords)]
        a0, a1 = int(max(0, c0 - xfade / 2) * SR), int(min(duration, c0 + chord_len + xfade / 2) * SR)
        if a1 <= a0:
            break
        tt = t[a0:a1] - t[a0]
        L = tt[-1] if len(tt) else 1
        env = np.minimum(1, np.minimum(tt / xfade, (L - tt) / xfade)).clip(0) ** 1.5
        seg = np.zeros(a1 - a0)
        for p in notes[1:]:
            f = midi(p + 12)
            for det in (-0.12, 0.0, 0.12):  # light chorus
                ph = rng.uniform(0, 2 * np.pi)
                ff = f * 2 ** (det / 12)
                seg += np.sin(2 * np.pi * ff * tt + ph) + 0.25 * np.sin(4 * np.pi * ff * tt + ph)
        seg *= 1 + 0.15 * np.sin(2 * np.pi * 0.13 * tt)  # slow swell
        pad[a0:a1] += (seg * env / 14).astype(np.float32)
        fb = midi(notes[0] - 12)
        bass[a0:a1] += (0.5 * np.sin(2 * np.pi * fb * tt) * env).astype(np.float32)
        k += 1

    # plucks: soft bell-ish arpeggio on eighths, sometimes skipped, with a dotted-eighth echo
    pluck = np.zeros(n, np.float32)
    step = beat / 2
    i = 0
    pattern = [0, 2, 3, 4, 3, 2, 4, 1]
    while i * step < duration:
        start = i * step
        chord = chords[int(start // chord_len) % len(chords)]
        if rng.random() < 0.72:
            p = chord[1:][pattern[i % len(pattern)] % 4] + 24
            f = midi(p)
            a = int(start * SR)
            L = min(int(1.4 * SR), n - a)
            tt = np.arange(L) / SR
            tone = np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * 2 * f * tt) * np.exp(-tt * 9)
            vel = 0.55 + 0.45 * rng.random()
            pluck[a:a + L] += (tone * np.exp(-tt * 3.2) * np.minimum(1, tt / 0.004) * vel / 6).astype(np.float32)
        i += 1
    d = int(beat * 0.75 * SR)
    echo = pluck.copy()
    for rep, g in ((1, 0.38), (2, 0.16), (3, 0.06)):
        echo[d * rep:] += pluck[: n - d * rep] * g
    pluck = onepole(echo, 3800)

    mix = 0.9 * pad + 0.55 * bass + 0.7 * pluck
    # fade in 2 s, fade out 3 s
    fade = np.minimum(1, np.minimum(t / 2.0, (duration - t) / 3.0)).clip(0)
    mix *= fade
    return (mix / (np.abs(mix).max() or 1)).astype(np.float32)


def duck_envelope(voice, attack=0.06, release=0.5):
    """0..1 follower of voice activity."""
    hop = SR // 100
    frames = len(voice) // hop
    rms = np.array([np.sqrt(np.mean(voice[i * hop:(i + 1) * hop] ** 2) + 1e-12) for i in range(frames)])
    active = (rms > 0.02).astype(float)
    env = np.zeros(frames)
    up, down = 1 - np.exp(-1 / (attack * 100)), 1 - np.exp(-1 / (release * 100))
    acc = 0.0
    for i, v in enumerate(active):
        acc += (v - acc) * (up if v > acc else down)
        env[i] = acc
    return np.interp(np.arange(len(voice)) / hop, np.arange(frames), env).astype(np.float32)


def write_wav(path, stereo):
    pcm = (np.clip(stereo, -1, 1) * 32767).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def build(timeline, in_mp4, out_mp4, work):
    work = Path(work)
    cache = work / "tts-cache"
    cache.mkdir(parents=True, exist_ok=True)
    duration = timeline["duration"]
    segments = [s for s in timeline["segments"] if s["caption"]]

    print("voiceover:")
    voice = build_voice(segments, duration, cache)
    music = build_music(duration)
    env = duck_envelope(voice)
    gain = 10 ** (MUSIC_DB / 20) * 10 ** (DUCK_DB * env / 20)
    m = music * gain
    # slight stereo width for the music, voice centred
    shift = int(0.011 * SR)
    left = voice + m
    right = voice + np.concatenate([np.zeros(shift, np.float32), m[:-shift]])
    wav = work / "narration.wav"
    write_wav(wav, np.stack([left, right], axis=1) * 0.9)

    tmp = Path(out_mp4).with_name(Path(out_mp4).stem + ".audio-tmp.mp4")
    subprocess.run([
        "ffmpeg", "-y", "-loglevel", "error", "-i", str(in_mp4), "-i", str(wav),
        "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy",
        "-af", "loudnorm=I=-16:TP=-1.5:LRA=11", "-ar", str(SR), "-c:a", "aac", "-b:a", "128k",
        "-shortest", "-movflags", "+faststart", str(tmp),
    ], check=True)
    tmp.replace(out_mp4)
    print("wrote", out_mp4, "(with audio)")


if __name__ == "__main__":
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    tl = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    wd = sys.argv[4] if len(sys.argv) > 4 else tempfile.mkdtemp(prefix="kl-narrate-")
    build(tl, sys.argv[2], sys.argv[3], wd)
