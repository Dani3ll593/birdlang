using System.Text.Json;
using Avalonia.Media;

// Sistema de temas dinámicos (Oscuro / Claro) y modding JSON en caliente.
// Cero persistencia en disco de datos de llamada; solo lectura opcional de 'tema.json'.
sealed class Tema
{
    public class DefinicionTema
    {
        public string Nombre { get; set; } = "Oscuro";
        public string FondoVentana { get; set; } = "#131826";
        public string FondoPanel { get; set; } = "#1A2234";
        public string FondoBandaIngles { get; set; } = "#162035";
        public string FondoBandaEspanol { get; set; } = "#14262E";
        public string TextoPrincipal { get; set; } = "#F1F5F9";
        public string TextoSecundario { get; set; } = "#94A3B8";
        public string Borde { get; set; } = "#283550";
        public string BordeSupervision { get; set; } = "#FFB300";
        public string FondoControl { get; set; } = "#202B42";
        public string TextoControl { get; set; } = "#F1F5F9";
        public string Acento { get; set; } = "#00E5FF";
        public string AcentoSecundario { get; set; } = "#2962FF";
        public string CifraColor { get; set; } = "#00E5FF";
        public string Fuente { get; set; } = "Segoe UI";
        public double TamanoFuente { get; set; } = 15;
    }

    public static DefinicionTema Oscuro => new()
    {
        Nombre = "Oscuro",
        FondoVentana = "#131826",
        FondoPanel = "#1A2234",
        FondoBandaIngles = "#162035",
        FondoBandaEspanol = "#14262E",
        TextoPrincipal = "#F1F5F9",
        TextoSecundario = "#94A3B8",
        Borde = "#283550",
        BordeSupervision = "#FFB300",
        FondoControl = "#202B42",
        TextoControl = "#F1F5F9",
        Acento = "#00E5FF",
        AcentoSecundario = "#2962FF",
        CifraColor = "#00E5FF",
        Fuente = "Segoe UI",
        TamanoFuente = 15
    };

    public static DefinicionTema Claro => new()
    {
        Nombre = "Claro",
        FondoVentana = "#F1F5F9",
        FondoPanel = "#FFFFFF",
        FondoBandaIngles = "#F0F6FC",
        FondoBandaEspanol = "#F2F9F4",
        TextoPrincipal = "#0F172A",
        TextoSecundario = "#475569",
        Borde = "#CBD5E1",
        BordeSupervision = "#D97706",
        FondoControl = "#E2E8F0",
        TextoControl = "#0F172A",
        Acento = "#0284C7",
        AcentoSecundario = "#1D4ED8",
        CifraColor = "#0369A1",
        Fuente = "Segoe UI",
        TamanoFuente = 15
    };

    public DefinicionTema Actual { get; private set; }
    public bool EsModoOscuro => Actual.Nombre == "Oscuro";

    public event Action? TemaCambiado;

    public Tema(DefinicionTema inicial)
    {
        Actual = inicial;
    }

    public static Tema Inicializar(string? rutaTemaJson = null)
    {
        if (!string.IsNullOrEmpty(rutaTemaJson) && File.Exists(rutaTemaJson))
        {
            try
            {
                string json = File.ReadAllText(rutaTemaJson);
                var def = JsonSerializer.Deserialize<DefinicionTema>(json);
                if (def != null) return new Tema(def);
            }
            catch { }
        }
        return new Tema(Oscuro);
    }

    public void Alternar()
    {
        Actual = EsModoOscuro ? Claro : Oscuro;
        TemaCambiado?.Invoke();
    }

    public void Establecer(DefinicionTema nuevoTema)
    {
        Actual = nuevoTema;
        TemaCambiado?.Invoke();
    }

    // Pinceles listos para Avalonia
    public IBrush BrushFondoVentana => Solid(Actual.FondoVentana);
    public IBrush BrushFondoPanel => Solid(Actual.FondoPanel);
    public IBrush BrushFondoBandaIngles => Solid(Actual.FondoBandaIngles);
    public IBrush BrushFondoBandaEspanol => Solid(Actual.FondoBandaEspanol);
    public IBrush BrushTextoPrincipal => Solid(Actual.TextoPrincipal);
    public IBrush BrushTextoSecundario => Solid(Actual.TextoSecundario);
    public IBrush BrushBorde => Solid(Actual.Borde);
    public IBrush BrushBordeSupervision => Solid(Actual.BordeSupervision);
    public IBrush BrushFondoControl => Solid(Actual.FondoControl);
    public IBrush BrushTextoControl => Solid(Actual.TextoControl);
    public IBrush BrushAcento => Solid(Actual.Acento);
    public IBrush BrushAcentoSecundario => Solid(Actual.AcentoSecundario);
    public IBrush BrushCifra => Solid(Actual.CifraColor);
    public FontFamily FamiliaFuente => new(Actual.Fuente);

    static SolidColorBrush Solid(string hex)
    {
        try { return new SolidColorBrush(Color.Parse(hex)); }
        catch { return new SolidColorBrush(Colors.Gray); }
    }
}
