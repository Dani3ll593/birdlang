using System.Text;
using System.Text.RegularExpressions;

// Buscador de glosario técnico y regionalismos en RAM (README §5).
// Búsqueda bidireccional (EN <-> ES) instantánea (< 5 ms), con filtros de país y categoría.
// Cero IA, determinista, puramente volátil en memoria.
sealed class BuscadorGlosario
{
    public record EntradaGlosario(
        string TerminoIngles,
        string TraduccionEspanol,
        string Categoria,
        string Definicion,
        string Pais,
        string InglesNorm,
        string EspanolNorm,
        string[] InglesTokens,
        string[] EspanolTokens,
        string MetaInglesP,
        string MetaEspanolP);

    public record CandidatoGlosario(
        string Termino,
        string Traduccion,
        string Categoria,
        string Definicion,
        string Pais,
        double Puntuacion)
    {
        public string LineaCorta => $"{Termino}  ➔  {Traduccion}";
        public string DetalleCompleto
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append($"{Termino} ➔ {Traduccion}");
                if (!string.IsNullOrEmpty(Pais) && Pais != "Todos") sb.Append($" [{Pais}]");
                if (!string.IsNullOrEmpty(Categoria)) sb.Append($" ({Categoria})");
                if (!string.IsNullOrEmpty(Definicion)) sb.Append($" — {Definicion}");
                return sb.ToString();
            }
        }
    }

    readonly EntradaGlosario[] entradas;
    readonly Dictionary<string, List<EntradaGlosario>> indiceExacto;

    public BuscadorGlosario(IEnumerable<string> lineasTsv)
    {
        var lista = new List<EntradaGlosario>();
        indiceExacto = new(StringComparer.OrdinalIgnoreCase);

        foreach (var linea in lineasTsv)
        {
            var l = linea.Trim();
            if (l.Length == 0 || l.StartsWith("TerminoIngles", StringComparison.OrdinalIgnoreCase)) continue;
            var partes = l.Split('\t');
            if (partes.Length < 2) continue;

            string en = partes[0].Trim();
            string es = partes[1].Trim();
            string cat = partes.Length > 2 ? partes[2].Trim() : "";
            string def = partes.Length > 3 ? partes[3].Trim() : "";
            string pais = partes.Length > 4 ? partes[4].Trim() : "Todos";
            if (string.IsNullOrEmpty(pais)) pais = "Todos";

            string enNorm = Normalizar(en);
            string esNorm = Normalizar(es);
            var (enP, _) = DoubleMetaphone(enNorm);
            var (esP, _) = DoubleMetaphone(esNorm);
            var enToks = enNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var esToks = esNorm.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var ent = new EntradaGlosario(en, es, cat, def, pais, enNorm, esNorm, enToks, esToks, enP, esP);
            lista.Add(ent);

            // Índices exactos por término completo y por cada variante separada por ';'
            // ("libertad bajo palabra; libertad sin fianza")
            AgregarIndice(enNorm, ent);
            AgregarIndice(esNorm, ent);
            foreach (var v in Variantes(en)) if (v != enNorm) AgregarIndice(v, ent);
            foreach (var v in Variantes(es)) if (v != esNorm) AgregarIndice(v, ent);
        }

        entradas = [.. lista];
    }

    static IEnumerable<string> Variantes(string s) =>
        s.Contains(';') ? s.Split(';').Select(Normalizar).Where(v => v.Length >= 2) : [];

    void AgregarIndice(string clave, EntradaGlosario ent)
    {
        if (clave.Length < 2) return;
        if (!indiceExacto.TryGetValue(clave, out var l))
        {
            l = [];
            indiceExacto[clave] = l;
        }
        l.Add(ent);
    }

    public static BuscadorGlosario Cargar(string rutaTsv)
    {
        if (!File.Exists(rutaTsv)) return new BuscadorGlosario([]);
        return new BuscadorGlosario(File.ReadLines(rutaTsv));
    }

    public int TotalTerminos => entradas.Length;

    public IReadOnlyList<CandidatoGlosario> Buscar(string consulta, string? filtroPais = null, string? filtroCategoria = null, int maxResultados = 6)
    {
        if (string.IsNullOrWhiteSpace(consulta)) return [];
        string q = Normalizar(consulta);
        if (q.Length < 2) return [];

        var (qMeta, _) = DoubleMetaphone(q);
        bool filtrarPais = !string.IsNullOrEmpty(filtroPais) && !filtroPais.Equals("Todos", StringComparison.OrdinalIgnoreCase);
        bool filtrarCat = !string.IsNullOrEmpty(filtroCategoria) && !filtroCategoria.Equals("Todos", StringComparison.OrdinalIgnoreCase);

        var resultados = new List<CandidatoGlosario>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Coincidencia exacta directa (O(1))
        if (indiceExacto.TryGetValue(q, out var exactos))
        {
            foreach (var e in exactos)
            {
                if (filtrarPais && !e.Pais.Equals("Todos", StringComparison.OrdinalIgnoreCase) && !e.Pais.Equals(filtroPais, StringComparison.OrdinalIgnoreCase)) continue;
                if (filtrarCat && !e.Categoria.Contains(filtroCategoria!, StringComparison.OrdinalIgnoreCase)) continue;

                // Determinar dirección (EN -> ES o ES -> EN)
                bool coincideIngles = e.InglesNorm.Equals(q, StringComparison.OrdinalIgnoreCase) || Variantes(e.TerminoIngles).Contains(q);
                string term = coincideIngles ? e.TerminoIngles : e.TraduccionEspanol;
                string trad = coincideIngles ? e.TraduccionEspanol : e.TerminoIngles;

                string key = $"{term}|{trad}";
                if (vistos.Add(key))
                {
                    resultados.Add(new CandidatoGlosario(term, trad, e.Categoria, e.Definicion, e.Pais, 100));
                }
            }
        }

        var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 2. Búsqueda por prefijo, contiene, tokens y fonética sobre la tabla en memoria
        for (int i = 0; i < entradas.Length && resultados.Count < maxResultados * 3; i++)
        {
            var e = entradas[i];
            if (filtrarPais && !e.Pais.Equals("Todos", StringComparison.OrdinalIgnoreCase) && !e.Pais.Equals(filtroPais, StringComparison.OrdinalIgnoreCase)) continue;
            if (filtrarCat && !e.Categoria.Contains(filtroCategoria!, StringComparison.OrdinalIgnoreCase)) continue;

            double ptsEn = Puntuar(q, qWords, qMeta, e.InglesNorm, e.InglesTokens, e.MetaInglesP);
            double ptsEs = Puntuar(q, qWords, qMeta, e.EspanolNorm, e.EspanolTokens, e.MetaEspanolP);

            if (ptsEn >= 50 || ptsEs >= 50)
            {
                bool mejorEn = ptsEn >= ptsEs;
                string term = mejorEn ? e.TerminoIngles : e.TraduccionEspanol;
                string trad = mejorEn ? e.TraduccionEspanol : e.TerminoIngles;
                double mejorPts = Math.Max(ptsEn, ptsEs);

                // Bono si coincide el país preferido
                if (filtrarPais && e.Pais.Equals(filtroPais, StringComparison.OrdinalIgnoreCase))
                {
                    mejorPts += 5;
                }

                string key = $"{term}|{trad}";
                if (vistos.Add(key))
                {
                    resultados.Add(new CandidatoGlosario(term, trad, e.Categoria, e.Definicion, e.Pais, mejorPts));
                }
            }
        }

        return resultados
            .OrderByDescending(r => r.Puntuacion)
            .Take(maxResultados)
            .ToList();
    }

    // Evalúa si un texto seleccionado es con alta probabilidad un término de glosario vs. una dirección
    public bool EsProbableGlosario(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return false;
        string t = texto.Trim();
        // Si inicia con números o contiene patrones claros de dirección postal, no es glosario
        if (Regex.IsMatch(t, @"^\d+\s") || Regex.IsMatch(t, @"\b(?:st|ave|blvd|rd|dr|way|ct|ln|hwy)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }
        if (Regex.IsMatch(t, @"^\d{5}$")) return false;

        var candidatos = Buscar(t, maxResultados: 1);
        return candidatos.Count > 0 && candidatos[0].Puntuacion >= 60;
    }

    static double Puntuar(string q, string[] qWords, string qMeta, string targetNorm, string[] tWords, string targetMeta)
    {
        // Consulta, términos, tokens y fonética ya vienen normalizados: basta Ordinal.
        if (targetNorm.Equals(q, StringComparison.Ordinal)) return 100;
        if (targetNorm.StartsWith(q, StringComparison.Ordinal)) return 85;
        if (targetNorm.Contains(q, StringComparison.Ordinal)) return 72;

        // Búsqueda por palabras completas (tokens precomputados)
        int matches = 0;
        for (int i = 0; i < qWords.Length; i++)
        {
            var qw = qWords[i];
            if (qw.Length >= 3)
            {
                for (int j = 0; j < tWords.Length; j++)
                {
                    if (tWords[j].StartsWith(qw, StringComparison.Ordinal))
                    {
                        matches++;
                        break;
                    }
                }
            }
        }
        if (qWords.Length > 0 && matches == qWords.Length) return 80;
        if (matches > 0) return 55 + (matches * 5);

        // Fonética DoubleMetaphone
        if (qMeta.Length >= 3 && targetMeta.Length >= 3 && targetMeta.StartsWith(qMeta, StringComparison.Ordinal))
        {
            return 58;
        }

        return 0;
    }

    public static string Normalizar(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string texto = s.Trim().ToLowerInvariant();
        texto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in texto)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)) sb.Append(c);
            }
        }
        return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
    }

    // Fonética simplificada DoubleMetaphone (igual a Calles.cs)
    static (string Primaria, string Secundaria) DoubleMetaphone(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return ("", "");
        string t = texto.ToUpperInvariant();
        var p = new StringBuilder();
        var s = new StringBuilder();

        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            switch (c)
            {
                case 'B': p.Append('P'); s.Append('P'); break;
                case 'C':
                    if (i + 1 < t.Length && (t[i + 1] == 'E' || t[i + 1] == 'I' || t[i + 1] == 'Y'))
                    {
                        p.Append('S'); s.Append('S');
                    }
                    else if (i + 1 < t.Length && t[i + 1] == 'H')
                    {
                        p.Append('X'); s.Append('X'); i++;
                    }
                    else
                    {
                        p.Append('K'); s.Append('K');
                    }
                    break;
                case 'D': p.Append('T'); s.Append('T'); break;
                case 'F': p.Append('F'); s.Append('F'); break;
                case 'G':
                    if (i + 1 < t.Length && (t[i + 1] == 'E' || t[i + 1] == 'I' || t[i + 1] == 'Y'))
                    {
                        p.Append('J'); s.Append('J');
                    }
                    else
                    {
                        p.Append('K'); s.Append('K');
                    }
                    break;
                case 'H':
                    if (i == 0 || "AEIOU".IndexOf(t[i - 1]) >= 0)
                    {
                        p.Append('H'); s.Append('H');
                    }
                    break;
                case 'J': p.Append('J'); s.Append('J'); break;
                case 'K': p.Append('K'); s.Append('K'); break;
                case 'L': p.Append('L'); s.Append('L'); break;
                case 'M': p.Append('M'); s.Append('M'); break;
                case 'N': p.Append('N'); s.Append('N'); break;
                case 'P':
                    if (i + 1 < t.Length && t[i + 1] == 'H') { p.Append('F'); s.Append('F'); i++; }
                    else { p.Append('P'); s.Append('P'); }
                    break;
                case 'Q': p.Append('K'); s.Append('K'); break;
                case 'R': p.Append('R'); s.Append('R'); break;
                case 'S':
                    if (i + 1 < t.Length && t[i + 1] == 'H') { p.Append('X'); s.Append('X'); i++; }
                    else { p.Append('S'); s.Append('S'); }
                    break;
                case 'T':
                    if (i + 1 < t.Length && t[i + 1] == 'H') { p.Append('0'); s.Append('T'); i++; }
                    else { p.Append('T'); s.Append('T'); }
                    break;
                case 'V': p.Append('F'); s.Append('F'); break;
                case 'W': p.Append('W'); s.Append('W'); break;
                case 'X': p.Append("KS"); s.Append("KS"); break;
                case 'Y': p.Append('Y'); s.Append('Y'); break;
                case 'Z': p.Append('S'); s.Append('S'); break;
            }
        }
        return (p.ToString(), s.ToString());
    }
}
