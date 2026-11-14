# Actualizador de Birdlang. Es un programa aparte: Birdlang no tiene código de
# red y nunca se conecta. Este script es lo único que se conecta, solo a las
# versiones publicadas del repositorio en GitHub, y no envía nada de una
# sesión. Se puede borrar de la carpeta: Birdlang funciona igual, sin el aviso
# de actualización.
#
#   -Revisar                     responde "nueva <versión>" o "al dia"
#   -Instalar [-Esperar <pid>]   espera a que Birdlang se cierre, baja la versión
#                                nueva, comprueba su SHA-256, reemplaza los
#                                archivos y vuelve a abrir Birdlang

param([switch]$Revisar, [switch]$Instalar, [int]$Esperar)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repo = 'Dani3ll593/birdlang'
$carpeta = $PSScriptRoot
Add-Type -AssemblyName System.Net.Http, System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Cliente([bool]$redirigir) {
    $manejador = New-Object System.Net.Http.HttpClientHandler
    $manejador.AllowAutoRedirect = $redirigir
    $c = New-Object System.Net.Http.HttpClient($manejador)
    $c.Timeout = [TimeSpan]::FromMinutes(30)
    $c.DefaultRequestHeaders.UserAgent.ParseAdd('birdlang-actualizador')
    $c
}

# version.txt: una "clave valor" por línea.
function Leer([string]$texto) {
    $d = @{}
    foreach ($l in $texto -split "`r?`n") {
        $p = $l.Trim() -split '\s+', 2
        if ($p.Count -eq 2) { $d[$p[0]] = $p[1] }
    }
    $d
}

function UltimaVersion {
    $c = Cliente $true
    $c.DefaultRequestHeaders.Accept.ParseAdd('application/vnd.github+json')
    $c.GetStringAsync("https://api.github.com/repos/$repo/releases/latest").Result | ConvertFrom-Json
}

function Bajar($version, [string]$nombre, [string]$destino) {
    $recurso = $version.assets | Where-Object name -eq $nombre
    if (-not $recurso) { throw "La versión publicada no trae $nombre" }
    Write-Host "Bajando $nombre ($([math]::Round($recurso.size / 1MB)) MB)..."
    $respuesta = (Cliente $true).GetAsync($recurso.browser_download_url, 'ResponseHeadersRead').Result
    if (-not $respuesta.IsSuccessStatusCode) { throw "GitHub respondió $([int]$respuesta.StatusCode) al bajar $nombre" }
    $archivo = [IO.File]::Create($destino)
    try { $respuesta.Content.ReadAsStreamAsync().Result.CopyTo($archivo) } finally { $archivo.Close() }
}

function Comprobar([string]$ruta, [string]$esperado) {
    if ((Get-FileHash $ruta -Algorithm SHA256).Hash -ne $esperado) {
        throw "$(Split-Path $ruta -Leaf) no coincide con su SHA-256: no se instala"
    }
}

$local = Leer (Get-Content (Join-Path $carpeta 'version.txt') -Raw)

if ($Revisar) {
    try {
        $v = (UltimaVersion).tag_name.TrimStart('v')
        if ([version]$v -gt [version]$local['version']) { "nueva $v" } else { 'al dia' }
    } catch { 'error' }
    exit 0
}

if (-not $Instalar) { Write-Host 'Uso: actualizar.ps1 -Revisar | -Instalar'; exit 1 }

$instalando = $false
try {
    if ($Esperar) { Wait-Process -Id $Esperar -ErrorAction SilentlyContinue }
    while (Get-Process Birdlang -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$carpeta\*" }) {
        Read-Host 'Birdlang sigue abierto. Ciérralo y presiona Enter'
    }
    $version = UltimaVersion
    $temporal = Join-Path $env:TEMP 'birdlang-actualizacion'
    if (Test-Path $temporal) { Remove-Item -Recurse -Force $temporal }
    New-Item -ItemType Directory $temporal | Out-Null

    Bajar $version 'version.txt' (Join-Path $temporal 'version.txt')
    $nueva = Leer (Get-Content (Join-Path $temporal 'version.txt') -Raw)
    $programa = Join-Path $temporal 'programa.zip'
    Bajar $version 'programa.zip' $programa
    Comprobar $programa $nueva['programa.zip']
    $conModelos = $nueva['modelos'] -ne $local['modelos']
    if ($conModelos) {
        $modelos = Join-Path $temporal 'modelos.zip'
        Bajar $version 'modelos.zip' $modelos
        Comprobar $modelos $nueva['modelos.zip']
    }

    Write-Host 'Instalando...'
    $instalando = $true
    [IO.Compression.ZipFile]::ExtractToDirectory($programa, (Join-Path $temporal 'programa'))
    # version.txt va al final, solo si todo salió bien.
    Remove-Item -Force (Join-Path $temporal 'programa\version.txt') -ErrorAction SilentlyContinue
    Copy-Item -Recurse -Force (Join-Path $temporal 'programa\*') $carpeta
    if ($conModelos) {
        [IO.Compression.ZipFile]::ExtractToDirectory($modelos, (Join-Path $temporal 'nuevos'))
        $viejos = Join-Path $carpeta 'modelos'
        if (Test-Path $viejos) { Remove-Item -Recurse -Force $viejos }
        Move-Item (Join-Path $temporal 'nuevos\modelos') $carpeta
    }
    Copy-Item -Force (Join-Path $temporal 'version.txt') $carpeta
    Remove-Item -Recurse -Force $temporal
    Write-Host "Listo: Birdlang $($nueva['version'])."
    Start-Process (Join-Path $carpeta 'Birdlang.exe')
} catch {
    Write-Host "No se pudo actualizar: $($_.Exception.Message)" -ForegroundColor Red
    if ($instalando) { Write-Host 'La instalación quedó a medias. Vuelve a abrir Birdlang y presiona "Actualizar ahora".' }
    else { Write-Host 'Birdlang quedó en la versión anterior.' }
    Read-Host 'Enter para cerrar'
}
