using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;

// Moonshine trae su propio onnxruntime.dll (1.23). Se carga desde la carpeta
// del programa antes que nada, y ONNX Runtime para C# (identificador y
// detector de voz) usa ese mismo.
static class Nativos
{
    static bool cargado;

    [DllImport("ucrtbase.dll", EntryPoint = "_wputenv", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    static extern int WPutEnv(string env);

    public static void UsarMoonshine()
    {
        if (cargado) return;
        // Limita hilos de ONNX Runtime para evitar contención de CPU en arranque dual.
        Environment.SetEnvironmentVariable("MOONSHINE_ORT_SINGLE_THREAD", "1");
        Environment.SetEnvironmentVariable("OMP_NUM_THREADS", "2");
        try { WPutEnv("MOONSHINE_ORT_SINGLE_THREAD=1"); WPutEnv("OMP_NUM_THREADS=2"); } catch { }
        var ort = Cargar("onnxruntime.dll");
        var moonshine = Cargar("moonshine.dll");
        Resolver(typeof(InferenceSession).Assembly, "onnxruntime", ort);
        Resolver(typeof(Nativos).Assembly, "moonshine", moonshine);
        cargado = true;
    }

    public static string Ruta(params string[] partes) => Path.Combine([AppContext.BaseDirectory, .. partes]);

    static IntPtr Cargar(string dll) => NativeLibrary.Load(Ruta("nativos", "moonshine", dll));

    static void Resolver(Assembly ensamblado, string nombre, IntPtr modulo) =>
        NativeLibrary.SetDllImportResolver(ensamblado, (n, _, _) => n == nombre ? modulo : IntPtr.Zero);
}
