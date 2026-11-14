using System.Text;
using System.Text.RegularExpressions;

// Buscador fonético de calles y resolutor determinista de direcciones de EE. UU. (README §6).
// Resuelve número, cardinal, calle, sufijo, unidad, ciudad, estado y código postal.
// Cero IA, cero red, determinista y volátil en RAM.
sealed class BuscadorCalles
{
    public record CandidatoCalle(
        string Nombre,
        string Completo,
        string? Numero,
        string? Cardinal,
        string? Sufijo,
        string? Unidad,
        string? Ciudad,
        string? Estado,
        string? CodigoPostal,
        bool ZipFueIngresado,
        string? ZipSugerido,
        double Puntuacion)
    {
        // Constructor de compatibilidad
        public CandidatoCalle(string nombre, string completo, string? numero, string? cardinal, string? sufijo, double puntuacion)
            : this(nombre, completo, numero, cardinal, sufijo, null, null, null, null, false, null, puntuacion) { }

        // Genera el texto con un nuevo sufijo (ej: al hacer clic en chip "St" o "Ave").
        public string ConSufijo(string nuevoSufijo) =>
            FormatearCompleto(Nombre, Numero, Cardinal, nuevoSufijo, Unidad, Ciudad, Estado, ZipFueIngresado ? CodigoPostal : null);

        // Agrega el ZIP sugerido a la dirección completa
        public string ConZipSugerido() =>
            FormatearCompleto(Nombre, Numero, Cardinal, Sufijo, Unidad, Ciudad, Estado, ZipSugerido ?? CodigoPostal);

        // Retorna solo la calle y número/unidad
        public string SoloCalle() =>
            FormatearCalle(Nombre, Numero, Cardinal, Sufijo, Unidad);

        // Retorna solo la localidad geográfica (Ciudad, Estado [ZIP si fue ingresado])
        public string SoloLocalidad() =>
            FormatearLocalidad(Ciudad, Estado, ZipFueIngresado ? CodigoPostal : null);
    }

    public record EntradaPostal(string Zip, string Ciudad, string Estado);

    public record EntradaCiudad(
        string Ciudad,
        string Estado,
        string CiudadMinuscula,
        string Primaria,
        string Secundaria,
        int ConteoZips,
        string ZipPrimario);

    public record ComponentesDireccion(
        string NucleoCalle,
        string? Numero,
        string? Cardinal,
        string? Sufijo,
        string? Unidad,
        string? Ciudad,
        string? Estado,
        string? CodigoPostal,
        bool ZipIngresado,
        string? ZipSugerido);

    record EntradaCalle(string Nombre, string NombreMinuscula, string Primaria, string Secundaria, int Prioridad);

    readonly EntradaCalle[] entradas;
    readonly HashSet<string> callesMinusculas;
    readonly Dictionary<string, EntradaPostal> postalesPorZip;
    readonly EntradaCiudad[] ciudades;
    readonly Dictionary<(string CiudadLower, string EstadoLower), EntradaCiudad> ciudadEstadoIndice;
    readonly Dictionary<string, EntradaCiudad[]> ciudadesPorEstado;

