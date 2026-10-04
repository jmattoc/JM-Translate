"""Spike de conversión de timbre (OpenVoice v2, CPU): Piper (inglés) -> voz de referencia.
Uso: python ov_test.py [ruta_voz_referencia.wav]
Sin argumento usa una voz Piper en español solo para medir tiempos.
"""
import os
import sys
import time

import numpy as np
import sherpa_onnx
import soundfile as sf
import torch

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "openvoice"))
from openvoice.api import ToneColorConverter  # noqa: E402
from openvoice.mel_processing import spectrogram_torch  # noqa: E402

MODELS = os.path.join(HERE, "..", "models")
OUT = os.path.join(HERE, "..", "scratch")
os.makedirs(OUT, exist_ok=True)
torch.set_num_threads(int(os.environ.get("OV_THREADS", "6")))


def piper(dir_, name):
    cfg = sherpa_onnx.OfflineTtsConfig(
        model=sherpa_onnx.OfflineTtsModelConfig(
            vits=sherpa_onnx.OfflineTtsVitsModelConfig(
                model=os.path.join(MODELS, dir_, name + ".onnx"),
                tokens=os.path.join(MODELS, dir_, "tokens.txt"),
                data_dir=os.path.join(MODELS, dir_, "espeak-ng-data"),
            ),
            num_threads=2,
        )
    )
    return sherpa_onnx.OfflineTts(cfg)


en = piper("vits-piper-en_US-lessac-medium", "en_US-lessac-medium")
text = "Can you walk me through how you would design a distributed system with Kubernetes?"
t0 = time.perf_counter()
g = en.generate(text, sid=0, speed=1.0)
print(f"TTS en: {(time.perf_counter()-t0)*1000:.0f} ms, {len(g.samples)/g.sample_rate:.1f} s de audio")
src_wav = os.path.join(OUT, "piper_en.wav")
sf.write(src_wav, np.array(g.samples, dtype=np.float32), g.sample_rate)

ref = sys.argv[1] if len(sys.argv) > 1 else None
if ref is None:
    es = piper("vits-piper-es_MX-ald-medium", "es_MX-ald-medium")
    ge = es.generate("Hola, este es un audio de referencia para medir la velocidad del sistema de conversion de voz.", sid=0, speed=1.0)
    ref = os.path.join(OUT, "ref_fake.wav")
    sf.write(ref, np.array(ge.samples, dtype=np.float32), ge.sample_rate)

ckpt = os.path.join(MODELS, "openvoice-v2", "converter")
conv = ToneColorConverter(os.path.join(ckpt, "config.json"), device="cpu", enable_watermark=False)
conv.load_ckpt(os.path.join(ckpt, "checkpoint.pth"))

t0 = time.perf_counter()
tgt_se = conv.extract_se(ref)
src_se = conv.extract_se(src_wav)
print(f"extract_se (una sola vez): {(time.perf_counter()-t0)*1000:.0f} ms")

for i in range(3):
    t0 = time.perf_counter()
    audio = conv.convert(src_wav, src_se, tgt_se, tau=0.3, message="")
    ms = (time.perf_counter() - t0) * 1000
    print(f"convert #{i+1}: {ms:.0f} ms para {len(audio)/conv.hps.data.sampling_rate:.1f} s de audio")
sf.write(os.path.join(OUT, "converted.wav"), audio, conv.hps.data.sampling_rate)
print("listo ->", os.path.abspath(os.path.join(OUT, "converted.wav")))
