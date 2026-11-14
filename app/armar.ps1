# Arma la carpeta portable de Birdlang en app\dist\Birdlang (o app\dist\Birdlang-Empresas).
# Se corre en la PC donde se compila o en la compilación automática de GitHub.
# Los modelos y la DLL de Moonshine los baja descargar.ps1 a app\descargas.

[CmdletBinding()]
param(
    [switch]$Empresas
)

$ErrorActionPreference = 'Stop'
$descargas = Join-Path $PSScriptRoot 'descargas'

if ($Empresas) {
    $dist = Join-Path $PSScriptRoot 'dist\Birdlang-Empresas'
} else {
    $dist = Join-Path $PSScriptRoot 'dist\Birdlang'
}

# Se arma desde cero: una carpeta ya usada puede traer archivos sueltos (el
# preferencias.txt de una prueba, algo copiado a mano) que irían en el paquete.
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }

& (Join-Path $PSScriptRoot 'descargar.ps1')

dotnet publish (Join-Path $PSScriptRoot 'Birdlang') -c Release -o $dist -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación' }

Copy-Item -Recurse -Force (Join-Path $descargas 'nativos') $dist
$modelos = Join-Path $dist 'modelos'
New-Item -ItemType Directory -Force $modelos | Out-Null
foreach ($m in 'moonshine', 'lid', 'silero_vad.onnx') {
    Copy-Item -Recurse -Force (Join-Path $descargas "modelos\$m") $modelos
}
# Moonshine y su ONNX Runtime necesitan el runtime de Visual C++, que el equipo
# puede no tener. Se copia junto al programa (despliegue local, permitido por
# Microsoft en el REDIST de Visual Studio) y también junto a esas DLL, que se
# cargan con su ruta completa: así las encuentra con cualquier orden de búsqueda
# de Windows. Se obtienen del paquete oficial VC\Redist\MSVC (Visual Studio / Build Tools).
# Se toma la versión más nueva entre los redistribuibles instalados (Visual
# Studio o Build Tools, como en la compilación automática). Nunca de System32:
# esas copias no vienen con permiso de redistribución.
# ONNX Runtime 1.23 no arranca con la 14.29 de VS 2019 (error 1114); con 14.40
# o más funciona, y por eso se exige 14.40.
$dllsVC = 'msvcp140.dll', 'msvcp140_1.dll', 'vcruntime140.dll', 'vcruntime140_1.dll'
$candidatas = @(Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT",
        "${env:ProgramFiles}\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64\Microsoft.VC*.CRT" -Directory -ErrorAction SilentlyContinue |
    ForEach-Object FullName)
$crtDir = $candidatas | Where-Object { Test-Path (Join-Path $_ 'msvcp140.dll') } |
    Sort-Object { (Get-Item (Join-Path $_ 'msvcp140.dll')).VersionInfo.FileVersionRaw } | Select-Object -Last 1
$versionVC = if ($crtDir) { (Get-Item (Join-Path $crtDir 'msvcp140.dll')).VersionInfo.FileVersionRaw } else { [version]'0.0' }
if ($versionVC -lt [version]'14.40') {
    throw "El runtime de Visual C++ más nuevo encontrado es $versionVC ($crtDir); ONNX Runtime necesita 14.40 o más. Instalar Build Tools: winget install Microsoft.VisualStudio.BuildTools (con VC.Tools.x86.x64)."
}
Write-Host "Copiando runtime de Visual C++ $versionVC desde: $crtDir"
foreach ($d in $dllsVC) {
    Copy-Item -Force (Join-Path $crtDir $d) $dist
    Copy-Item -Force (Join-Path $crtDir $d) (Join-Path $dist 'nativos\moonshine')
}

# Sin el generador de volcados de .NET: un volcado tendría subtítulos y notas
# (README §4).
Remove-Item -Force (Join-Path $dist 'createdump.exe')

