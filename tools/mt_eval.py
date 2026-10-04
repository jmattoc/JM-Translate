"""Evalúa motores de traducción con frases típicas de entrevistas técnicas: calidad (a ojo) y latencia.
Motores: opus (base), big (tc-big, solo en->es), nllb greedy y nllb beam 2.
"""
import os
import sys
import time

import ctranslate2
from transformers import AutoTokenizer, MarianTokenizer

MODELS = "../models"
LANG = {"en": "eng_Latn", "es": "spa_Latn"}
THREADS = 6

EN = [
    "Can you walk me through how you would design a distributed system with Kubernetes?",
    "What is your experience with CI/CD pipelines and infrastructure as code?",
    "How do you handle eventual consistency when a service fails halfway through a saga?",
    "We use a message broker to keep the services decoupled and we deploy with blue green releases.",
    "Tell me about a time you had to refactor a legacy monolith into microservices.",
    "I'd like to understand how you approach code reviews and mentoring junior developers.",
    "The role involves owning the on-call rotation and improving our observability stack.",
    "How would you optimize a slow SQL query that joins five large tables?",
    "We expect you to write unit tests and integration tests before merging to main.",
    "Do you have any questions for us about the team or the roadmap?",
    "Our system processes about ten thousand requests per second at peak.",
    "What trade-offs would you consider between consistency and availability?",
]
ES = [
    "Diseñaría una arquitectura de microservicios con colas de mensajes y una base de datos por servicio.",
    "Tengo diez años de experiencia con dotnet, SQL Server y despliegues en la nube.",
    "Para la consistencia eventual usaría el patrón saga con compensaciones y un outbox transaccional.",
    "Trabajé como arquitecto de soluciones, definiendo la integración entre sistemas legados y APIs modernas.",
    "Lideré la migración de un monolito a microservicios reduciendo los tiempos de despliegue en un cuarenta por ciento.",
    "Me gusta revisar el código con calma y explicar las decisiones para que el equipo aprenda.",
    "Optimizaría la consulta revisando el plan de ejecución, los índices y las estadísticas de la tabla.",
    "Primero escribiría pruebas unitarias y luego pruebas de integración en el pipeline.",
    "Mi inglés está mejorando, pero prefiero que me repitan la pregunta si no la entiendo bien.",
    "Me interesa saber cómo es el proceso de incorporación y qué esperan de mí en los primeros tres meses.",
    "Utilizamos contenedores y orquestación para escalar horizontalmente según la demanda.",
    "En mi último proyecto implementé autenticación con tokens y control de acceso por roles.",
]


class Engine:
    def __init__(self, name, tr, tok_for, fmt):
        self.name, self.tr, self.tok_for, self.fmt = name, tr, tok_for, fmt

    def translate(self, src, tgt, text):
        return self.fmt(self, src, tgt, text)


def marian(pair, path):
    tok = MarianTokenizer.from_pretrained(path)
    tr = ctranslate2.Translator(f"{MODELS}/{os.path.basename(path) if False else ''}", device="cpu")
    return tok, tr


def main():
    engines = {}

    def load_marian(key, hf, local):
        tok = MarianTokenizer.from_pretrained(hf)
        tr = ctranslate2.Translator(f"{MODELS}/{local}", device="cpu", intra_threads=THREADS)
        engines[key] = (tok, tr)

    load_marian("opus en-es", "Helsinki-NLP/opus-mt-en-es", "opus-en-es")
    load_marian("opus es-en", "Helsinki-NLP/opus-mt-es-en", "opus-es-en")
    if os.path.exists(f"{MODELS}/opus-big-en-es/model.bin"):
        load_marian("big en-es", "Helsinki-NLP/opus-mt-tc-big-en-es", "opus-big-en-es")

    nllb_tr = ctranslate2.Translator(f"{MODELS}/nllb-600m", device="cpu", intra_threads=THREADS)
    nllb_tok = {l: AutoTokenizer.from_pretrained("facebook/nllb-200-distilled-600M", src_lang=c) for l, c in LANG.items()}

    def run_marian(key, text, beam=2):
        tok, tr = engines[key]
        toks = tok.convert_ids_to_tokens(tok.encode(text))
        out = tr.translate_batch([toks], beam_size=beam)[0].hypotheses[0]
        return tok.decode(tok.convert_tokens_to_ids(out), skip_special_tokens=True)

    def run_nllb(src, tgt, text, beam):
        tok = nllb_tok[src]
        toks = tok.convert_ids_to_tokens(tok.encode(text))
        out = nllb_tr.translate_batch([toks], target_prefix=[[LANG[tgt]]], beam_size=beam)[0].hypotheses[0]
        return tok.decode(tok.convert_tokens_to_ids(out), skip_special_tokens=True)

    run_nllb("en", "es", "warm up", 1)
    for src, tgt, texts in (("en", "es", EN), ("es", "en", ES)):
        pair = f"{src}-{tgt}"
        cands = [("opus", lambda t: run_marian(f"opus {pair}", t))]
        if f"big {pair}" in engines:
            cands.append(("big", lambda t: run_marian(f"big {pair}", t)))
        cands += [("nllb g1", lambda t: run_nllb(src, tgt, t, 1)), ("nllb b2", lambda t: run_nllb(src, tgt, t, 2))]
        totals = {n: 0.0 for n, _ in cands}
        print(f"\n################ {src.upper()} -> {tgt.upper()} ################")
        for t in texts:
            print(f"\n» {t}")
            for name, fn in cands:
                t0 = time.perf_counter()
                r = fn(t)
                ms = (time.perf_counter() - t0) * 1000
                totals[name] += ms
                print(f"  {name:8s} {ms:4.0f} ms  {r}")
        print("\n  promedio ms:", {n: round(v / len(texts)) for n, v in totals.items()})


main()
