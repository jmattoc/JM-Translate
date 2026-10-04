"""Compara una pregunta del entrevistador con las preguntas típicas del banco por SIGNIFICADO (no por palabras exactas).

Usa all-MiniLM-L6-v2 (22 M de parámetros, ~90 MB, CPU): unos 10–30 ms por pregunta.
Todo en memoria; nada se guarda.
"""
import os

import numpy as np
import torch
from transformers import AutoModel, AutoTokenizer


class Matcher:
    def __init__(self, path):
        torch.set_num_threads(2)
        self.tok = AutoTokenizer.from_pretrained(path)
        self.model = AutoModel.from_pretrained(path).eval()
        self._cache = {}

    def embed(self, texts):
        missing = [t for t in dict.fromkeys(texts) if t not in self._cache]
        if missing:
            batch = self.tok(missing, padding=True, truncation=True, max_length=64, return_tensors="pt")
            with torch.no_grad():
                out = self.model(**batch).last_hidden_state
            mask = batch["attention_mask"].unsqueeze(-1).float()
            vec = (out * mask).sum(1) / mask.sum(1).clamp(min=1e-9)   # promedio ponderado por la máscara
            vec = torch.nn.functional.normalize(vec, dim=1).numpy()
            for t, v in zip(missing, vec):
                self._cache[t] = v
        return np.stack([self._cache[t] for t in texts])

    def rank(self, query, candidates):
        """candidates: lista de {"id": str, "texts": [str, ...]}. Devuelve [{"id", "score"}] de mayor a menor."""
        q = self.embed([query])[0]
        scored = []
        for c in candidates:
            if not c["texts"]:
                continue
            m = self.embed(c["texts"]) @ q
            scored.append({"id": c["id"], "score": float(m.max())})
        return sorted(scored, key=lambda s: -s["score"])


def default_path():
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(here, "..", "models", "minilm")