    public BuscadorCalles(IEnumerable<string> nombres, IEnumerable<string>? lineasPostales = null)
    {
        var listaCalles = new List<EntradaCalle>();
        callesMinusculas = new(StringComparer.OrdinalIgnoreCase);
        int prioridad = 0;
        foreach (var linea in nombres)
        {
            var limpio = linea.Trim();
            if (limpio.Length == 0) continue;
            var (p, s) = DoubleMetaphone(limpio);
            string lower = limpio.ToLowerInvariant();
            listaCalles.Add(new EntradaCalle(limpio, lower, p, s, prioridad++));
            callesMinusculas.Add(lower);
        }
        entradas = [.. listaCalles];

        postalesPorZip = new(StringComparer.Ordinal);
        ciudadEstadoIndice = new();
        var listaCiudades = new List<EntradaCiudad>();

        if (lineasPostales != null)
        {
            var gruposCiudad = new Dictionary<(string Ciudad, string Estado), List<string>>();

            foreach (var l in lineasPostales)
            {
                var lin = l.Trim();
                if (lin.Length == 0 || lin.StartsWith("ZIP", StringComparison.OrdinalIgnoreCase)) continue;
                int tab1 = lin.IndexOf('\t');
                if (tab1 <= 0) continue;
                int tab2 = lin.IndexOf('\t', tab1 + 1);
                if (tab2 <= 0) continue;

                string zip = lin[..tab1].Trim();
                string ciudad = lin[(tab1 + 1)..tab2].Trim();
                string estado = lin[(tab2 + 1)..].Trim();

                postalesPorZip[zip] = new EntradaPostal(zip, ciudad, estado);

                var key = (ciudad, estado);
                if (!gruposCiudad.TryGetValue(key, out var zips))
                {
                    zips = [];
                    gruposCiudad[key] = zips;
                }
                zips.Add(zip);
            }

            foreach (var (k, zips) in gruposCiudad)
            {
                string trans = TransliterarHispanoAnglo(k.Ciudad);
                var (p, s) = DoubleMetaphone(trans);
                var ec = new EntradaCiudad(k.Ciudad, k.Estado, k.Ciudad.ToLowerInvariant(), p, s, zips.Count, zips[0]);
                listaCiudades.Add(ec);
                ciudadEstadoIndice[(k.Ciudad.ToLowerInvariant(), k.Estado.ToLowerInvariant())] = ec;
            }
        }
        ciudades = [.. listaCiudades];
        ciudadesPorEstado = listaCiudades
            .GroupBy(c => c.Estado, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public static BuscadorCalles Cargar(string rutaArchivo) =>
        Cargar(rutaArchivo, Path.Combine(Path.GetDirectoryName(rutaArchivo) ?? "", "codigos_postales.tsv"));

    public static BuscadorCalles Cargar(string rutaCalles, string? rutaCodigosPostales)
    {
        IEnumerable<string> calles = File.Exists(rutaCalles) ? File.ReadLines(rutaCalles) : [];
        IEnumerable<string>? postales = (rutaCodigosPostales != null && File.Exists(rutaCodigosPostales))
            ? File.ReadLines(rutaCodigosPostales)
            : null;
        return new(calles, postales);
    }

    public List<CandidatoCalle> Buscar(string? consulta, int top = 5)
    {
        if (string.IsNullOrWhiteSpace(consulta)) return [];

        var d = ParsearDireccion(consulta);

        // Si no hay núcleo de calle pero sí hay ciudad o código postal (búsqueda directa de localidad o ZIP)
        if (d.NucleoCalle.Length == 0)
        {
            if (!string.IsNullOrEmpty(d.Ciudad) || !string.IsNullOrEmpty(d.CodigoPostal))
            {
                string loc = FormatearLocalidad(d.Ciudad, d.Estado, d.ZipIngresado ? d.CodigoPostal : null);
                return [new CandidatoCalle(
                    Nombre: d.Ciudad ?? d.CodigoPostal ?? "",
                    Completo: loc,
                    Numero: null,
                    Cardinal: null,
                    Sufijo: null,
                    Unidad: null,
                    Ciudad: d.Ciudad,
                    Estado: d.Estado,
                    CodigoPostal: d.CodigoPostal,
                    ZipFueIngresado: d.ZipIngresado,
                    ZipSugerido: d.ZipSugerido,
                    Puntuacion: 100.0)];
            }
            return [];
        }

        string transliterado = TransliterarHispanoAnglo(d.NucleoCalle);
        var (metaPrim, metaSec) = DoubleMetaphone(transliterado);
        var (origPrim, origSec) = DoubleMetaphone(d.NucleoCalle);
        string orig = d.NucleoCalle.ToLowerInvariant();
        string trans = transliterado.ToLowerInvariant();

        var resultados = new List<(EntradaCalle Entrada, double Puntuacion)>(entradas.Length);

        for (int i = 0; i < entradas.Length; i++)
        {
            var e = entradas[i];
            double p = Puntuar(orig, trans, metaPrim, metaSec, origPrim, origSec, e);
            if (p > 25)
            {
                // Desempate sutil por frecuencia nacional (índice menor en el archivo de calles)
                double puntajeFinal = p + (10.0 / (10.0 + e.Prioridad * 0.05));
                resultados.Add((e, puntajeFinal));
            }
        }

        var candidatosOrdenados = resultados
            .OrderByDescending(r => r.Puntuacion)
            .Take(top)
            .Select(r => new CandidatoCalle(
                Nombre: r.Entrada.Nombre,
                Completo: FormatearCompleto(r.Entrada.Nombre, d.Numero, d.Cardinal, d.Sufijo, d.Unidad, d.Ciudad, d.Estado, d.ZipIngresado ? d.CodigoPostal : null),
                Numero: d.Numero,
                Cardinal: d.Cardinal,
                Sufijo: d.Sufijo,
                Unidad: d.Unidad,
                Ciudad: d.Ciudad,
                Estado: d.Estado,
                CodigoPostal: d.CodigoPostal,
                ZipFueIngresado: d.ZipIngresado,
                ZipSugerido: d.ZipSugerido,
                Puntuacion: r.Puntuacion))
            .ToList();

        // Si no hubo resultados en diccionario, o si el usuario introdujo un nombre literal en una dirección
        // estructurada que no coincide exactamente con el top del diccionario:
        bool tieneEstructuraDireccion = d.Numero != null || d.Sufijo != null || d.Ciudad != null || d.Estado != null || d.CodigoPostal != null;
        if (d.NucleoCalle.Length > 0)
        {
            string nombreLiteral = CapitalizarPalabras(d.NucleoCalle);
            bool yaExisteEnTop = candidatosOrdenados.Any(c => c.Nombre.Equals(nombreLiteral, StringComparison.OrdinalIgnoreCase));

            if (!yaExisteEnTop)
            {
                var candLiteral = new CandidatoCalle(
                    Nombre: nombreLiteral,
                    Completo: FormatearCompleto(nombreLiteral, d.Numero, d.Cardinal, d.Sufijo, d.Unidad, d.Ciudad, d.Estado, d.ZipIngresado ? d.CodigoPostal : null),
                    Numero: d.Numero,
                    Cardinal: d.Cardinal,
                    Sufijo: d.Sufijo,
                    Unidad: d.Unidad,
                    Ciudad: d.Ciudad,
                    Estado: d.Estado,
                    CodigoPostal: d.CodigoPostal,
                    ZipFueIngresado: d.ZipIngresado,
                    ZipSugerido: d.ZipSugerido,
                    Puntuacion: 80.0);

                if (candidatosOrdenados.Count == 0)
                {
                    candidatosOrdenados.Add(candLiteral);
                }
                else if (tieneEstructuraDireccion && candidatosOrdenados[0].Puntuacion < 55.0)
                {
                    // Si el mejor del diccionario no alcanza coincidencia fonética clara (<55) y el usuario escribió
                    // una dirección con número/sufijo/ciudad/ZIP, priorizar el nombre literal ingresado
                    candidatosOrdenados.Insert(0, candLiteral);
                    if (candidatosOrdenados.Count > top) candidatosOrdenados.RemoveAt(candidatosOrdenados.Count - 1);
                }
                else if (tieneEstructuraDireccion && candidatosOrdenados.Count < top)
                {
                    // Ofrecer también la opción literal al final de la lista de candidatos
                    candidatosOrdenados.Add(candLiteral);
                }
            }
        }

        return candidatosOrdenados;
    }

    public static string CapitalizarPalabras(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var partes = texto.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < partes.Length; i++)
        {
            string p = partes[i];
            if (p.Length > 0)
            {
                partes[i] = char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..].ToLowerInvariant() : "");
            }
        }
        return string.Join(' ', partes);
    }

    public static string FormatearCalle(string? nombre, string? numero, string? cardinal, string? sufijo, string? unidad = null)
    {
        if (string.IsNullOrEmpty(nombre)) return "";
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(numero)) sb.Append(numero).Append(' ');
        if (!string.IsNullOrEmpty(cardinal)) sb.Append(cardinal).Append(' ');
        sb.Append(nombre);
        if (!string.IsNullOrEmpty(sufijo)) sb.Append(' ').Append(sufijo);
        if (!string.IsNullOrEmpty(unidad)) sb.Append(' ').Append(unidad);
        return sb.ToString();
    }

    public static string FormatearLocalidad(string? ciudad, string? estado, string? zip)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(ciudad)) sb.Append(ciudad);
        if (!string.IsNullOrEmpty(estado))
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(estado);
        }
        if (!string.IsNullOrEmpty(zip))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(zip);
        }
        return sb.ToString();
    }

    public static string FormatearCompleto(string? nombre, string? numero, string? cardinal, string? sufijo, string? unidad, string? ciudad, string? estado, string? zip)
    {
        string calle = FormatearCalle(nombre, numero, cardinal, sufijo, unidad);
        string loc = FormatearLocalidad(ciudad, estado, zip);
        if (calle.Length > 0 && loc.Length > 0) return $"{calle}, {loc}";
        if (calle.Length > 0) return calle;
        return loc;
    }

    // Formatear legacy
    public static string Formatear(string nombre, string? numero, string? cardinal, string? sufijo) =>
        FormatearCalle(nombre, numero, cardinal, sufijo, null);

    // Compatibilidad legacy
    public static (string Nucleo, string? Numero, string? Cardinal, string? Sufijo) ParsearComponentes(string entrada)
    {
        var dummy = new BuscadorCalles([]);
        var d = dummy.ParsearDireccion(entrada);
        return (d.NucleoCalle, d.Numero, d.Cardinal, d.Sufijo);
    }

    // Desglose quirúrgico de dirección: número, cardinal, calle, sufijo, unidad, ciudad, estado, zip.
    public ComponentesDireccion ParsearDireccion(string entrada)
    {
        string t = NormalizarTexto(entrada);
        string? numero = null, cardinal = null, sufijo = null, unidad = null;
        string? ciudad = null, estado = null, zip = null, zipSugerido = null;
        bool zipIngresado = false;

        // 1. Regla 1: Desambiguación entre número de 5 dígitos y código postal.
        // 1.1 Si la consulta entera es exactamente un número de 5 dígitos:
        if (Regex.IsMatch(t, @"^\d{5}$"))
        {
            if (postalesPorZip.TryGetValue(t, out var entZip))
            {
                return new ComponentesDireccion(
                    NucleoCalle: "",
                    Numero: null,
                    Cardinal: null,
                    Sufijo: null,
                    Unidad: null,
                    Ciudad: entZip.Ciudad,
                    Estado: entZip.Estado,
                    CodigoPostal: entZip.Zip,
                    ZipIngresado: true,
                    ZipSugerido: null);
            }
        }

        // 1.2 Si la consulta termina con un número de 5 dígitos al final:
        var matchZipFinal = Regex.Match(t, @"\b(\d{5})\s*$");
        if (matchZipFinal.Success)
        {
            string candZip = matchZipFinal.Groups[1].Value;
            if (postalesPorZip.TryGetValue(candZip, out var entZip))
            {
                zip = candZip;
                ciudad = entZip.Ciudad;
                estado = entZip.Estado;
                zipIngresado = true;
                t = t[..matchZipFinal.Index].Trim();
            }
        }

        // 2. Extracción de Unidad / Apartamento (apt 4b, ste 200, unit 12, #4b)
        var matchUnit = RegexUnidad.Match(t);
        if (matchUnit.Success)
        {
            string pref = matchUnit.Groups[1].Value.ToLowerInvariant();
            string val = matchUnit.Groups[2].Value.ToUpperInvariant();
            unidad = pref switch
            {
                "apt" or "apto" or "apartment" or "apartamento" or "#" => $"Apt {val}",
                "suite" or "ste" => $"Ste {val}",
                "unit" or "unidad" => $"Unit {val}",
                "bldg" or "building" or "edificio" => $"Bldg {val}",
                "fl" or "floor" or "piso" => $"Fl {val}",
                "rm" or "room" => $"Rm {val}",
                "spc" or "space" => $"Spc {val}",
                _ => $"{char.ToUpperInvariant(pref[0])}{pref[1..]} {val}"
            };
            t = (t[..matchUnit.Index] + " " + t[(matchUnit.Index + matchUnit.Length)..]).Trim();
        }

        var tokens = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0)
        {
            return new ComponentesDireccion("", numero, cardinal, sufijo, unidad, ciudad, estado, zip, zipIngresado, zipSugerido);
        }

        // 3. Limpieza de frases conversacionales iniciales
        if (tokens.Count >= 3 && tokens[0] == "i" && tokens[1] == "live" && tokens[2] == "in")
        {
            tokens.RemoveRange(0, 3);
        }
        else if (tokens.Count >= 2 && tokens[0] == "vivo" && tokens[1] == "en")
        {
            tokens.RemoveRange(0, 2);
        }
        else if (tokens.Count >= 3 && tokens[0] == "en" && tokens[1] == "la" && (tokens[2] == "calle" || tokens[2] == "avenida"))
        {
            tokens.RemoveRange(0, 3);
        }

        // 4. Regla 2: Reconocimiento de Estado AL FINAL
        if (estado != null)
        {
            // El ZIP ya definió el estado: retirar del final si el usuario lo repitió
            if (tokens.Count > 0)
            {
                if (tokens[^1].Equals(estado, StringComparison.OrdinalIgnoreCase) ||
                    (EstadosNombres.TryGetValue(tokens[^1], out var stNom) && stNom.Equals(estado, StringComparison.OrdinalIgnoreCase)))
                {
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }

            // Retirar del final si el usuario escribió la ciudad del ZIP (textual o fonética)
            if (ciudad != null && tokens.Count > 0)
            {
                var ciudadTokens = ciudad.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Count >= ciudadTokens.Length)
                {
                    bool coincideFinal = true;
                    for (int k = 0; k < ciudadTokens.Length; k++)
                    {
                        if (tokens[tokens.Count - ciudadTokens.Length + k] != ciudadTokens[k])
                        {
                            coincideFinal = false;
                            break;
                        }
                    }
                    if (coincideFinal)
                    {
                        tokens.RemoveRange(tokens.Count - ciudadTokens.Length, ciudadTokens.Length);
                    }
                    else if (tokens.Count > 0)
                    {
                        var ec = BuscarCiudad(tokens[^1], estado);
                        if (ec != null && ec.Ciudad.Equals(ciudad, StringComparison.OrdinalIgnoreCase))
                        {
                            tokens.RemoveAt(tokens.Count - 1);
                        }
                    }
                }
            }
        }
        else if (tokens.Count > 1)
        {
            // Solo evaluar Estado si hay al menos 2 tokens (para no confundir una calle llamada 'Washington' o 'Michigan' con el estado)
            if (tokens.Count >= 2)
            {
                string st2 = $"{tokens[^2]} {tokens[^1]}";
                if (EstadosNombres.TryGetValue(st2, out var stCod))
                {
                    estado = stCod;
                    tokens.RemoveRange(tokens.Count - 2, 2);
                }
            }
            if (estado == null && tokens.Count > 1)
            {
                string candSt = tokens[^1];
                if (Estados2Letras.TryGetValue(candSt, out var st2))
                {
                    estado = st2;
                    tokens.RemoveAt(tokens.Count - 1);
                }
                else if (EstadosNombres.TryGetValue(candSt, out var stNom))
                {
                    estado = stNom;
                    tokens.RemoveAt(tokens.Count - 1);
                }
            }
        }

        // 5. Si tenemos estado (vía ZIP o vía estado al final), buscar la ciudad que le precede
        if (ciudad == null && estado != null && tokens.Count > 0)
        {
            EntradaCiudad? ciudadDetectada = null;
            int tokensConsumidos = 0;

            for (int len = Math.Min(3, tokens.Count); len >= 1; len--)
            {
                string candCity = string.Join(' ', tokens.Skip(tokens.Count - len));
                var matchCity = BuscarCiudad(candCity, estado);
                if (matchCity != null)
                {
                    ciudadDetectada = matchCity;
                    tokensConsumidos = len;
                    break;
                }
            }

            if (ciudadDetectada != null)
            {
                ciudad = ciudadDetectada.Ciudad;
                tokens.RemoveRange(tokens.Count - tokensConsumidos, tokensConsumidos);

                if (ciudadDetectada.ConteoZips == 1)
                {
                    zip = ciudadDetectada.ZipPrimario;
                }
                else
                {
                    zipSugerido = ciudadDetectada.ZipPrimario;
                }
            }
        }

        // 6. Sufijo vial al final del tramo de calle (después de retirar ciudad/estado/zip)
        if (tokens.Count > 1)
        {
            if (Sufijos.TryGetValue(tokens[^1], out var suf))
            {
                sufijo = suf;
                tokens.RemoveAt(tokens.Count - 1);
            }
        }

        // 7. Número inicial de casa (ej: 120 o 10420)
        if (tokens.Count > 0 && Regex.IsMatch(tokens[0], @"^\d+$"))
        {
            numero = tokens[0];
            tokens.RemoveAt(0);
        }

        // 8. Cardinal inicial
        if (tokens.Count > 1)
        {
            if (Cardinales.TryGetValue(tokens[0], out var card))
            {
                cardinal = card;
                tokens.RemoveAt(0);
            }
        }

        string nucleo = string.Join(' ', tokens).Trim();
        return new ComponentesDireccion(nucleo, numero, cardinal, sufijo, unidad, ciudad, estado, zip, zipIngresado, zipSugerido);
    }

    EntradaCiudad? BuscarCiudad(string texto, string estadoFiltro)
    {
        if (string.IsNullOrWhiteSpace(texto) || ciudadesPorEstado.Count == 0) return null;
        string tMin = texto.Trim().ToLowerInvariant();
        string trans = TransliterarHispanoAnglo(tMin);
        var (metaP, metaS) = DoubleMetaphone(trans);
        var (origP, origS) = DoubleMetaphone(tMin);

        string estLower = estadoFiltro.ToLowerInvariant();
        if (ciudadEstadoIndice.TryGetValue((tMin, estLower), out var directa)) return directa;
        if (ciudadEstadoIndice.TryGetValue((trans, estLower), out var transDirecta)) return transDirecta;

        if (!ciudadesPorEstado.TryGetValue(estadoFiltro, out var lista)) return null;

        EntradaCiudad? mejor = null;
        double mejorPuntaje = 0;

        for (int i = 0; i < lista.Length; i++)
        {
            var c = lista[i];
            double p = PuntuarCiudad(tMin, trans, metaP, metaS, origP, origS, c);
            if (p > mejorPuntaje)
            {
                mejorPuntaje = p;
                mejor = c;
            }
        }

        if (mejorPuntaje >= 50) return mejor;
        return null;
    }

    static double PuntuarCiudad(string orig, string trans, string metaP, string metaS, string origP, string origS, EntradaCiudad c)
    {
        string nombre = c.CiudadMinuscula;
        if (nombre.Equals(orig, StringComparison.Ordinal) || nombre.Equals(trans, StringComparison.Ordinal)) return 100;
        if (nombre.StartsWith(orig, StringComparison.Ordinal) || nombre.StartsWith(trans, StringComparison.Ordinal)) return 85;

        double pts = 0;
        if (c.Primaria == metaP || c.Primaria == origP) pts = 75;
        else if (c.Secundaria.Length > 0 && (c.Secundaria == metaS || c.Secundaria == origS || c.Secundaria == metaP)) pts = 65;

        if (pts > 0) return pts;

        if (Math.Abs(trans.Length - nombre.Length) <= 2)
        {
            int d = Levenshtein(trans, nombre);
            if (d <= 2) return Math.Max(0.0, 1.0 - (double)d / Math.Max(trans.Length, nombre.Length)) * 60;
        }

        return 0;
    }

    // Capa 1: Transliteración fonética hispana hacia ortografía/fonética inglesa.
    public static string TransliterarHispanoAnglo(string texto)
    {
        string t = NormalizarTexto(texto);

        // Prótesis 'es-' ante consonante (ej: 'esprinfild' -> 'springfield', 'escuirrel' -> 'squirrel')
        t = Regex.Replace(t, @"^es([ptckqmnl])", "s$1");

        // Diptongos velares iniciales o tras vocal -> w (ej: 'gualnut' -> 'walnut', 'guashington' / 'uashington' -> 'washington')
        t = Regex.Replace(t, @"^(?:gu|hu|u)[aaoó]", "w");
        t = Regex.Replace(t, @"^u[aaeioóu]", "w");
        t = Regex.Replace(t, @"gu[aaoó]", "w");
        t = Regex.Replace(t, @"gü[eeií]", "w");
        t = Regex.Replace(t, @"hu[aaeioóu]", "w");

        // Fricativa velar española 'j' -> 'h' (ej: 'jaiguey' -> 'highway', 'jaliud' -> 'hollywood', 'joniguel' -> 'honeywell')
        t = t.Replace('j', 'h');

        // Palatales 'y' / 'll' -> 'j' inglesa (ej: 'yeferson' -> 'jefferson')
        t = Regex.Replace(t, @"^(?:y|ll)([aeiou])", "j$1");
        t = Regex.Replace(t, @"([aeiou])(?:y|ll)([aeiou])", "$1j$2");

        // Patrones fonéticos de ciudades y nombres conocidos
        t = t.Replace("albakerki", "albuquerque");
        t = t.Replace("sirakius", "syracuse");
        t = t.Replace("finix", "phoenix");
        t = t.Replace("joniguel", "honeywell");
        t = t.Replace("honiguel", "honeywell");
        t = t.Replace("leic", "lake");
        t = t.Replace("chesnat", "chestnut");
        t = t.Replace("lincon", "lincoln");
        t = t.Replace("pain", "pine");
        t = t.Replace("hailand", "highland");
        t = t.Replace("chorch", "church");
        t = t.Replace("oc", "oak");
        t = t.Replace("ban nuis", "van nuys");
        t = t.Replace("granbiu", "grandview");
        t = Regex.Replace(t, @"br[oó]d?g[uü]ey", "broadway");
        t = t.Replace("brod", "broad");
        t = t.Replace("guey", "way");
        t = t.Replace("güey", "way");
        t = t.Replace("aiguey", "ighway");
        t = t.Replace("iud", "ywood");
        t = t.Replace("iu", "iew");
        t = t.Replace("tri", "chtree");
        t = t.Replace("pich", "peach");
        t = t.Replace("bich", "beach");
        t = t.Replace("saut", "south");
        t = t.Replace("eipc", "apc");
        t = t.Replace("eip", "ap");
        t = t.Replace("eik", "ak");
        t = t.Replace("ei", "a");

        // Sibilantes y africadas sh <-> ch
        t = t.Replace("ch", "sh");

        return t;
    }

    static double Puntuar(string orig, string trans,
                          string metaPrim, string metaSec,
                          string origPrim, string origSec,
                          EntradaCalle c)
    {
        string nombre = c.NombreMinuscula;

        if (nombre.Equals(orig, StringComparison.Ordinal) || nombre.Equals(trans, StringComparison.Ordinal)) return 100;

        bool prefijo = nombre.StartsWith(orig, StringComparison.Ordinal) || nombre.StartsWith(trans, StringComparison.Ordinal);
        if (prefijo)
            return 85 + (double)Math.Min(orig.Length, nombre.Length) / Math.Max(orig.Length, nombre.Length) * 10;

        double puntosMetaphone = 0;
        if (c.Primaria == metaPrim || c.Primaria == origPrim) puntosMetaphone = 75;
        else if (c.Secundaria.Length > 0 && (c.Secundaria == metaSec || c.Secundaria == origSec || c.Secundaria == metaPrim)) puntosMetaphone = 65;
        else if (c.Primaria.Length > 0 && (c.Primaria == metaSec || c.Primaria == origSec)) puntosMetaphone = 60;

        if (puntosMetaphone == 0)
        {
            int maxDiff = Math.Abs(trans.Length - nombre.Length);
            int maxLen = Math.Max(trans.Length, nombre.Length);
            if (maxDiff >= (maxLen + 1) / 2) return 0;

            bool mismoInicio = (nombre.Length > 0 && trans.Length > 0 && nombre[0] == trans[0])
                            || (nombre.Length > 0 && orig.Length > 0 && nombre[0] == orig[0]);

            if (!mismoInicio && maxDiff > 1) return 0;
        }

        int dMin = Levenshtein(trans, nombre);
        if (orig != trans && dMin > 2)
        {
            int d2 = Levenshtein(orig, nombre);
            if (d2 < dMin) dMin = d2;
        }
        int lenMax = Math.Max(trans.Length, nombre.Length);
        double simLev = Math.Max(0.0, 1.0 - (double)dMin / lenMax) * 50;

        double puntosSub = 0;
        if (nombre.Length >= orig.Length && (nombre.Contains(orig, StringComparison.Ordinal) || nombre.Contains(trans, StringComparison.Ordinal)))
            puntosSub = 15;

        return Math.Max(puntosMetaphone + (simLev * 0.4), simLev + puntosSub);
    }

    public static (string Primaria, string Secundaria) DoubleMetaphone(string palabra)
    {
        if (string.IsNullOrWhiteSpace(palabra)) return ("", "");
        string inStr = NormalizarTexto(palabra).ToUpperInvariant();
        int len = inStr.Length;
        int current = 0;

        var primary = new StringBuilder(6);
        var secondary = new StringBuilder(6);

        if (inStr.StartsWith("W"))
        {
            primary.Append('W');
            secondary.Append('W');
            current = 1;
        }
        else if (inStr[0] is 'A' or 'E' or 'I' or 'O' or 'U')
        {
            primary.Append('A');
            secondary.Append('A');
            current = 1;
        }

        while (primary.Length < 6 && current < len)
        {
            char ch = inStr[current];
            switch (ch)
            {
                case 'B':
                    primary.Append('P');
                    secondary.Append('P');
                    current += (current + 1 < len && inStr[current + 1] == 'B') ? 2 : 1;
                    break;
                case 'C':
                    if (current + 1 < len && inStr[current + 1] == 'H')
                    {
                        primary.Append('X');
                        secondary.Append('X');
                        current += 2;
                    }
                    else if (current + 1 < len && inStr[current + 1] is 'I' or 'E' or 'Y')
                    {
                        primary.Append('S');
                        secondary.Append('S');
                        current += 2;
                    }
                    else
                    {
                        primary.Append('K');
                        secondary.Append('K');
                        current += (current + 1 < len && inStr[current + 1] == 'C') ? 2 : 1;
                    }
                    break;
                case 'D':
                    if (current + 1 < len && inStr[current + 1] == 'G')
                    {
                        primary.Append('J');
                        secondary.Append('J');
                        current += 2;
                    }
                    else
                    {
                        primary.Append('T');
                        secondary.Append('T');
                        current += (current + 1 < len && inStr[current + 1] == 'D') ? 2 : 1;
                    }
                    break;
                case 'F':
                    primary.Append('F');
                    secondary.Append('F');
                    current += (current + 1 < len && inStr[current + 1] == 'F') ? 2 : 1;
                    break;
                case 'G':
                    if (current + 1 < len && inStr[current + 1] == 'H')
                    {
                        current += 2;
                    }
                    else if (current + 1 < len && inStr[current + 1] is 'I' or 'E' or 'Y')
                    {
                        primary.Append('J');
                        secondary.Append('K');
                        current += 2;
                    }
                    else
                    {
                        primary.Append('K');
                        secondary.Append('K');
                        current += (current + 1 < len && inStr[current + 1] == 'G') ? 2 : 1;
                    }
                    break;
                case 'H':
                    if (current == 0 || (current + 1 < len && inStr[current + 1] is 'A' or 'E' or 'I' or 'O' or 'U'))
                    {
                        primary.Append('H');
                        secondary.Append('H');
                    }
                    current++;
                    break;
                case 'J':
                    primary.Append('J');
                    secondary.Append('J');
                    current += (current + 1 < len && inStr[current + 1] == 'J') ? 2 : 1;
                    break;
                case 'K':
                    primary.Append('K');
                    secondary.Append('K');
                    current += (current + 1 < len && inStr[current + 1] == 'K') ? 2 : 1;
                    break;
                case 'L':
                    primary.Append('L');
                    secondary.Append('L');
                    current += (current + 1 < len && inStr[current + 1] == 'L') ? 2 : 1;
                    break;
                case 'M':
                    primary.Append('M');
                    secondary.Append('M');
                    current += (current + 1 < len && inStr[current + 1] == 'M') ? 2 : 1;
                    break;
                case 'N':
                    primary.Append('N');
                    secondary.Append('N');
                    current += (current + 1 < len && inStr[current + 1] == 'N') ? 2 : 1;
                    break;
                case 'P':
                    if (current + 1 < len && inStr[current + 1] == 'H')
                    {
                        primary.Append('F');
                        secondary.Append('F');
                        current += 2;
                    }
                    else
                    {
                        primary.Append('P');
                        secondary.Append('P');
                        current += (current + 1 < len && inStr[current + 1] == 'P') ? 2 : 1;
                    }
                    break;
                case 'Q':
                    primary.Append('K');
                    secondary.Append('K');
                    current++;
                    break;
                case 'R':
                    primary.Append('R');
                    secondary.Append('R');
                    current += (current + 1 < len && inStr[current + 1] == 'R') ? 2 : 1;
                    break;
                case 'S':
                    if (current + 1 < len && inStr[current + 1] == 'H')
                    {
                        primary.Append('X');
                        secondary.Append('X');
                        current += 2;
                    }
                    else
                    {
                        primary.Append('S');
                        secondary.Append('S');
                        current += (current + 1 < len && inStr[current + 1] == 'S') ? 2 : 1;
                    }
                    break;
                case 'T':
                    if (current + 1 < len && inStr[current + 1] == 'H')
                    {
                        primary.Append('0');
                        secondary.Append('T');
                        current += 2;
                    }
                    else if (current + 1 < len && inStr[current + 1] == 'C' && current + 2 < len && inStr[current + 2] == 'H')
                    {
                        primary.Append('X');
                        secondary.Append('X');
                        current += 3;
                    }
                    else
                    {
                        primary.Append('T');
                        secondary.Append('T');
                        current += (current + 1 < len && inStr[current + 1] == 'T') ? 2 : 1;
                    }
                    break;
                case 'V':
                    primary.Append('F');
                    secondary.Append('F');
                    current += (current + 1 < len && inStr[current + 1] == 'V') ? 2 : 1;
                    break;
                case 'W':
                    primary.Append('W');
                    secondary.Append('A');
                    current++;
                    break;
                case 'X':
                    primary.Append("KS");
                    secondary.Append("KS");
                    current++;
                    break;
                case 'Z':
                    primary.Append('S');
                    secondary.Append('S');
                    current += (current + 1 < len && inStr[current + 1] == 'Z') ? 2 : 1;
                    break;
                default:
                    current++;
                    break;
            }
        }

        return (primary.ToString(), secondary.ToString());
    }

    public static int Levenshtein(string s, string t)
    {
        if (s == t) return 0;
        int n = s.Length, m = t.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        Span<int> v0 = stackalloc int[m + 1];
        Span<int> v1 = stackalloc int[m + 1];

        for (int j = 0; j <= m; j++) v0[j] = j;

        for (int i = 0; i < n; i++)
        {
            v1[0] = i + 1;
            for (int j = 0; j < m; j++)
            {
                int cost = (s[i] == t[j]) ? 0 : 1;
                v1[j + 1] = Math.Min(
                    Math.Min(v1[j] + 1, v0[j + 1] + 1),
                    v0[j] + cost);
            }
            v1.CopyTo(v0);
        }

        return v0[m];
    }

    static string NormalizarTexto(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (char c in texto.Trim().ToLowerInvariant())
        {
            char n = c switch
            {
                'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' => 'u', 'ü' => 'u', 'ñ' => 'n',
                _ => c
            };
            if (char.IsLetterOrDigit(n) || char.IsWhiteSpace(n)) sb.Append(n);
        }
        return sb.ToString();
    }

    static readonly Regex RegexUnidad = new(
        @"\b(apt|apto|apartment|apartamento|suite|ste|unit|unidad|bldg|building|edificio|fl|floor|piso|rm|room|spc|space|#)\s*([a-zA-Z0-9\-]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Dictionary<string, string> Cardinales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nort"] = "N", ["north"] = "N", ["n"] = "N",
        ["saut"] = "S", ["south"] = "S", ["sur"] = "S", ["s"] = "S",
        ["ist"] = "E", ["east"] = "E", ["este"] = "E", ["e"] = "E",
        ["guest"] = "W", ["uest"] = "W", ["west"] = "W", ["oeste"] = "W", ["w"] = "W",
        ["nortist"] = "NE", ["northeast"] = "NE", ["ne"] = "NE",
        ["nortguest"] = "NW", ["northwest"] = "NW", ["nw"] = "NW",
        ["sautist"] = "SE", ["southeast"] = "SE", ["se"] = "SE",
        ["sautguest"] = "SW", ["southwest"] = "SW", ["sw"] = "SW",
    };

    static readonly Dictionary<string, string> Sufijos = new(StringComparer.OrdinalIgnoreCase)
    {
        // Street
        ["estrit"] = "St", ["strit"] = "St", ["street"] = "St", ["st"] = "St",
        // Avenue
        ["aveniu"] = "Ave", ["avenida"] = "Ave", ["avenue"] = "Ave", ["ave"] = "Ave",
        // Boulevard
        ["bulevar"] = "Blvd", ["boulevard"] = "Blvd", ["blvd"] = "Blvd",
        // Road
        ["roud"] = "Rd", ["rod"] = "Rd", ["road"] = "Rd", ["rd"] = "Rd",
        // Drive
        ["draiv"] = "Dr", ["dray"] = "Dr", ["drive"] = "Dr", ["dr"] = "Dr",
        // Lane
        ["lein"] = "Ln", ["lane"] = "Ln", ["ln"] = "Ln",
        // Court
        ["cort"] = "Ct", ["court"] = "Ct", ["ct"] = "Ct",
        // Way
        ["guei"] = "Way", ["uei"] = "Way", ["way"] = "Way", ["wy"] = "Way",
        // Square
        ["square"] = "Sq", ["sq"] = "Sq", ["sqr"] = "Sq", ["sqre"] = "Sq", ["squ"] = "Sq",
        ["escuer"] = "Sq", ["escueir"] = "Sq", ["eskuer"] = "Sq", ["skuer"] = "Sq",
        ["esquere"] = "Sq", ["squere"] = "Sq", ["esquer"] = "Sq",
        // Plaza
        ["plaza"] = "Plz", ["plz"] = "Plz", ["plasa"] = "Plz",
        // Place
        ["place"] = "Pl", ["pl"] = "Pl", ["pleis"] = "Pl", ["pleys"] = "Pl",
        // Terrace
        ["terrace"] = "Ter", ["ter"] = "Ter", ["terras"] = "Ter", ["teraza"] = "Ter", ["terraza"] = "Ter",
        // Trail
        ["trail"] = "Trl", ["trl"] = "Trl", ["treil"] = "Trl",
        // Loop
        ["loop"] = "Loop", ["lup"] = "Loop",
        // Circle
        ["circle"] = "Cir", ["circulo"] = "Cir", ["cir"] = "Cir", ["sercol"] = "Cir",
        // Parkway
        ["parkway"] = "Pkwy", ["pkwy"] = "Pkwy", ["parcuei"] = "Pkwy", ["pargüey"] = "Pkwy", ["parguey"] = "Pkwy",
        // Highway
        ["highway"] = "Hwy", ["hwy"] = "Hwy", ["jaiguey"] = "Hwy", ["jaigüey"] = "Hwy",
        // Alley
        ["alley"] = "Aly", ["aly"] = "Aly", ["ali"] = "Aly",
        // Path
        ["path"] = "Path", ["pas"] = "Path",
        // Cove
        ["cove"] = "Cv", ["cv"] = "Cv", ["couv"] = "Cv",
        // Crossing
        ["crossing"] = "Xing", ["xing"] = "Xing", ["crosin"] = "Xing",
        // Expressway
        ["expressway"] = "Expy", ["expy"] = "Expy", ["expresguey"] = "Expy",
        // Turnpike
        ["turnpike"] = "Tpke", ["tpke"] = "Tpke", ["tornpaik"] = "Tpke"
    };

    static readonly Dictionary<string, string> Estados2Letras = new(StringComparer.OrdinalIgnoreCase)
    {
        ["al"] = "AL", ["ak"] = "AK", ["az"] = "AZ", ["ar"] = "AR", ["ca"] = "CA", ["co"] = "CO",
        ["ct"] = "CT", ["de"] = "DE", ["fl"] = "FL", ["ga"] = "GA", ["hi"] = "HI", ["id"] = "ID",
        ["il"] = "IL", ["in"] = "IN", ["ia"] = "IA", ["ks"] = "KS", ["ky"] = "KY", ["la"] = "LA",
        ["me"] = "ME", ["md"] = "MD", ["ma"] = "MA", ["mi"] = "MI", ["mn"] = "MN", ["ms"] = "MS",
        ["mo"] = "MO", ["mt"] = "MT", ["ne"] = "NE", ["nv"] = "NV", ["nh"] = "NH", ["nj"] = "NJ",
        ["nm"] = "NM", ["ny"] = "NY", ["nc"] = "NC", ["nd"] = "ND", ["oh"] = "OH", ["ok"] = "OK",
        ["or"] = "OR", ["pa"] = "PA", ["ri"] = "RI", ["sc"] = "SC", ["sd"] = "SD", ["tn"] = "TN",
        ["tx"] = "TX", ["ut"] = "UT", ["vt"] = "VT", ["va"] = "VA", ["wa"] = "WA", ["wv"] = "WV",
        ["wi"] = "WI", ["wy"] = "WY", ["dc"] = "DC", ["pr"] = "PR"
    };

    static readonly Dictionary<string, string> EstadosNombres = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alabama"] = "AL",
        ["alaska"] = "AK",
        ["arizona"] = "AZ",
        ["arkansas"] = "AR",
        ["california"] = "CA", ["calif"] = "CA",
        ["colorado"] = "CO",
        ["connecticut"] = "CT",
        ["delaware"] = "DE",
        ["florida"] = "FL", ["fla"] = "FL",
        ["georgia"] = "GA",
        ["hawaii"] = "HI", ["hawai"] = "HI",
        ["idaho"] = "ID",
        ["illinois"] = "IL",
        ["indiana"] = "IN", ["ind"] = "IN",
        ["iowa"] = "IA",
        ["kansas"] = "KS", ["kans"] = "KS",
        ["kentucky"] = "KY",
        ["louisiana"] = "LA",
        ["maine"] = "ME",
        ["maryland"] = "MD",
        ["massachusetts"] = "MA", ["mass"] = "MA",
        ["michigan"] = "MI", ["mich"] = "MI",
        ["minnesota"] = "MN", ["minn"] = "MN",
        ["mississippi"] = "MS", ["miss"] = "MS",
        ["missouri"] = "MO",
        ["montana"] = "MT",
        ["nebraska"] = "NE", ["neb"] = "NE",
        ["nevada"] = "NV", ["nev"] = "NV",
        ["new hampshire"] = "NH",
        ["new jersey"] = "NJ",
        ["new mexico"] = "NM", ["nuevo mexico"] = "NM",
        ["new york"] = "NY", ["nueva york"] = "NY",
        ["north carolina"] = "NC", ["carolina del norte"] = "NC",
        ["north dakota"] = "ND", ["dakota del norte"] = "ND",
        ["ohio"] = "OH",
        ["oklahoma"] = "OK", ["okla"] = "OK",
        ["oregon"] = "OR",
        ["pennsylvania"] = "PA", ["pensilvania"] = "PA", ["penn"] = "PA",
        ["rhode island"] = "RI",
        ["south carolina"] = "SC", ["carolina del sur"] = "SC",
        ["south dakota"] = "SD", ["dakota del sur"] = "SD",
        ["tennessee"] = "TN", ["tenn"] = "TN",
        ["texas"] = "TX", ["tejas"] = "TX", ["tex"] = "TX",
        ["utah"] = "UT",
        ["vermont"] = "VT",
        ["virginia"] = "VA",
        ["washington"] = "WA", ["wash"] = "WA",
        ["west virginia"] = "WV", ["virginia occidental"] = "WV",
        ["wisconsin"] = "WI", ["wisc"] = "WI",
        ["wyoming"] = "WY",
        ["distrito de columbia"] = "DC", ["district of columbia"] = "DC",
        ["puerto rico"] = "PR"
    };
}
