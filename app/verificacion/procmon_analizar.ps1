# procmon_analizar.ps1 (en el paquete, carpeta verificacion)
# Analiza el archivo CSV exportado de Process Monitor y el log de muestreo de red.
# Filtra por Birdlang.exe y sus procesos hijos.
# Cuenta eventos TCP/UDP, escrituras de archivo, escrituras de registro y lecturas de pagefile.sys.

[CmdletBinding()]
param(
    [string]$RutaCsv = '',
    [string]$LogRed = '',
    [string]$InformeMd = '',
    [string]$Version = '0.1.1',
    [string]$Duracion = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RutaCsv)) {
    $RutaCsv = Join-Path $PSScriptRoot 'descargas\procmon\sesion.csv'
}
if ([string]::IsNullOrWhiteSpace($LogRed)) {
    $LogRed = Join-Path $PSScriptRoot 'descargas\procmon\red_muestreo.log'
}
if ([string]::IsNullOrWhiteSpace($InformeMd)) {
    $InformeMd = Join-Path $PSScriptRoot 'descargas\procmon\informe.md'
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " ANÁLISIS DE REGISTRO PROCESS MONITOR" -ForegroundColor Cyan
Write-Host " Archivo CSV: $RutaCsv" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $RutaCsv)) {
    throw "No se encontró el archivo CSV en '$RutaCsv'."
}

# Lector rápido en C# para procesar decenas de miles de eventos en milisegundos
if (-not ([System.Management.Automation.PSTypeName]'FastProcmonReader').Type) {
    $csharpSource = @"
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

public class ProcmonItem {
    public string TimeOfDay;
    public string ProcessName;
    public int Pid;
    public string Operation;
    public string Path;
    public string Result;
    public string Detail;
}

public static class FastProcmonReader {
    public static List<ProcmonItem> ReadAll(string filePath) {
        var list = new List<ProcmonItem>();
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(fs, Encoding.UTF8)) {
            string header = reader.ReadLine();
            if (header == null) return list;
            
            string[] headerCols = SplitRow(header);
            int colTime = Array.IndexOf(headerCols, "Time of Day");
            int colProcess = Array.IndexOf(headerCols, "Process Name");
            int colPid = Array.IndexOf(headerCols, "PID");
            int colOp = Array.IndexOf(headerCols, "Operation");
            int colPath = Array.IndexOf(headerCols, "Path");
            int colResult = Array.IndexOf(headerCols, "Result");
            int colDetail = Array.IndexOf(headerCols, "Detail");
            
            if (colTime < 0) colTime = 0;
            if (colProcess < 0) colProcess = 1;
            if (colPid < 0) colPid = 2;
            if (colOp < 0) colOp = 3;
            if (colPath < 0) colPath = 4;
            if (colResult < 0) colResult = 5;
            if (colDetail < 0) colDetail = 6;
            
            string line;
            while ((line = reader.ReadLine()) != null) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] parts = SplitRow(line);
                if (parts.Length <= colPath) continue;
                
                var item = new ProcmonItem();
                item.TimeOfDay = colTime < parts.Length ? parts[colTime] : "";
                item.ProcessName = colProcess < parts.Length ? parts[colProcess] : "";
                int p;
                int.TryParse(colPid < parts.Length ? parts[colPid] : "0", out p);
                item.Pid = p;
                item.Operation = colOp < parts.Length ? parts[colOp] : "";
                item.Path = colPath < parts.Length ? parts[colPath] : "";
                item.Result = colResult < parts.Length ? parts[colResult] : "";
                item.Detail = colDetail < parts.Length ? parts[colDetail] : "";
                list.Add(item);
            }
        }
        return list;
    }
    
    private static string[] SplitRow(string line) {
        var res = new List<string>();
        bool inQuotes = false;
        var sb = new StringBuilder();
        for (int i = 0; i < line.Length; i++) {
            char c = line[i];
            if (c == '"') {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') {
                    sb.Append('"');
                    i++;
                } else {
                    inQuotes = !inQuotes;
                }
            } else if (c == ',' && !inQuotes) {
                res.Add(sb.ToString().Trim(' ', '"'));
                sb.Length = 0;
            } else {
                sb.Append(c);
            }
        }
        res.Add(sb.ToString().Trim(' ', '"'));
        return res.ToArray();
    }
}
"@
    Add-Type -TypeDefinition $csharpSource
}

