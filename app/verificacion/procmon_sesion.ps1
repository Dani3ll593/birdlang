# procmon_sesion.ps1 (en el paquete, carpeta verificacion)
# Inicia y gestiona una sesión de captura con Process Monitor (Sysinternals)
# Monitorea eventos de red y genera registros PML y CSV filtrado para Birdlang.exe.

[CmdletBinding()]
param(
    [int]$DuracionSegundos = 1800,
    [string]$SalidaPml = '',
    [string]$SalidaCsv = '',
    [string]$LogRed = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($SalidaPml)) {
    $SalidaPml = Join-Path $PSScriptRoot 'descargas\procmon\sesion.pml'
}
if ([string]::IsNullOrWhiteSpace($SalidaCsv)) {
    $SalidaCsv = Join-Path $PSScriptRoot 'descargas\procmon\sesion.csv'
}
if ([string]::IsNullOrWhiteSpace($LogRed)) {
    $LogRed = Join-Path $PSScriptRoot 'descargas\procmon\red_muestreo.log'
}

# 0. Verificaciones previas y localización de binarios/configuración
# En el paquete, Birdlang.exe está en la carpeta de arriba; en el código fuente,
# en la carpeta que arma app\armar.ps1.
$candidatosBirdlang = @(
    (Join-Path $PSScriptRoot '..\Birdlang.exe'),
    (Join-Path $PSScriptRoot '..\dist\Birdlang\Birdlang.exe')
)
$exeBirdlang = $candidatosBirdlang | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exeBirdlang) {
    throw "No se encontró Birdlang.exe junto a la carpeta verificacion. En el código fuente, correr antes app\armar.ps1."
}

$configPmc = Join-Path $PSScriptRoot 'procmon_birdlang.pmc'
if (-not (Test-Path $configPmc)) {
    throw "No se encontró el archivo de configuración de filtro 'procmon_birdlang.pmc'."
}

$candidatosProcmon = @(
    (Join-Path $PSScriptRoot 'Procmon64.exe'),
    (Join-Path $PSScriptRoot 'descargas\procmon\Procmon64.exe')
)
$procmonExe = $candidatosProcmon | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $procmonExe) {
    throw "No se encontró Procmon64.exe. Por favor descargue Process Monitor del sitio oficial de Sysinternals (https://learn.microsoft.com/sysinternals/downloads/procmon) y extraiga Procmon64.exe junto a este script."
}

# Preparar directorio de salida
$dirSalida = Split-Path $SalidaPml
if (-not (Test-Path $dirSalida)) {
    New-Item -ItemType Directory -Force -Path $dirSalida | Out-Null
}
Get-ChildItem -Path $dirSalida -Filter "*.pml" -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path $SalidaCsv) { Remove-Item -Force $SalidaCsv }
if (Test-Path $LogRed) { Remove-Item -Force $LogRed }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " SESIÓN DE PROCESS MONITOR - BIRDLANG" -ForegroundColor Cyan
Write-Host " Duración máxima: $DuracionSegundos segundos (~$([math]::Round($DuracionSegundos / 60, 1)) min)" -ForegroundColor Cyan
Write-Host " Filtro PMC: $configPmc" -ForegroundColor Cyan
Write-Host " BackingFile PML: $SalidaPml" -ForegroundColor Cyan
Write-Host " CSV final: $SalidaCsv" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Arrancar Process Monitor con filtro antes de abrir Birdlang (cubre arranque)
Write-Host "[1/4] Iniciando Process Monitor con filtro cargado..." -ForegroundColor Yellow
$proc = Start-Process -FilePath $procmonExe -ArgumentList "/AcceptEula", "/Quiet", "/Minimized", "/LoadConfig", "`"$configPmc`"", "/BackingFile", "`"$SalidaPml`"" -PassThru
Start-Sleep -Seconds 2

if ($proc.HasExited) {
    throw "Process Monitor terminó inmediatamente (código $($proc.ExitCode)). Asegúrese de ejecutar este script con privilegios de Administrador para permitir la carga del controlador de Sysinternals."
}
Write-Host "  Process Monitor en ejecución (PID: $($proc.Id))." -ForegroundColor Green

# 2. Abrir Birdlang para capturar arranque, sesión y cierre
Write-Host "[2/4] Iniciando Birdlang ($exeBirdlang)..." -ForegroundColor Yellow
$procBirdlang = Start-Process -FilePath $exeBirdlang -PassThru
Start-Sleep -Seconds 2

if ($procBirdlang.HasExited) {
    & $procmonExe /Terminate
    throw "Birdlang se cerró inmediatamente tras arrancar."
}
Write-Host "  Birdlang en ejecución (PID: $($procBirdlang.Id))." -ForegroundColor Green
Write-Host "  Pulsa 'Escuchar' en Birdlang para iniciar la sesión de prueba." -ForegroundColor Cyan
Write-Host "  La captura finalizará cuando cierres Birdlang o transcurran $DuracionSegundos s." -ForegroundColor Cyan

# 3. Muestreo de red y espera a que el usuario cierre Birdlang o pasen los 1800 s
Write-Host "[3/4] Monitoreando conexiones de red cada 10 s..." -ForegroundColor Yellow
$inicio = [DateTime]::UtcNow
$finObjetivo = $inicio.AddSeconds($DuracionSegundos)

