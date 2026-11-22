#!/usr/bin/env python3
"""Render the outdoor prototype's original mono equipment sounds, offline.

Each room bed has a six-second period. Crossfade a short preroll of filtered
air noise into its tail so looping neither clicks nor fades to silence.
"""
import math
from pathlib import Path
import random
import struct
import wave

OUTPUT = Path(__file__).resolve().parents[2] / 'Assets/Resources/Audio/Halls'
RATE = 16000
PERIOD = 6


def tone(hz, t):
    return math.sin(math.tau * hz * t)


def ping(t, start, hz, decay):
    age = t - start
    if age < 0:
        return 0
    return min(1, age / .012) * math.exp(-decay * age) * tone(hz, age)


def write(name, samples):
    peak = max(abs(v) for v in samples)
    pcm = [round(v / peak * 23000) for v in samples]
    with wave.open(str(OUTPUT / (name + '.wav')), 'wb') as f:
        f.setparams((1, 2, RATE, len(pcm), 'NONE', 'not compressed'))
        f.writeframes(struct.pack('<%dh' % len(pcm), *pcm))


def bed(name, seed, signal):
    rng = random.Random(seed)
    overlap = RATE // 8
    air = []
    low = 0
    for _ in range(RATE * PERIOD + overlap):
        low += .11 * (rng.uniform(-1, 1) - low)
        air.append(low)
    samples = []
    for i in range(RATE * PERIOD):
        noise = air[i + overlap]
        k = i - (RATE * PERIOD - overlap)
        if k >= 0:
            mix = k / overlap
            noise = (1 - mix) * noise + mix * air[k]
        samples.append(signal(i / RATE, noise))
    write(name, samples)


def notes(name, duration, sequence):
    samples = []
    for i in range(round(RATE * duration)):
        t = i / RATE
        value = sum(level * ping(t, start, frequency, decay) for start, frequency, decay, level in sequence)
        samples.append(value * min(1, (duration - t) / .025))
    write(name, samples)


if __name__ == '__main__':
    OUTPUT.mkdir(parents=True, exist_ok=True)
    bed('Security', 10, lambda t, air: .22*tone(105, t) + .08*tone(210, t)*(1+.18*tone(.5, t)) + .48*air)
    bed('Cooling', 11, lambda t, air: 1.25*air + .16*tone(145, t)*(1+.22*tone(15, t))
        + .065*(ping(t, 1.4, 830, 36) + ping(t, 1.56, 960, 50) + ping(t, 4.3, 720, 42)))
    bed('Monitoring', 12, lambda t, air: .14*tone(154, t) + .08*tone(231, t) + .37*air + .09*ping(t, 2, 610, 8))
    bed('Ventilation', 13, lambda t, air: .92*air*(1+.16*tone(3, t)) + .12*tone(77, t) + .085*tone(185, t))
    bed('Generator', 14, lambda t, air: .28*tone(66, t) + (.17*tone(132, t)+.09*tone(264, t))*(1+.24*tone(11, t)) + .65*air)
    notes('Relay', .19, [(0, 430, 31, .75), (.029, 780, 37, .45)])
    notes('Authenticate', .52, [(0, 480, 15, .65), (.106, 620, 16, .78), (.225, 810, 13, .82)])
    notes('Reject', .25, [(0, 324, 20, .7), (.085, 235, 21, .7)])
    notes('Advisory', .70, [(0, 360, 10, .7), (.26, 320, 10, .65)])
