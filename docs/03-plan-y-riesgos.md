# 03 – Plan y riesgos

## Fases

| Fase | Entrega | Criterio de éxito |
|---|---|---|
| 0 | Documentación (este conjunto) | Alcance acordado |
| 1 | **Spike técnico**: medir en esta PC STT, MT y TTS locales con audio real de entrevista técnica | Latencia total ≤ 3 s y calidad aceptable en términos técnicos |
| 2 | **MVP "ellos → yo"**: loopback → STT → MT → subtítulos y voz en español | Entender una reunión real de Meet/Teams |
| 3 | **MVP "yo → ellos"**: micrófono → STT → MT → TTS → VB-Cable | Una persona de prueba me entiende en inglés por Meet |
| 4 | Glosario técnico, ajuste de VAD, evitar eco, UI usable | Entrevista simulada de 30 min sin fallos |
| 5 | Clonación de voz (si hay GPU o presupuesto) | Mi voz en inglés |
| 6 | Mac (Intel) | Ver nota abajo |
| 7 | Android como visor de subtítulos/control | — |

## Resultados del spike (Fase 1, parcial)

Medido en esta PC (i7-12700KF, 6 hilos, CPU), frases técnicas de 5–7 s, ida y vuelta TTS → STT. Código en `src/JmTranslate.Spike`.

| Etapa | Modelo | Tiempo por frase | Observación |
|---|---|---|---|
| TTS | Piper medium (en/es) | 0.12–0.5 s | Muy rápido, sobra margen |
| STT | Whisper small int8 | 1.5–2.1 s (un pico de 7 s) | ~0.3× tiempo real; calidad buena, falla en siglas (RabbitMQ → "Rabat MQ") |
| MT | Opus-MT en↔es, CTranslate2 int8 (Python) | 50–80 ms | Calidad buena; fallos: "pipelines" → "tuberías", "message broker" → "corredor de mensajes", sujeto omitido en español ("Diseñaría" → "It would design") |

Conclusión: el pipeline funciona en CPU y STT es el cuello de botella; MT y TTS casi no suman latencia.
Estimado total por frase: ~2–3 s. Código de MT en `tools/mt_test.py`.
Los errores de MT se corrigen con glosario técnico y, para el sujeto omitido, con una regla o un modelo mayor (Fase 4).

Notas del STT: Mitigaciones: probar Whisper base, reducir hilos competidores, partir frases largas con VAD y usar un glosario/prompt con términos técnicos.

## Resultados: clonación de timbre con OpenVoice v2 (CPU)

Código en `tools/ov_test.py`; modelos en `models/openvoice-v2`. Piper genera el inglés y OpenVoice cambia el timbre a una voz de referencia.

| Medida | Resultado |
|---|---|
| Extracción del timbre de referencia | ~1.3 s, una sola vez |
| Conversión | ~2.5 s por cada ~4 s de audio (≈0.65× tiempo real) |
| Efecto de los hilos (4, 6, 12, 20) | Ninguno: el tiempo no baja |

Integrado en la app como casilla "Mi voz" (`/clone` en `tools/mt_server.py`). Ya dentro del servicio, con el timbre en caché y sin recargar audio, la conversión baja a ~0.95 s por 3.9 s de audio (≈0.25× tiempo real; la primera llamada ~3.7 s por calentamiento). Latencia total estimada con voz clonada: ~3.5–4.5 s por frase corta. Requiere `models/mi-voz.wav` (20–30 s de voz propia).
Vías de mejora: exportar el convertidor a ONNX con int8, convertir por trozos de frase, o usar una GPU moderna.

## Riesgos

