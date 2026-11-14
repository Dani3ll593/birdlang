# Birdlang — Security Sheet / Ficha de seguridad

[English](#english) | [Español](#español)

<a name="english"></a>
## English

**What it is.** A desktop tool for English ↔ Spanish interpreters on Windows. It
shows live subtitles of the call, a glossary and notes. It **does not translate**
and contains no chatbot or language model. Its only AI is speech recognition,
which runs on the computer's own processor.

**What it listens to.** Only the sound of the browser running the call (Chrome,
Edge or Brave): all of that browser's sound, not a single tab. It never uses the
microphone.

**No internet.** Birdlang contains no code that can connect to a network. Every
build is checked automatically and stops if any is found. In a 30-minute test
session with Microsoft Process Monitor it made **0 network connections and wrote
0 files**. The enterprise package also leaves out the separate update tool.

**Nothing from the call is saved.** Audio, subtitles, notes and searches stay in
memory only. They are erased when the interpreter presses "End session", after
15 minutes without activity, or when the program closes. A phone number copied
for dialing is cleared from the clipboard after 20 seconds and never enters the
clipboard history. The enterprise package writes nothing to disk; the
standard package only saves "don't show again" for the volunteer notice
(`preferencias.txt`).

**What we recommend to IT**
1. **Turn on BitLocker.** When memory runs low, Windows may move part of any
   program's memory to the disk (the page file). With BitLocker that copy is
   encrypted.
2. **Turn off hibernation** (`powercfg /h off`). Hibernation writes the whole
   memory to disk.
3. Optional: block `Birdlang.exe` in Windows Firewall. Birdlang does not use the
   network.

**Limits.** Windows records *that* the program ran, not what was said. The
Birdlang window is hidden from screen sharing and screen recording, but software
with administrator rights could still read it. The interpreter can make it
visible for a quality supervisor ("Supervision" mode): the window then shows an
amber border, and the mode turns itself off after 20 minutes.

**How IT can check it.** The `verificacion` folder of the Birdlang package
(in the source code: `app/verificar_sin_red.ps1` and `app/verificacion/`) contains
the scripts for the same checks: the no-network check of the program files and a Process Monitor
session with its analysis.

**Disclaimer.** Birdlang supports the interpreter and does not replace their
judgment. On its own it does not certify compliance with HIPAA, GLBA, GDPR or
any other standard. Interpreters must follow their agency's and client's
policies before using it on real calls.

<a name="español"></a>
## Español

**Qué es.** Una herramienta de escritorio para intérpretes inglés ↔ español en
Windows. Muestra subtítulos de la llamada en vivo, un glosario y notas. **No
traduce** y no tiene chatbot ni modelo de lenguaje. Su única IA es el
reconocimiento de voz, que corre en el procesador del propio equipo.

**Qué escucha.** Solo el sonido del navegador donde corre la llamada (Chrome,
Edge o Brave): todo el sonido de ese navegador, no una sola pestaña. Nunca usa
el micrófono.

**Sin internet.** Birdlang no tiene código capaz de conectarse a una red. Cada
versión se revisa automáticamente y el armado se detiene si aparece alguno. En
una sesión de prueba de 30 minutos con Process Monitor de Microsoft hizo **0
conexiones de red y escribió 0 archivos**. La versión para empresas, además, va
sin el actualizador aparte.

**No se guarda nada de la llamada.** El audio, los subtítulos, las notas y las
búsquedas viven solo en la memoria. Se borran cuando el intérprete pulsa «Fin de
sesión», tras 15 minutos sin actividad o al cerrar el programa. Un teléfono
copiado para marcar se borra del portapapeles a los 20 segundos y nunca entra al
historial del portapapeles. La versión para empresas no escribe nada en
disco; la Estándar solo guarda «No volver a mostrar» del aviso para
voluntarios (`preferencias.txt`).

**Qué recomendamos a TI**
1. **Activar BitLocker.** Cuando falta memoria, Windows puede pasar parte de la
   memoria de cualquier programa al disco (el archivo de paginación). Con
   BitLocker esa copia queda cifrada.
2. **Desactivar la hibernación** (`powercfg /h off`). Al hibernar, Windows
   escribe toda la memoria en el disco.
3. Opcional: bloquear `Birdlang.exe` en el Firewall de Windows. Birdlang no
   usa la red.

**Límites.** Windows registra *que* el programa se abrió, no lo que se dijo. La
ventana de Birdlang no aparece al compartir o grabar la pantalla, pero un
programa con permisos de administrador sí podría leerla. El intérprete puede
hacerla visible para un supervisor de calidad (modo «Supervisión»): la ventana
muestra entonces un borde ámbar y el modo se apaga solo a los 20 minutos.

**Cómo comprobarlo.** La carpeta `verificacion` del paquete de Birdlang (en el
código fuente: `app/verificar_sin_red.ps1` y `app/verificacion/`) trae los scripts
para repetir las mismas pruebas: la revisión de que los archivos del programa no tienen
código de red y una sesión con Process Monitor con su análisis.

**Descargo de responsabilidad.** Birdlang apoya al intérprete y no reemplaza su
criterio. Por sí solo no certifica el cumplimiento de HIPAA, GLBA, RGPD ni
ninguna otra norma. Cada intérprete debe seguir las políticas de su agencia y
de su cliente antes de usarlo en llamadas reales.
