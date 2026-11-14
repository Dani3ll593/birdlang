# Baja a app\descargas los modelos y la DLL de Moonshine que usa Birdlang. Lo
# llama armar.ps1: solo se corre al compilar, y Birdlang recibe la carpeta ya
# armada y no se conecta a nada. Si un archivo ya está, no se vuelve a bajar.

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$raiz = Join-Path $PSScriptRoot 'descargas'

function Bajar($url, $destino) {
    if (Test-Path $destino) { return }
    New-Item -ItemType Directory -Force (Split-Path $destino) | Out-Null
    Write-Host "Bajando $url"
    Invoke-WebRequest -UseBasicParsing $url -OutFile "$destino.parcial"
    Move-Item "$destino.parcial" $destino
}

function SacarDeZip($zip, $patron, $carpeta) {
    New-Item -ItemType Directory -Force $carpeta | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($e in $z.Entries | Where-Object { $_.FullName -like $patron }) {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $carpeta $e.Name), $true)
        }
    } finally { $z.Dispose() }
}

# --- Moonshine 0.1.5: la DLL viene solo en el paquete de Python ---
$rueda = Join-Path $raiz 'paquetes\moonshine_voice-0.1.5-py3-none-win_amd64.whl'
if (-not (Test-Path $rueda)) {
    $info = Invoke-RestMethod 'https://pypi.org/pypi/moonshine-voice/0.1.5/json'
    $url = ($info.urls | Where-Object { $_.filename -like '*win_amd64.whl' }).url
    Bajar $url $rueda
}
SacarDeZip $rueda 'moonshine_voice/*.dll' (Join-Path $raiz 'nativos\moonshine')

# --- Modelos de Moonshine Streaming ---
$archivos = 'adapter.ort', 'cross_kv.ort', 'decoder_kv.ort', 'encoder.ort',
            'frontend.model.ort', 'frontend.weights.ort', 'streaming_config.json', 'tokenizer.bin'
$modelos = @{
    'tiny-streaming-en'  = 'quantized_26_08_21'
    'small-streaming-en' = 'quantized_26_08_21'
    'tiny-streaming-es'  = 'quantized_26_08_24'
    'small-streaming-es' = 'quantized_26_08_24'
}
foreach ($m in $modelos.Keys) {
    foreach ($a in $archivos) {
        Bajar "https://download.moonshine.ai/model/$m/$($modelos[$m])/$a" (Join-Path $raiz "modelos\moonshine\$m\$a")
    }
}

# --- Detector de voz (Silero VAD v6, MIT) ---
Bajar 'https://github.com/snakers4/silero-vad/raw/v6.2.3/src/silero_vad/data/silero_vad.onnx' (Join-Path $raiz 'modelos\silero_vad.onnx')

# --- Identificador de idioma (VoxLingua107 ECAPA, Apache-2.0, exportado a ONNX int8) ---
$lid = 'https://huggingface.co/krut42/voice-lid-ecapa-voxlingua107/resolve/main'
Bajar "$lid/lid-ecapa-voxlingua107.int8.onnx" (Join-Path $raiz 'modelos\lid\lid-ecapa-voxlingua107.int8.onnx')
Bajar "$lid/lid-labels.txt" (Join-Path $raiz 'modelos\lid\lid-labels.txt')

Write-Host 'Listo.'
