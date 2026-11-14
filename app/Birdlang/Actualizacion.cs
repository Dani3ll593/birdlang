using System.Diagnostics;

// El aviso de versión nueva. Birdlang no se conecta: le pregunta al
// actualizador aparte (actualizar.ps1), que es lo único que habla con GitHub,
// y solo lee su respuesta. Si el actualizador no está en la carpeta (la
// versión para empresas), no hay aviso.
static class Actualizacion
{
    static string Script => Nativos.Ruta("actualizar.ps1");

    public static bool Disponible => File.Exists(Script);

    // Devuelve la versión nueva (ej. "0.2.0"), "al dia" si no hay novedades,
    // o null si no se pudo saber (por tiempo de espera o error).
    public static async Task<string?> Revisar()
    {
        if (!Disponible) return null;
        try
        {
            using var p = Process.Start(PowerShell(false, "-Revisar"));
            if (p == null) return null;
            var respuesta = p.StandardOutput.ReadToEndAsync();
            if (await Task.WhenAny(respuesta, Task.Delay(TimeSpan.FromSeconds(30))) != respuesta)
            {
                p.Kill();
                return null;
            }
            var texto = (await respuesta).Trim();
            if (texto.StartsWith("nueva ")) return texto[6..];
            if (texto == "al dia") return "al dia";
            return null;
        }
        catch
        {
            return null;
        }
    }

    // Abre el actualizador en su propia ventana; espera a que Birdlang se cierre.
    public static void Instalar() =>
        Process.Start(PowerShell(true, "-Instalar", "-Esperar", Environment.ProcessId.ToString()));

    static ProcessStartInfo PowerShell(bool visible, params string[] argumentos)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = !visible,
            RedirectStandardOutput = !visible,
        };
        foreach (var a in (string[])["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Script, .. argumentos])
            info.ArgumentList.Add(a);
        return info;
    }
}
