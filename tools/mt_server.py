"""Servicio local de traducción (Opus-MT + CTranslate2, CPU). Solo escucha en 127.0.0.1.

POST /translate  {"pair": "en-es" | "es-en", "text": "..."}  ->  {"text": "...", "ms": 55}
GET  /health     ->  ok
Todo en memoria: no se guarda nada.
"""
import json
import os
import re
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import ctranslate2
from transformers import MarianTokenizer

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.join(HERE, "..", "models")
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 5005


def load_glossary():
    path = os.path.join(HERE, "glossary.json")
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    return {}


GLOSSARY = load_glossary()

# Medido con frases de entrevistas técnicas (tools/mt_eval.py):
#  - en->es: Opus-MT "tc-big" mejora claramente a la base (+~70 ms).
#  - es->en: la base es la mejor opción disponible; NLLB-600M no mejoraba y era 8x más lento.
MODEL_FOR_PAIR = {
    "en-es": ("Helsinki-NLP/opus-mt-tc-big-en-es", "opus-big-en-es"),
    "es-en": ("Helsinki-NLP/opus-mt-es-en", "opus-es-en"),
}
ENGINES = {}
for pair, (hf_name, local_dir) in MODEL_FOR_PAIR.items():
    if not os.path.exists(os.path.join(MODELS, local_dir, "model.bin")):  # respaldo: modelo base
        hf_name, local_dir = f"Helsinki-NLP/opus-mt-{pair}", f"opus-{pair}"
    tok = MarianTokenizer.from_pretrained(hf_name)
    tr = ctranslate2.Translator(os.path.join(MODELS, local_dir), device="cpu", inter_threads=1, intra_threads=4)
    ENGINES[pair] = (tok, tr)

# Español con sujeto omitido: "Diseñaría…" sale como "It would design". Quien habla de sí mismo dice "I would".
CONDITIONAL_START = re.compile(r"^\W*\w+r[ií]a\b", re.IGNORECASE)
NOT_BARE_VERB = {
    "i", "i'd", "i'll", "i'm", "we", "we'd", "you", "he", "she", "they", "it", "the", "a", "an", "this", "that",
    "my", "our", "there", "if", "when", "first", "then", "after", "before", "for", "in", "on", "with", "to", "as",
    "at", "by", "of", "from", "and", "but", "so", "because", "probably", "maybe", "perhaps", "personally", "also",
}


SENTENCE_SPLIT = re.compile(r"(?<=[.!?])\s+")


def translate_sentences(pair, text):
    """Traduce cada oración por separado: los modelos Opus tienden a omitir oraciones cuando reciben varias juntas."""
    tok, tr = ENGINES[pair]
    sentences = [s for s in SENTENCE_SPLIT.split(text.strip()) if s.strip()] or [text]
    batch = [tok.convert_ids_to_tokens(tok.encode(s)) for s in sentences]
    results = tr.translate_batch(batch, beam_size=2)
    return " ".join(tok.decode(tok.convert_tokens_to_ids(r.hypotheses[0]), skip_special_tokens=True) for r in results)


def translate(pair, text):
    result = translate_sentences(pair, text)
    if pair == "es-en" and CONDITIONAL_START.match(text):
        if result.startswith("It would "):
            result = "I would " + result[len("It would "):]
        elif result.split(" ", 1)[0].lower() not in NOT_BARE_VERB:
            # el traductor dejó el verbo suelto ("Optimize the query…"); quien habla de sí mismo dice "I would…"
            result = "I would " + result[0].lower() + result[1:]
    for wrong, right in GLOSSARY.get(pair, {}).items():
        result = result.replace(wrong, right)
    return result


