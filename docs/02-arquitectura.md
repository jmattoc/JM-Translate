# 02 – Arquitectura

## Flujo general

```
Reunión (Meet/Teams/Zoom)
   │ audio de ellos (inglés)                          audio mío (inglés) ▲
   ▼                                                                    │
[Loopback WASAPI] → VAD → STT(en) → MT(en→es) ─┬→ Subtítulos           │
                                               └→ TTS(es) → mis audífonos
                                                                        │
[Mi micrófono]   → VAD → STT(es) → MT(es→en) → TTS(en) → [Micrófono virtual]
```

Dos pipelines independientes que corren en paralelo.

## Componentes

| Etapa | Función | Opción elegida (CPU) |
|---|---|---|
| Captura de sistema | Audio de la reunión | WASAPI loopback (NAudio) |
| Captura de micrófono | Mi voz | WASAPI (NAudio) |
| VAD | Detectar inicio/fin de frase | Silero VAD (ONNX) |
| STT | Voz → texto, en streaming | Whisper (modelo small/base, int8) vía whisper.cpp / Whisper.net; alternativa: sherpa-onnx |
| MT | Traducción en↔es | Opus-MT (Marian) en ONNX Runtime o CTranslate2, con post-glosario técnico |
| TTS | Texto → voz | Piper (ONNX) vía sherpa-onnx; voces en-US y es-ES/es-MX |
| Salida a audífonos | Voz traducida | WASAPI (NAudio) |
| Salida a reunión | Mi voz en inglés | Cable de audio virtual (VB-Cable) como "micrófono" |
| UI | Control y subtítulos | Ventana WPF/WinUI mínima, siempre visible |

## Stack: .NET (C#)

**Decisión:** .NET 10 (ya instalado, SDK 10.0.400), porque:
- Es la especialidad del autor: mantenimiento y evolución más fáciles.
- NAudio resuelve WASAPI loopback, captura y salida con muy buen soporte en Windows.
- sherpa-onnx y ONNX Runtime tienen bindings C#, así que VAD, STT y TTS corren en el mismo proceso sin servidores externos.
- Python no está instalado y no aporta ventaja clara para un MVP en Windows.

**Decisión de MT (tras el spike):** la traducción corre en un **sidecar Python con CTranslate2** (Opus-MT int8, 50–80 ms por frase) hablando por localhost con la app .NET. En .NET no hay un runtime de MT equivalente sin escribir el bucle de decodificación a mano. Python es portable dentro del proyecto (`tools/`, vía uv), sin instalar nada en el sistema. Los modelos se convierten con `ct2-transformers-converter`.

## Por qué todo en CPU

La GT 730 (arquitectura Kepler) no soporta CUDA moderno ni tiene memoria útil. El i7-12700KF sí puede correr:
- Whisper small/base en int8 en tiempo casi real.
- Opus-MT en decenas de ms por frase.
- Piper TTS mucho más rápido que el tiempo real.

Esto implica **voz sintética** (no clonada) en el MVP. Clonar voz de calidad en CPU en tiempo real no es realista hoy.

## Latencia estimada (CPU)

| Etapa | Tiempo aprox. |
|---|---|
| Espera de fin de frase (VAD) | 0.4–0.8 s |
| STT | 0.3–1.0 s |
| MT | < 0.2 s |
| TTS | 0.2–0.5 s |
| **Total** | **~1.5–2.5 s** |

Reducible con STT en streaming y traducción incremental por segmentos.

## Mic virtual

Windows no permite crear un micrófono virtual desde una app sin un driver. Se usa **VB-Cable** (gratuito para uso personal): la app escribe la voz en inglés en "CABLE Input" y en Meet/Teams/Zoom se elige "CABLE Output" como micrófono.

## Privacidad

Todo en memoria. Sin base de datos, sin archivos de audio ni transcripciones. Sin llamadas a internet en tiempo de ejecución (los modelos se descargan una sola vez al instalar).
