"""Generate the project's original, small board-feedback WAV assets using Python's standard library.

No external samples, network, Unity state or global random generator are used.
Run from any directory: python Tools/Audio/GenerateBoardSounds.py [--verify] [--preview]
"""

from __future__ import annotations

import argparse
import hashlib
import io
import math
from pathlib import Path
import random
import struct
import uuid
import wave


ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Assets/_FantasyKingdom/FeedbackAudio/Clips"
PREVIEW = ROOT / "Docs/PlaytestScreenshots/board-feedback-20261018/sound-preview.wav"
RATE = 22_050
PEAK = 0.66
TAU = 2 * math.pi
SOUNDS = (
    ("Move", 0.23), ("Attack", 0.32), ("Projectile", 0.26),
    ("Magic", 0.48), ("Hit", 0.25), ("Heal", 0.58),
    ("Shield", 0.38), ("Summon", 0.74), ("Build", 0.48),
    ("Skill", 0.44), ("Victory", 0.78), ("Defeat", 0.78),
    ("UiClick", 0.12),
)


def env(time: float, duration: float, attack: float = 0.006, decay: float = 12) -> float:
    if time < 0 or time >= duration:
        return 0
    fade = min(1, time / attack, (duration - time) / 0.024)
    return max(0, fade) * math.exp(-decay * time)


def bell(time: float, frequency: float, duration: float, decay: float = 8) -> float:
    return env(time, duration, decay=decay) * (
        math.sin(TAU * frequency * time)
        + 0.24 * math.sin(TAU * frequency * 2.03 * time)
        + 0.10 * math.sin(TAU * frequency * 3.12 * time)
    )


def impact(time: float, frequency: float, duration: float, decay: float = 22) -> float:
    """A damped, inharmonic wood/stone body rather than a pitched electronic beep."""
    if time < 0:
        return 0
    value = 0.0
    for ratio, level, damping in ((1, 0.75, 1), (1.71, 0.42, 1.65), (2.83, 0.25, 2.9), (4.37, 0.11, 4.8)):
        value += level * math.sin(TAU * frequency * ratio * time) \
            * env(time, duration, attack=0.0008, decay=decay * damping)
    return value


def chess_contact(time: float, duration: float, noise: float) -> float:
    # Hard marble contact, then the broader wooden-board body and a small settling tap.
    # Frequencies are fixed: no sine sweep, glittering chime or sustained oscillator.
    return (0.75 * impact(time, 153, duration, 29)
            + 0.36 * impact(time, 91, duration, 34)
            + 0.22 * impact(time - 0.026, 167, duration - 0.026, 45)
            + 0.55 * noise * env(time, duration, attack=0.0006, decay=125)
            + 0.10 * noise * env(time - 0.026, duration - 0.026, attack=0.0006, decay=150))


def synthesize(kind: str, duration: float, seed: int) -> list[float]:
    rng = random.Random(seed)
    values = []
    smooth_noise = 0.0
    bass_noise = 0.0
    for sample in range(round(duration * RATE)):
        time = sample / RATE
        # Separate low and mid bands create the weight of a surface/material contact.
        # No high-frequency white-noise layer is mixed directly into the output.
        smooth_noise += 0.19 * (rng.uniform(-1, 1) - smooth_noise)
        bass_noise += 0.055 * (smooth_noise - bass_noise)
        contact_noise = smooth_noise - bass_noise
        if kind == "Move":
            sound = chess_contact(time, duration, contact_noise)
        elif kind == "Attack":
            sound = (0.75 * impact(time, 84, duration, 21)
                     + 0.26 * impact(time, 193, duration, 13)
                     + 1.10 * bass_noise * env(time, duration, attack=0.003, decay=23)
                     + 0.50 * contact_noise * env(time, duration, attack=0.001, decay=56))
        elif kind == "Projectile":
            sound = (0.60 * impact(time, 119, duration, 26)
                     + 0.22 * impact(time, 241, duration, 35)
                     + 0.75 * smooth_noise * env(time, duration, attack=0.005, decay=30))
        elif kind == "Magic":
            sound = (0.44 * bell(time, 82.41, duration, 7)
                     + 0.28 * bell(time - 0.035, 123.47, duration - 0.035, 9)
                     + 0.30 * impact(time - 0.06, 137, duration - 0.06, 11)
                     + 0.70 * bass_noise * env(time, duration, attack=0.025, decay=8))
        elif kind == "Hit":
            sound = (0.80 * impact(time, 76, duration, 25)
                     + 0.32 * impact(time, 171, duration, 40)
                     + 0.90 * bass_noise * env(time, duration, attack=0.001, decay=34)
                     + 0.46 * contact_noise * env(time, duration, attack=0.001, decay=73))
        elif kind == "Heal":
            sound = sum(0.32 * bell(time - onset, frequency, duration - onset, 5)
                        for onset, frequency in ((0, 130.81), (0.065, 164.81), (0.13, 196)))
            sound += 0.30 * bass_noise * env(time, duration, attack=0.05, decay=7)
        elif kind == "Shield":
            sound = (0.65 * impact(time, 110, duration, 18)
                     + 0.28 * impact(time, 254, duration, 11)
                     + 0.25 * smooth_noise * env(time, duration, attack=0.002, decay=70))
        elif kind == "Summon":
            sound = (0.34 * bell(time, 65.41, duration, 3)
                     + 0.24 * bell(time - 0.12, 98, duration - 0.12, 4)
                     + 0.52 * impact(time - 0.20, 109, duration - 0.20, 10)
                     + 0.85 * bass_noise * env(time, duration, attack=0.10, decay=3.5))
        elif kind == "Build":
            sound = 0.0
            for onset, frequency, level in ((0, 97, 0.68), (0.12, 120, 0.57), (0.255, 146, 0.46)):
                local = time - onset
                sound += level * (impact(local, frequency, duration - onset, 26)
                                  + 0.60 * contact_noise * env(local, duration - onset, attack=0.001, decay=70))
        elif kind == "Skill":
            sound = (0.44 * bell(time, 82.41, duration, 7)
                     + 0.30 * bell(time - 0.04, 123.47, duration - 0.04, 8)
                     + 0.34 * impact(time - 0.08, 137, duration - 0.08, 13)
                     + 0.50 * bass_noise * env(time, duration, attack=0.015, decay=10))
        elif kind == "Victory":
            sound = sum(0.29 * bell(time - onset, frequency, duration - onset, 5)
                        for onset, frequency in ((0, 110), (0.13, 138.59), (0.26, 164.81), (0.40, 220)))
            sound += 0.28 * impact(time, 73.42, duration, 12)
        elif kind == "Defeat":
            sound = sum(0.32 * bell(time - onset, frequency, duration - onset, 5)
                        for onset, frequency in ((0, 110), (0.20, 87.31), (0.40, 73.42)))
            sound += 0.25 * impact(time, 61.74, duration, 11)
        elif kind == "UiClick":
            sound = (0.64 * impact(time, 225, duration, 64)
                     + 0.33 * contact_noise * env(time, duration, attack=0.0007, decay=115))
        else:
            raise ValueError(kind)
        values.append(sound)
    # Small, non-repeating room/board reflections give a tangible material tail.
    # They are baked into the clip: no runtime reverb filter, delay source or extra voice.
    dry = list(values)
    for delay, strength in ((0.023, 0.12), (0.047, 0.055)):
        offset = round(delay * RATE)
        for sample in range(offset, len(values)):
            values[sample] += dry[sample - offset] * strength
    for sample in range(len(values)):
        time = sample / RATE
        values[sample] *= min(1, time / 0.0007, max(0, (duration - time) / 0.024))
    peak = max(abs(value) for value in values)
    result = [value * PEAK / max(peak, 1e-9) for value in values]
    result[0] = result[-1] = 0
    return result