class VoiceCloner:
    """Cambia el timbre de la voz sintética por el de models/mi-voz.* (OpenVoice v2). Carga perezosa."""

    def __init__(self):
        self.lock = threading.Lock()
        self.conv = None
        self.tgt_se = None
        self.src_se = None  # se calcula con la primera frase larga; antes, por frase

    @staticmethod
    def reference_path():
        for ext in ("wav", "mp3", "m4a", "ogg", "flac"):
            p = os.path.join(MODELS, f"mi-voz.{ext}")
            if os.path.exists(p):
                return p
        return None

    def _load(self):
        import librosa  # noqa: F401
        import soundfile as sf
        import torch
        torch.set_num_threads(4)
        sys.path.insert(0, os.path.join(HERE, "openvoice"))
        from openvoice.api import ToneColorConverter

        ref = self.reference_path()
        if ref is None:
            raise FileNotFoundError("No existe models/mi-voz.(wav|mp3|m4a|ogg|flac)")
        ckpt = os.path.join(MODELS, "openvoice-v2", "converter")
        self.conv = ToneColorConverter(os.path.join(ckpt, "config.json"), device="cpu", enable_watermark=False)
        self.conv.load_ckpt(os.path.join(ckpt, "checkpoint.pth"))
        self.tgt_se = self.conv.extract_se(ref)
        self._sf = sf

    def use_voice(self, voice_id):
        """Si cambia la voz base, el timbre de origen cacheado deja de valer."""
        if voice_id != getattr(self, "voice_id", None):
            self.voice_id = voice_id
            self.src_se = None

    def convert(self, samples, rate):
        """samples: float32 mono a `rate` Hz. Devuelve float32 mono a `rate` Hz (mismo tamaño aprox.)."""
        import librosa
        import numpy as np
        import torch
        from openvoice.mel_processing import spectrogram_torch

        with self.lock:
            if self.conv is None:
                self._load()
            hps = self.conv.hps
            sr = hps.data.sampling_rate
            audio = librosa.resample(samples, orig_sr=rate, target_sr=sr) if rate != sr else samples
            with torch.no_grad():
                y = torch.FloatTensor(audio).unsqueeze(0)
                spec = spectrogram_torch(y, hps.data.filter_length, sr, hps.data.hop_length, hps.data.win_length, center=False)
                if self.src_se is None or len(audio) / sr < 3.0:
                    src_se = self.conv.model.ref_enc(spec.transpose(1, 2)).unsqueeze(-1)
                    if self.src_se is None and len(audio) / sr >= 3.0:
                        self.src_se = src_se
                else:
                    src_se = self.src_se
                lengths = torch.LongTensor([spec.size(-1)])
                out = self.conv.model.voice_conversion(spec, lengths, sid_src=src_se, sid_tgt=self.tgt_se, tau=0.3)[0][0, 0]
                out = out.data.cpu().float().numpy()
            if rate != sr:
                out = librosa.resample(out, orig_sr=sr, target_sr=rate)
            return np.ascontiguousarray(out, dtype=np.float32)


CLONER = VoiceCloner()


class LazyMatcher:
    """Comparación por significado de la pregunta del entrevistador con las del banco (tools/matcher.py)."""

    def __init__(self):
        self.lock = threading.Lock()
        self.matcher = None

    def get(self):
        with self.lock:
            if self.matcher is None:
                sys.path.insert(0, HERE)
                from matcher import Matcher, default_path
                self.matcher = Matcher(default_path())
            return self.matcher


MATCHER = LazyMatcher()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def _send(self, code, body, ctype="application/json"):
        data = body if isinstance(body, bytes) else body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        self._send(200, "ok", "text/plain")

    def do_POST(self):
        if self.path == "/match":
            try:
                n = int(self.headers.get("Content-Length", 0))
                req = json.loads(self.rfile.read(n).decode("utf-8"))
                t0 = time.perf_counter()
                ranked = MATCHER.get().rank(req["query"], req["candidates"])
                ms = int((time.perf_counter() - t0) * 1000)
                self._send(200, json.dumps({"results": ranked[:5], "ms": ms}, ensure_ascii=False))
            except Exception as e:  # noqa: BLE001
                self._send(500, json.dumps({"error": str(e)}))
            return
        if self.path == "/clone":
            try:
                import numpy as np
                n = int(self.headers.get("Content-Length", 0))
                rate = int(self.headers.get("X-Sample-Rate", "22050"))
                CLONER.use_voice(self.headers.get("X-Voice-Id", ""))
                samples = np.frombuffer(self.rfile.read(n), dtype=np.float32).copy()
                out = CLONER.convert(samples, rate)
                self._send(200, out.tobytes(), "application/octet-stream")
            except Exception as e:  # noqa: BLE001
                self._send(500, json.dumps({"error": str(e)}))
            return
        try:
            n = int(self.headers.get("Content-Length", 0))
            req = json.loads(self.rfile.read(n).decode("utf-8"))
            t0 = time.perf_counter()
            text = translate(req["pair"], req["text"])
            ms = int((time.perf_counter() - t0) * 1000)
            self._send(200, json.dumps({"text": text, "ms": ms}, ensure_ascii=False))
        except Exception as e:  # noqa: BLE001
            self._send(500, json.dumps({"error": str(e)}))


if __name__ == "__main__":
    translate("en-es", "warm up")
    translate("es-en", "calentamiento")
    if VoiceCloner.reference_path():
        def _preload():
            with CLONER.lock:
                if CLONER.conv is None:
                    CLONER._load()
        threading.Thread(target=_preload, daemon=True).start()
    threading.Thread(target=MATCHER.get, daemon=True).start()  # carga el comparador mientras la app arranca
    print("ready", flush=True)
    ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