Write-Host "Leyendo y filtrando eventos..." -ForegroundColor Yellow
$items = [FastProcmonReader]::ReadAll($RutaCsv)

# Cualquier perfil (C:\Users\<nombre>), no solo el de este equipo: la captura
# puede venir de otra máquina y el informe no debe llevar nombres de usuario.
function Anonimizar([string]$ruta) { $ruta -replace '^[A-Za-z]:\\Users\\[^\\]+', '%USERPROFILE%' }

$totalEventosBirdlang = 0
$eventosTcp = 0
$eventosUdp = 0
$hijosDetectados = [System.Collections.Generic.HashSet[int]]::new()
$pidsBirdlang = [System.Collections.Generic.HashSet[int]]::new()

$escriturasArchivo = [System.Collections.Generic.List[PSCustomObject]]::new()
$escriturasRegistro = [System.Collections.Generic.List[PSCustomObject]]::new()
$lecturasPagefile = [System.Collections.Generic.List[PSCustomObject]]::new()

$primeraHora = $null
$carpetaBirdlang = $null
$ultimaHora = $null

foreach ($it in $items) {
    $pName = $it.ProcessName.Trim()
    $esBirdlang = ($pName -eq "Birdlang.exe")
    if ($esBirdlang) {
        $pidsBirdlang.Add($it.Pid) | Out-Null
    } elseif (-not $hijosDetectados.Contains($it.Pid)) {
        continue
    }

    $totalEventosBirdlang++
    if ($null -eq $primeraHora -and -not [string]::IsNullOrWhiteSpace($it.TimeOfDay)) {
        $primeraHora = $it.TimeOfDay
    }
    if (-not [string]::IsNullOrWhiteSpace($it.TimeOfDay)) {
        $ultimaHora = $it.TimeOfDay
    }

    $op = $it.Operation.Trim()
    $path = $it.Path.Trim()
    $result = $it.Result.Trim()
    $detail = $it.Detail.Trim()

    # La carpeta desde la que corrió Birdlang (sale del evento de arranque). Las
    # comillas solo aparecen si la ruta tiene espacios. Se anonimiza igual que las
    # rutas de las escrituras, para poder compararlas.
    if ($esBirdlang -and $op -eq "Process Start" -and $detail -match 'Command line: "?(.*?[\\/])Birdlang\.exe') {
        $carpetaBirdlang = Anonimizar $matches[1]
    }

    # Detectar creación de procesos hijos
    if ($op -eq "Process Create") {
        if ($detail -match 'PID:\s*(\d+)') {
            $hijoPid = [int]$matches[1]
            $hijosDetectados.Add($hijoPid) | Out-Null
            Write-Host "  ALERTA: Proceso hijo detectado PID: $hijoPid" -ForegroundColor Red
        }
    }

    # Red TCP / UDP
    if ($op -like "*TCP*" -or $op -like "*UDP*") {
        if ($op -like "*TCP*") { $eventosTcp++ }
        if ($op -like "*UDP*") { $eventosUdp++ }
    }

    # Lecturas de pagefile.sys
    if ($path -like "*pagefile.sys*") {
        $lecturasPagefile.Add([PSCustomObject]@{
            Hora = $it.TimeOfDay
            Operacion = $op
            Resultado = $result
            Detalle = $detail
        })
    }

    # Escrituras de archivo
    $esEscrituraArchivo = $false
    if ($op -in 'WriteFile', 'SetRenameInformationFile', 'SetDispositionInformationFile', 'SetEndOfFileInformationFile') {
        $esEscrituraArchivo = $true
    } elseif ($op -eq 'CreateFile') {
        if ($detail -match '(?i)(Generic Write|WriteData|Create|Overwrite|OpenIf|OverwriteIf)') {
            $esEscrituraArchivo = $true
        }
    }

    if ($esEscrituraArchivo) {
        $pathAnon = Anonimizar $path
        $escriturasArchivo.Add([PSCustomObject]@{
            Hora = $it.TimeOfDay
            Operacion = $op
            Ruta = $pathAnon
            Resultado = $result
            Detalle = $detail
        })
    }

    # Escrituras de registro
    if ($op -in 'RegSetValue', 'RegCreateKey', 'RegDeleteValue') {
        $tipo = ""
        if ($detail -match 'Type:\s*([^,]+)') {
            $tipo = $matches[1].Trim()
            if ($detail -match 'Length:\s*([^,]+)') {
                $tipo += " ($($matches[1].Trim()) bytes)"
            }
        } else {
            $tipo = $op
        }

        $pathAnon = Anonimizar $path
        $escriturasRegistro.Add([PSCustomObject]@{
            Hora = $it.TimeOfDay
            Operacion = $op
            Ruta = $pathAnon
            Tipo = $tipo
            Resultado = $result
            Detalle = $detail
        })
    }
}