| Riesgo | Impacto | Mitigación |
|---|---|---|
| Latencia > 3 s en CPU | Conversación incómoda | Whisper más pequeño, STT streaming, traducir por segmentos |
| Calidad de MT en términos técnicos | Mensajes confusos | Glosario, post-procesado, modelos de mayor calidad si la CPU alcanza |
| Voz sintética robótica | Se nota que es traducción | Elegir buenas voces Piper; clonación en Fase 5 |
| Bucle de eco | Audio inservible | Audífonos obligatorios; no capturar la salida propia |
| Detección por políticas de empresa | Descalificación | Decisión del usuario; no se resuelve técnicamente |
| Mac Intel: sin aceleración de IA y con drivers virtuales distintos | Rendimiento menor | Mac queda para después de validar Windows |
| Android no permite inyectar micrófono sin root | Funcionalidad limitada | Solo visor/control |
| Dos pipelines compiten por la CPU | Más latencia | Hilos separados, prioridades, modelos ligeros |

## Decisiones tomadas

1. Empezar solo por Windows.
2. Costo cero y todo local, sin guardar datos.
3. Stack .NET (C#).
4. Voz sintética en el MVP; clonación después.
5. Micrófono virtual vía VB-Cable.

## Preguntas abiertas

- ~~RAM~~: resuelto, 16 GB (alcanza para Whisper small/base + Opus-MT + Piper a la vez).
- ¿Tienes instalado VB-Cable o permiso para instalar un driver de audio?
- ¿Un GPU más moderno (por ejemplo, una RTX usada) es posible a futuro? Habilitaría clonación de voz y mejor calidad.
- ¿Subtítulos como ventana flotante, o superpuestos a la pantalla?

## Mejoras de rendimiento (iteración 2)

| Cambio | Resultado |
|---|---|
| STT: Whisper small → **Parakeet-TDT 0.6B v3 int8** (sherpa-onnx) | ~0.25–0.4 s por 5 s de audio (Whisper: ~2 s). Más preciso en inglés técnico; detecta es/en solo. |
| MT: Opus-MT vs NLLB-200 600M | NLLB es algo más natural pero ~0.9 s contra ~0.2 s. **Se mantiene Opus-MT + glosario** (`tools/glossary.json`) por latencia. |
| Pausa del VAD | 0.5 s → 0.35 s |
| Correcciones de términos tras el STT | `Terms` en `TranslationPipeline.cs` (.NET, SQL Server, RabbitMQ, Kubernetes…) |
| Medición | La barra de estado muestra el tiempo de cada etapa por frase |

Los modelos Whisper quedan en `models/` solo como respaldo; se pueden borrar.

## Iteración 3: voz más continua y más natural

Problema reportado: al enviar audio por WhatsApp se oía "habla, silencio, habla". Causa: cada frase completa se procesaba (texto → traducción → voz → timbre) antes de sonar, y entre frases se acumulaban pausas.

| Cambio | Efecto |
|---|---|
| Corte adaptativo de frases: pausas desde 0.2 s del VAD; un tramo sale al juntar ≥ 2.5 s de voz o tras 0.5 s de silencio | Tramos más cortos y frecuentes, sin cortar frases muy cortas |
| Temporizador que rellena silencio | El loopback no entrega datos en silencio; sin esto la última frase podía no cerrarse |
| Dos etapas en hilos separados (voz→texto+traducción \| síntesis+timbre) | Se solapan: mientras suena un tramo se prepara el siguiente |
| Síntesis por cláusulas (puntuación y palabras de enlace, ~25–55 caracteres) | La voz empieza antes: frase larga de ~5 s pasó de 2.8 s a 1.5 s hasta el primer sonido |
| Motor Kokoro opcional (voces masculinas `adam`, `michael`, `eric`, `liam`, `onyx`) | Más natural que Piper; ~0.22× tiempo real (≈ +0.5 s por cláusula) |
| Casilla "Enviar voz": si se desmarca, solo subtítulos en inglés (teleprompter) | Sin latencia de síntesis; se lee y se habla con la propia voz |
| Corrección: `X-Voice-Id` con caracteres no ASCII hacía fallar "Mi voz" en silencio | Arreglado |

Autoprueba sin interfaz: `JmTranslate.App.exe --selftest <audio.wav> <informe.txt> [es]` reproduce un audio por la salida predeterminada, lo captura con el pipeline y escribe lo entendido, la traducción y los tiempos. Con `es` prueba "yo → ellos" (Kokoro + mi voz, saliendo por el cable virtual).

Limitación conocida: si la voz sale por el mismo dispositivo que se captura en "Ellos → yo", se descarta la captura mientras suena; lo ideal es usar un dispositivo distinto.

## Iteración 4: calidad de traducción

Comparación con 12 frases de entrevista técnica por dirección (`tools/mt_eval.py`):

| Motor | Latencia media | Veredicto |
|---|---|---|
| Opus-MT base | ~55 ms | Rápido; fallos de terminología y sujeto |
| **Opus-MT tc-big (solo en→es)** | ~130 ms | **Elegido para en→es**: mejor léxico ("revisiones de código", "desacoplados") |
| NLLB-200 600M (greedy / beam 2) | ~440 / ~480 ms | Descartado: 4–8× más lento y no mejor (escribió "refactar", tradujo "consulta" como "consultation") |

Cambios aplicados:
- en→es usa tc-big; es→en sigue con la base (no existe tc-big es→en).
- **Se traduce oración por oración.** Una prueba real mostró que, con dos oraciones juntas, el modelo omitía la primera ("Hello. Thanks." desaparecía).
- **Limpieza antes de traducir** (`TextCleanup.cs`): muletillas ("eh", "um", "este,"), repeticiones ("pues, pues", "que que") y términos mal reconocidos.
- **Glosario editable** en `tools/glossary.json` (`stt`, `en-es`, `es-en`), sin recompilar.
- **Reglas es→en:** el condicional sin sujeto ("Diseñaría…") sale como "I would…" en vez de "It would…" o un verbo suelto.

Límites conocidos: el pretérito de verbos poco frecuentes puede salir en futuro ("Lideré" → "I'll handle…"); se mitiga con el glosario, no se resuelve del todo con este modelo.

## Iteración 5: preparación para entrevistas

Todo se probó con autopruebas sin interfaz (`JmTranslate.App.exe --pronunciation|--phrases|--ptttest|--soak`), reproduciendo audio por el cable virtual para no sonar por los altavoces.

| Mejora | Resultado medido |
|---|---|
| **Banco de frases** con audio en caché | 9 frases preparadas en 70–1200 ms cada una y reconocidas correctamente al volver a transcribirlas. Con tu timbre se conservan, con un caso de menor claridad ("Sorry, I didn't catch that. But you say it again."). |
| **Pulsar para hablar** | Sin la tecla: 0 frases captadas. Con la tecla y al soltarla: la frase llega 0.5 s después. |
| **Silencio de emergencia** y **modo compacto** | Implementados; los 11 atajos globales (Ctrl+Alt+Numpad0…9 y Numpad.) quedan registrados al abrir la app. |
| **Nivelación de volumen** | Cada frase enviada se normaliza (RMS ≈ -18 dBFS, pico ≤ 0.9) para que el control automático de ganancia de Teams/Zoom/Meet no la recorte. |
| **Pronunciación de siglas** | Una lista general **empeoraba** varios casos ("A P I" se leía "app I", "g R P C" se leía "N G R P C"). Se reemplazó por reglas **por motor de voz** y solo para casos verificados con `tools/pron_variants.py`. |
| **Prueba de estabilidad de 10 min** (Kokoro + tu voz, 111 frases) | 0 alertas; la voz empieza a los **1.5 s de mediana** (p95 1.7 s, máx. 2.0 s); memoria estable (app 1.2→1.3 GB, Python 2.1→2.0 GB, sin fugas); CPU de la app ~10 %. |

Corrección encontrada en las pruebas: la limpieza de texto quitaba el espacio de " .NET" ("con.NET"); ya se conserva.

Límite conocido: la prueba de estabilidad usa un audio sintético limpio. Con una persona hablando y el uso simultáneo de Teams, Zoom o Meet, la carga será mayor; conviene repetirla con una llamada real.
