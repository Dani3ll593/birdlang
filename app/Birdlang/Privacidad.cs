using System.Runtime.InteropServices;

// Las protecciones de README §4 que no dependen de la interfaz.
static class Privacidad
{
    // Si el programa falla, Windows no guarda un volcado: tendría lo que hay
    // en memoria (subtítulos y notas).
    public static void SinVolcados() => WerAddExcludedApplication(Environment.ProcessPath!, false);

    // La ventana no sale en capturas ni al compartir pantalla (Windows 10 2004+).
    // No es una barrera absoluta (README §4).
    public static void OcultarDeCapturas(IntPtr ventana) => SetWindowDisplayAffinity(ventana, 0x11);

    // Modo Supervisión QA: permite temporalmente la captura en vivo para monitoreo.
    // Al quitar la exclusión, las capturas reciben la ventana corrida unos 30 px
    // hacia arriba (la barra de título tapa el contenido) hasta que cambia de
    // tamaño: se agranda 1 px y se devuelve, sin moverla ni activarla. Maximizada
    // eso no alcanza: se restaura y se vuelve a maximizar.
    public static void PermitirCapturas(IntPtr ventana)
    {
        SetWindowDisplayAffinity(ventana, 0x00);
        if (IsZoomed(ventana))
        {
            ShowWindow(ventana, 9); // SW_RESTORE
            ShowWindow(ventana, 3); // SW_MAXIMIZE
            return;
        }
        if (!GetWindowRect(ventana, out var r)) return;
        const uint soloTamano = 0x0002 | 0x0004 | 0x0010; // SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE
        SetWindowPos(ventana, IntPtr.Zero, 0, 0, r.Derecha - r.Izquierda + 1, r.Abajo - r.Arriba, soloTamano);
        SetWindowPos(ventana, IntPtr.Zero, 0, 0, r.Derecha - r.Izquierda, r.Abajo - r.Arriba, soloTamano);
    }

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Izquierda, Arriba, Derecha, Abajo; }

    // Copia texto sin que entre al historial del portapapeles (Win+V) ni a la
    // nube. Solo se usa para las métricas y dial-out; las notas no se pueden copiar.
    public static void Copiar(string texto)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            SetClipboardData(13, Global(MemoryMarshal.AsBytes((texto + "\0").AsSpan()))); // CF_UNICODETEXT
            foreach (var formato in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard", "ExcludeClipboardContentFromMonitorProcessing" })
                SetClipboardData(RegisterClipboardFormatW(formato), Global(new byte[4]));
        }
        finally { CloseClipboard(); }
    }

    // Vacía completamente el portapapeles
    public static void LimpiarPortapapeles()
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try { EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    static CancellationTokenSource? timerDialOutCts;

    // Copia un número para Dial-Out con auto-vaciado a los 'segundos' (20s por defecto).
    public static void CopiarTemporalDialOut(string numero, int segundos = 20, Action? alExpirar = null)
    {
        timerDialOutCts?.Cancel();
        timerDialOutCts?.Dispose();
        timerDialOutCts = new CancellationTokenSource();
        var token = timerDialOutCts.Token;

        Copiar(numero);

        Task.Delay(TimeSpan.FromSeconds(segundos), token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !token.IsCancellationRequested)
            {
                LimpiarPortapapeles();
                alExpirar?.Invoke();
            }
        });
    }

    // Cancelar cualquier temporizador de Dial-Out pendiente y vaciar portapapeles
    public static void CancelarDialOut()
    {
        timerDialOutCts?.Cancel();
        LimpiarPortapapeles();
    }

    // Expresión regular que detecta números de teléfono:
    // - Estándar Norteamericano (10 u 11 dígitos, con o sin código de país 1/extensión): (305) 555-1234, 305-555-1234, 305 555 1234, 3055551234, 1-305-555-1234
    // - Números internacionales con prefijo y separadores: +52 55 1234 5678
    // - Locales de 7 dígitos con separador o continuos: 555-1234, 555 1234, 5551234
    // - Cifras continuas de 10 u 11 dígitos (evitando falsos positivos de 8 y 9 dígitos en transcripción)
    public static readonly System.Text.RegularExpressions.Regex PatronTelefono = new(
        @"(?:\+?1[-.\s]?)?\(?[2-9]\d{2}\)?[-.\s]?\d{3}[-.\s]?\d{4}(?:\s*(?:ext|x|ext\.)\s*\d{1,5})?|\b(?:\+?\d{1,3}[-.\s]?)?\(?\d{2,4}\)?[-.\s]\d{3,4}[-.\s]\d{3,4}\b|\b\d{3}[-.\s]\d{4}\b|\b1?\d{10}\b|\b\d{7}\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static List<string> ExtraerTelefonos(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return [];
        var matches = PatronTelefono.Matches(texto);
        var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var val = m.Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
            int digitos = 0;
            for (int i = 0; i < val.Length; i++)
            {
                if (char.IsDigit(val[i])) digitos++;
            }
            // Aceptamos longitudes telefónicas estándar (7 local, 10 nacional, 11 con país 1, 12-15 internacional)
            // Rechazamos 8 y 9 dígitos sueltos porque corresponden a pólizas/IDs/fechas o fragmentos incompletos
            if (digitos == 7 || digitos == 10 || digitos == 11 || (digitos >= 12 && digitos <= 15))
            {
                res.Add(val);
            }
        }
        return res.ToList();
    }

    static unsafe IntPtr Global(ReadOnlySpan<byte> datos)
    {
        var memoria = GlobalAlloc(2, (nuint)datos.Length); // GMEM_MOVEABLE
        datos.CopyTo(new Span<byte>((void*)GlobalLock(memoria), datos.Length));
        GlobalUnlock(memoria);
        return memoria;
    }

    // Bloquea el búfer en RAM física para que Windows no lo mande a pagefile.sys.
    // Amplía el límite mínimo de working set con un margen seguro (+4 MB) sin
    // recortar el máximo, para evitar ERROR_WORKING_SET_QUOTA sin permisos de admin.
    public static bool BloquearMemoria(IntPtr direccion, nuint bytes)
    {
        if (VirtualLock(direccion, bytes)) return true;
        var proceso = GetCurrentProcess();
        if (GetProcessWorkingSetSize(proceso, out var min, out var max))
        {
            var nuevoMin = min + bytes + 4 * 1024 * 1024;
            var nuevoMax = Math.Max(max, nuevoMin + 4 * 1024 * 1024);
            if (SetProcessWorkingSetSize(proceso, nuevoMin, nuevoMax))
                return VirtualLock(direccion, bytes);
        }
        return false;
    }

    public static bool DesbloquearMemoria(IntPtr direccion, nuint bytes) => VirtualUnlock(direccion, bytes);

    [DllImport("wer.dll", CharSet = CharSet.Unicode)] static extern int WerAddExcludedApplication(string exe, bool todosLosUsuarios);
    [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr ventana, uint afinidad);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr ventana, out Rect rectangulo);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr ventana, IntPtr despuesDe, int x, int y, int ancho, int alto, uint opciones);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr ventana);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr ventana, int modo);
    [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr ventana);
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern IntPtr SetClipboardData(uint formato, IntPtr datos);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormatW(string nombre);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint opciones, nuint bytes);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr memoria);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr memoria);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool GetProcessWorkingSetSize(IntPtr proceso, out nuint minimo, out nuint maximo);
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr proceso, nuint minimo, nuint maximo);
    [DllImport("kernel32.dll")] static extern bool VirtualLock(IntPtr direccion, nuint bytes);
    [DllImport("kernel32.dll")] static extern bool VirtualUnlock(IntPtr direccion, nuint bytes);
}
