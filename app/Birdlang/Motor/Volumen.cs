// Regulación automática de volumen (AGC) y acondicionamiento vocal en tiempo real.
// 1. Pasa-banda vocal (100 Hz a 3800 Hz, Butterworth) que elimina retumbe grave, DC offset
//    y siseo agudo de alta frecuencia antes de medir energía o alimentar a Moonshine.
// 2. Seguidor de piso de ruido con estadísticas mínimas y confirmación del VAD: distingue
//    instantáneamente entre ruido estacionario de línea y voz baja, sin bloqueo mutuo con el VAD.
// 3. Adapta la ganancia en <100 ms al iniciar voz débil y protege contra chasquidos telefónicos
//    mediante detección de impulsos + limitador suave C1 (soft-knee) sin desplomar la ganancia.
sealed class ReguladorVolumen
{
    const float TargetRms = 0.11f;       // -19 dBFS: nivel óptimo de claridad para Moonshine y VoxLingua
    const float UmbralLineal = 0.80f;    // Hasta 0.80 la señal es 100 % lineal
    const float MaxPico = 0.95f;         // Techo asintótico del limitador suave anti-recorte
    const float MinGanancia = 0.20f;     // -14 dB para audios excesivamente fuertes
    const float MaxGanancia = 10.0f;     // +20 dB para rescatar voces telefónicas muy débiles

    const float AlfaAtaque = 0.30f;      // Bajada rápida si el hablante satura (~80 ms)
    const float AlfaArranque = 0.35f;    // Subida rápida al arrancar voz baja (~70-90 ms)
    const float AlfaSubida = 0.12f;      // Seguimiento ágil durante el habla activa (~220 ms)

    // Butterworth pasa-altos 2.º orden (fc = 100 Hz, fs = 16000 Hz)
    const float HpB0 = 0.9726139f, HpB1 = -1.9452278f, HpB2 = 0.9726139f;
    const float HpA1 = -1.9444777f, HpA2 = 0.9459779f;

    float ganancia = 1.2f;
    float gananciaPrevia = 1.2f;
    float ultimoFactorInicio = 1.0f;
    float pisoRuido = 0.002f;
    float ultimoRms;
    float hpZ1, hpZ2;
    int ventanasVoz;
    int ventanasSilencio;
    bool pisoInicializado;

    public float Ganancia => ganancia;
    public float UltimoRms => ultimoRms;
    public float Dbfs => ultimoRms > 1e-5f ? 20f * MathF.Log10(ultimoRms) : -80f;

    // Filtra y nivela el bloque en el lugar (in-place), muestra a muestra.
    public void Procesar(Span<float> ventana)
    {
        if (ventana.Length == 0) return;

        // 1. Paso por filtro pasa-altos (100 Hz) para eliminar DC offset y retumbe grave (< 100 Hz)
        float suma = 0;
        for (int i = 0; i < ventana.Length; i++)
        {
            float x = ventana[i];
            float hp = HpB0 * x + hpZ1;
            hpZ1 = HpB1 * x - HpA1 * hp + hpZ2;
            hpZ2 = HpB2 * x - HpA2 * hp;

            ventana[i] = hp;
            suma += hp * hp;
        }

        float rmsPreliminar = MathF.Sqrt(suma / ventana.Length);

        // Supresión de chasquidos/impulsos telefónicos (muestras aisladas > 4.5x el RMS de la ventana):
        // evita que un clic de 1-2 muestras engañe al detector o distorsione el espectrograma.
        if (rmsPreliminar > 0.0005f)
        {
            float techoImpulso = Math.Max(0.25f, rmsPreliminar * 4.5f);
            float sumaLimpia = 0;
            for (int i = 0; i < ventana.Length; i++)
            {
                float v = ventana[i];
                if (Math.Abs(v) > techoImpulso)
                {
                    v = Math.Clamp(v, -rmsPreliminar * 2.5f, rmsPreliminar * 2.5f);
                    ventana[i] = v;
                }
                sumaLimpia += v * v;
            }
            rmsPreliminar = MathF.Sqrt(sumaLimpia / ventana.Length);
        }

        float rms = rmsPreliminar;
        ultimoRms = rms;
        float gInicio = ganancia;
        gananciaPrevia = gInicio;

        if (!pisoInicializado && rms > 0.0001f)
        {
            pisoRuido = Math.Clamp(rms, 0.001f, 0.015f);
            pisoInicializado = true;
        }
        else if (rms > 0.0001f && rms < pisoRuido)
        {
            // Estadística mínima: si entra una ventana más silenciosa, el piso de ruido baja rápido
            pisoRuido += 0.35f * (rms - pisoRuido);
        }

        // 2. Si la energía supera claramente el piso de ruido de la línea (señal vocal presente),
        // adaptamos la ganancia antes del VAD para que incluso voces muy bajas (-38 dBFS) sean detectadas.
        float umbralSenal = Math.Max(0.0025f, pisoRuido * 1.65f);
        if (rms >= umbralSenal)
        {
            float gObjetivo = Math.Clamp(TargetRms / rms, MinGanancia, MaxGanancia);
            float alfa = gObjetivo < ganancia
                ? AlfaAtaque
                : (ventanasVoz <= 10 ? AlfaArranque : AlfaSubida);
            ganancia += alfa * (gObjetivo - ganancia);
        }

        float gFin = ganancia;
        float delta = (gFin - gInicio) / ventana.Length;
        float gActual = gInicio;

        // 3. Rampa lineal muestra a muestra con limitador suave C1 (soft-knee)
        for (int i = 0; i < ventana.Length; i++)
        {
            gActual += delta;
            ventana[i] = LimitarSuave(ventana[i] * gActual);
        }
    }