Write-Host "Lectura completada. Eventos totales de Birdlang: $totalEventosBirdlang" -ForegroundColor Green

if ($totalEventosBirdlang -eq 0) {
    throw "La sesión no registró a Birdlang."
}

# Determinar la duración real de la sesión
$duracionRealSegundos = $null
$cierreAnticipado = $false

# 1. Buscar metadatos sesion_info.json en el directorio del CSV
$dirCsv = Split-Path $RutaCsv
$infoSesion = Join-Path $dirCsv 'sesion_info.json'
if (Test-Path $infoSesion) {
    try {
        $meta = Get-Content $infoSesion -Raw | ConvertFrom-Json
        if ($meta.DuracionRealSegundos) {
            $duracionRealSegundos = [int]$meta.DuracionRealSegundos
            $cierreAnticipado = [bool]$meta.CierreUsuario -or [bool]$meta.CierreAnticipado
        }
    } catch { }
}

# 2. Buscar en $LogRed si no se obtuvo de sesion_info.json
if ($null -eq $duracionRealSegundos -and (Test-Path $LogRed)) {
    $lineaDur = Get-Content $LogRed | Where-Object { $_ -match '^#\s*DURACION_REAL_SEGUNDOS:(\d+)' } | Select-Object -Last 1
    if ($lineaDur -match '^#\s*DURACION_REAL_SEGUNDOS:(\d+)') {
        $duracionRealSegundos = [int]$matches[1]
    }
}

# 3. Calcular a partir de marcas de tiempo en el CSV si no hay archivo de metadatos
if ($null -eq $duracionRealSegundos -and $primeraHora -and $ultimaHora) {
    try {
        $t1 = [TimeSpan]::Parse($primeraHora.Split(',')[0])
        $t2 = [TimeSpan]::Parse($ultimaHora.Split(',')[0])
        $dif = ($t2 - $t1).TotalSeconds
        if ($dif -gt 0) {
            $duracionRealSegundos = [int]$dif
        }
    } catch { }
}

$textoDuracion = if ($duracionRealSegundos) {
    $min = [math]::Round($duracionRealSegundos / 60.0, 1)
    if ($cierreAnticipado) {
        "$min min ($duracionRealSegundos s, sesión finalizada por cierre de la aplicación)"
    } else {
        "$min min ($duracionRealSegundos s)"
    }
} elseif (-not [string]::IsNullOrWhiteSpace($Duracion)) {
    $Duracion
} else {
    "No especificada"
}

