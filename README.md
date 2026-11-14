# Birdlang: mesa de trabajo para intérpretes (español ↔ inglés)

**[English summary](#english-summary)** · Página:
[dani3ll593.github.io/birdlang](https://dani3ll593.github.io/birdlang/)

Programa de escritorio para intérpretes que trabajan por teléfono o video
(OPI/VRI). Muestra en vivo los subtítulos del audio de la llamada, en inglés y
en español, con los números en cifras, y trae un glosario, un buscador de
calles de EE. UU. y un cuaderno de notas. **No traduce, no se conecta a
internet y no guarda nada de la llamada.**

- **Descargar:** en [Releases](https://github.com/Dani3ll593/birdlang/releases).
  `Birdlang-<versión>.zip` trae el actualizador; `Birdlang-Empresas-<versión>.zip`
  va sin él. Es una carpeta portable: se descomprime y se abre `Birdlang.exe`,
  sin instalar nada.
- **Sistema:** Windows 10 22H2 u 11, de 64 bits (§2). La versión para Mac está
  en desarrollo.
- **Sin firma digital, por ahora:** al abrirlo, Windows puede mostrar un aviso
  de SmartScreen. Para comprobar la descarga, compara su SHA-256 con
  `SHA256SUMS.txt`, en la misma versión (§7).
- **Documentos:** [privacidad](PRIVACY.md) ·
  [ficha para empresas](SEGURIDAD_EMPRESAS.md) ·
  [reportar un problema de seguridad](SECURITY.md) ·
  [cómo colaborar](CONTRIBUTING.md) · [código de conducta](CODE_OF_CONDUCT.md)
- **Licencias:** el código, [Apache-2.0](LICENSE); los datos del glosario y de
  calles, CC BY-SA 4.0; los códigos postales de GeoNames, CC BY 4.0. Los
  componentes de terceros están en [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## 1. Las reglas que mandan

**Para quién:** intérpretes latinoamericanos que trabajan para el mercado
estadounidense, por teléfono o video (OPI/VRI), en salud, seguros, finanzas y
temas legales. La plataforma de llamadas corre en Chrome, Edge o Brave.

Lo que prohíben las empresas de interpretación, y lo que por eso hace Birdlang:

| Prohibido | Lo que hace Birdlang |
|---|---|
| Guardar notas | Las notas **viven solo en la RAM** y se borran al cerrar la sesión. No hay botón de guardar |
| Exportar | **No exporta nada**: ni notas, ni subtítulos, ni historial |
| Traducción con IA | **No traduce.** No hay modelos de lenguaje ni traducción automática |
| Ejecución web | **Programa nativo**: sin navegador, sin WebView y **sin código de red**. No se conecta a nada |

**Los únicos componentes de IA son tres modelos locales ejecutados en el procesador**:
- el reconocimiento de voz (**Moonshine Streaming**);
- la detección de voz (**Silero VAD**);
- la identificación de idioma (**VoxLingua107 ECAPA**).

Corren **sin conexión**, dentro del programa; no traducen, solo transcriben e
identifican turnos, y lo que oyen no sale del equipo ni queda en disco.

Funciona en Windows 10 y en Windows 11. La versión para Mac está en desarrollo.

---

## 2. En qué computadoras corre

Los requisitos típicos de las empresas de interpretación remota usan el
**PassMark CPU Mark** ([cpubenchmark.net](https://www.cpubenchmark.net/cpu_test_info.html)).
Interprefy, por ejemplo, pide 5.550 en su
[página actual](https://knowledge.interprefy.com/technical-requirements-for-interpreters).

| | **Requisitos Recomendados** | **Requisitos Mínimos** (estándar de la industria) |
|---|---|---|
| Memoria RAM | 16 GB | 8 GB |
| Procesador (PassMark) | 6–8 núcleos / 12–16 hilos (~14.000+ PassMark). Ejemplos: Ryzen 7 7730U (18.600+), Core i7-1255U (13.500+) | 4 núcleos / 8 hilos (~5.500–6.500 PassMark). Ejemplos: Core i5-8250U (5.778), Ryzen 5 3500U (6.768) |
| Motor de voz | Moonshine **Small** (recomendado y por defecto) | Moonshine **Small** o **Tiny** |
| Rendimiento (Moonshine Small) | Medido en una laptop con Ryzen 7 7730U y 16 GB: **1–2 % CPU promedio** · **7–8 % peor segundo** · **596–752 MB RAM máxima** · Demora mediana **1,64–1,82 s**, 9 de cada 10 en **1,83–1,95 s**, peor caso **hasta 4,72 s** | Estimado, sin medir todavía en un equipo de esta clase: **~5–12 % CPU promedio** · **~18–25 % peor segundo** · demora mediana **menor a 2,0 s** |
| Sistema operativo | **Windows 10 22H2 u 11 (64 bits)** | igual |
| Aceleración GPU | No hace falta (inferencia 100 % en procesador) | — |

**Por debajo del mínimo (medido):** en un Core i3-4150T (2 núcleos) con 8 GB y
Moonshine Small, **15 % CPU promedio** · **30–34 % peor segundo** · **712–737 MB
RAM máxima** · demora mediana **2,13–2,25 s**. Una versión anterior perdía el
1,4 % del audio en los picos; la actual guarda más audio y falta medirla en
este equipo.

**Windows 8 y macOS High Sierra no se pueden soportar, y no por Birdlang:**
- **Chrome** ya exige Windows 10 y macOS 13. La última versión para Windows 8
  fue la 109.
- **ONNX Runtime**, el motor que ejecuta los modelos, exige Windows 10 y macOS
  13.3 ([fuente](https://onnxruntime.ai/docs/build/inferencing.html)).

---

## 3. Subtítulos con motor propio

### 3.1 El recorrido del audio

```
audio de la llamada (navegador) ─► 16 kHz ─► AGC (-20 dBFS) ─► Silero VAD (0,42 s) ─► Arranque dual (EN + ES)
                                                                                           │
    ┌──────────────────────────────────────────────────────────────────────────────────────┴───────────────┐
    │                                                                                                      │
    ▼                                                                                                      ▼
[1,45 s / 1,85 s voz activa] ─► VoxLingua107 ─► Descarta perdedor ─► Pausa (≥ 0,20 s) ─────────► Cifras.cs ─► Banda
                                (margen confianza)  (cero alucinación) (corte cambio hablante)   (números)   (UI)
```

1. **Se captura solo el audio del navegador** donde corre la plataforma (Chrome,
   Edge o Brave), no el de todo el equipo (§3.4). El
   micrófono no es una entrada: la voz del intérprete no se transcribe.
2. **Normalización AGC en tiempo real (-20 dBFS):** ganancia dinámica suave con
   limitador anti-recorte que nivela el volumen telefónico heterogéneo antes de
   los clasificadores.
3. **Detector de voz** (Silero VAD, MIT): ventanas de 32 ms a 16 kHz con umbral de
   0,40. Corta la frase tras 0,42 s de silencio continuo y purga de inmediato
   el búfer `PreVoz`. **Si el texto termina en un número, espera 1,2 s:** quien
   dicta un teléfono hace pausas entre grupos ("877 … 236 … 4471"), y si la
   frase se cerrara en esa pausa, Moonshine en inglés podría completar el
   número con dígitos inventados. En español Moonshine escribe los dígitos en
   palabras.
4. **Arranque dual optimizado:** al detectar inicio de voz, se envían las primeras
   muestras a ambos motores Moonshine (EN y ES) alimentando su stream de audio sin
   llamadas redundantes a `Avanzar()`, manteniendo la CPU en ~1 %.
5. **Decisión de idioma con VoxLingua107 ECAPA:** evalúa exclusivamente el búfer
   de ventanas con voz activa pura (sin silencios ni audio anterior al disparo).
   La primera evaluación se dispara a los 1,45 s de duración (si acumula
   1,0 s o más de voz activa pura). Si el margen de confianza es estrecho
   (menor a 0,50), realiza una segunda y definitiva evaluación a los 1,85 s
   (o al acumular 1,4 s de voz activa).
6. **Descarte instantáneo del motor perdedor:** el stream del motor no seleccionado
   se libera con `Descartar()`, y el motor ganador asume la frase completa sin
   demora acumulada.
7. **Detección de cambio de hablante tras una pausa:** si tras una pausa de al
   menos 0,20 s que no cerró la frase la voz se reanuda, y con 1,2 s de voz
   VoxLingua detecta cambio de idioma con alta confianza (margen de 0,80 o
   más), la frase previa se cierra con su texto anterior a la pausa (con los
   números en cifras) y el nuevo motor arranca con el nuevo idioma desde la
   pausa. Otra pausa no reinicia la detección: en "Sí, correcto", la coma tras
   "Sí" haría perder el punto donde cambió el hablante.
8. **Texto palabra por palabra y conversión de cifras:** cada motor emite texto
   incremental cada ~0,5 s. Al cerrarse cada tramo o frase, `Cifras.cs` convierte
   con reglas fijas las palabras numéricas a dígitos ("tres cero cinco" → "305",
   montos, fechas y unidades médicas) según el idioma de la banda. Las horas van
   con dos puntos: "seven thirty p.m." y el "7.30 p.m." que escribe Moonshine
   dan `7:30 p.m.`, y "a las diez y cuarenta y cinco", `a las 10:45`. Sin señal
   de hora ("el siete treinta de la calle Ocho") queda `730`. Moonshine en
   inglés ya escribe los teléfonos en cifras.
9. **Sin abreviar repeticiones:** el texto es eco de lo que dicen las partes
   ("No, no, no" queda así). El único freno a los bucles del decodificador es
   `max_tokens_per_second` = 5.

**Demora:** se mide desde que la persona empieza a hablar hasta que aparece su
primer texto. Con Moonshine Small en una laptop con Ryzen 7 7730U, en 3 h 32 min
de uso (1.440 frases): mediana **1,64 s**, 9 de cada 10 en **1,83 s** y peor
caso **4,72 s**, con el procesador al 2 % de promedio y 8 % en el peor segundo,
y 752 MB de RAM como máximo.

**Límite conocido:** la precisión con audio telefónico (8 kHz, español
latinoamericano) todavía no está medida.

### 3.2 El motor: Moonshine v2 Streaming

| | Por qué |
|---|---|
| **Licencia MIT, también los modelos en español** | Se puede incluir en un programa de código abierto y usar comercialmente ([licencia](https://github.com/moonshine-ai/moonshine/blob/main/LICENSE)) |
| **Precisión** | Español: Small 4,9 %, Tiny 6,2 % de error (FLEURS + MLS). Inglés: Small 7,8 %, Tiny 12 % ([modelos](https://github.com/moonshine-ai/moonshine/blob/main/docs/models/available-models.md)) |
| **Demora** | Tras terminar una frase: Tiny 69 ms, Small 165 ms en x86 ([comparación](https://github.com/moonshine-ai/moonshine/blob/main/docs/moonshine-vs-whisper.md)) |
| **Peso** | Tiny 34M y Small 123M parámetros, en int8 |
| **Integración** | API en C. Desde C# se llama directo a esa API: no hay paquete NuGet |

### 3.3 Decidir el idioma de cada frase

- **Identificador:** SpeechBrain VoxLingua107 ECAPA (Apache-2.0, 84,5 MB). Se
  limita a decidir entre inglés y español y se exporta a ONNX una sola vez.
  Corre en 2 hilos y evalúa en ~30 ms sobre ventanas de voz activa limpia.
- **Precisión:** 98–100 % con 1,45–1,85 s de audio de voz activa pura.
- **Frases cortas:** si una frase concluye antes del umbral de evaluación, se
  completa con silencio hasta 1,0 s y VoxLingua la evalúa igual.
- **Corrección manual y atajos de teclado:**
  - `F1`: fuerza de inmediato el idioma de la frase a **inglés**.
  - `F2`: fuerza de inmediato el idioma de la frase a **español**.
  - `F3` o `Ctrl + Espacio`: **invierte el idioma** de la frase en curso.
    Si la frase está activa, limpia la banda errónea y vuelve a transcribir el
    audio acumulado con el otro motor.
  - **Clic en una frase cerrada:** pasa la línea a la otra banda y vuelve a
    aplicar `Cifras.cs` con las reglas del otro idioma.
  - **Sombrear con el ratón parte de un subtítulo:** el texto se busca en el
    glosario y en calles, sin pasar por el portapapeles.
  - `F4` o `Ctrl + B`: lleva al buscador de calles. `F5` o `Ctrl + G`: al del
    glosario.

### 3.4 Captura del audio del navegador

*Loopback* por proceso: se apunta al proceso principal del navegador
(`chrome.exe`, `msedge.exe` o `brave.exe`) **incluyendo sus procesos hijos**,
porque el audio sale de un proceso hijo. Probado con Edge (Windows 10) y Brave
(Windows 11). Usa [NAudio 3.0](https://github.com/naudio/NAudio/blob/main/RELEASE_NOTES.md)
(MIT) y requiere, en la práctica, Windows 10 2004 o posterior.

---

## 4. Nada en disco

**Qué se promete, con honestidad:** Birdlang **no escribe en disco nada de lo
que se dice en la llamada ni de lo que escribe el intérprete**. El sistema sí
registra que el programa se ejecutó (en Windows, Prefetch y AmCache): eso no
es contenido.

| Por dónde se puede escapar | Qué hace Birdlang |
|---|---|
| **Paginación** | El búfer de audio de la frase activa (1,28 MB, 320.000 floats) está **anclado en RAM física con `VirtualLock`** y **se sobrescribe con ceros** (`Span.Clear()`) al terminar cada frase y al cerrar sesión. Los textos de .NET y la memoria interna del motor de voz (ONNX) no se pueden bloquear en memoria administrada; quedan protegidos de escrituras en claro mediante el cifrado de disco del sistema (**BitLocker**, que cifra el archivo de paginación `pagefile.sys`) |
| **Hibernación** | El bloqueo de memoria no la cubre. **Le toca a TI**: cifrado del disco y la hibernación apagada |
| **El motor de modelos (ONNX Runtime)** | Solo el modo de **procesador**. Sin guardar modelos optimizados, sin perfiles de rendimiento y con la memoria interna liberada (ONNX Runtime no borra lo que libera). Los modelos se cargan **desde la carpeta del programa**. Con Moonshine, nunca su función de carga automática, que **descarga a una caché** |
| **Portapapeles** | **No se puede copiar desde las notas ni desde los subtítulos.** Cualquier otra copia se marca para que no entre al historial de Windows (`Win+V`) ni a la nube. Para pasar al marcador un teléfono dictado, el **Dial-Out Seguro** lo copia y vacía el portapapeles a los 20 segundos, con un botón para vaciarlo antes. Un número que solo salió de los subtítulos lleva **«verificar»**: lo escuchó el motor de voz y hay que confirmarlo con la persona antes de marcar. La marca se va si el intérprete lo escribe en las notas |
| **Capturas, Recall y compartir pantalla** | `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. **No es una barrera absoluta**: se puede saltar inyectando código en el proceso. El intérprete puede activar el **modo Supervisión** para que un supervisor vea la pantalla: borde ámbar visible y apagado automático a los 20 minutos |
| **Corrector y predicción de texto** | Apagados en el cuadro de notas |
| **Volcados por error** | Excluido con `WerAddExcludedApplication`, y la carpeta va sin `createdump.exe`. **Ningún mensaje de error lleva texto** de las notas ni de los subtítulos |
| **Archivos temporales** | Se distribuye como **carpeta**, no como un `.exe` que se autoextrae en `%TEMP%` |

- **"Fin de sesión" borra todo:** audio en búfer, subtítulos, notas, búsquedas,
  métricas y teléfonos de Dial-Out. También se borra solo tras **15 minutos sin
  actividad** (sin voz detectada ni pulsaciones en el teclado). El cuaderno de
  notas tiene además un botón de **borrado rápido** entre llamadas.
- **No se guarda nada en disco, ni siquiera las preferencias** (país, tamaño
  de letra): viven en RAM y vuelven a sus valores iniciales al cerrar. **Única
  excepción:** en la versión Estándar, «No volver a mostrar» del aviso de
  colaboración escribe `colaborar=no` en `preferencias.txt`, junto al programa
  (al borrar la carpeta no queda rastro). La versión para empresas no muestra el
  aviso y no escribe nada.
- **Aviso «Colabora con el proyecto»** (solo Estándar, uno de cada 4 inicios al
  azar): pide a los voluntarios el bloque de «📋 Métricas» de una sesión en un
  Google Form. Ese bloque solo tiene datos técnicos (procesador, RAM, Windows,
  tiempos y conteos), sin texto ni nombres: no expone a nadie. Birdlang no abre
  el formulario ni lo envía: copia el enlace y el voluntario pega las métricas.
- **Comprobación con Process Monitor:** en sesiones de prueba de 30 y 40
  minutos, registrando todo acceso a disco y red del programa, hubo 0 eventos
  de red y 0 escrituras de archivo (sin incluir el cierre del programa). Los
  scripts para repetirla van en la carpeta `verificacion` de cada paquete
  (§7).

---

## 5. El glosario comunitario: regionalismos y modismos

**El corazón del proyecto.** Un glosario abierto, construido por intérpretes,
de lo que **no está en los glosarios oficiales**: los regionalismos de cada país
latinoamericano, el español de EE. UU. y los modismos, en los dos sentidos.

### 5.1 Qué tiene una entrada

`datos/glosario.tsv` tiene una entrada por línea, con seis columnas:

| Columna | Qué lleva |
|---|---|
| `TerminoIngles` | El término en inglés |
| `TraduccionEspanol` | Uno o más equivalentes en español, separados por `;` |
| `Categoria` | `Médico`, `Legal`, `Finanzas`, `Seguros`, `Impuestos`, `Gobierno`, `Vehículos`, `Vivienda` o `General` |
| `DefinicionNotas` | Una nota breve, con palabras propias |
| `Pais` | `Todos`, `MX`, `CO`, `PR`, `US` (español de EE. UU.), `AR`, `VE` o `Centroamérica` |
| `Fuente` | De dónde sale la entrada, para la atribución |

- **Cada país va en su propia entrada.** *Guagua* es "bus" en el Caribe y
  Canarias, y "bebé" en los Andes.
- **Los modismos van en los dos sentidos:** *beat around the bush* → *andarse
  por las ramas* / *irse por las ramas*, con el país de cada variante.
- **El español de EE. UU.** (*aseguranza*, *la troca*, *los biles*, *el lonche*,
  *parquear*) es lo que más aparece en las llamadas y lo que menos está
  documentado.

### 5.2 De dónde sale

Solo de fuentes abiertas y de dominio público:

| Fuente | Qué aporta | Licencia | Entradas |
|---|---|---|---|
| [Wikidata](https://query.wikidata.org/) | Términos y alias de 13 clases técnicas (enfermedad, síntoma, procedimiento, prueba, anatomía, medicamento, principio activo, especialidad, delito, concepto jurídico, proceso judicial, tribunal, instrumento financiero). Solo tipos ("tribunal de apelación", "bono convertible"), sin hechos concretos (nada con fecha, país o lugar) | CC0 1.0 (dominio público) | 9.962 |
| [Wiktionary vía kaikki.org](https://kaikki.org/dictionary/Spanish/) (español) | La base regional: acepciones de áreas técnicas útiles (medicina, anatomía, derecho, finanzas, seguros, gobierno, vehículos, vivienda) y términos con marca de país hispanohablante | CC BY-SA 4.0 + GFDL | 9.150 |
| [NCI Cancer.gov Dictionary](https://webapis.cancer.gov/glossary/v1/Terms/Cancer.gov/Patient/) | Glosario de oncología y medicina del Instituto Nacional del Cáncer, con definiciones en español recortadas a 200 caracteres | Dominio público federal de EE. UU. (17 U.S.C. § 105) | 7.962 |
| [Wiktionary vía kaikki.org](https://kaikki.org/dictionary/English/) (inglés) | Traducciones técnicas inglés → español de sentidos especializados (medicina, anatomía, farmacología, derecho, crimen, finanzas, seguros, impuestos, inmigración) | CC BY-SA 4.0 + GFDL | 6.533 |
| [MedlinePlus (NLM)](https://medlineplus.gov/xml.html) | Temas de salud bilingües de la Biblioteca Nacional de Medicina y sus variantes autorizadas ("also called") | Dominio público federal de EE. UU. | 2.795 |
| [CFPB](https://files.consumerfinance.gov/f/201510_cfpb_spanish-style-guide-glossary.pdf) | Glosario oficial de términos financieros y de crédito al consumo | Dominio público federal de EE. UU. | 1.888 |
| [IRS Pub 850](https://www.irs.gov/pub/irs-pdf/p850.pdf) | Glosario oficial de palabras y frases tributarias | Dominio público federal de EE. UU. | 1.331 |
| Birdlang (lista propia) | Latinismos jurídicos usuales y regionalismos de interpretación comunitaria para EE. UU. e Hispanoamérica | CC BY-SA 4.0 | 155 |
| [CMS/DOL SBC](https://www.cms.gov/cciio/resources/forms-reports-and-other-resources/downloads/uniform-glossary-final.pdf) | Glosario uniforme de cobertura y términos médicos de la ACA, con definiciones oficiales | Dominio público federal de EE. UU. | 43 |
| **Total** | | | **39.819** |

**Licencias:**
- Datos del glosario: **CC BY-SA 4.0**, obligatoria porque la base viene de
  Wiktionary. La atribución va en `datos/ATRIBUCIONES.txt`, dentro de la
  carpeta del programa.
- Código: **Apache-2.0**.

### 5.3 Cómo aporta la comunidad

- **Con el [formulario del glosario](https://github.com/Dani3ll593/birdlang/issues/new?template=glosario.yml)**
  (hace falta una cuenta de GitHub): término en inglés, equivalente en español,
  área, una nota con palabras propias y, si corresponde, país y fuente. Al
  enviarlo se acepta publicar el aporte bajo CC BY-SA 4.0.
- **Una marca de país necesita la confirmación de dos hablantes nativos.**
- **No se copian definiciones** de diccionarios ni manuales con derechos de
  autor.
- **Lo vulgar u ofensivo no se borra:** el intérprete lo necesita entender.
- **Prohibido el contenido de llamadas reales** o datos de pacientes, ni
  siquiera como ejemplo.
- Las propuestas aceptadas se agregan al glosario y llegan con la versión
  siguiente de Birdlang.

Los detalles están en [CONTRIBUTING.md](CONTRIBUTING.md).

### 5.4 Cómo llega al programa

- **El glosario va dentro de cada versión de Birdlang**, de solo lectura, y se
  carga entero en la RAM, en una tabla con índice: no hay base de datos en
  disco.
- **Para actualizarlo se baja la versión nueva.** Birdlang no se conecta a
  nada.
- La búsqueda va en los dos sentidos (inglés ↔ español), no distingue acentos
  y compara también por sonido (Double Metaphone).
- **Con un país elegido**, muestra las acepciones de ese país y las generales,
  con las del país primero.
- **Sombrear parte de un subtítulo** lo busca (§3.3).

---

## 6. Calles, sin red

Buscar en Google Maps es salir a la web y mandarle a Google una dirección, que
en salud es un dato protegido. Google Maps no está cubierto por el acuerdo
HIPAA de Google. Birdlang lo resuelve sin red:

- **Diccionario de calles y topónimos de EE. UU.** (`datos/calles.txt`): lista
  propia del proyecto (árboles, topónimos, apellidos y nombres comunes; 10.669
  vías, 85 KB), con licencia CC BY-SA 4.0.
- **Códigos postales** (`datos/codigos_postales.tsv`, 40.979 códigos con su
  ciudad y estado): de [GeoNames](https://www.geonames.org), con licencia
  [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/deed.es). La atribución
  va en `datos/ATRIBUCIONES.txt`, dentro de la carpeta del programa.
- **Algoritmo fonético determinista en 3 capas** ([`Calles.cs`](app/Birdlang/Motor/Calles.cs)):
  1. *Capa 1:* descompone números iniciales (`120`, `1248`), direcciones
     cardinales (`nort` → N, `saut` → S, etc.) y sufijos hablados
     (`estrit` → St, `aveniu` → Ave, `roud` → Rd, `draiv` → Dr),
     y pasa pronunciaciones hispanas a grafía inglesa (`guálnut` →
     *Walnut*, `pichtri` → *Peachtree*, `yéferson` → *Jefferson*,
     `bródgüey` → *Broadway*, `jonigüel` → *Honeywell*).
  2. *Capa 2:* Double Metaphone adaptado que preserva la `W` inicial.
  3. *Capa 3:* puntuación híbrida por prefijo, fonética y distancia de
     Levenshtein. Cero IA, cero llamadas a la red.
- **Flujo con teclado, chips USPS y selección arrastrando** ([`Ventana.cs`](app/Birdlang/Ventana.cs) y [`Banda.cs`](app/Birdlang/Banda.cs)):
  - Sombrear con el ratón las palabras de la dirección en un subtítulo las
    pasa al buscador de calles.
  - En el cuadro de búsqueda: `Enter` inserta el candidato #1; `Alt+1..5` o
    `Ctrl+1..5` inserta el candidato elegido; `Escape` limpia y vuelve a notas.
  - Cada candidato muestra chips de sufijo USPS (`St`, `Ave`, `Blvd`, `Rd`,
    `Dr`, `Way`, `Ct`, `Ln`). Un clic en el candidato o en un chip lo inserta
    en las notas, en la posición del cursor, sin tocar el portapapeles.
- **Privacidad (§4):** los subtítulos y el buscador no se pueden copiar y no
  tienen menú contextual; todo se borra con «Fin de sesión» y por inactividad.
- **Precisión y demora:** en 48 pronunciaciones de prueba (nombres de calles
  dichos a la española, inventados), 46 dan la calle correcta en primer lugar
  (95,8 %) y 47 entre los tres primeros (97,9 %). Cada búsqueda tarda 1,87 ms
  de promedio, y 9 de cada 10, menos de 2,97 ms, en una PC con Ryzen 5 5600.

---

## 7. Cómo se construye

- **C# con [Avalonia 12](https://docs.avaloniaui.net/docs/supported-platforms)
  sobre .NET 10 LTS** (soporte hasta noviembre de 2028): interfaz nativa, sin
  WebView, con licencia MIT.
- **Motor de voz:** Moonshine por su API en C, llamada desde C#, con ONNX
  Runtime solo en el procesador.
- **Audio:** NAudio 3 (captura por proceso de Windows).
- **Glosario y calles:** tablas en memoria, cargadas desde `datos/`.
- **Interfaz en español.**
- **Distribución en carpeta portable**, nunca como un ejecutable que se
  autoextrae: un ZIP por versión en GitHub Releases, con su SHA-256. La carpeta
  ocupa unos 590 MB y el ZIP unos 380 MB (versión 0.1.1).

### Compilar

Hace falta Windows 10 u 11 de 64 bits, el SDK de .NET 10 y Visual Studio Build
Tools con las herramientas de C++
(`winget install Microsoft.VisualStudio.BuildTools`, con
`VC.Tools.x86.x64`): de ahí sale el runtime de Visual C++ (14.40 o más) que va
junto al programa.

```powershell
.\app\armar.ps1             # app\dist\Birdlang: la versión Estándar
.\app\armar.ps1 -Empresas   # app\dist\Birdlang-Empresas: sin el actualizador
.\app\empaquetar.ps1        # app\dist\publicar: los ZIP, version.txt y SHA256SUMS.txt
```

La primera vez, `armar.ps1` baja los modelos y la DLL de Moonshine con
[`app/descargar.ps1`](app/descargar.ps1) (de download.moonshine.ai, PyPI,
GitHub y Hugging Face). Esa descarga es del armado, no de Birdlang, que no
tiene código de red.

### Sin código de red, comprobable

- [`app/verificar_sin_red.ps1`](app/verificar_sin_red.ps1) corre al final de
  cada armado y corta si encuentra algo de red: revisa los ensamblados, sus
  referencias y llamadas nativas (P/Invoke), el código fuente, las librerías de
  terceros y las importaciones de las DLL nativas.
- La compilación automática de GitHub
  ([`.github/workflows/compilar.yml`](.github/workflows/compilar.yml)) arma las
  dos versiones desde cero, vuelve a correr el verificador desde el paquete con
  PowerShell 7 y empaqueta.
- Cada paquete trae la carpeta `verificacion`, con el mismo verificador y los
  scripts de Process Monitor ([`app/verificacion`](app/verificacion/)), para que
  el área de TI de una empresa repita las pruebas
  ([ficha para empresas](SEGURIDAD_EMPRESAS.md)).
- Para comprobar una descarga: `Get-FileHash Birdlang-<versión>.zip` en
  PowerShell, y comparar con `SHA256SUMS.txt`.

### Actualizaciones, con un programa aparte

`actualizar.ps1` es un script que se puede leer entero.
- Solo cuando el intérprete pulsa «Buscar actualizaciones», Birdlang ejecuta el
  script para consultar si hay versión nueva. Birdlang no se conecta: solo lee
  la respuesta del script. Si hay versión nueva, muestra un aviso con un botón;
  si no, lo avisa en la barra de estado.
- Con el botón del aviso, Birdlang borra la sesión y se cierra. El script baja
  la versión de GitHub Releases, comprueba su SHA-256, reemplaza los archivos y
  vuelve a abrir Birdlang. Los modelos se bajan solo si cambiaron.
- **La versión para empresas va sin el script:** el botón no aparece, no revisa
  nada ni muestra avisos.

### Firma

Las primeras versiones van sin firma. Se pedirá la firma gratuita de
[SignPath Foundation](https://signpath.org/terms) para código abierto.

---

## English summary

Birdlang is a native Windows desktop workbench for English ↔ Spanish
interpreters who work over the phone or video (OPI/VRI) in healthcare,
insurance, finance and legal settings. It shows live subtitles of the call
audio, with numbers as digits, plus a glossary of about 40,000 entries, an
offline US street finder and a notebook. It only listens to the browser running
the call (Chrome, Edge or Brave), never to the microphone.

- **It does not translate.** There are no language models. The only AI is
  local speech recognition (Moonshine), voice detection (Silero VAD) and
  language identification (VoxLingua107), all running on the CPU.
- **It has no network code and writes nothing from the call to disk.** Notes,
  subtitles and audio live in RAM only. Details in [PRIVACY.md](PRIVACY.md) and
  the [enterprise security sheet](SEGURIDAD_EMPRESAS.md), both in English and
  Spanish.
- **Download:** [Releases](https://github.com/Dani3ll593/birdlang/releases),
  a portable folder for 64-bit Windows 10 22H2 and 11. The enterprise package
  ships without the separate updater. Early releases are not code-signed yet:
  check the SHA-256 against `SHA256SUMS.txt`.
- **Build from source:** section 7 above (`.\app\armar.ps1`).
- **License:** code under Apache-2.0; glossary and street data under
  CC BY-SA 4.0; ZIP codes from GeoNames under CC BY 4.0.
- **Contributing and security:** [CONTRIBUTING.md](CONTRIBUTING.md),
  [SECURITY.md](SECURITY.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
