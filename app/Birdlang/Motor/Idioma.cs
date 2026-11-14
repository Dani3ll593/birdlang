using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

// Identificador de idioma (VoxLingua107 ECAPA). Solo compara inglés contra español.
sealed class Identificador : IDisposable
{
    readonly InferenceSession sesion;
    readonly int en, es;

    public Identificador(string carpeta)
    {
        using var opt = new SessionOptions
        {
            IntraOpNumThreads = 2,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        sesion = new InferenceSession(Path.Combine(carpeta, "lid-ecapa-voxlingua107.int8.onnx"), opt);
        var etiquetas = File.ReadAllLines(Path.Combine(carpeta, "lid-labels.txt"));
        en = Array.IndexOf(etiquetas, "en");
        es = Array.IndexOf(etiquetas, "es");
    }

    public (string Idioma, float Margen) Evaluar(ReadOnlySpan<float> audio)
    {
        var entrada = new DenseTensor<float>(audio.ToArray(), [1, audio.Length]);
        using var r = sesion.Run([NamedOnnxValue.CreateFromTensor("wav", entrada)]);
        var logp = r.First().AsTensor<float>();
        float diff = logp[0, en] - logp[0, es];
        return diff >= 0 ? ("en", diff) : ("es", -diff);
    }

    public string Decidir(ReadOnlySpan<float> audio) => Evaluar(audio).Idioma;

    public void Dispose() => sesion.Dispose();
}

// Detector de voz Silero v6: corta el audio en frases.
sealed class Vad : IDisposable
{
    public const int Ventana = 512; // 32 ms a 16 kHz
    readonly InferenceSession sesion;
    readonly DenseTensor<long> tasa = new(new long[] { Recorrido.Tasa }, ReadOnlySpan<int>.Empty);
    readonly float[] entrada = new float[64 + Ventana]; // 64 muestras de contexto + 512 nuevas
    float[] estado = new float[2 * 128];

    public Vad(string modelo)
    {
        using var opt = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        sesion = new InferenceSession(modelo, opt);
    }

    // Probabilidad de voz en la siguiente ventana de 512 muestras.
    public float Probabilidad(ReadOnlySpan<float> ventana)
    {
        Array.Copy(entrada, Ventana, entrada, 0, 64);
        ventana.CopyTo(entrada.AsSpan(64));
        using var r = sesion.Run([
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(entrada, [1, entrada.Length])),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(estado, [2, 1, 128])),
            NamedOnnxValue.CreateFromTensor("sr", tasa)]);
        var salidas = r.ToList();
        estado = salidas[1].AsTensor<float>().ToArray();
        return salidas[0].AsTensor<float>().GetValue(0);
    }

    public void Dispose() => sesion.Dispose();
}
