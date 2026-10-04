# 06 – Instalación desde cero

El repositorio solo trae el código. Los modelos (~4 GB) y el entorno de Python se descargan siguiendo esta guía. Todo corre en local; no hace falta cuenta ni pago.

## Requisitos

- Windows 11 y .NET SDK 10.
- [VB-Cable](https://vb-audio.com/Cable/) (micrófono virtual, gratis para uso personal).
- Git y unos 7 GB libres.

## 1. Python portable (dentro del proyecto)

Desde `tools/`, en PowerShell:

```powershell
# uv (gestor de Python portable)
Invoke-WebRequest https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip -OutFile uv.zip
Expand-Archive uv.zip -DestinationPath uv; Remove-Item uv.zip

$env:UV_CACHE_DIR = "$PWD\.uvcache"; $env:UV_PYTHON_INSTALL_DIR = "$PWD\python"
.\uv\uv.exe venv --python 3.12 .venv
.\uv\uv.exe pip install --python .venv\Scripts\python.exe ctranslate2 sentencepiece huggingface_hub `
    librosa soundfile pydub inflect unidecode eng_to_ipa wavmark pypinyin cn2an jieba langid sherpa-onnx
.\uv\uv.exe pip install --python .venv\Scripts\python.exe torch transformers `
    --index-url https://download.pytorch.org/whl/cpu --extra-index-url https://pypi.org/simple
```

## 2. OpenVoice (cambio de timbre)

```powershell
git clone --depth 1 https://github.com/myshell-ai/OpenVoice.git openvoice
git -C openvoice apply ..\tools\patches\openvoice-enable-watermark.patch   # permite desactivar la marca de agua
```

## 3. Modelos (carpeta `models/`)

Todos salvo los de traducción vienen de los lanzamientos de [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx/releases). Se extraen con `tar xjf <archivo>` dentro de `models/`.

| Modelo | Origen | Carpeta resultante |
|---|---|---|
| Detector de voz | `asr-models/silero_vad.onnx` | `silero_vad.onnx` |
| Reconocimiento (Parakeet v3) | `asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2` | `sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/` |
| Voz en español | `tts-models/vits-piper-es_MX-ald-medium.tar.bz2` | `vits-piper-es_MX-ald-medium/` |
| Voces en inglés (Piper) | `tts-models/vits-piper-en_US-john-medium.tar.bz2` y las que quieras (`ryan-medium`, `joe-medium`, `hfc_male-medium`, `lessac-medium`) | `vits-piper-en_US-*/` |
| Voz en inglés natural (opcional) | `tts-models/kokoro-multi-lang-v1_0.tar.bz2` | `kokoro-multi-lang-v1_0/` |
| Cambio de timbre | Hugging Face `myshell-ai/OpenVoiceV2` | `openvoice-v2/` |

Traducción (se convierte a CTranslate2 int8, desde `tools/`):

```powershell
$env:HF_HUB_DISABLE_SYMLINKS_WARNING = "1"
.\.venv\Scripts\ct2-transformers-converter.exe --model Helsinki-NLP/opus-mt-tc-big-en-es --output_dir ..\models\opus-big-en-es --quantization int8
.\.venv\Scripts\ct2-transformers-converter.exe --model Helsinki-NLP/opus-mt-es-en        --output_dir ..\models\opus-es-en     --quantization int8
```

Descarga de OpenVoice v2:

```powershell
.\.venv\Scripts\python.exe -c "from huggingface_hub import snapshot_download; snapshot_download('myshell-ai/OpenVoiceV2', local_dir='../models/openvoice-v2')"
```

## 4. Tu voz (opcional)

Para usar "Mi voz", graba 20–30 s hablando natural, en silencio, y guárdala como `models/mi-voz.wav` (también sirve mp3, m4a, ogg o flac). **Este archivo está excluido del repositorio** a propósito: es un dato personal.

## 5. Compilar y ejecutar

```powershell
cd src\JmTranslate.App
dotnet build -c Release
..\..\Iniciar-JM-Translate.cmd
```

Autopruebas sin interfaz:

```powershell
JmTranslate.App.exe --selftest <audio.wav> <informe.txt> [es]   # pipeline completo
JmTranslate.App.exe --cleantest <informe.txt>                   # limpieza de texto
```

## Qué no se sube a Git

`models/`, `tools/.venv`, `tools/python`, `tools/uv`, `tools/openvoice`, `scratch/`, `bin/` y `obj/`, además de cualquier `models/mi-voz.*`. Ver `.gitignore`.