# Ensamblados de diseñador y librerías de red nativas no utilizadas
foreach ($d in 'Avalonia.DesignerSupport.dll', 'Avalonia.Remote.Protocol.dll', 'msquic.dll') {
    $f = Join-Path $dist $d
    if (Test-Path $f) { Remove-Item -Force $f }
}

$datos = Join-Path $dist 'datos'
New-Item -ItemType Directory -Force $datos | Out-Null
foreach ($f in 'calles.txt', 'codigos_postales.tsv', 'glosario.tsv', 'ATRIBUCIONES.txt') {
    $origen = Join-Path $PSScriptRoot "Birdlang\datos\$f"
    if (Test-Path $origen) { Copy-Item -Force $origen $datos }
}

$medios = Join-Path $dist 'medios'
New-Item -ItemType Directory -Force $medios | Out-Null
foreach ($f in 'birdlang.ico', 'logo.png') {
    $origen = Join-Path $PSScriptRoot "Birdlang\medios\$f"
    if (Test-Path $origen) { Copy-Item -Force $origen $medios }
}

# Documentación legal y avisos de terceros en la raíz de la distribución portable
$raizRepo = Join-Path $PSScriptRoot '..'
foreach ($f in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'PRIVACY.md', 'SEGURIDAD_EMPRESAS.md') {
    $origen = Join-Path $raizRepo $f
    if (Test-Path $origen) { Copy-Item -Force $origen $dist }
}
$licenciasOrigen = Join-Path $raizRepo 'licencias'
if (Test-Path $licenciasOrigen) {
    # Si la carpeta ya existe, Copy-Item la copiaría dentro (licencias\licencias)
    $licenciasDist = Join-Path $dist 'licencias'
    if (Test-Path $licenciasDist) { Remove-Item -Recurse -Force $licenciasDist }
    Copy-Item -Recurse -Force $licenciasOrigen $licenciasDist
}

# Scripts de verificación y auditoría para que TI pueda comprobar la app
$verifDir = Join-Path $dist 'verificacion'
New-Item -ItemType Directory -Force $verifDir | Out-Null
Copy-Item -Force (Join-Path $PSScriptRoot 'verificar_sin_red.ps1') $verifDir
foreach ($f in 'procmon_sesion.ps1', 'procmon_analizar.ps1', 'procmon_birdlang.pmc') {
    Copy-Item -Force (Join-Path $PSScriptRoot "verificacion\$f") $verifDir
}

# El actualizador aparte (la versión para empresas va sin él) y version.txt:
# la versión del programa y una huella de los modelos, para que el
# actualizador baje los modelos solo cuando cambian.
if (-not $Empresas) {
    Copy-Item -Force (Join-Path $PSScriptRoot 'actualizar.ps1') $dist
} else {
    # En la versión para empresas no puede haber ningún actualizar.ps1 ni .ps1 fuera de verificacion/
    $actDist = Join-Path $dist 'actualizar.ps1'
    if (Test-Path $actDist) { Remove-Item -Force $actDist }
    Get-ChildItem -Path $dist -Filter "*.ps1" -File | Remove-Item -Force
}

$version = dotnet msbuild (Join-Path $PSScriptRoot 'Birdlang\Birdlang.csproj') -getProperty:Version
$lineas = Get-ChildItem -Recurse -File $modelos | Sort-Object FullName |
    ForEach-Object { '{0} {1}' -f $_.FullName.Substring($modelos.Length), (Get-FileHash $_.FullName).Hash }
$sha = [Security.Cryptography.SHA256]::Create()
$huella = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($lineas -join "`n")) | ForEach-Object { $_.ToString('x2') })
Set-Content -Encoding ascii (Join-Path $dist 'version.txt') "version $version", "modelos $($huella.Substring(0, 16))"

$mb = (Get-ChildItem -Recurse $dist | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Listo: {0} ({1:0} MB)" -f $dist, $mb) -ForegroundColor Cyan

# Verificación de cero red al final del armado. Si falla, corta con throw.
& (Join-Path $PSScriptRoot 'verificar_sin_red.ps1') -Carpeta $dist -Empresas:$Empresas
