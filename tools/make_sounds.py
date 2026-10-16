#!/usr/bin/env python3
"""Synthesises AVAMarioBros's sound effects and tunes into ../src/AVAMarioBros/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMarioBros", "Sounds")
RNG = random.Random(1983)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Jump.wav", tone(0.16, 300, 900, "pulse", attack=0.003, release=0.8), 0.24)
    # A ledge bumped from below: a hollow metal thunk.
    write("Bump.wav", mix(tone(0.14, 180, 90, "square", attack=0.002, release=1.2), tone(0.06, 1200, 400, "noise", release=1, gain=0.4)), 0.4)
    # A pest flipped over: a springy boing.
    write("Flip.wav", tone(0.3, 400, 400, "tri", attack=0.003, release=0.7, vibrato=0.25, vib_rate=22), 0.34)
    write("Kick.wav", cat(mix(tone(0.07, 900, 300, "noise", attack=0.001, release=1), tone(0.07, 250, 120, "square", release=1, gain=0.6)), tone(0.3, 900, 200, "tri", release=0.6)), 0.38)
    write("Coin.wav", cat(tone(0.06, hz("B5"), kind="pulse", release=0.3), tone(0.26, hz("E6"), kind="pulse", release=0.8)), 0.3)
    # The jolt block: a deep thud and a rattle.
    write("Jolt.wav", mix(tone(0.6, 120, 35, "sine", attack=0.002, release=0.5), tone(0.5, 600, 80, "noise", release=0.5, gain=0.6)), 0.55)
    write("Angry.wav", tone(0.35, 140, 160, "saw", attack=0.01, release=0.6, vibrato=0.08, vib_rate=30), 0.3)
    write("Righted.wav", tone(0.2, 600, 300, "tri", release=0.6, vibrato=0.1, vib_rate=25), 0.26)
    write("Ember.wav", mix(tone(0.5, 2500, 600, "noise", attack=0.05, release=0.6), tone(0.4, 200, 300, "saw", release=0.6, gain=0.3)), 0.3)
    write("Pipe.wav", cat(tone(0.08, 220, 330, "square", release=0.6), tone(0.08, 330, 440, "square", release=0.6)), 0.2)
    write("Death.wav", cat(tone(0.25, 900, 300, "square", release=0.5), *[tone(0.14, hz(n), kind="pulse", release=0.4) for n in ["G4", "E4", "C4", "G3"]]), 0.44)
    write("PhaseStart.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6"]]), 0.3)
    write("PhaseClear.wav", tune(160, [("G5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("E6", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 1), ("G2", 0.5), ("C3", 1.5)], kind="pulse"), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "G6", "C6", "G6", "C7"]]), 0.38)

    # The phase's music: a bouncy, busy little loop over an oom-pah bass (an original composition), a whole
    # number of beats long so it loops cleanly.
    lead = [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("E5", 0.5), ("F5", 0.5), ("D5", 0.5), ("B4", 1),
            ("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("F5", 0.5), ("G5", 0.5), ("E5", 0.5), ("C5", 1),
            ("E5", 0.5), ("G5", 0.5), ("C6", 0.5), ("G5", 0.5), ("A5", 0.5), ("F5", 0.5), ("D5", 1),
            ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 2)]
    bass = ([("C3", 0.5), ("G3", 0.5)] * 2 + [("G2", 0.5), ("D3", 0.5)] * 2 + [("D3", 0.5), ("A3", 0.5)] * 2 + [("C3", 0.5), ("G3", 0.5)] * 2
            + [("C3", 0.5), ("G3", 0.5)] * 2 + [("F2", 0.5), ("C3", 0.5)] * 2 + [("G2", 0.5), ("D3", 0.5)] * 2 + [("C3", 2)])
    write("Music.wav", tune(150, lead, bass, kind="pulse"), 0.24)
    title_lead = lead + [("A5", 1), ("G5", 0.5), ("E5", 0.5), ("F5", 1), ("D5", 1), ("E5", 0.5), ("C5", 0.5), ("D5", 0.5), ("B4", 0.5), ("C5", 2)]
    title_bass = bass + [("F2", 1), ("C3", 1), ("G2", 1), ("D3", 1), ("C3", 1), ("G2", 1), ("C3", 2)]
    write("TitleTune.wav", cat(tune(140, title_lead, title_bass, kind="pulse"), silence(1.0)), 0.42)
    write("StartTune.wav", tune(160, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1), ("G5", 0.5), ("C6", 1)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(70, [("E5", 1), ("C5", 1), ("A4", 1), ("B4", 1), ("C5", 4)], [("C3", 2), ("A2", 2), ("G2", 2), ("C2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