def wav_bytes(values: list[float]) -> bytes:
    stream = io.BytesIO()
    with wave.open(stream, "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(b"".join(struct.pack("<h", round(value * 32767)) for value in values))
    return stream.getvalue()


def importer_bytes(filename: str) -> bytes:
    guid = uuid.uuid5(uuid.NAMESPACE_URL, "fantasy-kingdom/feedback-audio/" + filename).hex
    return ("fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "AudioImporter:\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 7\n"
            "  defaultSettings:\n"
            "    serializedVersion: 2\n"
            "    loadType: 0\n"
            "    sampleRateSetting: 0\n"
            "    sampleRateOverride: 22050\n"
            "    compressionFormat: 0\n"
            "    quality: 1\n"
            "    conversionMode: 0\n"
            "    preloadAudioData: 1\n"
            "  platformSettingOverrides: {}\n"
            "  forceToMono: 1\n"
            "  normalize: 0\n"
            "  ambisonic: 0\n"
            "  loadInBackground: 0\n"
            "  userData: Original procedural board sound; GenerateBoardSounds.py\n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n").encode("utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify", action="store_true", help="Verify existing assets without writing files.")
    parser.add_argument("--preview", action="store_true", help="Also make a labelled-order listening WAV under Docs (no spoken labels).")
    args = parser.parse_args()
    if not args.verify:
        OUTPUT.mkdir(parents=True, exist_ok=True)
    total_bytes = 0
    preview = [0.0] * round(RATE * 0.20)
    for index, (kind, duration) in enumerate(SOUNDS):
        values = synthesize(kind, duration, 821_700 + index)
        payload = wav_bytes(values)
        preview.extend(value * 0.80 for value in values)
        preview.extend([0.0] * round(RATE * 0.36))
        target = OUTPUT / f"Board{kind}.wav"
        if args.verify:
            if not target.is_file() or target.read_bytes() != payload:
                raise RuntimeError(f"Generated WAV differs: {target}")
        elif not target.exists() or target.read_bytes() != payload:
            target.write_bytes(payload)
        meta = target.with_suffix(".wav.meta")
        if not args.verify and not meta.exists():
            meta.write_bytes(importer_bytes(target.name))
        with wave.open(io.BytesIO(payload), "rb") as clip:
            assert clip.getnchannels() == 1 and clip.getsampwidth() == 2 and clip.getframerate() == RATE
            assert 0.099 <= clip.getnframes() / RATE <= 0.8
            samples = struct.unpack("<" + "h" * clip.getnframes(), clip.readframes(clip.getnframes()))
            assert samples[0] == 0 and samples[-1] == 0
            assert 0 < max(abs(value) for value in samples) < 32767
        total_bytes += len(payload)
        print(f"{target.name}: {len(payload):,} bytes SHA256={hashlib.sha256(payload).hexdigest()}")
    print(f"{'Verified' if args.verify else 'Generated'} {len(SOUNDS)} original WAV files: {total_bytes:,} bytes total")
    assert total_bytes < 400 * 1024
    if args.preview and not args.verify:
        PREVIEW.parent.mkdir(parents=True, exist_ok=True)
        PREVIEW.write_bytes(wav_bytes(preview))
        print(f"Listening preview ({len(preview) / RATE:.2f}s): {PREVIEW}")


if __name__ == "__main__":
    main()
