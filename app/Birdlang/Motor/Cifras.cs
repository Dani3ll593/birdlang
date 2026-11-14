// Pasa los números dichos en palabras a cifras, con reglas fijas (sin IA).
// Los motores escriben "veintinueve" o "five five five", y para comparar con
// el guion (y para resaltar números en Birdlang) hacen falta las cifras.
//
// Un número se corta donde la gramática no deja seguir: "nineteen sixty-two"
// da 19 y 62, y "cinco cinco cinco" da 5, 5 y 5, que juntos son 1962 y 555.
static partial class Cifras
{
    enum Clase { Unidad, Diez, Decena, DecenaCompuesta, Centena, Hundred, Mil, Millon }

    record Palabra(Clase Clase, long Valor, bool Ordinal = false);

    static readonly Dictionary<string, Palabra> En = Lexico(
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"],
        ["ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"],
        ["twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"]);

    static readonly Dictionary<string, Palabra> Es = Lexico(
        ["cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve"],
        ["diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve"],
        ["veinte", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"]);

    static Cifras()
    {
        En["oh"] = En["o"] = new(Clase.Unidad, 0);
        En["hundred"] = new(Clase.Hundred, 100);
        En["thousand"] = new(Clase.Mil, 1000);
        En["million"] = new(Clase.Millon, 1_000_000);
        string[] ordinales = ["", "first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth",
            "tenth", "eleventh", "twelfth", "thirteenth", "fourteenth", "fifteenth", "sixteenth", "seventeenth",
            "eighteenth", "nineteenth"];
        for (int i = 1; i < ordinales.Length; i++) En[ordinales[i]] = new(i < 10 ? Clase.Unidad : Clase.Diez, i, true);
        string[] decenas = ["twentieth", "thirtieth", "fortieth", "fiftieth", "sixtieth", "seventieth", "eightieth", "ninetieth"];
        for (int i = 0; i < decenas.Length; i++) En[decenas[i]] = new(Clase.Decena, 20 + 10 * i, true);

        foreach (var (p, v) in new[] { ("un", 1), ("una", 1), ("dieciseis", 16), ("primero", 1), ("primer", 1) })
            Es[p] = new(p == "dieciseis" ? Clase.Diez : Clase.Unidad, v, p.StartsWith("prim"));
        string[] veinti = ["veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis",
            "veintisiete", "veintiocho", "veintinueve"];
        for (int i = 0; i < veinti.Length; i++)
        {
            Es[veinti[i]] = new(Clase.DecenaCompuesta, 21 + i);
            Es[SinTilde(veinti[i])] = new(Clase.DecenaCompuesta, 21 + i);
        }
        Es["veintiún"] = Es["veintiun"] = new(Clase.DecenaCompuesta, 21);
        Es["cien"] = Es["ciento"] = new(Clase.Centena, 100);
        string[] cientos = ["doscientos", "trescientos", "cuatrocientos", "quinientos", "seiscientos", "setecientos",
            "ochocientos", "novecientos"];
        for (int i = 0; i < cientos.Length; i++)
        {
            Es[cientos[i]] = new(Clase.Centena, 200 + 100 * i);
            Es[cientos[i][..^2] + "as"] = new(Clase.Centena, 200 + 100 * i);
        }
        Es["mil"] = new(Clase.Mil, 1000);
        Es["millón"] = Es["millon"] = Es["millones"] = new(Clase.Millon, 1_000_000);
    }

    static Dictionary<string, Palabra> Lexico(string[] unidades, string[] dieces, string[] decenas)
    {
        var d = new Dictionary<string, Palabra>();
        for (int i = 0; i < 10; i++) d[unidades[i]] = new(Clase.Unidad, i);
        for (int i = 0; i < 10; i++) d[dieces[i]] = new(Clase.Diez, 10 + i);
        for (int i = 0; i < 8; i++) d[decenas[i]] = new(Clase.Decena, 20 + 10 * i);
        return d;
    }

    // Una palabra en minúsculas y dónde está en el texto. La coma, el punto y
    // similares quedan como "," porque cortan un número: "doce, mil
    // novecientos" no es 12 900.
    record struct Token(string Palabra, int Inicio, int Fin);

    static List<Token> Tokens(string texto)
    {
        var lista = new List<Token>();
        int inicio = -1;
        for (int k = 0; k <= texto.Length; k++)
        {
            if (k < texto.Length && (char.IsLetterOrDigit(texto[k]) || texto[k] == '\''))
            {
                if (inicio < 0) inicio = k;
                continue;
            }
            if (inicio >= 0) lista.Add(new(texto[inicio..k].ToLowerInvariant(), inicio, k));
            inicio = -1;
            if (k < texto.Length && texto[k] is ',' or '.' or ';' or ':' or '?' or '!') lista.Add(new(",", k, k + 1));
        }
        return lista;
    }

    static string SinTilde(string s) => s.Replace('á', 'a').Replace('é', 'e').Replace('í', 'i').Replace('ó', 'o').Replace('ú', 'u');

    // Un número del texto: de qué palabra a cuál (sin incluir Hasta), sus
    // cifras y el sufijo de un ordinal en inglés ("14th").
    record Grupo(int Desde, int Hasta, string Cifras, string Sufijo);

    static List<Grupo> Grupos(string[] palabras, string idioma)
    {
        var lexico = idioma == "en" ? En : Es;
        var grupos = new List<Grupo>();
        int i = 0;
        while (i < palabras.Length)
        {
            var w = palabras[i];
            if (w.Any(char.IsDigit))
            {
                // Ya viene en cifras ("14th", "500"): cuentan solo los dígitos.
                grupos.Add(new(i, i + 1, string.Concat(w.Where(char.IsDigit)), ""));
                i++;
            }
            else if (idioma == "en" && (w == "double" || w == "triple") && i + 1 < palabras.Length
                     && lexico.TryGetValue(palabras[i + 1], out var u) && u.Clase == Clase.Unidad && !u.Ordinal)
            {
                grupos.Add(new(i, i + 2, new string((char)('0' + u.Valor), w == "double" ? 2 : 3), ""));
                i += 2;
            }
            else if (Numero(palabras, i, lexico, idioma) is (long valor, int fin, bool ordinal))
            {
                string sufijo = !ordinal || idioma != "en" ? ""
                    : (valor % 100) is 11 or 12 or 13 ? "th"
                    : (valor % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
                grupos.Add(new(i, fin, valor.ToString(), sufijo));
                i = fin;
            }
            else i++;
        }
        return grupos;
    }

    // Las cifras de todos los números del texto, una tras otra.
    public static string Extraer(string texto, string idioma) =>
        string.Concat(Grupos(Tokens(texto).Select(t => t.Palabra).ToArray(), idioma).Select(g => g.Cifras));

    // El texto con los números dichos en palabras pasados a cifras; el resto
    // queda igual. Los números de menos de 100 dichos seguidos, sin pausa, se
    // juntan: "cinco cinco cinco" da 555 y "nineteen sixty-two", 1962. Los
    // dígitos sueltos también, aunque el motor ponga comas entre ellos ("tres,
    // tres, uno" da 331). Un "un", "una" o "one" suelto queda en letras: "un
    // paciente" no es "1 paciente". Las horas van con dos puntos: "seven thirty
    // p.m." y el "7.30 p.m." que escribe Moonshine dan 7:30 p.m.
    public static string EnTexto(string texto, string idioma)
    {
        texto = HoraConPunto().Replace(texto, "$1:$2");
        var tokens = Tokens(texto);
        var palabras = tokens.Select(t => t.Palabra).ToArray();
        var grupos = Grupos(palabras, idioma);
        // Si entre dos grupos solo hay comas o nada.
        bool Juntos(Grupo a, Grupo b) => palabras[a.Hasta..b.Desde].All(w => w == ",");

        var sb = new System.Text.StringBuilder();
        int copiado = 0;
        Grupo? previo = null;
        string racha = ""; // las cifras ya juntadas con el grupo anterior
        bool rachaConComas = false;
        for (int k = 0; k < grupos.Count; k++)
        {
            var g = grupos[k];
            if (palabras[g.Desde].Any(char.IsDigit)) continue;
            if (g.Hasta == g.Desde + 1 && palabras[g.Desde] is "un" or "una" or "uno" or "one"
                && !(k > 0 && Juntos(grupos[k - 1], g)) && !(k + 1 < grupos.Count && Juntos(g, grupos[k + 1]))) continue;
            int inicio = tokens[g.Desde].Inicio;
            var entre = texto[copiado..inicio];
            bool seguido = previo is { Sufijo: "" } && Juntos(previo, g)
                && string.IsNullOrWhiteSpace(entre) && previo.Cifras.Length <= 2 && g.Cifras.Length <= 2;
            // Con coma, solo si toda la serie va dígito a dígito con comas:
            // en "tres cero cinco, cinco…" la coma separa 305 de 555.
            bool conComa = previo is { Sufijo: "" } && Juntos(previo, g) && entre.Trim() == ","
                && g.Cifras.Length == 1 && (racha.Length == 1 || rachaConComas);
            bool hora = previo is { Sufijo: "" } && racha == previo.Cifras && EsHora(palabras, previo, g, idioma);
            if (hora) (entre, racha, rachaConComas) = (":", "", false);
            else if (seguido || conComa)
            {
                entre = "";
                racha += g.Cifras;
                rachaConComas |= conComa;
            }
            else (racha, rachaConComas) = (g.Cifras, false);
            sb.Append(entre).Append(g.Cifras).Append(g.Sufijo);
            copiado = tokens[g.Hasta - 1].Fin;
            // Tras una hora no se junta nada más: "7:30" y no "7:305".
            previo = hora ? null : g;
        }
        return sb.Append(texto[copiado..]).ToString();
    }

    // Si la hora h y los minutos m forman una hora: "seven thirty p.m.", "a las
    // siete treinta", "a las diez y cuarenta y cinco". Sin esas señales,
    // "siete treinta" puede ser una dirección y queda en 730.
    static bool EsHora(string[] palabras, Grupo h, Grupo m, string idioma)
    {
        if (h.Cifras.Length > 2 || int.Parse(h.Cifras) > 23 || m.Cifras.Length != 2 || int.Parse(m.Cifras) is < 10 or > 59)
            return false;
        var entre = palabras[h.Hasta..m.Desde];
        if (!(entre.Length == 0 || idioma == "es" && entre is ["y"])) return false;
        string Despues(int i) => m.Hasta + i < palabras.Length ? palabras[m.Hasta + i] : "";
        if (idioma == "en")
            return Despues(0) is "am" or "pm" or "o'clock" || Despues(0) is "a" or "p" && Despues(1) == "," && Despues(2) == "m";
        return h.Desde > 0 && palabras[h.Desde - 1] is "las" or "la"
            || Despues(0) == "de" && Despues(1) == "la" && Despues(2) is "mañana" or "tarde" or "noche"
            || Despues(0) is "horas" or "hrs";
    }

    // Si el texto termina en un número: "…call back at 877", "…al ocho siete
    // siete". Quien dicta un teléfono por grupos puede seguir tras la pausa.
    public static bool TerminaEnNumero(string texto, string idioma)
    {
        var w = Tokens(texto).Select(t => t.Palabra).LastOrDefault(p => p != ",");
        return w != null && (w.Any(char.IsDigit) || w is not ("oh" or "o" or "un" or "una")
            && (idioma == "en" ? En : Es).TryGetValue(w, out var p) && !p.Ordinal);
    }

    // "7.30 p.m." como lo escribe Moonshine en inglés.
    [System.Text.RegularExpressions.GeneratedRegex(@"\b([01]?\d|2[0-3])\.([0-5]\d)(?=\s?[ap]\.?m\b)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex HoraConPunto();

    // Lee el número más largo que empieza en la palabra i. Devuelve su valor,
    // dónde termina y si es ordinal, o null si ahí no empieza un número.
    static (long Valor, int Fin, bool Ordinal)? Numero(string[] palabras, int i, Dictionary<string, Palabra> lexico, string idioma)
    {
        long total = 0, actual = 0;
        Clase? anterior = null;
        bool ordinal = false;
        int j = i;
        while (j < palabras.Length)
        {
            var w = palabras[j];
            // "a hundred", "a thousand"
            if (idioma == "en" && w == "a" && anterior == null && j + 1 < palabras.Length
                && lexico.TryGetValue(palabras[j + 1], out var m) && m.Clase is Clase.Hundred or Clase.Mil)
                w = "one";
            // "y" solo une decena y unidad (treinta y cinco); "and" sigue a
            // hundred o thousand (three hundred and fifteen). Una coma tras
            // mil o cientos no corta el número ("one thousand, two hundred"),
            // y en inglés tampoco entre decena y unidad ("forty, seven").
            if (anterior != null && j + 1 < palabras.Length && lexico.TryGetValue(palabras[j + 1], out var s)
                && (idioma == "es" && w == "y" && anterior == Clase.Decena && s.Clase == Clase.Unidad
                    || idioma == "en" && w == "and" && anterior is Clase.Hundred or Clase.Mil or Clase.Millon
                       && Sigue(anterior.Value, s.Clase, idioma)
                    || w == "," && s.Valor != 0 && Sigue(anterior.Value, s.Clase, idioma)
                       && (anterior is Clase.Hundred or Clase.Centena or Clase.Mil or Clase.Millon
                           || idioma == "en" && anterior == Clase.Decena)))
            {
                j++;
                continue;
            }
            // "oh" (u "o") es cero solo junto a otro número (no "Oh, okay").
            if (w is "oh" or "o" && anterior == null && !EsNumero(palabras, j, -1, lexico) && !EsNumero(palabras, j, 1, lexico)) break;
            if (!lexico.TryGetValue(w, out var p)) break;
            if (anterior != null && (!Sigue(anterior.Value, p.Clase, idioma) || p.Valor == 0)) break;
            // En español la unidad va tras la decena solo con "y".
            if (idioma == "es" && anterior == Clase.Decena && p.Clase == Clase.Unidad && palabras[j - 1] != "y") break;
            switch (p.Clase)
            {
                case Clase.Hundred: actual = (actual == 0 ? 1 : actual) * 100; break;
                case Clase.Mil or Clase.Millon:
                    total += (actual == 0 ? 1 : actual) * p.Valor;
                    actual = 0;
                    break;
                default: actual += p.Valor; break;
            }
            // Una decena seguida de unidad queda completa ("forty-five").
            anterior = anterior == Clase.Decena && p.Clase == Clase.Unidad ? Clase.DecenaCompuesta : p.Clase;
            j++;
            // Un ordinal cierra el número, y el cero va siempre solo.
            ordinal = p.Ordinal;
            if (p.Ordinal || p.Valor == 0) break;
        }
        return j == i ? null : (total + actual, j, ordinal);
    }

    // Si la palabra vecina (antes o después, sin contar comas) es un número.
    static bool EsNumero(string[] palabras, int i, int paso, Dictionary<string, Palabra> lexico)
    {
        do i += paso; while (i >= 0 && i < palabras.Length && palabras[i] == ",");
        return i >= 0 && i < palabras.Length && (lexico.ContainsKey(palabras[i]) || palabras[i].Any(char.IsDigit));
    }

    // Si la palabra de clase b puede seguir a la de clase a dentro del mismo número.
    static bool Sigue(Clase a, Clase b, string idioma) => a switch
    {
        Clase.Unidad or Clase.Diez or Clase.DecenaCompuesta =>
            b is Clase.Mil or Clase.Millon || idioma == "en" && b == Clase.Hundred,
        Clase.Decena => b is Clase.Unidad or Clase.Mil or Clase.Millon,
        Clase.Centena or Clase.Hundred => b is Clase.Unidad or Clase.Diez or Clase.Decena or Clase.DecenaCompuesta
            or Clase.Mil or Clase.Millon,
        Clase.Mil => b is Clase.Unidad or Clase.Diez or Clase.Decena or Clase.DecenaCompuesta or Clase.Centena,
        Clase.Millon => b is Clase.Unidad or Clase.Diez or Clase.Decena or Clase.DecenaCompuesta or Clase.Centena or Clase.Mil,
        _ => false,
    };
}
