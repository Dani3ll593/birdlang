using System.Diagnostics;
using System.Runtime.InteropServices;


// Moonshine Streaming por su API en C (moonshine-c-api.h de la versión 0.1.5).
sealed unsafe class Moonshine : IDisposable
{
    const int VersionCabecera = 30000; // MOONSHINE_HEADER_VERSION
    // El texto se pide de nuevo cada 0,5 s de audio, como hace la librería de
    // Python de Moonshine. Si la pasada anterior tardó más, se espera eso
    // (hasta 5 s): así un equipo lento no se atrasa cada vez más.
    public const double Intervalo = 0.5;
    readonly int transcriptor;
    int flujo = -1;
    double sinTexto, ultimaPasada;

    // Duración máxima registrada de una pasada de transcripción (en ms).
    public double PasadaMasLentaMs { get; private set; }

    // Las líneas de la última transcripción. Moonshine corta el habla en
    // líneas en las pausas naturales, y una línea completa ya no cambia.
    public List<(string Texto, bool Completa)> Lineas { get; private set; } = [];

    public Moonshine(string carpeta, bool pequeno, params (string Nombre, string Valor)[] opciones)
    {
        var lista = new Opcion[opciones.Length];
        for (int i = 0; i < opciones.Length; i++)
        {
            lista[i].Nombre = Marshal.StringToCoTaskMemUTF8(opciones[i].Nombre);
            lista[i].Valor = Marshal.StringToCoTaskMemUTF8(opciones[i].Valor);
        }
        try
        {
            fixed (Opcion* p = lista)
                transcriptor = Revisar(moonshine_load_transcriber_from_files(
                    carpeta, pequeno ? 4u : 2u, p, (ulong)lista.Length, VersionCabecera));
        }
        finally
        {
            foreach (var o in lista) { Marshal.FreeCoTaskMem(o.Nombre); Marshal.FreeCoTaskMem(o.Valor); }
        }
    }

    public void Empezar()
    {
        if (flujo >= 0) moonshine_free_stream(transcriptor, flujo);
        flujo = Revisar(moonshine_create_stream(transcriptor, 0));
        Revisar(moonshine_start_stream(transcriptor, flujo));
        (sinTexto, ultimaPasada) = (0, 0);
    }

    public void Agregar(ReadOnlySpan<float> audio, int tasa = Recorrido.Tasa)
    {
        fixed (float* p = audio)
            Revisar(moonshine_transcribe_add_audio_to_stream(transcriptor, flujo, p, (ulong)audio.Length, tasa, 0));
        sinTexto += (double)audio.Length / tasa;
    }

    // El texto actualizado, si ya tocaba otra pasada (o se pide ahora); si no, null.
    public string? Avanzar(bool ahora = false)
    {
        if (!ahora && sinTexto < Math.Min(Math.Max(Intervalo, ultimaPasada), 10 * Intervalo)) return null;
        var reloj = Stopwatch.StartNew();
        var texto = Texto();
        (sinTexto, ultimaPasada) = (0, reloj.Elapsed.TotalSeconds);
        return texto;
    }

    public string Texto()
    {
        var sw = Stopwatch.StartNew();
        Revisar(moonshine_transcribe_stream(transcriptor, flujo, 0, out var t));
        var res = Unir(t);
        double ms = sw.Elapsed.TotalMilliseconds;
        if (ms > PasadaMasLentaMs) PasadaMasLentaMs = ms;
        return res;
    }

    public string Terminar()
    {
        var sw = Stopwatch.StartNew();
        Revisar(moonshine_stop_stream(transcriptor, flujo));
        Revisar(moonshine_transcribe_stream(transcriptor, flujo, 0, out var t));
        var texto = Unir(t);
        double ms = sw.Elapsed.TotalMilliseconds;
        if (ms > PasadaMasLentaMs) PasadaMasLentaMs = ms;
        moonshine_free_stream(transcriptor, flujo);
        flujo = -1;
        return texto;
    }

    public void ReiniciarMetricas()
    {
        PasadaMasLentaMs = 0;
    }

    public void Descartar()
    {
        if (flujo >= 0) moonshine_free_stream(transcriptor, flujo);
        flujo = -1;
        Lineas = [];
    }

    public void Dispose()
    {
        if (flujo >= 0) moonshine_free_stream(transcriptor, flujo);
        moonshine_free_transcriber(transcriptor);
    }

    string Unir(Transcripcion* t)
    {
        Lineas = [];
        for (ulong i = 0; i < t->line_count; i++)
        {
            var texto = Marshal.PtrToStringUTF8(t->lines[i].text)?.Trim();
            // Sin abreviar repeticiones: el intérprete es eco de lo que dicen las partes.
            if (!string.IsNullOrEmpty(texto) && texto.Any(char.IsLetterOrDigit))
                Lineas.Add((texto, t->lines[i].is_complete != 0));
        }
        return string.Join(" ", Lineas.Select(l => l.Texto));
    }

    static int Revisar(int codigo) => codigo >= 0 ? codigo
        : throw new InvalidOperationException("Moonshine: " + Marshal.PtrToStringUTF8(moonshine_error_to_string(codigo)));

    struct Opcion { public IntPtr Nombre, Valor; }

#pragma warning disable CS0649 // Los llena la librería.
    struct Linea
    {
        public IntPtr text, audio_data;
        public nuint audio_data_count;
        public float start_time, duration;
        public ulong id;
        public sbyte is_complete, is_updated, is_new, has_text_changed, have_speakers_changed;
        public IntPtr speaker_spans;
        public ulong speaker_span_count;
        public uint last_transcription_latency_ms;
        public IntPtr words;
        public ulong word_count;
    }

    struct Transcripcion { public Linea* lines; public ulong line_count; }
#pragma warning restore CS0649

    const string Dll = "moonshine";
    [DllImport(Dll)] static extern int moonshine_load_transcriber_from_files(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint arch, Opcion* options, ulong count, int version);
    [DllImport(Dll)] static extern void moonshine_free_transcriber(int t);
    [DllImport(Dll)] static extern int moonshine_create_stream(int t, uint flags);
    [DllImport(Dll)] static extern int moonshine_free_stream(int t, int s);
    [DllImport(Dll)] static extern int moonshine_start_stream(int t, int s);
    [DllImport(Dll)] static extern int moonshine_stop_stream(int t, int s);
    [DllImport(Dll)] static extern int moonshine_transcribe_add_audio_to_stream(int t, int s, float* audio, ulong n, int rate, uint flags);
    [DllImport(Dll)] static extern int moonshine_transcribe_stream(int t, int s, uint flags, out Transcripcion* transcript);
    [DllImport(Dll)] static extern IntPtr moonshine_error_to_string(int error);
}
