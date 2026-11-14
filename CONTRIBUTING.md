# Contributing to Birdlang / Guía de Contribución para Birdlang

[English](#english) | [Español](#español)

---

<a name="english"></a>
## English

Thank you for your interest in contributing to Birdlang! We welcome contributions from professional interpreters, software engineers, and language specialists.

Before participating, please review our contribution guidelines and mandatory compliance rules.  
*(Note: The macOS version is currently under development; this release is exclusively for 64-bit Windows 10 and 11).*

---

### 1. Mandatory Ethical Rule: Zero Call Data

> [!CAUTION]
> **NEVER SUBMIT REAL CALL DATA, PATIENT NAMES, POLICY NUMBERS, LEGAL CASE DETAILS, OR CONFIDENTIAL INFORMATION.**
> Any Issue, Pull Request, or glossary suggestion containing Protected Health Information (PHI) or private call excerpts will be immediately deleted, and the contributor will be blocked to protect confidentiality and compliance standards (HIPAA/GLBA).

---

### 2. Contributing to the Community Glossary

The terminological database (`glosario.tsv`) is distributed under **Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0)**.

#### Submission Criteria:
- **Licensing Consent:** By submitting terms, translations, or notes, you explicitly agree to license your contribution under CC BY-SA 4.0.
- **No Copyrighted Textbooks or Dictionaries:** Do not copy definitions or explanatory paragraphs verbatim from copyrighted glossaries, books, or paid platforms. Term-to-term translation equivalents and original rephrased notes are welcome.
- **Regional Dialects:** Terms specific to a country or region require confirmation by at least **two native speakers** before being tagged with a country code.
- **Submission via GitHub:** Use the [glossary proposal form](https://github.com/Dani3ll593/birdlang/issues/new?template=glosario.yml) (it needs a GitHub account).
- **Future Web Form:** An optional web form may be added in the future using lightweight static CMS tools (such as Sveltia CMS or Decap CMS; never third-party trackers or Google Forms). All submissions undergo manual human review prior to merging into a release. The only Google Form in the project is the optional performance-metrics form, which never receives glossary content.

---

### 3. Contributing to the Codebase

All source code contributions are licensed under the **Apache License, Version 2.0**.

#### Non-Negotiable Architectural Rules:
1. **Zero Network Code:** Never introduce `HttpClient`, `Socket`, `WebClient`, or external API calls into `Birdlang.dll`. Each release binary is strictly inspected to confirm that `Birdlang.dll` does not reference `System.Net` or similar networking assemblies.
2. **Volatile In-Memory Operation:** Audio, live transcription strings, and interpreter notes must reside solely in transient RAM. Never write session data to disk, temp folders, or error logs.
3. **Deterministic Rules & Local Inference:** Birdlang does not use Large Language Models (LLMs) or cloud translation. The only artificial intelligence components are three local CPU models:
   - Speech recognition (**Moonshine Streaming**);
   - Voice activity detection (**Silero VAD**);
   - Spoken language identification (**VoxLingua107 ECAPA**).
4. **Native Code:** No browser runtimes, Electron, or WebView. The application is built natively on C#, .NET 10, and Avalonia UI (Windows 10/11 64-bit; macOS version under development).

---

<a name="español"></a>
## Español

¡Gracias por tu interés en contribuir a Birdlang! Agradecemos aportes tanto de intérpretes profesionales como de desarrolladores y lingüistas.

Antes de colaborar, por favor revisa nuestras pautas y reglas mandatorias de cumplimiento normativo.  
*(Nota: La versión para Mac está en desarrollo; esta versión es exclusivamente para Windows 10 y 11 de 64 bits).*

---

### 1. Regla Ética Mandatoria: Cero Datos de Llamadas

> [!CAUTION]
> **NUNCA ENVÍES DATOS DE LLAMADAS REALES, NOMBRES DE PACIENTES, PÓLIZAS, NÚMEROS DE CASOS NI NINGUNA INFORMACIÓN CONFIDENCIAL.**
> Cualquier consulta, propuesta o sugerencia de glosario que contenga información médica protegida (PHI) o extractos de llamadas reales será eliminada de inmediato y el usuario bloqueado para salvaguardar la confidencialidad y normativas vigentes (HIPAA/GLBA).

---

### 2. Aportes al Glosario Comunitario

La base terminológica (`glosario.tsv`) se distribuye bajo la licencia **Creative Commons Atribución-CompartirIgual 4.0 Internacional (CC BY-SA 4.0)**.

#### Criterios de Aceptación:
- **Aceptación de Licencia:** Al proponer términos, equivalencias o notas, aceptas explícitamente licenciar tu aporte bajo CC BY-SA 4.0.
- **Prohibido Copiar Textos Protegidos:** No se admiten definiciones textuales copiadas de diccionarios propietarios, manuales protegidos o plataformas de pago. Se aceptan equivalencias bilingües término a término y notas explicativas redactadas con palabras propias.
- **Marcas de País y Regionalismos:** Los términos coloquiales o específicos de un país requieren la confirmación de **dos hablantes nativos** antes de incluir la etiqueta correspondiente.
- **Propuestas mediante GitHub:** Usa el [formulario de propuesta para el glosario](https://github.com/Dani3ll593/birdlang/issues/new?template=glosario.yml) (hace falta una cuenta de GitHub).
- **Formulario Web Futuro:** Próximamente se podrá habilitar un formulario web complementario utilizando herramientas estáticas libres (como Sveltia CMS o Decap CMS; nunca formularios comerciales ni Google Forms). Todo término pasa por revisión manual de intérpretes antes de incorporarse al repositorio. El único Google Form del proyecto es el formulario opcional de métricas de rendimiento, que nunca recibe términos del glosario.

---

### 3. Aportes al Código Fuente

Todo el código fuente se aporta bajo la licencia **Apache License, Version 2.0**.

#### Reglas de Arquitectura No Negociables:
1. **Sin Código de Red:** Jamás incorpores `HttpClient`, `Socket`, `WebClient` ni peticiones remotas en el binario principal (`Birdlang.dll`). Cada versión distribuida se inspecciona de forma estricta para confirmar que `Birdlang.dll` no referencia `System.Net` ni espacios de nombres similares.
2. **Volatilidad Estricta en Memoria:** El audio, los subtítulos y las notas existen únicamente en la memoria RAM transitoria. Nunca persistas datos de sesión en disco, archivos temporales ni registros de depuración.
3. **Determinismo e Inferencia Local en CPU:** Birdlang no usa modelos de lenguaje generativos (LLMs) ni traducción automática. Los únicos componentes de inteligencia artificial son tres modelos locales ejecutados en procesador:
   - Reconocimiento de voz (**Moonshine Streaming**);
   - Detección de actividad vocal (**Silero VAD**);
   - Identificación de idioma hablado (**VoxLingua107 ECAPA**).
4. **Código Nativo:** Sin navegadores embebidos, Electron ni WebView. La aplicación se ejecuta de forma nativa sobre C#, .NET 10 y Avalonia UI (para Windows 10/11 de 64 bits; la versión para Mac está en desarrollo).
