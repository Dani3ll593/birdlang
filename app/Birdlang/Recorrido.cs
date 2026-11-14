using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// Lo que se ve de una frase: lo que Moonshine ya cerró en una pausa, con los
// números en cifras, y lo que sigue abierto, en palabras.
record Vista(int Frase, string Idioma, string Cerrado, string Abierto);

// Solo números, nunca texto de la llamada.
record Medidas(int FrasesEn, int FrasesEs, double Escuchado, double? Demora, double? Demora90, double? DemoraPeor,
               double Atraso, double AtrasoMax, double AudioPerdido, int BloquesPerdidos, double PasadaMasLentaMs);

// El recorrido del audio (README §3.1): captura del navegador, detector de
// voz, identificador con 1,5 s de voz y el motor de ese idioma. Todo queda en
// memoria; no se escribe nada en disco.
sealed class Recorrido : IDisposable
{
    public const int Tasa = 16000;
    const float UmbralVoz = 0.5f, UmbralSilencio = 0.40f;
    const double PreVoz = 0.3;        // audio previo que se agrega al empezar la frase
    const double SilencioFin = 0.42;  // silencio que cierra una frase
    const double SilencioFinNumero = 1.2; // si el texto termina en un número ("877 … 236 … 4471")
    const double FraseMaxima = 20;
    const double Evaluacion1Duracion = 1.45; // duración de frase para la 1.ª evaluación
    const double Evaluacion1Voz = 1.0;      // voz activa mínima requerida para evaluar
    const double Evaluacion2Duracion = 1.85; // duración de frase para la decisión definitiva
    const double Evaluacion2Voz = 1.4;      // o voz activa acumulada para decidir definitivo

    readonly Dictionary<string, Moonshine> motores;
    readonly Identificador lid;
    readonly Vad vad;
    readonly ReguladorVolumen volumen = new();
    long recibidas, perdidasMuestras;
    int bloquesPerdidos;

    // Se llama desde el hilo del recorrido.
    public event Action<Vista>? Cambio;

    readonly object cerrojo = new();
    readonly List<double> demoras = [];
    int frasesEn, frasesEs;
    double escuchado, atraso, atrasoMax;

    public Recorrido(string tamano)
    {
        Nativos.UsarMoonshine();
        // Sin copias del audio ni recortes prematuros del VAD interno de Moonshine
        // (Silero VAD ya segmenta las frases afuera; vad_max_segment_duration corta tramos
        // continuos largos a los 9 s y max_tokens_per_second frena bucles del decodificador).
        var opcionesMotor = new[]
        {
            ("return_audio_data", "false"),
            ("vad_threshold", "0"),
            ("vad_max_segment_duration", "9"),
            ("max_tokens_per_second", "5.0")
        };
        motores = new[] { "es", "en" }.ToDictionary(i => i, i => new Moonshine(
            Nativos.Ruta("modelos", "moonshine", $"{tamano}-streaming-{i}"), tamano == "small", opcionesMotor));
        lid = new Identificador(Nativos.Ruta("modelos", "lid"));
        vad = new Vad(Nativos.Ruta("modelos", "silero_vad.onnx"));
        // Calentamiento: la primera pasada es lenta.
        foreach (var m in motores.Values)
        {
            m.Empezar();
            m.Agregar(new float[2 * Tasa]);
            m.Terminar();
        }
        lid.Decidir(new float[2 * Tasa]);
    }

    int fraseActual;
    bool enFraseActiva;
    volatile string? peticionForzarIdioma;
    Channel<(float[] Audio, double Llegada)>? canalActivo;

    public bool EsFraseActiva(int num) => enFraseActiva && num == fraseActual;

    public void ForzarIdioma(string? idioma = null)
    {
        peticionForzarIdioma = idioma ?? "toggle";
        canalActivo?.Writer.TryWrite((Array.Empty<float>(), 0));
    }

    public float DbfsActual => volumen.Dbfs;
    public float GananciaActual => volumen.Ganancia;

