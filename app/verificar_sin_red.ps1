# app/verificar_sin_red.ps1
# Verificador de "cero red" para Birdlang.
# Comprueba metadatos de Birdlang.dll, codigo fuente C#, proyecto y DLLs de terceros.
# Si detecta cualquier violacion, aborta inmediatamente con throw.

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Carpeta = $(if ((Split-Path $PSScriptRoot -Leaf) -ieq 'verificacion') { Split-Path $PSScriptRoot -Parent } else { Join-Path $PSScriptRoot 'dist\Birdlang' }),

    [switch]$Empresas
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Carpeta)) {
    throw "La carpeta especificada no existe: $Carpeta"
}
$Carpeta = (Resolve-Path -LiteralPath $Carpeta).ProviderPath

# La version para empresas es la que va sin actualizar.ps1: asi se reconoce
# aunque TI haya renombrado la carpeta al descomprimirla.
$tieneActualizador = Test-Path -LiteralPath (Join-Path $Carpeta 'actualizar.ps1')
$esEmpresas = $Empresas -or ($Carpeta -like '*Empresas*') -or -not $tieneActualizador

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " VERIFICADOR DE CERO RED - BIRDLANG" -ForegroundColor Cyan
Write-Host " Carpeta: $Carpeta" -ForegroundColor Cyan
Write-Host " Paquete: $(if ($esEmpresas) { 'Empresas (sin actualizar.ps1)' } else { 'Estandar (con actualizar.ps1)' })" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$archivosEsperados = @(
    'Birdlang.exe',
    'Birdlang.dll',
    'nativos\moonshine\moonshine.dll',
    'modelos\silero_vad.onnx',
    'datos\glosario.tsv',
    'datos\calles.txt',
    'datos\codigos_postales.tsv'
)
$faltantes = @($archivosEsperados | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Carpeta $_)) })
if ($faltantes.Count -gt 0) {
    throw "Faltan archivos esperados del paquete en '$Carpeta': $($faltantes -join ', ')"
}

