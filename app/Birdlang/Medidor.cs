using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

// Procesador y RAM del propio programa, como los muestra el Administrador de
// tareas. El promedio y el máximo cuentan solo mientras se escucha.
sealed class Medidor
{
    readonly Process yo = Process.GetCurrentProcess();
    readonly Stopwatch reloj = Stopwatch.StartNew();
    TimeSpan cpuAntes;
    double tAntes, cpuSuma, tSuma;

    public double Cpu { get; private set; }
    public double CpuPeor { get; private set; }
    public double CpuPromedio => tSuma == 0 ? 0 : cpuSuma / tSuma / Environment.ProcessorCount * 100;
    public long Ram { get; private set; }
    public long RamMax { get; private set; }

    public void Leer(bool escuchando)
    {
        yo.Refresh();
        double t = reloj.Elapsed.TotalSeconds;
        var cpu = yo.TotalProcessorTime;
        double usado = (cpu - cpuAntes).TotalSeconds, lapso = t - tAntes;
        Cpu = lapso > 0 ? usado / lapso / Environment.ProcessorCount * 100 : 0;
        Ram = yo.WorkingSet64;
        if (escuchando)
        {
            (cpuSuma, tSuma) = (cpuSuma + usado, tSuma + lapso);
            CpuPeor = Math.Max(CpuPeor, Cpu);
            RamMax = Math.Max(RamMax, Ram);
        }
        (cpuAntes, tAntes) = (cpu, t);
    }

    public void Reiniciar() => (cpuSuma, tSuma, CpuPeor, RamMax) = (0, 0, 0, 0);

    public static string Equipo()
    {
        var cpu = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "?");
        var version = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "");
        int compilacion = Environment.OSVersion.Version.Build;
        var memoria = new EstadoMemoria { Largo = (uint)Marshal.SizeOf<EstadoMemoria>() };
        GlobalMemoryStatusEx(ref memoria);
        GetSystemPowerStatus(out var energia);
        string enchufado = energia.ACLineStatus switch { 1 => "enchufada", 0 => "a batería", _ => "?" };
        return $"{cpu?.ToString()?.Trim()} ({Environment.ProcessorCount} hilos), {memoria.Total / (1024.0 * 1024 * 1024):0.0} GB, " +
               $"Windows {(compilacion >= 22000 ? 11 : 10)} {version} ({compilacion}), {enchufado}";
    }

    struct EstadoEnergia
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    struct EstadoMemoria
    {
        public uint Largo, Carga;
        public ulong Total, Disponible, TotalPaginacion, PaginacionDisponible, TotalVirtual, VirtualDisponible, VirtualExtendidaDisponible;
    }

    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out EstadoEnergia estado);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref EstadoMemoria estado);
}
