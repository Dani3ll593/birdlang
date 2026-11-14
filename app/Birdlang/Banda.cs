using Avalonia;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

// La banda de un idioma: una frase por párrafo, la última abajo. Lo cerrado
// va en blanco con los números resaltados; lo abierto, en gris, porque todavía
// puede cambiar.
sealed partial class Banda : Border
{
    const int MaximoFrases = 300;
    IBrush brushTexto = new SolidColorBrush(Color.Parse("#F2F2F2"));
    IBrush brushAbierto = new SolidColorBrush(Color.Parse("#A8A8A8"));
    IBrush brushNumero = new SolidColorBrush(Color.Parse("#FFC857"));

    readonly StackPanel frases = new() { Spacing = 6 };
    readonly ScrollViewer desplazar;
    readonly Dictionary<int, SelectableTextBlock> porNumero = [];
    readonly Dictionary<int, (string Cerrado, string Abierto)> vistas = [];
    readonly Dictionary<int, string> textos = [];
    readonly TextBlock cabecera;
    readonly TextBlock agcIndicador;
    double tamano = 20;

    public event Action<int, string>? LineaClickeada;
    public event Action<string>? BuscarCalleClickeada;
    public event Action<string>? TextoSeleccionado;
    public bool TieneFrases => porNumero.Count > 0;

    public Banda(string titulo)
    {
        Background = new SolidColorBrush(Color.Parse("#162035"));
        CornerRadius = new CornerRadius(8);
        Padding = new(12, 6);
        desplazar = new ScrollViewer { Content = frases };
        var panel = new DockPanel();
        cabecera = new TextBlock { Text = titulo, FontSize = 11, Foreground = brushAbierto, VerticalAlignment = VerticalAlignment.Center };
        agcIndicador = new TextBlock
        {
            Text = "AGC: -20 dBFS",
            FontSize = 10,
            Foreground = brushAbierto,
            VerticalAlignment = VerticalAlignment.Center
        };
        var cabeceraFila = new DockPanel { Margin = new(0, 0, 0, 4) };
        DockPanel.SetDock(agcIndicador, Dock.Right);
        cabeceraFila.Children.Add(agcIndicador);
        cabeceraFila.Children.Add(cabecera);
        DockPanel.SetDock(cabeceraFila, Dock.Top);
        panel.Children.Add(cabeceraFila);
        panel.Children.Add(desplazar);
        Child = panel;
    }

    public void ActualizarAgc(float dbfs, float ganancia)
    {
        if (dbfs <= -55)
        {
            agcIndicador.Text = "AGC: silencio";
            agcIndicador.Foreground = brushAbierto;
        }
        else
        {
            agcIndicador.Text = $"AGC: {dbfs:0.0} dBFS (×{ganancia:0.0})";
            agcIndicador.Foreground = dbfs switch
            {
                >= -8 => new SolidColorBrush(Color.Parse("#DC2626")),   // Rojo saturación
                >= -25 => new SolidColorBrush(Color.Parse("#16A34A")),  // Verde nominal
                _ => new SolidColorBrush(Color.Parse("#D97706"))        // Ámbar bajo
            };
        }
    }

    public void AplicarTema(IBrush fondo, IBrush texto, IBrush abierto, IBrush numero)
    {
        Background = fondo;
        brushTexto = texto;
        brushAbierto = abierto;
        brushNumero = numero;
        cabecera.Foreground = abierto;
        agcIndicador.Foreground = abierto;

        foreach (var (fraseNum, bloque) in porNumero)
        {
            bloque.Foreground = texto;
            if (vistas.TryGetValue(fraseNum, out var v))
            {
                bloque.Inlines = ArmarInlines(v.Cerrado, v.Abierto);
            }
        }
    }

    InlineCollection ArmarInlines(string cerrado, string abierto)
    {
        var partes = new InlineCollection();
        int desde = 0;
        foreach (Match m in Cifra().Matches(cerrado))
        {
            if (m.Index > desde) partes.Add(new Run(cerrado[desde..m.Index]));
            partes.Add(new Run(m.Value) { Foreground = brushNumero, FontWeight = FontWeight.Bold });
            desde = m.Index + m.Length;
        }
        if (desde < cerrado.Length) partes.Add(new Run(cerrado[desde..]));
        if (abierto.Length > 0)
            partes.Add(new Run((cerrado.Length > 0 ? " " : "") + abierto) { Foreground = brushAbierto });
        return partes;
    }

    public double Tamano
    {
        get => tamano;
        set
        {
            tamano = value;
            foreach (var t in porNumero.Values) t.FontSize = value;
        }
    }

    public void Mostrar(Vista v)
    {
        if (v.Cerrado.Length == 0 && v.Abierto.Length == 0)
        {
            Remover(v.Frase);
            return;
        }

        string textoCompleto = v.Cerrado.Length > 0 && v.Abierto.Length > 0
            ? $"{v.Cerrado} {v.Abierto}"
            : (v.Cerrado.Length > 0 ? v.Cerrado : v.Abierto);
        textos[v.Frase] = textoCompleto;
        vistas[v.Frase] = (v.Cerrado, v.Abierto);

        bool alFinal = desplazar.Offset.Y >= desplazar.Extent.Height - desplazar.Viewport.Height - 30;
        if (!porNumero.TryGetValue(v.Frase, out var bloque))
        {
            bloque = new SelectableTextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = tamano,
                Foreground = brushTexto,
                Cursor = new Cursor(StandardCursorType.Ibeam),
                ContextFlyout = null,
                ContextMenu = null,
            };
            // README §4: no se copia desde los subtítulos, sin menú contextual ni historial de portapapeles.
            bloque.CopyingToClipboard += (_, e) => e.Handled = true;
            ToolTip.SetTip(bloque, "Clic: mover de ventana | Arrastrar texto: consultar Glosario / Calles");
            int num = v.Frase;
            bloque.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                string sel = bloque.SelectedText?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(sel))
                {
                    bloque.ClearSelection();
                    TextoSeleccionado?.Invoke(sel);
                    BuscarCalleClickeada?.Invoke(sel);
                }
                else if (textos.TryGetValue(num, out var t))
                {
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                    {
                        TextoSeleccionado?.Invoke(t);
                        BuscarCalleClickeada?.Invoke(t);
                    }
                    else
                        LineaClickeada?.Invoke(num, t);
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            porNumero[v.Frase] = bloque;
            frases.Children.Add(bloque);
            if (frases.Children.Count > MaximoFrases)
            {
                var vieja = (SelectableTextBlock)frases.Children[0];
                frases.Children.RemoveAt(0);
                int clave = porNumero.First(p => p.Value == vieja).Key;
                porNumero.Remove(clave);
                textos.Remove(clave);
                vistas.Remove(clave);
            }
        }
        bloque.Inlines = ArmarInlines(v.Cerrado, v.Abierto);
        if (alFinal) desplazar.ScrollToEnd();
    }

    public void Remover(int numero)
    {
        if (porNumero.Remove(numero, out var b))
        {
            b.Inlines = null;
            frases.Children.Remove(b);
        }
        textos.Remove(numero);
        vistas.Remove(numero);
    }

    public void Limpiar()
    {
        foreach (var t in porNumero.Values) t.Inlines = null;
        porNumero.Clear();
        textos.Clear();
        vistas.Clear();
        frases.Children.Clear();
    }

    // Una cifra, con sus separadores: 305, 1,247.50, 10:45, 555-1248.
    [GeneratedRegex(@"\d(?:[\d.,:/\-]*\d)?")]
    private static partial Regex Cifra();
}
