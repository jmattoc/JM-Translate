# 07 – Guía para usar JM-Translate en una entrevista

## 1. Preparación (una sola vez)

1. **VB-Cable instalado** y `models/mi-voz.wav` grabada si quieres usar tu timbre (ver docs/06).
2. En **Frases**, escribe tus respuestas típicas en español (preséntate, tus proyectos, tus logros, por qué quieres el puesto) y pulsa **Traducir español → inglés**. **Lee y corrige el inglés**: lo que ahí quede es lo que se dirá.
3. Elige la voz y decide si usas «Mi voz». Luego pulsa **Preparar todas**: así cada frase suena al instante y con calidad máxima.
4. Ordena con ▲ ▼ las frases que más usarás: la posición decide el atajo (p1·1 a p1·9 en la primera página, p2·1 en la segunda…). La ventana Frases muestra la etiqueta de cada una.

## 2. Diez minutos antes de cada entrevista

- [ ] Audífonos puestos (sin ellos, tu micrófono recoge la voz traducida y se arma un bucle).
- [ ] Cierra otras aplicaciones pesadas: Teams, Zoom o Meet ya usan bastante procesador.
- [ ] Abre JM-Translate y pulsa **Iniciar todo**. Espera a ver «✔ Puedes hablar».
- [ ] En la plataforma de la reunión, micrófono = **CABLE Output**; altavoz = tus audífonos.
- [ ] En «Ellos → yo», «Audio de la reunión» = tus audífonos; deja **sin marcar «Voz en español»** si vas a leer solo los subtítulos.
- [ ] Prueba el audio con quien tengas a mano o con la frase **«Prueba de audio»** (envíala desde la ventana Frases con «Enviar ahora»): comprueba que se oye claro y sin cortes.
- [ ] Prepara **Compacto** (Ctrl+Alt+Numpad.) para tener solo el indicador y los subtítulos sobre la reunión.

## 3. Ajustes de audio de cada plataforma

Estas aplicaciones procesan el micrófono (supresión de ruido y control automático de volumen) y pueden recortar o atenuar una voz sintética. Los menús cambian con las versiones; busca estas opciones y verifica en tu versión:

| Plataforma | Qué revisar |
|---|---|
| **Zoom** | Audio → micrófono = CABLE Output. «Suprimir ruido de fondo»: Bajo o Desactivado. Desmarca «Ajustar automáticamente el volumen del micrófono». |
| **Microsoft Teams** | Dispositivos → micrófono = CABLE Output. «Supresión de ruido»: Baja o Desactivada. |
| **Google Meet** | Configuración → Audio → micrófono = CABLE Output. Prueba con la cancelación de ruido desactivada si la voz se entrecorta. |
| **WhatsApp (escritorio)** | Usa el micrófono predeterminado de comunicaciones de Windows: ponlo en CABLE Output antes de llamar y devuélvelo después. |

La app ya nivela el volumen de lo que envía, pero estos ajustes del lado de la plataforma importan igual.

## 4. Durante la entrevista

- **Habla cuando el indicador esté en verde.** Rojo = ellos aún te están oyendo; ámbar = traduciendo.
- **Frases cortas**, una o dos oraciones por vez. Las respuestas largas se parten solas, pero el retraso se nota menos con frases cortas.
- **Pulsar para hablar** (marcado en «Yo → ellos»): mantén la tecla mientras hablas y suéltala al terminar. El inglés sale de inmediato y no se cuelan toses ni ruidos.
- **Para ganar tiempo**, usa las frases de relleno (Numpad1…9) mientras piensas.
- **Respuestas largas ya preparadas**: dispáralas con su atajo; suenan con la mejor calidad y sin retraso de traducción.
- **Subtítulos**: lo que ellos dicen aparece en español abajo; el original en inglés se ve en gris si lo dejas marcado.

## 5. Atajos

| Atajo | Qué hace |
|---|---|
| Ctrl+Alt+Numpad1 … Numpad9 | Envía la frase de esa tecla en la **página actual** del banco (necesita Bloq Num activado) |
| Ctrl+Alt+Numpad+ | Cambia de página de atajos: la página 2 son las frases 10 a 18, y así. El botón «Frases» muestra la página (p1/4) |
| Ctrl+Alt+Numpad0 | **Silencio de emergencia**: corta lo que suena y bloquea el envío; vuelve a pulsar para reanudar |
| Ctrl+Alt+Numpad. | Modo compacto / normal |
| Tecla de pulsar para hablar | Elegida en «Yo → ellos» (Ctrl derecho, Alt derecho, F8, F9, F10) |

## 6. Plan B, si algo falla en plena entrevista

1. **Pulsa Silenciar** (Ctrl+Alt+Numpad0): deja de salir cualquier cosa.
2. **Cambia el micrófono de la plataforma a tu micrófono real** y di en inglés una frase sencilla: «Sorry, I'm having a technical issue, could we continue in a moment?».
3. **Usa el chat de la reunión**: en Frases, pulsa **Copiar inglés** y pega la respuesta.
4. Si no se arregla, pide reprogramar con calma. Una falla técnica es normal en cualquier entrevista remota.

## 7. Lo que conviene saber

- **Retraso:** una frase tarda entre 1.5 y 3 s en salir. Un «let me think about that…» a tiempo lo hace natural.
- **Nada se graba:** ni audio ni texto de la reunión. Solo se guardan localmente tus ajustes, tus frases y su audio preparado (carpeta `data/`, fuera de Git).
- **Antes de usar esto en una entrevista**, revisa las condiciones de la empresa: algunas piden confirmar que no usas asistentes de IA, y la traducción en vivo podría caer ahí. También puedes preguntar al reclutador si se permite una herramienta de apoyo de idioma. Esa decisión es tuya.
- **El inglés del trabajo diario** tendrá que salir de ti. Esta herramienta te ayuda a llegar; conviene ir practicando en paralelo.