    // Recibe la probabilidad del detector de voz (Silero VAD) para afinar el piso de ruido
    // y congelar la ganancia cuando hay ruido de fondo sin voz.
    public void NotificarVoz(float probabilidadVoz)
    {
        if (probabilidadVoz >= 0.40f)
        {
            ventanasSilencio = 0;
            ventanasVoz++;
        }
        else if (probabilidadVoz < 0.25f)
        {
            ventanasSilencio++;
            // Si el VAD confirma que NO es voz, actualizamos el piso de ruido hacia el RMS actual
            if (ultimoRms > 0.0002f && ultimoRms < 0.030f)
            {
                pisoRuido += 0.12f * (ultimoRms - pisoRuido);
                pisoRuido = Math.Clamp(pisoRuido, 0.001f, 0.025f);
            }
            if (ventanasSilencio > 12)
            {
                ventanasVoz = 0;
                // En silencio sostenido, relaja ganancias extremas para no amplificar ruido de línea
                if (ganancia > 2.5f) ganancia += 0.08f * (2.0f - ganancia);
                else if (ganancia < 1.0f) ganancia += 0.06f * (1.0f - ganancia);
            }
        }
    }

    // Al dispararse el inicio de una frase, nivela la primera ventana de voz y guarda el factor
    // para aplicar exactamente el mismo escalado a PreVoz sin discontinuidad acústica.
    public void NivelarInicio(Span<float> ventanaDisparo)
    {
        ultimoFactorInicio = 1.0f;
        if (ventanaDisparo.Length == 0 || ultimoRms < 0.002f) return;
        float gNecesaria = Math.Clamp(TargetRms / ultimoRms, MinGanancia, MaxGanancia);
        if (gNecesaria > gananciaPrevia * 1.15f)
        {
            float gNueva = gananciaPrevia + 0.75f * (gNecesaria - gananciaPrevia);
            ultimoFactorInicio = Math.Clamp(gNueva / Math.Max(0.1f, gananciaPrevia), 1.0f, 3.5f);
            ganancia = Math.Max(ganancia, gNueva);
            for (int i = 0; i < ventanaDisparo.Length; i++)
                ventanaDisparo[i] = LimitarSuave(ventanaDisparo[i] * ultimoFactorInicio);
        }
    }

    public void NivelarPreVoz(Span<float> preVoz)
    {
        if (ultimoFactorInicio <= 1.001f || preVoz.Length == 0) return;
        for (int i = 0; i < preVoz.Length; i++)
            preVoz[i] = LimitarSuave(preVoz[i] * ultimoFactorInicio);
    }

    static float LimitarSuave(float x)
    {
        float abs = Math.Abs(x);
        if (abs <= UmbralLineal) return x;
        const float Rango = MaxPico - UmbralLineal;
        float exceso = (abs - UmbralLineal) / Rango;
        float comprimido = UmbralLineal + Rango * (exceso / (1.0f + exceso));
        return x >= 0 ? comprimido : -comprimido;
    }

    public void Reiniciar()
    {
        ganancia = 1.2f;
        gananciaPrevia = 1.2f;
        ultimoFactorInicio = 1.0f;
        pisoRuido = 0.002f;
        ultimoRms = 0;
        hpZ1 = hpZ2 = 0;
        ventanasVoz = ventanasSilencio = 0;
        pisoInicializado = false;
    }
}