# Resumen de red por muestreo
$muestreoTcpMax = 0
$muestreoUdpMax = 0
$muestreoLineas = 0
if (Test-Path $LogRed) {
    $lineasRed = Get-Content $LogRed
    foreach ($lr in $lineasRed | Select-Object -Skip 1) {
        $partes = $lr.Split(',')
        if ($partes.Count -ge 4) {
            $muestreoLineas++
            $t = [int]$partes[2]
            $u = [int]$partes[3]
            if ($t -gt $muestreoTcpMax) { $muestreoTcpMax = $t }
            if ($u -gt $muestreoUdpMax) { $muestreoUdpMax = $u }
        }
    }
}

# Clasificación de escrituras de archivo
function Clasificar-RutaArchivo([string]$r) {
    # Primero: en la carpeta del programa solo puede escribirse preferencias.txt
    # («No volver a mostrar» del aviso de colaboración, versión Estándar).
    if ($carpetaBirdlang -and $r -ieq (Join-Path $carpetaBirdlang 'preferencias.txt')) {
        return @{ Categoria = 'Preferencia del aviso de colaboración'; Explicacion = 'preferencias.txt: solo debe decir colaborar=no (comprobarlo abriendo el archivo).' }
    }
    if (($carpetaBirdlang -and $r.StartsWith($carpetaBirdlang, [StringComparison]::OrdinalIgnoreCase)) -or $r -like '*\dist\Birdlang*') {
        return @{ Categoria = 'REVISAR: carpeta de Birdlang'; Explicacion = 'Birdlang no debe escribir en su carpeta: revisar a mano qué se escribió.' }
    }
    if ($r -like '*\AppData\Local\D3DSCache*') {
        return @{ Categoria = 'Caché de sombreadores de DirectX'; Explicacion = 'Windows guarda ahí código gráfico compilado de la tarjeta de video, nada de la sesión.' }
    }
    if ($r -like '*\Windows\Prefetch\*') {
        return @{ Categoria = 'Prefetch de Windows'; Explicacion = 'Registro del sistema operativo sobre inicio de ejecutables; sin contenido de llamada.' }
    }
    if ($r -like '*Microsoft\Crypto\*' -or $r -like '*\MachineKeys\*') {
        return @{ Categoria = 'Criptografía Windows / CNG'; Explicacion = 'Inicialización de proveedores criptográficos del sistema.' }
    }
    if ($r -like '*DirectX*' -or $r -like '*D3D*' -or $r -like '*DirectWrite*' -or $r -like '*igfx*' -or $r -like '*amdx*') {
        return @{ Categoria = 'Subsistema Gráfico / DirectX'; Explicacion = 'Caché de texturas o shaders de GPU.' }
    }
    if ($r -like '*Wer*' -or $r -like '*Windows Error Reporting*') {
        return @{ Categoria = 'Windows Error Reporting'; Explicacion = 'Exclusión de volcados de memoria.' }
    }
    return @{ Categoria = 'Sistema Windows [por verificar]'; Explicacion = 'Acceso de sistema operativo a verificar.' }
}

$archivosAgrupados = @($escriturasArchivo | Group-Object Ruta | ForEach-Object {
    $info = Clasificar-RutaArchivo $_.Name
    [PSCustomObject]@{
        Ruta = $_.Name
        Operaciones = ($_.Group | Select-Object -ExpandProperty Operacion -Unique) -join ', '
        Eventos = $_.Count
        Categoria = $info.Categoria
        Explicacion = $info.Explicacion
    }
})
# Escrituras de registro conocidas, sin contenido de la sesión
function Clasificar-RutaRegistro([string]$r) {
    if ($r -like '*\Notifications\Data\*') { return 'Almacén de estados de Windows (WNF): valor binario sin texto' }
    if ($r -like '*\Windows Error Reporting\ExcludedApplications*') { return 'Birdlang se excluye de los volcados de memoria por error (valor 1)' }
    if ($r -like '*\Software\AMD\HKIDs*') { return 'Controlador de video AMD: ruta del programa y API gráfica' }
    return $null
}
$registroSinClasificar = @($escriturasRegistro | Where-Object { -not (Clasificar-RutaRegistro $_.Ruta) })
$archivosSinClasificar = @($archivosAgrupados | Where-Object { $_.Categoria -like 'REVISAR*' -or $_.Categoria -like '*por verificar*' })

