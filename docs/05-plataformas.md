# 05 – Mac, Android y otros dispositivos

## Qué se puede y qué no

| Dispositivo | Escuchar (ellos → yo) | Hablar (yo → ellos) | Cómo |
|---|---|---|---|
| Windows | Sí | Sí | Hecho: cable virtual VB-Cable |
| Mac Intel | Sí | Sí | Cable virtual BlackHole; ver arquitectura |
| Android | Subtítulos y control | **No directo** | Android no deja inyectar un micrófono virtual en otra app sin root |

## Arquitectura recomendada: un motor, clientes finos

Hoy la lógica (reconocimiento, traducción, voz) vive dentro de la app de Windows. El plan es separarla:

```
                 ┌────────────────────────────┐
  Mac (cliente)  │  Motor JM-Translate (PC)   │  Android (navegador)
  audio ───────► │  VAD → texto → traducción  │ ◄─── subtítulos, estado,
  ◄─────────────  │  → voz → tu timbre         │      control remoto
  voz traducida   └────────────────────────────┘
```

- **Motor** (el PC con el i7): servicio local con una API de WebSocket (audio de entrada, subtítulos y audio de salida). La app de Windows pasa a ser un cliente más.
- **Cliente Mac**: captura el audio de la reunión y el micrófono, los envía al motor por la red local y reproduce lo que vuelve en un dispositivo virtual (BlackHole). Casi no consume CPU en el Mac.
- **Android**: página web (PWA), sin tienda de aplicaciones. Muestra subtítulos, el indicador "puedes hablar" y botones de control. Opcionalmente, el micrófono del móvil puede ser la entrada de voz que se envía al motor.
- **Fuera de casa:** Tailscale (gratis para uso personal) permite llegar al PC como si estuvieran en la misma red.

## Alternativa: motor local en el Mac

Todo lo que usamos es multiplataforma (sherpa-onnx, CTranslate2, OpenVoice), así que el motor puede correr también en un Mac Intel. La duda es el rendimiento: depende del procesador y la RAM del Mac. Se mide con un script de prueba antes de decidir. Ventaja: sin depender del PC. Desventaja: probablemente más lento y caliente.

## Por qué no un servidor en la nube

Funcionaría, pero con costo mensual (sobre todo con GPU) y enviando el audio de las entrevistas fuera de tu control. Queda como opción futura para una versión comercial.

## Android en llamadas del propio móvil

Si la entrevista es por el móvil (por ejemplo WhatsApp), no hay forma limpia de inyectar la voz traducida. Opciones prácticas: hacer la llamada desde el PC o el Mac, o usar el móvil solo como pantalla de subtítulos mientras hablas por otro dispositivo.

## Fases propuestas

1. Separar el motor de la app de Windows (API local).
2. Página web para Android (subtítulos + indicador + control).
3. Cliente Mac con BlackHole hablando con el motor.
4. Prueba de rendimiento del motor local en el Mac.

## Datos que faltan

- Modelo y memoria del Mac (procesador, RAM).
- Si el PC estará encendido y en la misma red durante las entrevistas.
- Si harás entrevistas desde el móvil.