# -----------------------------------------------------------------------------
# 0. SCRIPTS .ps1: solo los de verificacion/ y, en la version Estandar,
#    actualizar.ps1 en la raiz. Se revisa siempre, en las dos versiones.
# -----------------------------------------------------------------------------
Write-Host "[0/5] Verificando scripts .ps1 (solo verificacion/$(if (-not $esEmpresas) { ' y actualizar.ps1' }))..." -ForegroundColor Yellow
$infraccionesPs1 = @()
foreach ($s in Get-ChildItem -Path $Carpeta -Filter "*.ps1" -Recurse -File) {
    $rel = $s.FullName.Substring($Carpeta.Length).TrimStart('\', '/')
    if ($rel -ieq 'actualizar.ps1') {
        if ($esEmpresas) { $infraccionesPs1 += "actualizar.ps1 no esta permitido en la version para empresas" }
    } elseif (-not $rel.StartsWith("verificacion\", [StringComparison]::OrdinalIgnoreCase) -and -not $rel.StartsWith("verificacion/", [StringComparison]::OrdinalIgnoreCase)) {
        $infraccionesPs1 += "Script .ps1 fuera de verificacion/: $rel"
    }
}
if ($infraccionesPs1.Count -gt 0) {
    throw "VIOLACION DE SCRIPTS:`n" + ($infraccionesPs1 -join "`n")
}
Write-Host "  OK: 0 scripts .ps1 no autorizados." -ForegroundColor Green

# -----------------------------------------------------------------------------
# 1. CARGA DE System.Reflection.Metadata
# -----------------------------------------------------------------------------
Write-Host "[1/5] Cargando System.Reflection.Metadata..." -ForegroundColor Yellow

# PowerShell 7 trae su propia copia (la compilacion automatica la carga de ahi):
# con PowerShell 7 el verificador no necesita el SDK de .NET ni Visual Studio.
if ($PSVersionTable.PSEdition -eq 'Core') {
    Add-Type -AssemblyName System.Reflection.Metadata
    Write-Host "  OK: System.Reflection.Metadata de PowerShell $($PSVersionTable.PSVersion)" -ForegroundColor Green
} else {
$toolsDir = $null
$srmCandidates = @(
    "${env:ProgramFiles}\dotnet\sdk\*\Sdks\Microsoft.NET.Sdk\tools\net472\System.Reflection.Metadata.dll",
    "${env:ProgramFiles(x86)}\dotnet\sdk\*\Sdks\Microsoft.NET.Sdk\tools\net472\System.Reflection.Metadata.dll",
    "${env:ProgramFiles}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\System.Reflection.Metadata.dll",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\System.Reflection.Metadata.dll"
)

foreach ($c in $srmCandidates) {
    $match = Get-ChildItem $c -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($match) {
        $toolsDir = Split-Path $match.FullName
        break
    }
}

if (-not $toolsDir) {
    throw "No se encontro System.Reflection.Metadata.dll (net472) en el SDK de .NET ni en MSBuild. Correr el script con PowerShell 7 (pwsh), que la trae: https://aka.ms/powershell"
}

# Registrar AssemblyResolve seguro en C# para resolver dependencias (System.Memory, Immutable, etc.)
if (-not ([System.Management.Automation.PSTypeName]'BirdlangVerifierAssemblyResolver').Type) {
    $resolverSource = @'
using System;
using System.Reflection;
using System.IO;

public static class BirdlangVerifierAssemblyResolver {
    private static bool _busy = false;
    private static string _dir = null;

    public static void Init(string dir) {
        _dir = dir;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveHandler;
    }

    private static Assembly ResolveHandler(object sender, ResolveEventArgs args) {
        if (_busy || _dir == null) return null;
        _busy = true;
        try {
            string simpleName = new AssemblyName(args.Name).Name + ".dll";
            string path = Path.Combine(_dir, simpleName);
            if (File.Exists(path)) {
                return Assembly.LoadFrom(path);
            }
            return null;
        } finally {
            _busy = false;
        }
    }
}
'@
    Add-Type -TypeDefinition $resolverSource
}
[BirdlangVerifierAssemblyResolver]::Init($toolsDir)
[System.Reflection.Assembly]::LoadFrom((Join-Path $toolsDir 'System.Reflection.Metadata.dll')) | Out-Null
Write-Host "  OK: System.Reflection.Metadata cargado desde $toolsDir" -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 2. VERIFICACION DE Birdlang.dll (Metadatos)
# -----------------------------------------------------------------------------
Write-Host "[2/5] Inspeccionando metadatos de Birdlang.dll..." -ForegroundColor Yellow
$dllBirdlang = Join-Path $Carpeta 'Birdlang.dll'
if (-not (Test-Path $dllBirdlang)) {
    throw "No se encontro Birdlang.dll en $Carpeta"
}

$stream = [System.IO.File]::OpenRead($dllBirdlang)
$peReader = New-Object System.Reflection.PortableExecutable.PEReader($stream)
$mr = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)

# a.1) Referencias a ensamblados System.Net.*
foreach ($h in $mr.AssemblyReferences) {
    $ar = $mr.GetAssemblyReference($h)
    $asmName = $mr.GetString($ar.Name)
    if ($asmName -match '(?i)^System\.Net(\.|$)' -or $asmName -match '(?i)WebSocket') {
        $stream.Close()
        throw "VIOLACION DE RED: Birdlang.dll referencia el ensamblado prohibido '$asmName'."
    }
}

# a.2) Referencias a tipos de espacios System.Net*
foreach ($h in $mr.TypeReferences) {
    $tr = $mr.GetTypeReference($h)
    $ns = $mr.GetString($tr.Namespace)
    $tName = $mr.GetString($tr.Name)
    if ($ns -match '(?i)^System\.Net(\.|$)' -or $ns -match '(?i)WebSocket') {
        $stream.Close()
        throw "VIOLACION DE RED: Birdlang.dll referencia el tipo prohibido '$ns.$tName'."
    }
}

# a.3) ModuleRef (P/Invoke a librerias de red)
$modulosProhibidos = '^(ws2_32|wininet|winhttp|urlmon|iphlpapi|dnsapi|mswsock|websocket)(\.dll)?$'
$modRefCount = [System.Reflection.Metadata.Ecma335.MetadataReaderExtensions]::GetTableRowCount($mr, [System.Reflection.Metadata.Ecma335.TableIndex]::ModuleRef)
for ($i = 1; $i -le $modRefCount; $i++) {
    $h = [System.Reflection.Metadata.Ecma335.MetadataTokens]::ModuleReferenceHandle($i)
    $modRef = $mr.GetModuleReference($h)
    $modName = $mr.GetString($modRef.Name)
    if ($modName -match "(?i)$modulosProhibidos") {
        $stream.Close()
        throw "VIOLACION DE RED: Birdlang.dll contiene un ModuleRef (P/Invoke) prohibido a '$modName'."
    }
}

$stream.Close()
Write-Host "  OK: Birdlang.dll no referencia ensamblados, tipos ni P/Invoke de red." -ForegroundColor Green

# a.4) Regla de System.Diagnostics.Process (solo en Actualizacion para el actualizador)
$rutaAppCs = Join-Path $PSScriptRoot 'Birdlang'
$csprojPath = Join-Path $rutaAppCs 'Birdlang.csproj'
$archivosCs = if (Test-Path -LiteralPath $rutaAppCs) {
    @(Get-ChildItem -Path $rutaAppCs -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
} else {
    @()
}
$hayCodigoFuente = ($archivosCs.Count -gt 0) -and (Test-Path -LiteralPath $csprojPath)

# -----------------------------------------------------------------------------
# 3. VERIFICACION DEL CODIGO FUENTE (Simbolos prohibidos de red)
# -----------------------------------------------------------------------------
Write-Host "[3/5] Escaneando codigo fuente C# por simbolos de red..." -ForegroundColor Yellow
if (-not $hayCodigoFuente) {
    Write-Host "  OMITIDO: el paquete no trae codigo fuente; se revisan los binarios" -ForegroundColor DarkYellow
} else {
    Write-Host "  Verificando uso de System.Diagnostics.Process en codigo fuente..."
    foreach ($f in $archivosCs) {
        $relPath = $f.FullName.Substring($rutaAppCs.Length).TrimStart('\', '/')
        $lineas = Get-Content -Path $f.FullName
        $lineNum = 0
        foreach ($linea in $lineas) {
            $lineNum++
            # Process.Start esta estrictamente reservado para Actualizacion.cs
            if ($linea -match '\bProcess\.Start\b' -and $relPath -ne 'Actualizacion.cs') {
                throw "VIOLACION DE PROCESO: Uso prohibido de Process.Start en $relPath linea $lineNum. Solo Actualizacion.cs tiene permiso."
            }
            # Cualquier uso de System.Diagnostics.Process fuera de Actualizacion, Medidor (CPU) o Ventana (PID) esta prohibido
            if ($linea -match '\bSystem\.Diagnostics\.Process\b' -and $relPath -ne 'Actualizacion.cs' -and $relPath -ne 'Medidor.cs' -and $relPath -ne 'Ventana.cs') {
                throw "VIOLACION DE PROCESO: Uso no autorizado de System.Diagnostics.Process en $relPath linea $lineNum."
            }
        }
    }
    Write-Host "  OK: System.Diagnostics.Process verificado (Process.Start solo en Actualizacion.cs, y GetProcessesByName para buscar el navegador)." -ForegroundColor Green

    $patronRed = 'HttpClient|WebClient|WebRequest|Socket|TcpClient|UdpClient|WebSocket|Dns\.|NetworkInterface'

    $hallazgosCodigo = @()
    foreach ($f in $archivosCs) {
        $coincidencias = Select-String -Path $f.FullName -Pattern $patronRed
        foreach ($c in $coincidencias) {
            $rel = $f.FullName.Substring($rutaAppCs.Length).TrimStart('\', '/')
            $hallazgosCodigo += "{0}:{1}: {2}" -f $rel, $c.LineNumber, $c.Line.Trim()
        }
    }

    if ($hallazgosCodigo.Count -gt 0) {
        throw "VIOLACION DE RED EN CODIGO FUENTE:`n" + ($hallazgosCodigo -join "`n")
    }
    Write-Host "  OK: 0 simbolos de red en codigo fuente C#." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 4. VERIFICACION DEL ARCHIVO DE PROYECTO (Birdlang.csproj)
# -----------------------------------------------------------------------------
Write-Host "[4/5] Verificando exclusion de System.Net.Http en Birdlang.csproj..." -ForegroundColor Yellow
if (-not $hayCodigoFuente) {
    Write-Host "  OMITIDO: el paquete no trae codigo fuente; se revisan los binarios" -ForegroundColor DarkYellow
} else {
    $csprojXml = [xml](Get-Content $csprojPath)
    $usingHttpRemove = $csprojXml.SelectNodes("//Using[@Remove='System.Net.Http']")
    if ($usingHttpRemove.Count -eq 0) {
        throw "VIOLACION DE CONFIGURACION: Birdlang.csproj no contiene <Using Remove=`"System.Net.Http`" /> para anular el ImplicitUsing."
    }
    Write-Host "  OK: <Using Remove=`"System.Net.Http`" /> presente en Birdlang.csproj." -ForegroundColor Green
}

# -----------------------------------------------------------------------------
# 5. LIBRERIAS DE TERCEROS EN LA CARPETA PORTABLE
# -----------------------------------------------------------------------------
Write-Host "[5/5] Analizando librerias de terceros (ensamblados administrados y DLL nativas)..." -ForegroundColor Yellow

# Lector PE minimo en C#
if (-not ([System.Management.Automation.PSTypeName]'BirdlangPeImportReader').Type) {
    $peReaderSource = @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

public class PeSectionHeader {
    public uint Va;
    public uint Vs;
    public uint RawPtr;
}

public static class BirdlangPeImportReader {
    public static List<string> GetImports(string filePath) {
        var imports = new List<string>();
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var br = new BinaryReader(fs)) {
            if (fs.Length < 64) return imports;
            fs.Seek(0x3C, SeekOrigin.Begin);
            uint e_lfanew = br.ReadUInt32();
            if (e_lfanew + 256 > fs.Length) return imports;

            fs.Seek(e_lfanew, SeekOrigin.Begin);
            uint peSig = br.ReadUInt32();
            if (peSig != 0x00004550) return imports;

            ushort machine = br.ReadUInt16();
            ushort numSections = br.ReadUInt16();
            fs.Seek(12, SeekOrigin.Current);
            ushort sizeOfOptionalHeader = br.ReadUInt16();
            ushort characteristics = br.ReadUInt16();

            long optHeaderStart = fs.Position;
            ushort magic = br.ReadUInt16();
            bool is64 = (magic == 0x20B);

            int importDirOffset = is64 ? 120 : 104;
            if (sizeOfOptionalHeader < importDirOffset + 8) return imports;

            fs.Seek(optHeaderStart + importDirOffset, SeekOrigin.Begin);
            uint importRva = br.ReadUInt32();
            uint importSize = br.ReadUInt32();
            if (importRva == 0 || importSize == 0) return imports;

            fs.Seek(optHeaderStart + sizeOfOptionalHeader, SeekOrigin.Begin);
            var sections = new List<PeSectionHeader>();
            for (int i = 0; i < numSections; i++) {
                fs.Seek(8, SeekOrigin.Current);
                uint vs = br.ReadUInt32();
                uint va = br.ReadUInt32();
                uint rawSize = br.ReadUInt32();
                uint rawPtr = br.ReadUInt32();
                fs.Seek(16, SeekOrigin.Current);
                sections.Add(new PeSectionHeader { Va = va, Vs = Math.Max(vs, rawSize), RawPtr = rawPtr });
            }

            long importTableOffset = RvaToFileOffset(importRva, sections);
            if (importTableOffset < 0 || importTableOffset >= fs.Length) return imports;

            fs.Seek(importTableOffset, SeekOrigin.Begin);
            while (fs.Position + 20 <= fs.Length) {
                uint origFirstThunk = br.ReadUInt32();
                uint timeDateStamp = br.ReadUInt32();
                uint forwarderChain = br.ReadUInt32();
                uint nameRva = br.ReadUInt32();
                uint firstThunk = br.ReadUInt32();

                if (nameRva == 0) break;

                long savedPos = fs.Position;
                long nameOffset = RvaToFileOffset(nameRva, sections);
                if (nameOffset >= 0 && nameOffset < fs.Length) {
                    fs.Seek(nameOffset, SeekOrigin.Begin);
                    var sb = new StringBuilder();
                    while (fs.Position < fs.Length) {
                        byte b = br.ReadByte();
                        if (b == 0) break;
                        sb.Append((char)b);
                    }
                    if (sb.Length > 0) imports.Add(sb.ToString());
                }
                fs.Seek(savedPos, SeekOrigin.Begin);
            }
        }
        return imports;
    }

    private static long RvaToFileOffset(uint rva, List<PeSectionHeader> sections) {
        foreach (var s in sections) {
            if (rva >= s.Va && rva < s.Va + s.Vs) {
                return (long)rva - s.Va + s.RawPtr;
            }
        }
        return -1;
    }
}
'@
    Add-Type -TypeDefinition $peReaderSource
}

# Lista blanca vacia: ninguna libreria distribuida puede tener dependencias de red.
# Si alguna DLL con codigo o imports de red aparece, la verificacion debe fallar inmediatamente.
$ListaPermitidas = @{}

$dlls = Get-ChildItem -Path $Carpeta -Filter "*.dll" -Recurse

# 5.1 Ensamblados administrados de terceros
$ensambladosTerceros = $dlls | Where-Object {
    $_.Name -notlike "System.*" -and
    $_.Name -notlike "Microsoft.Win32.*" -and
    $_.Name -notlike "Microsoft.CSharp.*" -and
    $_.Name -notlike "Microsoft.VisualBasic.*" -and
    $_.Name -notlike "mscorlib*" -and
    $_.Name -notlike "netstandard*" -and
    $_.Name -ne "Birdlang.dll"
}

$infraccionesAdministradas = @()
foreach ($d in $ensambladosTerceros) {
    try {
        $s = [System.IO.File]::OpenRead($d.FullName)
        $pe = New-Object System.Reflection.PortableExecutable.PEReader($s)
        if ($pe.HasMetadata) {
            $mReader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            $netRefs = @()
            foreach ($h in $mReader.AssemblyReferences) {
                $ar = $mReader.GetAssemblyReference($h)
                $n = $mReader.GetString($ar.Name)
                if ($n -match '(?i)System\.Net\.(Sockets|Http)|WebSocket') {
                    $netRefs += $n
                }
            }
            if ($netRefs.Count -gt 0) {
                if ($ListaPermitidas.ContainsKey($d.Name)) {
                    Write-Host "  [LISTA BLANCA] $($d.Name): referencias ($($netRefs -join ', ')) permitidas por excepcion documentada." -ForegroundColor DarkYellow
                } else {
                    $infraccionesAdministradas += "$($d.Name) referenciando: $($netRefs -join ', ')"
                }
            }
        }
        $s.Close()
    } catch {
        # Si no es administrado, se procesa en el escaneo PE nativo
    }
}

if ($infraccionesAdministradas.Count -gt 0) {
    throw "VIOLACION DE RED EN LIBRERIAS ADMINISTRADAS NO PERMITIDAS:`n" + ($infraccionesAdministradas -join "`n")
}
Write-Host "  OK: Ensamblados administrados de terceros validados." -ForegroundColor Green

# 5.2 DLL nativas (analisis PE de importaciones)
$infraccionesNativas = @()
foreach ($d in $dlls) {
    try {
        $imports = [BirdlangPeImportReader]::GetImports($d.FullName)
        $netImports = $imports | Where-Object { $_ -match "(?i)$modulosProhibidos" }
        if ($netImports) {
            if ($ListaPermitidas.ContainsKey($d.Name)) {
                Write-Host "  [LISTA BLANCA NATIVA] $($d.Name): importa ($($netImports -join ', ')) permitido por excepcion documentada." -ForegroundColor DarkYellow
            } else {
                $infraccionesNativas += "$($d.FullName.Substring($Carpeta.Length).TrimStart('\', '/')) importa: $($netImports -join ', ')"
            }
        }
    } catch {
        Write-Host "  Advertencia al leer PE de $($d.Name): $_" -ForegroundColor DarkGray
    }
}

if ($infraccionesNativas.Count -gt 0) {
    throw "VIOLACION DE RED EN DLL NATIVAS NO PERMITIDAS:`n" + ($infraccionesNativas -join "`n")
}
Write-Host "  OK: DLL nativas validadas sin importaciones de red no permitidas." -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " VERIFICACION DE CERO RED COMPLETADA EXITOSAMENTE." -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