    public Medidas Medir()
    {
        lock (cerrojo)
        {
            var orden = demoras.Order().ToList();
            double? Percentil(double p) => orden.Count == 0 ? null : orden[Math.Max(0, (int)Math.Ceiling(p * orden.Count) - 1)];
            long pMuestras = Interlocked.Read(ref perdidasMuestras);
            int bPerdidos = Volatile.Read(ref bloquesPerdidos);
            double perdidosSeg = (double)pMuestras / Tasa;
            double pasadaMax = motores.Values.Count == 0 ? 0 : motores.Values.Max(m => m.PasadaMasLentaMs);
            return new(frasesEn, frasesEs, escuchado, Percentil(0.5), Percentil(0.9), Percentil(1),
                       atraso, atrasoMax, perdidosSeg, bPerdidos, pasadaMax);
        }
    }

    public void Reiniciar()
    {
        lock (cerrojo)
        {
            demoras.Clear();
            volumen.Reiniciar();
            (frasesEn, frasesEs, escuchado, atraso, atrasoMax) = (0, 0, 0, 0, 0);
            Interlocked.Exchange(ref perdidasMuestras, 0);
            Interlocked.Exchange(ref bloquesPerdidos, 0);
            foreach (var m in motores.Values) m.ReiniciarMetricas();
            peticionForzarIdioma = null;
            enFraseActiva = false;
            fraseActual = 0;
        }
    }

