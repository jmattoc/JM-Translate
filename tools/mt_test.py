"""Spike de traducción: Opus-MT (CTranslate2, int8, CPU). Mide calidad y latencia."""
import time
import ctranslate2
from transformers import MarianTokenizer

MODELS = "../models"


def load(pair):
    tok = MarianTokenizer.from_pretrained(f"Helsinki-NLP/opus-mt-{pair}")
    tr = ctranslate2.Translator(f"{MODELS}/opus-{pair}", device="cpu", inter_threads=1, intra_threads=6)
    return tok, tr


def translate(tok, tr, text):
    tokens = tok.convert_ids_to_tokens(tok.encode(text))
    out = tr.translate_batch([tokens], beam_size=2)[0].hypotheses[0]
    return tok.decode(tok.convert_tokens_to_ids(out), skip_special_tokens=True)


en_es = load("en-es")
es_en = load("es-en")

EN = [
    "Can you walk me through how you would design a distributed system with Kubernetes and event driven microservices?",
    "We use dependency injection, CQRS and a message broker like RabbitMQ to keep the services decoupled.",
    "What is your experience with CI/CD pipelines and infrastructure as code?",
    "How do you handle eventual consistency when a service fails halfway through a saga?",
]
ES = [
    "Diseñaría una arquitectura de microservicios con colas de mensajes y una base de datos por servicio.",
    "Tengo diez años de experiencia con dotnet, SQL Server y despliegues en la nube.",
    "Para la consistencia eventual usaría el patrón saga con compensaciones y un outbox transaccional.",
    "Trabajé como arquitecto de soluciones, definiendo la integración entre sistemas legados y APIs modernas.",
]

for name, (tok, tr), texts in (("EN->ES", en_es, EN), ("ES->EN", es_en, ES)):
    print(f"=== {name} ===")
    translate(tok, tr, texts[0])  # calentamiento
    for t in texts:
        t0 = time.perf_counter()
        r = translate(tok, tr, t)
        ms = (time.perf_counter() - t0) * 1000
        print(f"[{ms:.0f} ms] {t}\n         -> {r}")
