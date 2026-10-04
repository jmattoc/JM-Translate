# 04 – Posibles mejoras

Ordenadas por impacto en el uso real (entrevistas y llamadas) frente al esfuerzo. Los tiempos de referencia vienen de las pruebas del documento 03.

## Para sentir menos retraso

| # | Mejora | Qué ahorra | Esfuerzo |
|---|---|---|---|
| 1 | **Pulsar para hablar (atajo de teclado)**: mantienes una tecla mientras hablas y sueltas al terminar. El pipeline sabe exactamente cuándo acaba la frase y no espera al detector de pausas | 0.3–0.5 s por frase y menos cortes raros | Bajo |
| 2 | **Frases de relleno ya grabadas con tu voz** ("Let me think about that…", "Good question", "Could you repeat that?", "Give me a second"), reproducidas al instante con un atajo | Cubre las pausas incómodas: cero latencia | Bajo |
| 3 | **Voz en streaming real**: reconocimiento incremental (Parakeet/Zipformer en streaming) y traducción por trozos mientras aún hablas | ~1 s | Alto |
| 4 | **Motor de voz adaptativo**: usar Kokoro cuando hay margen y pasar a Piper si se acumula cola | Evita atrasos grandes sin que lo elijas | Medio |
| 5 | **Acelerar "Mi voz"**: exportar el convertidor OpenVoice a ONNX con int8, o convertir solo desde la segunda cláusula | ~0.3–0.5 s por cláusula | Medio |
| 6 | **Calentar modelos al abrir la app** y fijar hilos/prioridad para que el procesador no compita entre etapas | Primera frase más rápida | Bajo |
| 7 | **GPU moderna** (por ejemplo una RTX 3060 12 GB usada) | Todo el pipeline: ~0.5 s total y clonación real de voz | Inversión |

## Para que la traducción sea mejor

| # | Mejora | Detalle |
|---|---|---|
| 8 | **Glosario técnico ampliado** | Ya existen `tools/glossary.json` y las correcciones del reconocimiento. Crecer con los términos reales de tus entrevistas (nombres de patrones, productos, siglas) |
| 9 | **Pronunciación de siglas en inglés** | CQRS, SQL, gRPC, Kubernetes y similares suenan mal en Piper/Kokoro; se arregla con un diccionario de pronunciación |
| 10 | **Contexto entre frases** | Pasar la frase anterior al traductor para resolver sujetos omitidos ("Diseñaría…" → "I would design…") |
| 11 | **Modo calidad con NLLB** | Más natural pero ~0.7 s más lento; ofrecerlo como opción para respuestas largas, no para conversación |
| 12 | **Corrección con tus propias muestras** | Guardar (solo con tu permiso) pares frase/traducción que corriges para ampliar el glosario |

## Para usarlo más cómodo

| # | Mejora | Detalle |
|---|---|---|
| 13 | **Ventana compacta y translúcida** | Solo indicador "¿puedes hablar?" y subtítulos, siempre encima de la reunión, sin ocupar la pantalla |
| 14 | **Perfiles por aplicación** | Un clic para WhatsApp, Teams, Meet o Zoom con dispositivos y voz ya elegidos |
| 15 | **Aviso sonoro opcional** (un tono suave solo en tus audífonos) cuando ya puedes hablar | Complementa la barra |
| 16 | **Botón de silencio** | Corta lo que se envía al instante si algo sale mal |
| 17 | **Cambio de micrófono automático** | Poner y quitar "CABLE Output" como predeterminado sin hacerlo a mano |
| 18 | **Aviso de eco** | Detectar si la voz sale por el dispositivo que se captura |

## Producto y plataformas

| # | Mejora | Detalle |
|---|---|---|
| 19 | **Paquete listo para instalar** | Un instalador que incluya el servicio de traducción, los modelos y la detección del cable virtual |
| 20 | **Mac (Intel)** | Cable virtual BlackHole y la captura del sistema; CPU sin aceleración, por lo que quedaría más lento |
| 21 | **Android** | Visor de subtítulos y control remoto; no se puede inyectar micrófono sin root |
| 22 | **Versión comercial** | Si se vende: licencia, actualizaciones, opción con GPU y aviso claro de uso para cumplir políticas de cada empresa |

## Prioridad sugerida

1. Pulsar para hablar (1) y frases de relleno con tu voz (2): cuestan poco y atacan directamente el silencio incómodo.
2. Ventana compacta (13) y botón de silencio (16): hacen usable la herramienta en una llamada real.
3. Glosario y pronunciación (8, 9): suben la calidad con tus términos reales.
4. Voz en streaming (3) o GPU (7): los saltos grandes de latencia.
