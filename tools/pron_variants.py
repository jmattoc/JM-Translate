"""Prueba variantes de escritura para siglas problemáticas: sintetiza y vuelve a transcribir con Parakeet."""
import os

import numpy as np
import sherpa_onnx

M = "../models"


def piper(d, name):
    return sherpa_onnx.OfflineTts(sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(
        vits=sherpa_onnx.OfflineTtsVitsModelConfig(model=f"{M}/{d}/{name}.onnx", tokens=f"{M}/{d}/tokens.txt",
                                                   data_dir=f"{M}/{d}/espeak-ng-data"), num_threads=4)))


def kokoro():
    d = f"{M}/kokoro-multi-lang-v1_0"
    return sherpa_onnx.OfflineTts(sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(
        kokoro=sherpa_onnx.OfflineTtsKokoroModelConfig(model=f"{d}/model.onnx", voices=f"{d}/voices.bin", tokens=f"{d}/tokens.txt",
                                                       data_dir=f"{d}/espeak-ng-data", dict_dir=f"{d}/dict",
                                                       lexicon=f"{d}/lexicon-us-en.txt,{d}/lexicon-zh.txt"), num_threads=4)))


d = f"{M}/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8"
stt = sherpa_onnx.OfflineRecognizer.from_transducer(
    encoder=f"{d}/encoder.int8.onnx", decoder=f"{d}/decoder.int8.onnx", joiner=f"{d}/joiner.int8.onnx",
    tokens=f"{d}/tokens.txt", model_type="nemo_transducer", num_threads=4)


def heard(samples, rate):
    s = stt.create_stream()
    s.accept_waveform(rate, np.array(samples, dtype=np.float32))
    stt.decode_stream(s)
    return s.result.text.strip()


VARIANTS = {
    "API": ["API", "A P I", "A.P.I.", "ay pee eye"],
    "AWS": ["AWS", "A W S", "A.W.S.", "ay double you ess"],
    "ASP.NET": ["ASP.NET", "ASP dot net", "A S P dot net", "A.S.P. dot net"],
    "gRPC": ["gRPC", "g R P C", "G.R.P.C.", "gee R P C"],
    "Redis": ["Redis", "Reddis", "Red iss", "Redd-iss"],
    "CQRS": ["CQRS", "C Q R S", "C.Q.R.S.", "see Q R S"],
    "OAuth": ["OAuth", "oh auth", "O Auth", "oh-auth"],
    "RabbitMQ": ["RabbitMQ", "Rabbit M Q", "Rabbit.M.Q."],
    "JSON": ["JSON", "jason", "J son"],
    "Kubernetes": ["Kubernetes", "Koo-ber-net-eez", "Kuber-netties"],
    "TDD": ["TDD", "T D D", "T.D.D."],
}

for label, tts in (("Piper john", piper("vits-piper-en_US-john-medium", "en_US-john-medium")), ("Kokoro adam", kokoro())):
    print(f"\n===== {label} =====")
    for term, variants in VARIANTS.items():
        outs = []
        for v in variants:
            g = tts.generate(f"We use {v} in our stack.", sid=11 if "Kokoro" in label else 0, speed=1.0)
            outs.append(f"[{v}] → {heard(g.samples, g.sample_rate)}")
        print(f"{term}:\n   " + "\n   ".join(outs))
