# 01 – Alcance

## Contexto

El autor es analista programador senior y arquitecto de soluciones, hablante nativo de español. Postula a empresas cuyas entrevistas (y luego el trabajo diario) son en inglés. Necesita comunicarse sin que la barrera del idioma sea un problema, y sin pedir nada a los interlocutores.

## Objetivo

Herramienta personal que traduce en vivo, en ambas direcciones, la conversación de cualquier aplicación de videollamada, de forma transparente para la otra parte.

## Requisitos funcionales

| ID | Requisito | Prioridad |
|---|---|---|
| RF1 | Capturar el audio de la reunión (lo que dicen ellos) desde el sistema | MVP |
| RF2 | Mostrar lo que dicen en **subtítulos en español** | MVP |
| RF3 | Reproducir lo que dicen en **voz en español** por mis audífonos | MVP |
| RF4 | Capturar mi micrófono, traducir a inglés y enviarlo a un **micrófono virtual** que la app de reunión usa como entrada | MVP |
| RF5 | Voz en inglés **sintética** (neutral) | MVP |
| RF6 | Subtítulos y voz activables de forma independiente | MVP |
| RF7 | Glosario técnico (arquitectura, cloud, .NET, etc.) para mejorar términos | Fase 2 |
| RF8 | Voz en inglés **con mi propia voz** (clonación) | Fase 3 |
| RF9 | Mac (Intel) | Fase 4 |
| RF10 | Android (solo como visor de subtítulos/control) | Fase 5 |

## Requisitos no funcionales

- **Costo cero:** sin APIs de pago, todo corre en esta máquina.
- **Privacidad:** el audio se procesa en memoria; **no se guarda** audio ni transcripciones.
- **Independiente de la plataforma de reunión:** funciona con Meet, Teams, Zoom y cualquier otra, sin configuración del otro lado.
- **Latencia objetivo:** ≤ 2–3 s entre que alguien termina una frase y se oye/lee su traducción.
- **Hardware disponible:** i7-12700KF (12 núcleos), GPU GT 730 (**inútil para IA**, por eso todo va en CPU), Windows 11.

## Dentro del alcance (MVP)

- Windows 11, Español ↔ Inglés.
- Captura de audio del sistema y del micrófono, traducción local, subtítulos, voz sintética, micrófono virtual.
- Uso personal, un solo usuario.

## Fuera del alcance (por ahora)

- Mac, Android, otros idiomas.
- Clonación de voz (requiere GPU o servicio de pago).
- Grabación, historial o almacenamiento de cualquier tipo.
- Comercialización, cuentas, licencias, instalador pulido.
- Interfaz elaborada: el MVP es una ventana mínima de control y subtítulos.

## Consideraciones

- **Transparencia:** la traducción con voz tiene retraso y pausas perceptibles. Es difícil que sea 100 % indetectable. Conviene manejar las pausas con naturalidad.
- **Políticas de empresas:** algunas prohíben herramientas de IA en entrevistas. Es una decisión del usuario; no afecta la técnica pero sí el riesgo.
- **Audífonos obligatorios:** si no, el micrófono recoge la voz traducida y se genera un bucle.