    // Escucha al proceso del navegador hasta que se cancele.
    public async Task Escuchar(int proceso, CancellationToken cancelar)
    {
        // ~10 s de audio en bloques de 10 ms (~640 KB). Con 2 s, en un equipo de
        // 2 núcleos las pasadas lentas del motor (hasta 5,7 s) tiraban el 1,4 % del audio;
        // así un pico atrasa los subtítulos unos segundos en vez de perder palabras.
        var opciones = new BoundedChannelOptions(1000)
        {
            SingleWriter = true,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        };
        var canal = Channel.CreateBounded<(float[] Audio, double Llegada)>(opciones, itemDropped: bloque =>
        {
            if (bloque.Audio != null && bloque.Audio.Length > 0)
            {
                Interlocked.Add(ref perdidasMuestras, bloque.Audio.Length);
                Interlocked.Increment(ref bloquesPerdidos);
                Array.Clear(bloque.Audio);
            }
        });
        canalActivo = canal;
        // Si el navegador deja de mandar audio, nada despierta al lector y la frase
        // quedaría abierta hasta que vuelva el sonido: un bloque vacío cada 250 ms
        // lo despierta para que la cierre (como en ForzarIdioma).
        using var latido = new Timer(_ => canal.Writer.TryWrite((Array.Empty<float>(), 0)), null, 250, 250);
        var reloj = Stopwatch.StartNew();
        recibidas = 0;
        await using var grabador = await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)proceso)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(Tasa, 1))
            .BuildAsync();
        grabador.DataAvailable += (buffer, flags, _, _) =>
        {
            var muestras = MemoryMarshal.Cast<byte, float>(buffer).ToArray();
            if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(muestras);
            Interlocked.Add(ref recibidas, muestras.Length);
            canal.Writer.TryWrite((muestras, reloj.Elapsed.TotalSeconds));
        };

        // Estado de la frase en curso.
        const int CapacidadFrase = (int)(FraseMaxima * Tasa);
        var frase = GC.AllocateArray<float>(CapacidadFrase, pinned: true);
        var pin = GCHandle.Alloc(frase, GCHandleType.Pinned);
        var ptrFrase = pin.AddrOfPinnedObject();
        var bytesFrase = (nuint)(CapacidadFrase * sizeof(float));
        Privacidad.BloquearMemoria(ptrFrase, bytesFrase);
        int fraseLongitud = 0;
        bool enFrase = false;
        var pendiente = new List<float>();
        var previo = new Queue<float>();
        int pre = 0, silencio = 0, numero = 0;
        long procesadas = 0;
        double llegadaVoz = 0, ultimaLlegada = 0, ciclo = 0;
        string? activo = null;
        bool conTexto = false, esperaNumero = false;
        Vista? mostrada = null;

        // Búfer preasignado de voz activa para el identificador (sin PreVoz ni pausas).
        const int MuestrasLidMax = 2 * Tasa; // 32.000 floats = 2,0 s
        var audioLid = new float[MuestrasLidMax];
        int lidLongitud = 0;
        bool evaluado1 = false;

        // Búfer y estado para detectar cambios de idioma tras micropausa (interrupciones).
        const int MuestrasInterrupcion = (int)(1.5 * Tasa); // 24.000 floats
        var audioInterrupcion = new float[MuestrasInterrupcion];
        var ventanaAtenuada = new float[Vad.Ventana];
        int vozInterrupcion = 0, posPausa = -1, ventanasVozFrase = 0;
        List<(string Texto, bool Completa)>? lineasEnPausa = null;

        void Empezar(double llegada)
        {
            previo.CopyTo(frase, 0);
            fraseLongitud = previo.Count;
            pre = fraseLongitud;
            previo.Clear();
            volumen.NivelarPreVoz(frase.AsSpan(0, fraseLongitud));
            enFrase = true;
            enFraseActiva = true;
            llegadaVoz = llegada;
            (activo, conTexto, silencio, mostrada, esperaNumero) = (null, false, 0, null, false);
            lidLongitud = 0;
            ventanasVozFrase = 0;
            evaluado1 = false;
            posPausa = -1;
            lineasEnPausa = null;
            vozInterrupcion = 0;
            numero++;
            fraseActual = numero;

            string? preForzado = Interlocked.Exchange(ref peticionForzarIdioma, null);
            if (preForzado is "en" or "es")
            {
                activo = preForzado;
                evaluado1 = true;
                motores[activo].Empezar();
                motores[activo].Agregar(frase.AsSpan(0, fraseLongitud));
            }
            else
            {
                var span = frase.AsSpan(0, fraseLongitud);
                foreach (var m in motores.Values)
                {
                    m.Empezar();
                    m.Agregar(span);
                }
            }
        }

        void Mostrar()
        {
            var lineas = motores[activo!].Lineas;
            var vista = new Vista(numero, activo!,
                string.Join(" ", lineas.Where(l => l.Completa).Select(l => Cifras.EnTexto(l.Texto, activo!))),
                string.Join(" ", lineas.Where(l => !l.Completa).Select(l => l.Texto)));
            if (vista == mostrada || vista.Cerrado.Length + vista.Abierto.Length == 0) return;
            mostrada = vista;
            Cambio?.Invoke(vista);
        }

        void Actualizar(bool ahora = false)
        {
            if (motores[activo!].Avanzar(ahora) is not string texto) return;
            if (!conTexto && texto.Length > 0)
            {
                conTexto = true;
                lock (cerrojo) demoras.Add(reloj.Elapsed.TotalSeconds - llegadaVoz);
            }
            Mostrar();
        }

        // El identificador decide con voz activa limpia; si el margen entre inglés y
        // español es estrecho (< 0,50), espera a la 2.ª evaluación antes de descartar el motor opuesto.
        void Decidir(bool forzar = false)
        {
            var voz = lidLongitud >= Tasa
                ? audioLid.AsSpan(0, lidLongitud)
                : frase.AsSpan(pre, Math.Min(fraseLongitud - pre, (int)(Evaluacion1Duracion * Tasa)));
            if (voz.Length < Tasa)
            {
                var completa = new float[Tasa];
                voz.CopyTo(completa);
                voz = completa;
            }
            var (idioma, margen) = lid.Evaluar(voz);
            if (!forzar && margen < 0.50f) return;

            activo = idioma;
            motores[activo == "en" ? "es" : "en"].Descartar();
            motores[activo].Texto();
            if (!conTexto && motores[activo].Lineas.Count > 0)
            {
                conTexto = true;
                lock (cerrojo) demoras.Add(reloj.Elapsed.TotalSeconds - llegadaVoz);
            }
            Mostrar();
        }

        // Quien dicta un teléfono hace pausas entre grupos ("877 … 236 … 4471"). Si
        // la frase se cierra en esa pausa, Moonshine en inglés inventa los dígitos
        // que faltan ("877-444-4444"). Se mira el texto al día, no el de hace 0,5 s.
        bool TerminaEnNumero()
        {
            if (activo == null) Decidir(forzar: true);
            else Actualizar(ahora: true);
            var lineas = motores[activo!].Lineas;
            return lineas.Count > 0 && Cifras.TerminaEnNumero(lineas[^1].Texto, activo!);
        }

        double FinDeFrase() => esperaNumero ? SilencioFinNumero : SilencioFin;

        void CambiarIdiomaInterrupcion(string nuevoIdioma)
        {
            if (lineasEnPausa != null)
            {
                // La frase anterior termina aquí: se cierra entera, con sus números en cifras
                // (si quedara abierta, un teléfono no llegaría al Dial-Out).
                var vistaPausa = new Vista(numero, activo!,
                    string.Join(" ", lineasEnPausa.Select(l => Cifras.EnTexto(l.Texto, activo!))), "");
                if (vistaPausa.Cerrado.Length + vistaPausa.Abierto.Length > 0)
                {
                    mostrada = vistaPausa;
                    Cambio?.Invoke(vistaPausa);
                }
            }
            lock (cerrojo)
            {
                if (activo == "en") frasesEn++;
                else frasesEs++;
            }
            motores[activo!].Descartar();

            int rescateLongitud = Math.Max(0, fraseLongitud - posPausa);
            var rescate = new float[rescateLongitud];
            frase.AsSpan(posPausa, rescateLongitud).CopyTo(rescate);

            frase.AsSpan(0, fraseLongitud).Clear();
            rescate.CopyTo(frase, 0);
            fraseLongitud = rescateLongitud;
            pre = 0;
            numero++;
            fraseActual = numero;
            enFraseActiva = true;
            activo = nuevoIdioma;
            llegadaVoz = reloj.Elapsed.TotalSeconds - (double)rescateLongitud / Tasa;
            (conTexto, silencio, mostrada) = (false, 0, null);
            posPausa = -1;
            lineasEnPausa = null;
            vozInterrupcion = 0;
            audioInterrupcion.AsSpan().Clear();
            audioLid.AsSpan().Clear();
            lidLongitud = 0;
            ventanasVozFrase = Math.Max(1, rescateLongitud / Vad.Ventana);
            evaluado1 = false;

            motores[activo].Empezar();
            motores[activo].Agregar(frase.AsSpan(0, fraseLongitud));
            Actualizar();
        }

        void Terminar()
        {
            // Si fue un ruido corto, chasquido o tos (< 5 ventanas = 160 ms de voz real) y aún no emitió texto,
            // se descarta el stream sin obligar a Moonshine a alucinar sobre ruido.
            if (ventanasVozFrase < 5 && !conTexto)
            {
                foreach (var m in motores.Values) m.Descartar();
                numero--;
            }
            else
            {
                if (activo == null) Decidir(forzar: true); // decide al cerrar
                motores[activo!].Terminar();
                Mostrar();
                lock (cerrojo)
                {
                    if (activo == "en") frasesEn++;
                    else frasesEs++;
                }
            }
            frase.AsSpan(0, fraseLongitud).Clear();
            fraseLongitud = 0;
            enFrase = false;
            enFraseActiva = false;
            previo.Clear();
            audioLid.AsSpan(0, lidLongitud).Clear();
            lidLongitud = 0;
            ventanasVozFrase = 0;
            evaluado1 = false;
            posPausa = -1;
            lineasEnPausa = null;
            vozInterrupcion = 0;
            audioInterrupcion.AsSpan().Clear();
        }

        void ProcesarPeticionForzar()
        {
            if (peticionForzarIdioma == null || !enFrase) return;
            string? peticion = Interlocked.Exchange(ref peticionForzarIdioma, null);
            if (peticion == null) return;

            string nuevoIdioma;
            if (peticion == "toggle")
            {
                if (activo == "en") nuevoIdioma = "es";
                else if (activo == "es") nuevoIdioma = "en";
                else nuevoIdioma = "es";
            }
            else if (peticion is "en" or "es")
            {
                nuevoIdioma = peticion;
            }
            else return;

            if (nuevoIdioma == activo && activo != null) return;

            string idiomaViejo = activo ?? (nuevoIdioma == "en" ? "es" : "en");

            // Limpiar la línea de la banda equivocada emitiendo una vista vacía
            Cambio?.Invoke(new Vista(numero, idiomaViejo, "", ""));

            if (activo != null)
            {
                motores[activo].Descartar();
            }
            else
            {
                motores[idiomaViejo].Descartar();
            }

            activo = nuevoIdioma;
            evaluado1 = true;
            mostrada = null;
            posPausa = -1;
            lineasEnPausa = null;
            vozInterrupcion = 0;

            motores[activo].Empezar();
            if (fraseLongitud > 0)
            {
                motores[activo].Agregar(frase.AsSpan(0, fraseLongitud));
            }
            motores[activo].Texto();
            if (!conTexto && motores[activo].Lineas.Count > 0)
            {
                conTexto = true;
                lock (cerrojo) demoras.Add(reloj.Elapsed.TotalSeconds - llegadaVoz);
            }
            Mostrar();
        }

        grabador.StartRecording();
        try
        {
            while (!cancelar.IsCancellationRequested && await canal.Reader.WaitToReadAsync(cancelar))
            {
                while (canal.Reader.TryRead(out var bloque))
                {
                    if (peticionForzarIdioma != null) ProcesarPeticionForzar();
                    if (bloque.Audio.Length == 0) continue;

                    ultimaLlegada = bloque.Llegada;
                    pendiente.AddRange(bloque.Audio);
                    Array.Clear(bloque.Audio);
                    int usadas = 0;
                    for (; usadas + Vad.Ventana <= pendiente.Count; usadas += Vad.Ventana)
                    {
                        if (peticionForzarIdioma != null) ProcesarPeticionForzar();
                        var ventana = CollectionsMarshal.AsSpan(pendiente).Slice(usadas, Vad.Ventana);
                        volumen.Procesar(ventana);
                        float p = vad.Probabilidad(ventana);
                        volumen.NotificarVoz(p);
                        if (!enFrase && p >= UmbralVoz)
                        {
                            volumen.NivelarInicio(ventana);
                            Empezar(bloque.Llegada);
                        }
                        if (!enFrase)
                        {
                            foreach (var v in ventana) previo.Enqueue(v);
                            while (previo.Count > PreVoz * Tasa) previo.Dequeue();
                        }
                        else
                        {
                            int copia = Math.Min(ventana.Length, CapacidadFrase - fraseLongitud);
                            if (copia > 0)
                            {
                                ventana[..copia].CopyTo(frase.AsSpan(fraseLongitud));
                                fraseLongitud += copia;
                            }
                            bool esSilencio = p < UmbralSilencio;
                            if (!esSilencio) ventanasVozFrase++;

                            // Atenuar progresivamente colas de silencio (>128 ms) antes de Moonshine
                            // para evitar que el decodificador alucine coletillas sobre ruido de fondo.
                            ReadOnlySpan<float> entradaMotor = ventana;
                            if (esSilencio && silencio >= 4)
                            {
                                float atenuacion = Math.Max(0f, 1f - (silencio - 3) * 0.22f);
                                for (int k = 0; k < Vad.Ventana; k++) ventanaAtenuada[k] = ventana[k] * atenuacion;
                                entradaMotor = ventanaAtenuada;
                            }

                            if (activo != null)
                            {
                                motores[activo].Agregar(entradaMotor);
                                Actualizar();

                                // Detección de pausa (desde 6 ventanas, ~0,20 s, hasta que la frase se cierra) e interrupción de idioma
                                if (!esSilencio)
                                {
                                    if (silencio >= 6 && posPausa < 0)
                                    {
                                        posPausa = fraseLongitud - copia;
                                        lineasEnPausa = [..motores[activo].Lineas];
                                        vozInterrupcion = 0;
                                    }
                                    if (posPausa >= 0)
                                    {
                                        if (vozInterrupcion + Vad.Ventana <= audioInterrupcion.Length)
                                        {
                                            ventana.CopyTo(audioInterrupcion.AsSpan(vozInterrupcion));
                                            vozInterrupcion += Vad.Ventana;
                                        }
                                        if (vozInterrupcion >= (int)(1.2 * Tasa))
                                        {
                                            var (nuevo, margen) = lid.Evaluar(audioInterrupcion.AsSpan(0, vozInterrupcion));
                                            if (nuevo != activo && margen >= 0.80f)
                                            {
                                                CambiarIdiomaInterrupcion(nuevo);
                                            }
                                            else
                                            {
                                                posPausa = -1;
                                                lineasEnPausa = null;
                                                vozInterrupcion = 0;
                                            }
                                        }
                                    }
                                }
                                // Otra pausa no reinicia la detección: en "Sí, correcto" la coma tras
                                // "Sí" haría perder el punto donde cambió el hablante.
                            }
                            else
                            {
                                motores["en"].Agregar(entradaMotor);
                                motores["es"].Agregar(entradaMotor);
                                if (!esSilencio && lidLongitud + Vad.Ventana <= audioLid.Length)
                                {
                                    ventana.CopyTo(audioLid.AsSpan(lidLongitud));
                                    lidLongitud += Vad.Ventana;
                                }

                                int duracion = fraseLongitud - pre;
                                if (duracion >= (int)(Evaluacion2Duracion * Tasa) || lidLongitud >= (int)(Evaluacion2Voz * Tasa))
                                {
                                    Decidir(forzar: true);
                                }
                                else if (!evaluado1 && duracion >= (int)(Evaluacion1Duracion * Tasa) && lidLongitud >= (int)(Evaluacion1Voz * Tasa))
                                {
                                    evaluado1 = true;
                                    Decidir(forzar: false);
                                }
                            }

                            silencio = esSilencio ? silencio + 1 : 0;
                            if (silencio == 0) esperaNumero = false;
                            else if (silencio * Vad.Ventana >= SilencioFin * Tasa && !esperaNumero && ventanasVozFrase >= 5)
                                esperaNumero = TerminaEnNumero();
                            if (silencio * Vad.Ventana >= FinDeFrase() * Tasa || fraseLongitud >= CapacidadFrase) Terminar();
                        }
                        procesadas += Vad.Ventana;
                    }
                    CollectionsMarshal.AsSpan(pendiente)[..usadas].Clear();
                    pendiente.RemoveRange(0, usadas);
                }

                // Si el navegador deja de mandar audio, la frase termina.
                if (enFrase && reloj.Elapsed.TotalSeconds - ultimaLlegada > FinDeFrase() + 0.2) Terminar();
                double ahora = reloj.Elapsed.TotalSeconds;
                lock (cerrojo)
                {
                    long rec = Interlocked.Read(ref recibidas);
                    long perd = Interlocked.Read(ref perdidasMuestras);
                    atraso = Math.Max(0.0, (double)(rec - procesadas - perd) / Tasa);
                    atrasoMax = Math.Max(atrasoMax, atraso);
                    escuchado += ahora - ciclo;
                }
                ciclo = ahora;
            }
        }
        catch (OperationCanceledException)
        {
            // Salida limpia al cancelar la escucha.
        }
        finally
        {
            grabador.StopRecording();
            canal.Writer.TryComplete();
            if (enFrase) Terminar();
            foreach (var m in motores.Values) m.Descartar();
            CollectionsMarshal.AsSpan(pendiente).Clear();
            frase.AsSpan().Clear();
            Privacidad.DesbloquearMemoria(ptrFrase, bytesFrase);
            pin.Free();
            audioLid.AsSpan().Clear();
            audioInterrupcion.AsSpan().Clear();
            ventanaAtenuada.AsSpan().Clear();
            canalActivo = null;
            enFraseActiva = false;
        }
    }

    public void Dispose()
    {
        foreach (var m in motores.Values) m.Dispose();
        lid.Dispose();
        vad.Dispose();
    }
}