"Timestamp,BirdlangPID,TCP_Count,UDP_Count,Detalles" | Out-File -FilePath $LogRed -Encoding utf8

$muestra = 0
$cierreUsuario = $false

while ([DateTime]::UtcNow -lt $finObjetivo) {
    if ($procBirdlang.HasExited) {
        $cierreUsuario = $true
        Write-Host "  Birdlang se ha cerrado. Finalizando captura..." -ForegroundColor Cyan
        break
    }

    $muestra++
    $restante = [math]::Max(0, [int]($finObjetivo - [DateTime]::UtcNow).TotalSeconds)
    $ts = [DateTime]::UtcNow.ToString("o")

    $tcp = @(Get-NetTCPConnection -OwningProcess $procBirdlang.Id -ErrorAction SilentlyContinue)
    $udp = @(Get-NetUDPEndpoint -OwningProcess $procBirdlang.Id -ErrorAction SilentlyContinue)
    $linea = "$ts,$($procBirdlang.Id),$($tcp.Count),$($udp.Count),"
    if ($tcp.Count -gt 0) {
        $linea += "TCP: " + (($tcp | ForEach-Object { "$($_.LocalAddress):$($_.LocalPort)->$($_.RemoteAddress):$($_.RemotePort)" }) -join '; ')
    }
    if ($udp.Count -gt 0) {
        $linea += "UDP: " + (($udp | ForEach-Object { "$($_.LocalAddress):$($_.LocalPort)" }) -join '; ')
    }
    $linea | Out-File -FilePath $LogRed -Append -Encoding utf8
    Write-Host "  [$muestra] $ts - PID $($procBirdlang.Id): TCP=$($tcp.Count), UDP=$($udp.Count) (quedan ${restante}s)"

    $dormir = [math]::Min(10, $restante)
    if ($dormir -gt 0) {
        Start-Sleep -Seconds $dormir
    }
}

$duracionRealSegundos = [int]([DateTime]::UtcNow - $inicio).TotalSeconds
$duracionRealMinutos = [math]::Round($duracionRealSegundos / 60.0, 1)

Write-Host "  Duración real registrada: $duracionRealSegundos segundos (~$duracionRealMinutos min)." -ForegroundColor Cyan
if ($cierreUsuario) {
    Write-Host "  (Sesión finalizada por cierre de la aplicación)." -ForegroundColor Green
} else {
    Write-Host "  (Sesión finalizada al alcanzar el tiempo límite de $DuracionSegundos s)." -ForegroundColor Yellow
}
"# DURACION_REAL_SEGUNDOS:$duracionRealSegundos" | Out-File -FilePath $LogRed -Append -Encoding utf8

# Guardar metadatos de la sesión
$sesionInfoPath = Join-Path $dirSalida 'sesion_info.json'
@{
    DuracionSegundosObjetivo = $DuracionSegundos
    DuracionRealSegundos = $duracionRealSegundos
    DuracionRealMinutos = $duracionRealMinutos
    CierreUsuario = $cierreUsuario
    InicioUtc = $inicio.ToString("o")
    FinUtc = [DateTime]::UtcNow.ToString("o")
} | ConvertTo-Json | Set-Content -Path $sesionInfoPath -Encoding utf8

# 4. Detener Process Monitor, exportar con /SaveApplyFilter y borrar .pml
Write-Host "[4/4] Deteniendo captura y exportando CSV filtrado..." -ForegroundColor Yellow
& $procmonExe /Terminate
Start-Sleep -Seconds 3

$espera = 0
while (-not $proc.HasExited -and $espera -lt 15) {
    Start-Sleep -Seconds 1
    $espera++
}

# Exportar con /SaveApplyFilter
if (Test-Path $SalidaPml) {
    Write-Host "  Exportando registro aplicando filtro a: $SalidaCsv..." -ForegroundColor Yellow
    # Procmon no bloquea la consola: sin -Wait se borrarían los .pml con el CSV a medio escribir
    # (exportar 30 min tardó ~4 min).
    Start-Process -FilePath $procmonExe -Wait -ArgumentList '/AcceptEula', '/Quiet', '/Minimized', '/OpenLog', "`"$SalidaPml`"", '/LoadConfig', "`"$configPmc`"", '/SaveApplyFilter', '/SaveAs', "`"$SalidaCsv`""
    if (Test-Path $SalidaCsv) {
        $tamCsv = [math]::Round((Get-Item $SalidaCsv).Length / 1MB, 2)
        Write-Host "  Exportación exitosa: $SalidaCsv ($tamCsv MB)" -ForegroundColor Green

        # Borrar los .pml y dejar solo el CSV filtrado
        Write-Host "  Limpiando archivos binarios .pml..." -ForegroundColor Yellow
        Get-ChildItem -Path $dirSalida -Filter "*.pml" -ErrorAction SilentlyContinue | Remove-Item -Force
        Write-Host "  Archivos .pml eliminados correctamente." -ForegroundColor Green
    } else {
        Write-Host "  Advertencia: No se pudo generar $SalidaCsv tras 60 s." -ForegroundColor DarkYellow
    }
} else {
    Write-Host "  Advertencia: No se encontró el archivo de captura $SalidaPml." -ForegroundColor DarkYellow
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " SESIÓN DE CAPTURA FINALIZADA. Duración real: $duracionRealSegundos s (~$duracionRealMinutos min)" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green