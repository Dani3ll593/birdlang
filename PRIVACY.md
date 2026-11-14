# Privacy Policy / Política de Privacidad

[English](#english) | [Español](#español)

---

<a name="english"></a>
## English

### Core Privacy Policy

**Birdlang is a fully local, standalone Windows desktop application that does not collect, store, process, or transmit personal data, call audio, transcripts, or notes outside your computer's transient RAM memory. No feature of the program establishes internet connections, nor does it write session records or temporary cache files to disk. The software contains zero telemetry, zero analytics trackers, and zero user identifiers.**  
*(Note: The macOS version is currently under development; this release is exclusively for 64-bit Windows 10 and 11).*

---

### Technical Architecture and Operating System Considerations

1. **Strictly In-Memory Processing & Local AI Models:**
   - Audio received from the browser call, live transcription streams, and interpreter notes exist exclusively in volatile memory (RAM).
   - **Local Machine Learning Models:** All AI components run 100% locally on the CPU with zero cloud dependencies:
     1. Speech Recognition: **Moonshine Streaming**;
     2. Voice Activity Detection (VAD): **Silero VAD**;
     3. Spoken Language Identification (LID): **VoxLingua107 ECAPA**.
   - When a session ends or upon 15 minutes of user inactivity, phrase buffers are zeroed out in memory (`Array.Clear()`) and UI strings are discarded.
2. **Audio Capture Scope:**
   - Birdlang uses Windows WASAPI Process Loopback directed solely at the browser executing the call (`chrome.exe`, `msedge.exe`, or `brave.exe`).
   - **Capture scope:** Birdlang captures all audio played by the chosen browser, including other tabs; the interpreter's microphone is not captured because the browser does not play it back.
3. **Absence of Network Code & On-Demand Updater:**
   - The main binary (`Birdlang.dll`) does not reference network libraries (`System.Net`, sockets, HTTP clients) and cannot transmit data.
   - **Updater Script (`actualizar.ps1`):** The application does not check for updates on startup. Only when the interpreter explicitly clicks the "Buscar actualizaciones" (Check for Updates) button does the separate PowerShell script query GitHub Releases. GitHub receives standard technical connection data (IP address, timestamp, and user-agent); no session data, audio, or notes are ever transmitted. Installing updates is also strictly on demand. If `actualizar.ps1` is removed from the folder (as in the enterprise distribution), the update button is hidden and zero network connections are possible.
4. **User Preferences:**
   - Birdlang saves nothing to disk, not even preferences: font size, country filter and window settings live in RAM and reset to defaults when the program closes.
   - **Only exception (standard package):** choosing "No volver a mostrar" (don't show again) on the "Colabora con el proyecto" notice writes `colaborar=no` to `preferencias.txt` in the program folder. The enterprise package never shows the notice and writes nothing.
   - **Volunteer metrics form:** the notice invites volunteers to share the "📋 Métricas" summary of a session (practice or work) through a Google Form. The summary only holds technical performance data, never what was said. Birdlang does not open or send the form: it only copies its link. What volunteers paste there (processor model, RAM, Windows version, timings and counts, no call text) is received by Google under Google's terms.
5. **Operating System Limits & Recommendations (README §4):**
   - **Paging File (Swap):** In Windows systems without Full Disk Encryption (BitLocker), managed memory strings could theoretically be written to the system pagefile (`pagefile.sys`) under low-RAM conditions. Enabling BitLocker is strongly recommended.
   - **Hibernation:** Entering deep hibernation dumps active RAM contents to `hiberfil.sys`. Disabling OS hibernation on workstations handling confidential interpretation is recommended.
   - **Compliance Notice:** Birdlang provides local ergonomics and data volatility by design. It does not by itself certify HIPAA, GLBA, or GDPR compliance for an entire organization, nor does it alter the professional liability and contractual obligations of the human interpreter.

---

<a name="español"></a>
## Español

### Política de Privacidad Central

**Birdlang es una herramienta de escritorio completamente local e independiente para Windows que no recopila, almacena, procesa ni transmite información personal, audio de llamadas, subtítulos ni notas fuera de la memoria RAM transitoria de su equipo. Ninguna función del programa realiza conexiones a internet ni escribe registros de sesión o archivos temporales en el disco duro. El software no utiliza telemetría, identificadores de usuario ni servicios analíticos de terceros.**  
*(Nota: La versión para Mac está en desarrollo; esta versión es exclusivamente para Windows 10 y 11 de 64 bits).*

---

### Arquitectura Técnica y Consideraciones del Sistema Operativo

1. **Diseño Estrictamente en Memoria y Modelos Locales de IA:**
   - El audio recibido de la llamada en el navegador, el flujo de subtítulos y las notas del intérprete residen únicamente en la memoria RAM volátil.
   - **Componentes de Inteligencia Artificial:** Todos los modelos de IA se ejecutan de manera 100 % local en el procesador (CPU), sin conexión ni servicios en la nube:
     1. Reconocimiento de voz: **Moonshine Streaming**;
     2. Detección de voz (VAD): **Silero VAD**;
     3. Identificación de idioma hablado: **VoxLingua107 ECAPA**.
   - Al terminar una sesión o tras 15 minutos continuos de inactividad, los búferes de audio se limpian con ceros (`Array.Clear()`) y los textos en pantalla se descartan.
2. **Alcance de la Captura de Audio:**
   - Birdlang utiliza la API WASAPI Loopback de Windows dirigida exclusivamente al proceso del navegador donde corre la llamada (`chrome.exe`, `msedge.exe` o `brave.exe`).
   - **Alcance de captura:** Se captura todo el audio que reproduce el navegador elegido, incluidas otras pestañas; el micrófono del intérprete no, porque el navegador no lo reproduce.
3. **Ausencia de Código de Red y Actualizador a Pedido:**
   - El binario principal (`Birdlang.dll`) no contiene referencias a librerías de red (`System.Net`, sockets ni clientes HTTP) y carece de capacidad técnica de transmisión.
   - **Script actualizador (`actualizar.ps1`):** Birdlang no busca actualizaciones al abrir. Solo cuando el intérprete pulsa el botón «Buscar actualizaciones», el script aparte consulta GitHub Releases. GitHub recibe los datos técnicos de la conexión (dirección IP, hora y agente de usuario), y jamás se envía nada de la llamada ni de la sesión. La instalación también es a pedido. Si se borra `actualizar.ps1` de la carpeta (la versión para empresas), el botón no se muestra y no hay ninguna conexión.
4. **Preferencias:**
   - Birdlang no guarda nada en disco, ni siquiera las preferencias: el tamaño de letra, el país y la configuración de la ventana viven en RAM y vuelven a sus valores iniciales al cerrar el programa.
   - **Única excepción (versión Estándar):** elegir «No volver a mostrar» en el aviso «Colabora con el proyecto» escribe `colaborar=no` en `preferencias.txt`, en la carpeta del programa. La versión para empresas nunca muestra el aviso y no escribe nada.
   - **Formulario de métricas para voluntarios:** el aviso invita a compartir el resumen de «📋 Métricas» de una sesión (de práctica o de trabajo) en un Google Form. Ese resumen solo tiene datos técnicos de rendimiento, nunca lo que se dijo. Birdlang no abre ni envía el formulario: solo copia su enlace. Lo que el voluntario pega ahí (modelo del procesador, RAM, versión de Windows, tiempos y conteos, sin texto de la llamada) lo recibe Google, según sus condiciones.
5. **Límites del Sistema Operativo y Recomendaciones (README §4):**
   - **Archivo de Paginación:** En equipos de Windows sin cifrado de disco completo (BitLocker), el sistema operativo podría escribir fragmentos de RAM administrada en el archivo de paginación (`pagefile.sys`) si se agota la memoria física. Se recomienda mantener BitLocker activo.
   - **Hibernación:** La hibernación del sistema vuelca el estado de la memoria RAM a `hiberfil.sys`. Se aconseja deshabilitar la hibernación en estaciones de trabajo dedicadas a la interpretación.
   - **Aviso de Cumplimiento:** Birdlang provee aislamiento de datos por diseño técnico. No constituye por sí solo una certificación de cumplimiento HIPAA, GLBA o RGPD para una empresa, ni exime al intérprete de su responsabilidad profesional y deberes contractuales de confidencialidad.