# Generar informe Markdown en UTF-8 con BOM
$md = [System.Text.StringBuilder]::new()
$md.AppendLine("# Registro de Sesión con Process Monitor") | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("- **Fecha:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')") | Out-Null
$md.AppendLine("- **Versión:** $Version") | Out-Null
$md.AppendLine("- **Duración de la sesión:** $textoDuracion") | Out-Null
$md.AppendLine("- **Eventos de Birdlang.exe capturados:** $totalEventosBirdlang") | Out-Null
$md.AppendLine("- **Procesos hijos de Birdlang:** $($hijosDetectados.Count)") | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("## 1. Conexiones y Tráfico de Red") | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("| Tipo de Medición | Eventos TCP | Eventos UDP | Resultado |") | Out-Null
$md.AppendLine("|---|---|---|---|") | Out-Null
$md.AppendLine(("| Process Monitor (filtros kernel) | {0} | {1} | {2} |" -f $eventosTcp, $eventosUdp, $(if ($eventosTcp -eq 0 -and $eventosUdp -eq 0) { '0 eventos (cero red comprobado)' } else { 'VIOLACIÓN DETECTADA' }))) | Out-Null
$md.AppendLine(("| Muestreo periódico (Get-Net* cada 10 s) | {0} | {1} | {2} |" -f $muestreoTcpMax, $muestreoUdpMax, $(if ($muestreoTcpMax -eq 0 -and $muestreoUdpMax -eq 0) { '0 sockets abiertos' } else { 'SOCKETS DETECTADOS' }))) | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("## 2. Escrituras de Archivo Clasificadas") | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("Total de operaciones de escritura de archivo: $($escriturasArchivo.Count)") | Out-Null
$md.AppendLine() | Out-Null
if ($archivosAgrupados.Count -gt 0) {
    $md.AppendLine("| Ruta | Operaciones | Conteo | Categoría | Explicación |") | Out-Null
    $md.AppendLine("|---|---|---|---|---|") | Out-Null
    foreach ($a in $archivosAgrupados) {
        $md.AppendLine(("| `{0}` | {1} | {2} | {3} | {4} |" -f $a.Ruta, $a.Operaciones, $a.Eventos, $a.Categoria, $a.Explicacion)) | Out-Null
    }
} else {
    $md.AppendLine("*No se registraron operaciones de escritura en archivos por parte de Birdlang.exe.*") | Out-Null
}
$md.AppendLine() | Out-Null
$md.AppendLine("## 3. Escrituras en el Registro") | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("Total de operaciones de escritura en registro: $($escriturasRegistro.Count)") | Out-Null
$md.AppendLine() | Out-Null
if ($escriturasRegistro.Count -gt 0) {
    $md.AppendLine("| Hora | Ruta en Registro | Operación | Tipo / Detalle | Qué es |") | Out-Null
    $md.AppendLine("|---|---|---|---|---|") | Out-Null
    foreach ($r in $escriturasRegistro) {
        $que = Clasificar-RutaRegistro $r.Ruta
        if (-not $que) { $que = '**REVISAR A MANO**' }
        $md.AppendLine(("| `{0}` | `{1}` | {2} | {3} | {4} |" -f $r.Hora, $r.Ruta, $r.Operacion, $r.Tipo, $que)) | Out-Null
    }
    $md.AppendLine() | Out-Null
    if ($registroSinClasificar.Count -eq 0) {
        $md.AppendLine("**Explicación:** todas están clasificadas en la tabla; ninguna guarda texto de la sesión.") | Out-Null
    } else {
        $md.AppendLine("**REVISAR A MANO:** hay escrituras que el análisis no conoce. Leer cada valor y confirmar que no tiene nada de la sesión.") | Out-Null
    }
} else {
    $md.AppendLine("*No se registraron operaciones de escritura en el registro por parte de Birdlang.exe.*") | Out-Null
}
$md.AppendLine() | Out-Null
$md.AppendLine("## 4. Lecturas del Archivo de Paginación (pagefile.sys)") | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("Total de lecturas de `pagefile.sys`: $($lecturasPagefile.Count)") | Out-Null
$md.AppendLine() | Out-Null
if ($lecturasPagefile.Count -gt 0) {
    $md.AppendLine("| Hora | Operación | Detalle de Paginación |") | Out-Null
    $md.AppendLine("|---|---|---|") | Out-Null
    foreach ($p in $lecturasPagefile) {
        $md.AppendLine(("| `{0}` | {1} | {2} |" -f $p.Hora, $p.Operacion, $p.Detalle)) | Out-Null
    }
    $md.AppendLine() | Out-Null
}
$md.AppendLine("**Explicación (README §4):**") | Out-Null
$md.AppendLine("Las lecturas del archivo de paginación muestran que el administrador de memoria de Windows envió páginas de memoria de `Birdlang.exe` al disco (`pagefile.sys`) y las trajo de vuelta a la memoria física cuando fueron requeridas. Las operaciones de escritura a la paginación las realiza exclusivamente el proceso kernel de Windows (*System*), por lo que no figuran bajo el proceso `Birdlang.exe`.") | Out-Null
$md.AppendLine("Como establece el README §4, este es un límite general del sistema operativo: sin BitLocker, páginas de la memoria de cualquier aplicación pueden residir temporalmente en el archivo de paginación de Windows si hay presión de memoria.") | Out-Null
$md.AppendLine() | Out-Null
$md.AppendLine("## 5. Auditoría de Contenido") | Out-Null
$md.AppendLine() | Out-Null
# La conclusión sale de los datos: cualquier cosa sin clasificar obliga a revisar a mano.
$limpio = $archivosSinClasificar.Count -eq 0 -and $registroSinClasificar.Count -eq 0 -and ($eventosTcp + $eventosUdp) -eq 0 -and $hijosDetectados.Count -eq 0
$md.AppendLine("- Eventos TCP/UDP en Process Monitor: $($eventosTcp + $eventosUdp).") | Out-Null
$md.AppendLine("- Escrituras de archivo: $($escriturasArchivo.Count), sin clasificar: $($archivosSinClasificar.Count).") | Out-Null
$md.AppendLine("- Escrituras en el registro: $($escriturasRegistro.Count), sin clasificar: $($registroSinClasificar.Count).") | Out-Null
$md.AppendLine("- Carpeta de Birdlang (del evento de arranque): $(if ($carpetaBirdlang) { $carpetaBirdlang } else { 'no capturada: la captura empezó con Birdlang abierto' }).") | Out-Null
$md.AppendLine("- Procesos hijos: $($hijosDetectados.Count).") | Out-Null
if ($limpio) {
    $md.AppendLine("- **Resultado:** sin red y sin escrituras con contenido de la sesión; todo lo escrito está clasificado arriba.") | Out-Null
} else {
    $md.AppendLine("- **Resultado: REVISAR A MANO.** Hay eventos que el análisis no puede clasificar como ajenos a la sesión.") | Out-Null
}

$dirInf = Split-Path $InformeMd
if (-not (Test-Path $dirInf)) { New-Item -ItemType Directory -Force $dirInf | Out-Null }

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($InformeMd, $md.ToString(), $utf8Bom)

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " INFORME GENERADO EN: $InformeMd" -ForegroundColor Green
Write-Host " Eventos TCP: $eventosTcp | Eventos UDP: $eventosUdp" -ForegroundColor Green
Write-Host " Escrituras archivo: $($escriturasArchivo.Count) | Escrituras registro: $($escriturasRegistro.Count)" -ForegroundColor Green
Write-Host " Lecturas pagefile.sys: $($lecturasPagefile.Count)" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
