"""Compara Opus-MT contra NLLB-200 (600M, int8) en calidad y latencia."""
import time

import ctranslate2
from transformers import AutoTokenizer, MarianTokenizer

MODELS = "../models"
LANG = {"en": "eng_Latn", "es": "spa_Latn"}

nllb_tr = ctranslate2.Translator(f"{MODELS}/nllb-600m", device="cpu", inter_threads=1, intra_threads=6)
nllb_tok = {l: AutoTokenizer.from_pretrained("facebook/nllb-200-distilled-600M", src_lang=c) for l, c in LANG.items()}
opus = {}
for pair in ("en-es", "es-en"):
    opus[pair] = (MarianTokenizer.from_pretrained(f"Helsinki-NLP/opus-mt-{pair}"),
                  ctranslate2.Translator(f"{MODELS}/opus-{pair}", device="cpu", intra_threads=6))


def nllb(src, tgt, text, beam=2):
    tok = nllb_tok[src]
    tokens = tok.convert_ids_to_tokens(tok.encode(text))
    out = nllb_tr.translate_batch([tokens], target_prefix=[[LANG[tgt]]], beam_size=beam)[0].hypotheses[0]
    ids = tok.convert_tokens_to_ids(out)
    return tok.decode(ids, skip_special_tokens=True)


def marian(pair, text):
    tok, tr = opus[pair]
    tokens = tok.convert_ids_to_tokens(tok.encode(text))
    out = tr.translate_batch([tokens], beam_size=2)[0].hypotheses[0]
    return tok.decode(tok.convert_tokens_to_ids(out), skip_special_tokens=True)


CASES = [
    ("en", "es", "We use dependency injection, CQRS and a message broker like RabbitMQ to keep the services decoupled."),
    ("en", "es", "What is your experience with CI/CD pipelines and infrastructure as code?"),
    ("en", "es", "I'm going to work on that ticket today and I think we can deploy it to production next week."),
    ("en", "es", "Where it states that the style of some titles must be modified in option 1 and option 3 of the customer module."),
    ("es", "en", "Diseñaría una arquitectura de microservicios con colas de mensajes y una base de datos por servicio."),
    ("es", "en", "Hoy voy a trabajar en ese ticket y pienso desplegarlo a producción la próxima semana."),
    ("es", "en", "Para la consistencia eventual usaría el patrón saga con compensaciones y un outbox transaccional."),
    ("es", "en", "Trabajé como arquitecto de soluciones, definiendo la integración entre sistemas legados y APIs modernas."),
]

nllb("en", "es", "warm up")
for src, tgt, text in CASES:
    t0 = time.perf_counter(); a = marian(f"{src}-{tgt}", text); ta = (time.perf_counter() - t0) * 1000
    t0 = time.perf_counter(); b = nllb(src, tgt, text); tb = (time.perf_counter() - t0) * 1000
    print(f"\n[{src}->{tgt}] {text}\n  opus {ta:4.0f} ms: {a}\n  nllb {tb:4.0f} ms: {b}")
