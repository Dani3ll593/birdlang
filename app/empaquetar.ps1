# Arma los archivos de una versión en app\dist\publicar, a partir de las
# carpetas ya armadas (armar.ps1 y armar.ps1 -Empresas). Lo corre la
# compilación automática de GitHub (.github/workflows/compilar.yml), que
# publica la versión al subir una etiqueta v<versión>.
#
#   Birdlang-<versión>.zip            la carpeta completa, para la primera instalación
#   Birdlang-Empresas-<versión>.zip   la versión para empresas (sin actualizador)
#   programa.zip, modelos.zip         lo que baja el actualizador (los modelos, solo si cambian)
#   version.txt                       versión, huella de los modelos y SHA-256 de los dos zip anteriores
#   SHA256SUMS.txt                    el SHA-256 de todos los archivos

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$dist = Join-Path $PSScriptRoot 'dist\Birdlang'
$empresas = Join-Path $PSScriptRoot 'dist\Birdlang-Empresas'
$salida = Join-Path $PSScriptRoot 'dist\publicar'
foreach ($c in $dist, $empresas) {
    if (-not (Test-Path (Join-Path $c 'Birdlang.exe'))) { throw "Falta ${c}: correr antes armar.ps1 y armar.ps1 -Empresas" }
    # Si alguien abrió Birdlang desde dist y pulsó «No volver a mostrar», el aviso
    # quedaría apagado para todos los que bajen esta versión.
    if (Test-Path (Join-Path $c 'preferencias.txt')) { throw "${c} tiene preferencias.txt de una prueba: volver a correr armar.ps1" }
}

$info = @{}
foreach ($l in Get-Content (Join-Path $dist 'version.txt')) { $p = $l -split ' ', 2; $info[$p[0]] = $p[1] }
$version = $info.version

if (Test-Path $salida) { Remove-Item -Recurse -Force $salida }
New-Item -ItemType Directory $salida | Out-Null

[IO.Compression.ZipFile]::CreateFromDirectory($dist, (Join-Path $salida "Birdlang-$version.zip"), 'Optimal', $true)
[IO.Compression.ZipFile]::CreateFromDirectory($empresas, (Join-Path $salida "Birdlang-Empresas-$version.zip"), 'Optimal', $true)

$programa = Join-Path $salida 'programa'
New-Item -ItemType Directory $programa | Out-Null
Get-ChildItem $dist | Where-Object Name -ne 'modelos' | Copy-Item -Recurse -Destination $programa
[IO.Compression.ZipFile]::CreateFromDirectory($programa, (Join-Path $salida 'programa.zip'))
Remove-Item -Recurse -Force $programa
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $dist 'modelos'), (Join-Path $salida 'modelos.zip'), 'Optimal', $true)

$archivoVersion = Join-Path $salida 'version.txt'
Copy-Item (Join-Path $dist 'version.txt') $archivoVersion
foreach ($z in 'programa.zip', 'modelos.zip') {
    Add-Content -Encoding ascii $archivoVersion "$z $((Get-FileHash (Join-Path $salida $z) -Algorithm SHA256).Hash)"
}

$sumas = Get-ChildItem $salida -File | Sort-Object Name |
    ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -Encoding ascii (Join-Path $salida 'SHA256SUMS.txt') $sumas

Get-ChildItem $salida | ForEach-Object { '{0,-36} {1,8:0} MB' -f $_.Name, ($_.Length / 1MB) }
Write-Host "Listo: Birdlang $version en $salida" -ForegroundColor Cyan
