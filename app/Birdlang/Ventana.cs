using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

// Estación de trabajo de 3 columnas (Cockpit Layout, Versión 4.0):
// - Columna Izquierda (25%): Herramientas en RAM (Glosario arriba y Direcciones/Calles abajo)
// - Columna Central (45%): Subtítulos en vivo (Inglés arriba, Español abajo) con medidor AGC
// - Columna Derecha (30%): Cuaderno de Notas permanente + Barra de Dial-Out con auto-vaciado (20s)
// - Barra Superior: Modo Supervisión QA (borde ámbar + 20 min auto-off), Selector de Temas y controles
sealed class Ventana : Window
{
    static readonly (string Nombre, string Proceso)[] Navegadores = [("Edge", "msedge"), ("Chrome", "chrome"), ("Brave", "brave")];
    static readonly (string Nombre, string Carpeta)[] Modelos = [("Small", "small"), ("Tiny", "tiny")];
    static readonly string[] PaisesGlosario = ["Todos", "MX", "CO", "PR", "US", "AR", "VE", "Centroamérica"];

    readonly Tema tema;
    readonly Border marcoSupervision;
    bool modoSupervisionQA = false;
    DispatcherTimer? timerSupervisionQA;

    // Columna Izquierda: Herramientas
    BuscadorGlosario? buscadorGlosario;
    readonly BuscadorCalles buscadorCalles;
    readonly TextBox busquedaGlosario;
    readonly ComboBox selectorPais;
    readonly TextBlock tituloGlosario;
    readonly TextBlock ayudaGlosario;
    readonly StackPanel resultadosGlosario = new() { Spacing = 4 };
    List<BuscadorGlosario.CandidatoGlosario> candidatosGlosarioActuales = [];

    readonly TextBox busquedaCalles;
    readonly TextBlock cabeceraCalles;
    readonly TextBlock ayudaCalles;
    readonly StackPanel resultadosCalles = new() { Spacing = 6 };
    List<BuscadorCalles.CandidatoCalle> candidatosCallesActuales = [];

    // Columna Central: Subtítulos
    readonly Banda ingles = new("INGLÉS"), espanol = new("ESPAÑOL");

    // Columna Derecha: Notas y Dial-Out
    readonly TextBlock tituloNotas;
    readonly Button botonLimpiarNotas;
    readonly TextBox notas;
    readonly Border barraDialOut;
    readonly StackPanel interiorDialOut = new();
    readonly TextBlock tituloDialOut;
    readonly Button botonLimpiarDialOut;
    readonly WrapPanel chipsDialOut = new();
    readonly ScrollViewer scrollChips;
    readonly List<string> telefonosOrdenados = [];
    readonly HashSet<string> telefonosDeSubtitulos = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> telefonosDeNotas = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> telefonosDescartados = new(StringComparer.OrdinalIgnoreCase);
    string? telefonoCopiadoActivo;
    int segundosDialOutRestantes = 0;
    DispatcherTimer? timerCuentaRegresivaDialOut;

    // Encabezado de marca y onda de audio
    readonly TextBlock tituloApp;
    readonly Border badgeVersion;
    readonly Border separadorMarca;
    readonly StackPanel ondaAudio;
    readonly Border[] barrasOnda = new Border[4];
    readonly DispatcherTimer timerOndaAudio;
    static readonly double[] factoresOnda = [0.65, 1.0, 0.85, 0.7];
    readonly Random rnd = new();

    Border? panelGlosarioContenedor, panelCallesContenedor;

    // Controles de barra
    readonly ComboBox navegador, modelo;
    readonly Button escuchar;
    readonly ToggleButton botonSupervision;
    readonly Button botonTema;
    readonly Button botonActualizaciones;
    readonly Button menos;
    readonly Button mas;
    readonly ToggleButton encima;
    readonly Button copiar;
    readonly Button fin;
    readonly TextBlock estado;
    readonly Medidor medidor = new();
    readonly List<GridSplitter> splitters = [];

    Recorrido? recorrido;
    string? modeloCargado;
    CancellationTokenSource? cancelar;
    Task? escucha;
    string? escuchando; // nombre del navegador
    long ultimaActividad = Environment.TickCount64;
    const long InactividadMaximaMs = 15 * 60 * 1000; // 15 minutos en ms

    void RegistrarActividad() => Interlocked.Exchange(ref ultimaActividad, Environment.TickCount64);

    public Ventana()
    {
        Title = $"Birdlang {typeof(Ventana).Assembly.GetName().Version?.ToString(3)} - Cockpit";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 1240;
        Height = 680;
        MinWidth = 960;
        MinHeight = 460;

        string rutaIco = Nativos.Ruta("medios", "birdlang.ico");
        string rutaPng = Nativos.Ruta("medios", "logo.png");
        string rutaIcono = File.Exists(rutaIco) ? rutaIco : rutaPng;
        if (File.Exists(rutaIcono))
        {
            try { Icon = new WindowIcon(rutaIcono); } catch { }
        }

        // Inicializar tema (con lectura opcional de tema.json en datos/ o carpeta del programa)
        string rutaTema = File.Exists("tema.json") ? "tema.json" : Nativos.Ruta("datos", "tema.json");
        tema = Tema.Inicializar(rutaTema);
        Background = tema.BrushFondoVentana;

        // Cargar motores en RAM (el glosario se carga en segundo plano tras Opened)
        buscadorCalles = BuscadorCalles.Cargar(
            Nativos.Ruta("datos", "calles.txt"),
            Nativos.Ruta("datos", "codigos_postales.tsv"));

        // Cuaderno de notas (permanente en la columna derecha)
        tituloNotas = new TextBlock
        {
            Text = "📝 CUADERNO DE NOTAS",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = tema.BrushAcento,
            VerticalAlignment = VerticalAlignment.Center
        };

        notas = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            PlaceholderText = "Notas. Viven solo en memoria: no se guardan y no se pueden copiar.",
            ContextFlyout = null,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = tema.BrushFondoPanel,
            Foreground = tema.BrushTextoPrincipal,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        notas.CopyingToClipboard += (_, e) => e.Handled = true;
        notas.CuttingToClipboard += (_, e) => e.Handled = true;
        TextInputOptions.SetShowSuggestions(notas, false);
        TextInputOptions.SetIsSensitive(notas, true);
        notas.TextChanged += (_, _) =>
        {
            RegistrarActividad();
            EscanearTelefonos();
        };

        botonLimpiarNotas = new Button
        {
            Content = "Borrar notas",
            FontSize = 10,
            Padding = new Thickness(6, 1),
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(botonLimpiarNotas, "Borrar todo el texto de las notas rápidamente");
        botonLimpiarNotas.Click += (_, _) =>
        {
            RegistrarActividad();
            notas.Text = "";
            notas.Focus();
        };

        // Buscador de glosario (Columna izquierda, arriba)
        busquedaGlosario = new TextBox
        {
            FontSize = 14,
            PlaceholderText = "Cargando glosario…",
            ContextFlyout = null,
            Background = tema.BrushFondoPanel,
            Foreground = tema.BrushTextoPrincipal,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        busquedaGlosario.CopyingToClipboard += (_, e) => e.Handled = true;
        busquedaGlosario.CuttingToClipboard += (_, e) => e.Handled = true;
        TextInputOptions.SetShowSuggestions(busquedaGlosario, false);
        TextInputOptions.SetIsSensitive(busquedaGlosario, true);
        busquedaGlosario.TextChanged += (_, _) =>
        {
            RegistrarActividad();
            EjecutarBusquedaGlosario();
        };
        busquedaGlosario.KeyDown += (_, e) => ManejarTeclasBuscador(e, () =>
        {
            if (candidatosGlosarioActuales.Count > 0)
                InsertarEnNotas(candidatosGlosarioActuales[0].Traduccion);
        }, idx =>
        {
            if (idx >= 0 && idx < candidatosGlosarioActuales.Count)
                InsertarEnNotas(candidatosGlosarioActuales[idx].Traduccion);
        }, busquedaGlosario);

        selectorPais = new ComboBox
        {
            ItemsSource = PaisesGlosario,
            SelectedIndex = 0,
            FontSize = 11,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            CornerRadius = new CornerRadius(6)
        };
        selectorPais.SelectionChanged += (_, _) =>
        {
            RegistrarActividad();
            EjecutarBusquedaGlosario();
        };

        tituloGlosario = new TextBlock
        {
            Text = "📖 GLOSARIO TÉCNICO",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = tema.BrushAcento,
            VerticalAlignment = VerticalAlignment.Center
        };

        ayudaGlosario = new TextBlock
        {
            Text = "Enter: #1 · Alt+1..5: elegir · Esc: notas · Clic: insertar",
            FontSize = 10,
            Foreground = tema.BrushTextoSecundario,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 2, 2, 4)
        };

        // Buscador de calles (Columna izquierda, abajo)
        busquedaCalles = new TextBox
        {
            FontSize = 14,
            PlaceholderText = "Buscar dirección (ej: 120 nort guálnut mansi in 47304, pichtri, 90210)...",
            ContextFlyout = null,
            Background = tema.BrushFondoPanel,
            Foreground = tema.BrushTextoPrincipal,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        busquedaCalles.CopyingToClipboard += (_, e) => e.Handled = true;
        busquedaCalles.CuttingToClipboard += (_, e) => e.Handled = true;
        TextInputOptions.SetShowSuggestions(busquedaCalles, false);
        TextInputOptions.SetIsSensitive(busquedaCalles, true);
        busquedaCalles.TextChanged += (_, _) =>
        {
            RegistrarActividad();
            EjecutarBusquedaCalles();
        };
        busquedaCalles.KeyDown += (_, e) => ManejarTeclasBuscador(e, () =>
        {
            if (candidatosCallesActuales.Count > 0)
                InsertarEnNotas(candidatosCallesActuales[0].Completo);
        }, idx =>
        {
            if (idx >= 0 && idx < candidatosCallesActuales.Count)
                InsertarEnNotas(candidatosCallesActuales[idx].Completo);
        }, busquedaCalles);

        cabeceraCalles = new TextBlock
        {
            Text = "📍 DIRECCIONES Y CALLES",
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = tema.BrushAcento,
            Margin = new Thickness(0, 0, 0, 4)
        };

        ayudaCalles = new TextBlock
        {
            Text = "Enter: #1 · Alt+1..5: elegir · Esc: notas · Chips: sufijo/ZIP",
            FontSize = 10,
            Foreground = tema.BrushTextoSecundario,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 2, 2, 4)
        };

        // Barra Dial-Out
        tituloDialOut = new TextBlock
        {
            Text = "📞 DIAL-OUT SEGURO (Auto-vaciado 20s)",
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Foreground = tema.BrushAcento,
            VerticalAlignment = VerticalAlignment.Center
        };
        botonLimpiarDialOut = new Button
        {
            Content = "Limpiar",
            FontSize = 10,
            Padding = new Thickness(6, 1),
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(botonLimpiarDialOut, "Borrar todos los números detectados y vaciar el portapapeles");
        botonLimpiarDialOut.Click += (_, _) =>
        {
            RegistrarActividad();
            Privacidad.CancelarDialOut();
            telefonoCopiadoActivo = null;
            segundosDialOutRestantes = 0;
            timerCuentaRegresivaDialOut?.Stop();

            foreach (var tel in telefonosOrdenados)
            {
                string d = string.Concat(tel.Where(char.IsDigit));
                if (d.Length > 0) telefonosDescartados.Add(d);
                telefonosDescartados.Add(tel);
            }

            telefonosOrdenados.Clear();
            telefonosDeSubtitulos.Clear();
            telefonosDeNotas.Clear();
            RefrescarChipsDialOut();
        };

        scrollChips = new ScrollViewer
        {
            Content = chipsDialOut,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(2)
        };

        barraDialOut = new Border
        {
            Background = tema.BrushFondoPanel,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 4, 0, 0),
            Child = interiorDialOut
        };

        // Atajos globales
        AddHandler(KeyDownEvent, (sender, e) =>
        {
            RegistrarActividad();
            if (e.Key == Key.F1)
            {
                recorrido?.ForzarIdioma("en");
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                recorrido?.ForzarIdioma("es");
                e.Handled = true;
            }
            else if (e.Key == Key.F3 || (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                recorrido?.ForzarIdioma("toggle");
                e.Handled = true;
            }
            else if (e.Key == Key.F4 || (e.Key == Key.B && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                EnfocarBuscador(busquedaCalles);
                e.Handled = true;
            }
            else if (e.Key == Key.F5 || (e.Key == Key.G && e.KeyModifiers.HasFlag(KeyModifiers.Control)))
            {
                EnfocarBuscador(busquedaGlosario);
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);

        AddHandler(PointerPressedEvent, (_, _) => RegistrarActividad(), RoutingStrategies.Tunnel);

        // Conectar eventos de subtítulos
        ingles.LineaClickeada += (num, texto) => MoverLinea(num, texto, origen: ingles, destino: espanol, nuevoIdioma: "es");
        espanol.LineaClickeada += (num, texto) => MoverLinea(num, texto, origen: espanol, destino: ingles, nuevoIdioma: "en");

        ingles.TextoSeleccionado += EnrutarTextoSeleccionado;
        espanol.TextoSeleccionado += EnrutarTextoSeleccionado;

        // Armar Columna 1: Glosario arriba y Calles abajo con Splitter
        var panelIzquierdo = CrearColumnaHerramientas();

        // Armar Columna 2: Subtítulos Inglés y Español
        var panelCentral = new Grid { RowDefinitions = new("*,4,*") };
        panelCentral.Children.Add(ingles);
        panelCentral.Children.Add(Separador(Grid.RowProperty, 1));
        Grid.SetRow(espanol, 2);
        panelCentral.Children.Add(espanol);

        // Armar Columna 3: Notas permanentes + Barra Dial-Out
        var panelDerecho = CrearColumnaNotas();

        // Grid principal de 3 columnas (Cockpit Layout: 30% | 42% | 28%)
        var cockpitGrid = new Grid
        {
            ColumnDefinitions = new("30*, 4, 42*, 4, 28*"),
            Margin = new Thickness(4)
        };
        Grid.SetColumn(panelIzquierdo, 0);
        cockpitGrid.Children.Add(panelIzquierdo);

        cockpitGrid.Children.Add(Separador(Grid.ColumnProperty, 1));

        Grid.SetColumn(panelCentral, 2);
        cockpitGrid.Children.Add(panelCentral);

        cockpitGrid.Children.Add(Separador(Grid.ColumnProperty, 3));

        Grid.SetColumn(panelDerecho, 4);
        cockpitGrid.Children.Add(panelDerecho);

        // Encabezado de marca (Esquina superior izquierda)
        string rutaPngApp = Nativos.Ruta("medios", "logo.png");
        Image? iconoApp = null;
        if (File.Exists(rutaPngApp))
        {
            try
            {
                var bmp = new Bitmap(rutaPngApp);
                iconoApp = new Image
                {
                    Source = bmp,
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                RenderOptions.SetBitmapInterpolationMode(iconoApp, BitmapInterpolationMode.HighQuality);
            }
            catch { }
        }

        string version = typeof(Ventana).Assembly.GetName().Version?.ToString(2) ?? "4.0";
        tituloApp = new TextBlock
        {
            Text = "Birdlang",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Foreground = tema.BrushTextoPrincipal,
            VerticalAlignment = VerticalAlignment.Center
        };

        badgeVersion = new Border
        {
            Background = tema.BrushFondoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1),
            Margin = new Thickness(6, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = $"v{version}",
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = tema.BrushAcento,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        separadorMarca = new Border
        {
            Width = 1,
            Height = 18,
            Background = tema.BrushBorde,
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var bloqueMarca = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (iconoApp != null) bloqueMarca.Children.Add(iconoApp);
        bloqueMarca.Children.Add(tituloApp);
        bloqueMarca.Children.Add(badgeVersion);
        bloqueMarca.Children.Add(separadorMarca);

        // Barra superior de estado y controles
        navegador = new ComboBox
        {
            ItemsSource = Navegadores.Select(n => n.Nombre).ToArray(),
            SelectedIndex = 0,
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            CornerRadius = new CornerRadius(6)
        };
        modelo = new ComboBox
        {
            ItemsSource = Modelos.Select(m => m.Nombre).ToArray(),
            SelectedIndex = 0,
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            CornerRadius = new CornerRadius(6)
        };
        escuchar = new Button
        {
            Content = "Escuchar",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        escuchar.Click += async (_, _) => { if (escucha == null) await Iniciar(); else await Detener(); };

        botonSupervision = new ToggleButton
        {
            Content = "🔒 Oculto",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(botonSupervision, "Modo Supervisión QA (permite captura de pantalla temporal para monitoreo)");
        botonSupervision.IsCheckedChanged += (_, _) => AlternarModoSupervisionQA();

        botonTema = new Button
        {
            Content = tema.EsModoOscuro ? "☀️ Claro" : "🌙 Oscuro",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(botonTema, "Cambiar entre tema Oscuro y Claro");
        botonTema.Click += (_, _) =>
        {
            tema.Alternar();
            botonTema.Content = tema.EsModoOscuro ? "☀️ Claro" : "🌙 Oscuro";
            AplicarTemaVisual();
        };

        encima = new ToggleButton
        {
            Content = "📌 Encima",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(encima, "Mantener la ventana siempre visible encima de otras aplicaciones");
        encima.IsCheckedChanged += (_, _) => Topmost = encima.IsChecked == true;

        menos = new Button
        {
            Content = "A−",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        menos.Click += (_, _) => Letra(-2);

        mas = new Button
        {
            Content = "A+",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        mas.Click += (_, _) => Letra(2);

        copiar = new Button
        {
            Content = "📋 Métricas",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(copiar, "Copiar resumen de métricas de la llamada al portapapeles");
        copiar.Click += (_, _) => Privacidad.Copiar(Resumen());

        fin = new Button
        {
            Content = "Fin sesión",
            Background = new SolidColorBrush(Color.Parse("#7A2020")),
            Foreground = Brushes.White,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(fin, "Finalizar la llamada y limpiar subtítulos y notas de memoria");
        fin.Click += async (_, _) => await FinDeSesion();

        botonActualizaciones = new Button
        {
            Content = "Buscar actualizaciones",
            Background = tema.BrushFondoControl,
            Foreground = tema.BrushTextoControl,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            IsVisible = Actualizacion.Disponible
        };
        ToolTip.SetTip(botonActualizaciones, "Consultar si existe una versión más reciente en GitHub Releases");

        // Indicador de onda acústica inspirado en el logo
        var gradienteOnda = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.Parse("#2962FF"), 0),
                new GradientStop(Color.Parse("#00E5FF"), 1)
            ]
        };

        ondaAudio = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 22,
            Margin = new Thickness(10, 0, 6, 0)
        };
        ToolTip.SetTip(ondaAudio, "Nivel de voz en tiempo real (Moonshine AGC)");

        for (int i = 0; i < 4; i++)
        {
            var barra = new Border
            {
                Width = 3.5,
                Height = 4,
                MinHeight = 4,
                MaxHeight = 22,
                CornerRadius = new CornerRadius(1.75),
                Background = gradienteOnda,
                VerticalAlignment = VerticalAlignment.Center
            };
            barrasOnda[i] = barra;
            ondaAudio.Children.Add(barra);
        }

        timerOndaAudio = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        timerOndaAudio.Tick += (_, _) => AnimarOndaAudio();
        timerOndaAudio.Start();

        estado = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
            Foreground = tema.BrushTextoSecundario,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // Arriba, los controles de la sesión y de la vista: caben en una fila con
        // el ancho mínimo de la ventana. El estado va en su propia barra abajo,
        // a todo lo ancho, para que ningún botón lo tape.
        var controlesIzq = Fila(bloqueMarca, navegador, modelo, escuchar, botonSupervision, botonTema);
        var controlesDer = Fila(menos, mas, encima, fin);

        var barraSuperior = new DockPanel
        {
            Margin = new Thickness(8, 4),
            MinHeight = 36,
            LastChildFill = false
        };
        DockPanel.SetDock(controlesIzq, Dock.Left);
        DockPanel.SetDock(controlesDer, Dock.Right);
        barraSuperior.Children.Add(controlesIzq);
        barraSuperior.Children.Add(controlesDer);

        // Abajo: nivel de voz y estado; a la derecha, copiar esas cifras y buscar actualizaciones.
        var controlesEstado = Fila(copiar, botonActualizaciones);
        var barraEstado = new DockPanel
        {
            Margin = new Thickness(8, 0, 8, 4),
            MinHeight = 32
        };
        DockPanel.SetDock(controlesEstado, Dock.Right);
        DockPanel.SetDock(ondaAudio, Dock.Left);
        barraEstado.Children.Add(controlesEstado);
        barraEstado.Children.Add(ondaAudio);
        barraEstado.Children.Add(estado);

        // Aviso de actualización
        var avisoTexto = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0), Foreground = Brushes.White };
        var actualizar = new Button { Content = "Actualizar ahora" };
        actualizar.Click += async (_, _) =>
        {
            await FinDeSesion();
            Actualizacion.Instalar();
            Close();
        };
        var luego = new Button { Content = "Más tarde" };
        var avisoFila = new DockPanel();
        var avisoBotones = Fila(actualizar, luego);
        DockPanel.SetDock(avisoBotones, Dock.Right);
        avisoFila.Children.Add(avisoBotones);
        avisoFila.Children.Add(avisoTexto);
        var aviso = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1F3A5F")),
            Padding = new(10, 6),
            IsVisible = false,
            Child = avisoFila,
        };
        luego.Click += (_, _) => aviso.IsVisible = false;

        botonActualizaciones.Click += async (_, _) =>
        {
            botonActualizaciones.IsEnabled = false;
            try
            {
                estado.Text = "Buscando actualizaciones…";
                var resultado = await Actualizacion.Revisar();
                if (resultado == null)
                {
                    estado.Text = "No se pudo revisar";
                }
                else if (resultado == "al dia")
                {
                    estado.Text = "Birdlang está al día";
                }
                else
                {
                    avisoTexto.Text = $"Actualización disponible: Birdlang {resultado}. Al actualizar se cierra Birdlang y se borra la sesión.";
                    aviso.IsVisible = true;
                    estado.Text = "Actualización disponible";
                }
            }
            finally
            {
                botonActualizaciones.IsEnabled = true;
            }
        };

        // Aviso «Colabora con el proyecto» (Colaboracion.cs)
        var colaborarTexto = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(4, 0),
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            // «Fin de sesión» reinicia las métricas y vacía el portapapeles, y las dos
            // copias usan el mismo portapapeles: primero el enlace, después las métricas.
            Text = "¿Nos ayudas a medir Birdlang en más equipos? Copia el enlace y abre el formulario en tu navegador. " +
                   "Antes de pulsar «Fin de sesión», pulsa «📋 Métricas» y pega el resumen: solo datos técnicos, nada de lo que se dijo."
        };
        var copiarEnlace = new Button { Content = "Copiar enlace del formulario" };
        var ahoraNo = new Button { Content = "Ahora no" };
        var noMostrar = new Button { Content = "No volver a mostrar" };
        var colaborarFila = new DockPanel();
        var colaborarBotones = Fila(copiarEnlace, ahoraNo, noMostrar);
        DockPanel.SetDock(colaborarBotones, Dock.Right);
        colaborarFila.Children.Add(colaborarBotones);
        colaborarFila.Children.Add(colaborarTexto);
        var colaborar = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2A3550")),
            Padding = new(10, 6),
            IsVisible = Colaboracion.Mostrar(),
            Child = colaborarFila,
        };
        copiarEnlace.Click += (_, _) =>
        {
            Privacidad.Copiar(Colaboracion.Formulario);
            estado.Text = "Enlace copiado: ábrelo en tu navegador antes de copiar las métricas";
        };
        ahoraNo.Click += (_, _) => colaborar.IsVisible = false;
        noMostrar.Click += (_, _) =>
        {
            Colaboracion.NoVolverAMostrar();
            colaborar.IsVisible = false;
        };

        var contenidoRaiz = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto, Auto, Auto, *, Auto")
        };
        Grid.SetRow(aviso, 0);
        Grid.SetRow(colaborar, 1);
        Grid.SetRow(barraSuperior, 2);
        Grid.SetRow(cockpitGrid, 3);
        Grid.SetRow(barraEstado, 4);
        contenidoRaiz.Children.Add(aviso);
        contenidoRaiz.Children.Add(colaborar);
        contenidoRaiz.Children.Add(barraSuperior);
        contenidoRaiz.Children.Add(cockpitGrid);
        contenidoRaiz.Children.Add(barraEstado);

        // Marco perimetral para el Modo Supervisión QA (borde ámbar cuando está activo)
        marcoSupervision = new Border
        {
            BorderBrush = tema.BrushBordeSupervision,
            BorderThickness = new Thickness(0),
            Child = contenidoRaiz
        };
        Content = marcoSupervision;

        var reloj = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        reloj.Tick += (_, _) => Actualizar();
        reloj.Start();

        Opened += async (_, _) =>
        {
            if (TryGetPlatformHandle()?.Handle is IntPtr ventana) Privacidad.OcultarDeCapturas(ventana);

            // Cargar glosario en segundo plano sin demorar el arranque de la ventana
            _ = Task.Run(() =>
            {
                var ruta = Nativos.Ruta("datos", "glosario.tsv");
                var cargado = BuscadorGlosario.Cargar(ruta);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    buscadorGlosario = cargado;
                    busquedaGlosario.PlaceholderText = "Buscar en glosario (ej: affidavit, chamorro, copay)...";
                    if (!string.IsNullOrWhiteSpace(busquedaGlosario.Text))
                    {
                        EjecutarBusquedaGlosario();
                    }
                });
            });

            await Iniciar();
        };

        Closing += (_, _) =>
        {
            timerOndaAudio.Stop();
            cancelar?.Cancel();
            Privacidad.CancelarDialOut();
            notas.Text = "";
            busquedaCalles.Text = "";
            busquedaGlosario.Text = "";
        };

        // Carga inicial y aplicación del tema visual
        AplicarTemaVisual();
        EjecutarBusquedaGlosario();
        EjecutarBusquedaCalles();
    }

    Control CrearColumnaHerramientas()
    {
        // 1. Bloque Glosario (arriba)
        var cabeceraGlosario = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(selectorPais, Dock.Right);
        cabeceraGlosario.Children.Add(selectorPais);
        cabeceraGlosario.Children.Add(tituloGlosario);

        var arribaGlosario = new StackPanel();
        arribaGlosario.Children.Add(cabeceraGlosario);
        arribaGlosario.Children.Add(busquedaGlosario);
        arribaGlosario.Children.Add(ayudaGlosario);

        var scrollGlosario = new ScrollViewer
        {
            Content = resultadosGlosario,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var panelGlosario = new DockPanel { Margin = new Thickness(4) };
        DockPanel.SetDock(arribaGlosario, Dock.Top);
        panelGlosario.Children.Add(arribaGlosario);
        panelGlosario.Children.Add(scrollGlosario);

        panelGlosarioContenedor = new Border
        {
            Background = tema.BrushFondoPanel,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = panelGlosario
        };

        // 2. Bloque Calles y Direcciones (abajo)
        var arribaCalles = new StackPanel();
        arribaCalles.Children.Add(cabeceraCalles);
        arribaCalles.Children.Add(busquedaCalles);
        arribaCalles.Children.Add(ayudaCalles);

        var scrollCalles = new ScrollViewer
        {
            Content = resultadosCalles,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var panelCalles = new DockPanel { Margin = new Thickness(4) };
        DockPanel.SetDock(arribaCalles, Dock.Top);
        panelCalles.Children.Add(arribaCalles);
        panelCalles.Children.Add(scrollCalles);

        panelCallesContenedor = new Border
        {
            Background = tema.BrushFondoPanel,
            BorderBrush = tema.BrushBorde,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = panelCalles
        };

        // Grid dividido verticalmente con splitter
        var grid = new Grid { RowDefinitions = new("*, 4, *") };
        Grid.SetRow(panelGlosarioContenedor, 0);
        grid.Children.Add(panelGlosarioContenedor);

        grid.Children.Add(Separador(Grid.RowProperty, 1));

        Grid.SetRow(panelCallesContenedor, 2);
        grid.Children.Add(panelCallesContenedor);

        return grid;
    }

    Control CrearColumnaNotas()
    {
        // Cabecera superior del cuaderno de notas
        var cabeceraNotas = new DockPanel { Margin = new Thickness(4, 2, 4, 4) };
        DockPanel.SetDock(botonLimpiarNotas, Dock.Right);
        cabeceraNotas.Children.Add(botonLimpiarNotas);
        cabeceraNotas.Children.Add(tituloNotas);

        // Barra inferior de Dial-Out seguro
        var cabeceraDialOut = new DockPanel { Margin = new Thickness(4, 4, 4, 2) };
        DockPanel.SetDock(botonLimpiarDialOut, Dock.Right);
        cabeceraDialOut.Children.Add(botonLimpiarDialOut);
        cabeceraDialOut.Children.Add(tituloDialOut);

        interiorDialOut.Children.Clear();
        interiorDialOut.Children.Add(cabeceraDialOut);
        interiorDialOut.Children.Add(scrollChips);

        var panel = new DockPanel();
        DockPanel.SetDock(cabeceraNotas, Dock.Top);
        DockPanel.SetDock(barraDialOut, Dock.Bottom);
        panel.Children.Add(cabeceraNotas);
        panel.Children.Add(barraDialOut);
        panel.Children.Add(notas);

        RefrescarChipsDialOut();
        return panel;
    }

    // Enrutamiento inteligente al sombrear texto en subtítulos
    void EnrutarTextoSeleccionado(string texto)
    {
        RegistrarActividad();
        if (string.IsNullOrWhiteSpace(texto)) return;

        busquedaGlosario.Text = texto;
        busquedaCalles.Text = texto;

        if (buscadorGlosario?.EsProbableGlosario(texto) == true)
        {
            EnfocarBuscador(busquedaGlosario);
        }
        else
        {
            EnfocarBuscador(busquedaCalles);
        }
    }

    void EnfocarBuscador(TextBox cuadro)
    {
        Dispatcher.UIThread.Post(() =>
        {
            cuadro.Focus();
            cuadro.SelectAll();
        });
    }

    void ManejarTeclasBuscador(KeyEventArgs e, Action accionEnter, Action<int> accionNumero, TextBox cuadro)
    {
        RegistrarActividad();
        if (e.Key == Key.Enter)
        {
            accionEnter();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            cuadro.Text = "";
            notas.Focus();
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            int idx = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4,
                _ => -1
            };
            if (idx >= 0)
            {
                accionNumero(idx);
                e.Handled = true;
            }
        }
    }

    void EjecutarBusquedaGlosario()
    {
        resultadosGlosario.Children.Clear();
        candidatosGlosarioActuales.Clear();

        string? q = busquedaGlosario.Text?.Trim();
        if (string.IsNullOrEmpty(q))
        {
            resultadosGlosario.Children.Add(new TextBlock
            {
                Text = "Escribe para buscar términos médicos, legales, financieros o regionalismos.",
                FontSize = 12,
                Foreground = tema.BrushTextoSecundario,
                Margin = new Thickness(4)
            });
            return;
        }

        if (buscadorGlosario == null)
        {
            resultadosGlosario.Children.Add(new TextBlock
            {
                Text = "Cargando glosario en memoria…",
                FontSize = 12,
                Foreground = tema.BrushTextoSecundario,
                Margin = new Thickness(4)
            });
            return;
        }

        string pais = selectorPais.SelectedItem?.ToString() ?? "Todos";
        candidatosGlosarioActuales = buscadorGlosario.Buscar(q, filtroPais: pais, maxResultados: 6).ToList();

        if (candidatosGlosarioActuales.Count == 0)
        {
            resultadosGlosario.Children.Add(new TextBlock
            {
                Text = "Sin coincidencias en el glosario.",
                FontSize = 12,
                Foreground = tema.BrushTextoSecundario,
                Margin = new Thickness(4)
            });
            return;
        }

        for (int i = 0; i < candidatosGlosarioActuales.Count; i++)
        {
            var c = candidatosGlosarioActuales[i];
            int num = i + 1;

            var tarjeta = new Border
            {
                Background = tema.BrushFondoPanel,
                BorderBrush = tema.BrushBorde,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(tarjeta, $"Clic para insertar '{c.Traduccion}' en notas");

            var badge = new Border
            {
                Background = tema.BrushFondoControl,
                BorderBrush = tema.BrushBorde,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = $"{num}",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = tema.BrushAcento
                }
            };

            var contenido = new StackPanel { Spacing = 2 };
            var lineaPrincipal = new TextBlock
            {
                Text = c.LineaCorta,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = tema.BrushTextoPrincipal,
                TextWrapping = TextWrapping.Wrap
            };
            contenido.Children.Add(lineaPrincipal);

            if (!string.IsNullOrEmpty(c.Definicion) || (!string.IsNullOrEmpty(c.Pais) && c.Pais != "Todos"))
            {
                string extra = (!string.IsNullOrEmpty(c.Pais) && c.Pais != "Todos") ? $"[{c.Pais}] " : "";
                extra += c.Definicion;
                var lineaExtra = new TextBlock
                {
                    Text = extra,
                    FontSize = 11,
                    FontStyle = FontStyle.Italic,
                    Foreground = tema.BrushTextoSecundario,
                    TextWrapping = TextWrapping.Wrap
                };
                contenido.Children.Add(lineaExtra);
            }

            var fila = new DockPanel();
            DockPanel.SetDock(badge, Dock.Left);
            fila.Children.Add(badge);
            fila.Children.Add(contenido);

            tarjeta.Child = fila;
            string aInsertar = c.Traduccion;
            tarjeta.PointerPressed += (_, _) => InsertarEnNotas(aInsertar);

            resultadosGlosario.Children.Add(tarjeta);
        }
    }

    void EjecutarBusquedaCalles()
    {
        resultadosCalles.Children.Clear();
        candidatosCallesActuales.Clear();

        var texto = busquedaCalles.Text?.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            resultadosCalles.Children.Add(new TextBlock
            {
                Text = "Escribe para buscar direcciones o calles (número, calle, ciudad, estado o ZIP).",
                FontSize = 12,
                Foreground = tema.BrushTextoSecundario,
                Margin = new Thickness(4)
            });
            return;
        }

        candidatosCallesActuales = buscadorCalles.Buscar(texto, top: 5);
        if (candidatosCallesActuales.Count == 0)
        {
            resultadosCalles.Children.Add(new TextBlock
            {
                Text = "Sin coincidencias fonéticas ni postales.",
                FontSize = 12,
                Foreground = tema.BrushTextoSecundario,
                Margin = new Thickness(4)
            });
            return;
        }

        string[] sufijosChips = ["St", "Ave", "Blvd", "Rd", "Dr", "Way", "Ct", "Ln", "Sq", "Plz", "Ter", "Trl"];

        for (int i = 0; i < candidatosCallesActuales.Count; i++)
        {
            var c = candidatosCallesActuales[i];
            int numeroCandidato = i + 1;

            var tarjeta = new Border
            {
                Background = tema.BrushFondoPanel,
                BorderBrush = tema.BrushBorde,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 4),
                Margin = new Thickness(0, 0, 0, 6)
            };

            var contenido = new StackPanel { Spacing = 3 };

            var encabezado = new DockPanel { Cursor = new Cursor(StandardCursorType.Hand) };
            ToolTip.SetTip(encabezado, $"Clic para insertar '{c.Completo}' en notas");

            var badge = new Border
            {
                Background = tema.BrushFondoControl,
                BorderBrush = tema.BrushBorde,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1),
                Margin = new Thickness(0, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = $"{numeroCandidato}",
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    Foreground = tema.BrushAcento
                }
            };
            DockPanel.SetDock(badge, Dock.Left);
            encabezado.Children.Add(badge);

            string calleSolo = c.SoloCalle();
            bool tieneCalle = !string.IsNullOrEmpty(calleSolo);
            bool tieneCiudad = !string.IsNullOrEmpty(c.Ciudad);

            var panelLineas = new StackPanel { Spacing = 1 };

            var textoLinea1 = new TextBlock
            {
                Text = tieneCalle ? calleSolo : c.Completo,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = tema.BrushTextoPrincipal,
                TextWrapping = TextWrapping.Wrap
            };
            panelLineas.Children.Add(textoLinea1);

            if (tieneCalle && tieneCiudad)
            {
                string zipTexto = c.ZipFueIngresado ? $" {c.CodigoPostal}" : "";
                var textoLocalidad = new TextBlock
                {
                    Text = $"📍 {c.Ciudad}, {c.Estado}{zipTexto}",
                    FontSize = 11,
                    FontWeight = FontWeight.Medium,
                    Foreground = tema.BrushAcento,
                    TextWrapping = TextWrapping.Wrap
                };
                panelLineas.Children.Add(textoLocalidad);
            }

            encabezado.Children.Add(panelLineas);
            encabezado.PointerPressed += (_, _) => InsertarEnNotas(c.Completo);
            contenido.Children.Add(encabezado);

            // Fila de chips de sufijo y acciones
            var filaChips = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };

            if (tieneCalle)
            {
                foreach (var sufijo in sufijosChips)
                {
                    var chip = new Button
                    {
                        Content = sufijo,
                        FontSize = 10,
                        Padding = new Thickness(5, 1),
                        Margin = new Thickness(0, 0, 3, 3),
                        Background = tema.BrushFondoControl,
                        Foreground = tema.BrushTextoControl,
                        BorderBrush = tema.BrushBorde,
                        BorderThickness = new Thickness(1),
                        Cursor = new Cursor(StandardCursorType.Hand)
                    };
                    string conSufijo = c.ConSufijo(sufijo);
                    ToolTip.SetTip(chip, $"Insertar '{conSufijo}' en notas");
                    chip.Click += (_, _) => InsertarEnNotas(conSufijo);
                    filaChips.Children.Add(chip);
                }
            }

            if (!c.ZipFueIngresado && !string.IsNullOrEmpty(c.ZipSugerido))
            {
                var chipZip = new Button
                {
                    Content = $"+ {c.ZipSugerido}",
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    Padding = new Thickness(5, 1),
                    Margin = new Thickness(2, 0, 3, 3),
                    Background = tema.BrushFondoControl,
                    Foreground = tema.BrushAcento,
                    BorderBrush = tema.BrushAcento,
                    BorderThickness = new Thickness(1),
                    Cursor = new Cursor(StandardCursorType.Hand)
                };
                string conZip = c.ConZipSugerido();
                ToolTip.SetTip(chipZip, $"Insertar dirección con ZIP sugerido '{conZip}'");
                chipZip.Click += (_, _) => InsertarEnNotas(conZip);
                filaChips.Children.Add(chipZip);
            }

            contenido.Children.Add(filaChips);
            tarjeta.Child = contenido;
            resultadosCalles.Children.Add(tarjeta);
        }
    }

    void InsertarEnNotas(string texto)
    {
        RegistrarActividad();
        var actual = notas.Text ?? "";
        int start = Math.Min(notas.SelectionStart, notas.SelectionEnd);
        int end = Math.Max(notas.SelectionStart, notas.SelectionEnd);
        start = Math.Clamp(start, 0, actual.Length);
        end = Math.Clamp(end, 0, actual.Length);

        string prefijo = (start > 0 && !actual[..start].EndsWith(' ') && !actual[..start].EndsWith('\n')) ? " " : "";
        string aInsertar = prefijo + texto + " ";
        notas.Text = actual[..start] + aInsertar + actual[end..];
        notas.CaretIndex = start + aInsertar.Length;

        Dispatcher.UIThread.Post(() => notas.Focus());
    }

    // Dial-Out Seguro: Escaneo y renderizado de chips en tiempo real (más recientes primero)
    void EscanearTelefonos()
    {
        var telsNotas = Privacidad.ExtraerTelefonos(notas.Text ?? "");
        IncorporarTelefonos(telsNotas, esDeNotas: true);
    }

    public void RegistrarTelefonoDesdeSubtitulo(string texto)
    {
        var tels = Privacidad.ExtraerTelefonos(texto);
        if (tels.Count > 0)
        {
            Dispatcher.UIThread.Post(() => IncorporarTelefonos(tels, esDeNotas: false));
        }
    }

    void IncorporarTelefonos(IEnumerable<string> candidatos, bool esDeNotas)
    {
        bool cambio = false;
        foreach (var raw in candidatos)
        {
            string t = raw.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
            if (string.IsNullOrWhiteSpace(t)) continue;

            string dig = string.Concat(t.Where(char.IsDigit));
            if (dig.Length < 7) continue;
            // Lo escrito en las notas ya no lleva "verificar".
            if (esDeNotas && telefonosDeNotas.Add(dig)) cambio = true;

            // Si el número fue descartado por Limpiar, ignorar salvo que el usuario lo escriba de nuevo en notas
            if (telefonosDescartados.Contains(dig) || telefonosDescartados.Contains(t))
            {
                if (esDeNotas)
                {
                    telefonosDescartados.Remove(dig);
                    telefonosDescartados.Remove(t);
                }
                else
                {
                    continue;
                }
            }

            bool ignorar = false;
            for (int i = telefonosOrdenados.Count - 1; i >= 0; i--)
            {
                string existente = telefonosOrdenados[i];
                string digExistente = string.Concat(existente.Where(char.IsDigit));

                if (dig == digExistente)
                {
                    // Ya existe: si no está al principio, moverlo al frente
                    if (i != 0)
                    {
                        telefonosOrdenados.RemoveAt(i);
                        telefonosOrdenados.Insert(0, t);
                        cambio = true;
                    }
                    ignorar = true;
                    break;
                }

                // Subsunción: si el nuevo número contiene al existente y es más largo (ej. 3055551234 contiene 5551234 o 3055551)
                if (dig.Length > digExistente.Length && (dig.EndsWith(digExistente) || dig.Contains(digExistente)))
                {
                    telefonosOrdenados.RemoveAt(i);
                    cambio = true;
                }
                // Si el número existente contiene al nuevo y es más largo (el nuevo es solo un fragmento parcial menor)
                else if (digExistente.Length > dig.Length && (digExistente.EndsWith(dig) || digExistente.Contains(dig)))
                {
                    ignorar = true;
                    break;
                }
            }

            if (!ignorar)
            {
                // El más reciente siempre primero (a la izquierda, sin requerir scroll)
                telefonosOrdenados.Insert(0, t);
                cambio = true;
            }
        }

        while (telefonosOrdenados.Count > 8)
        {
            telefonosOrdenados.RemoveAt(telefonosOrdenados.Count - 1);
            cambio = true;
        }

        if (cambio)
        {
            RefrescarChipsDialOut();
        }
    }

    void RefrescarChipsDialOut()
    {
        chipsDialOut.Children.Clear();
        if (telefonosOrdenados.Count == 0)
        {
            chipsDialOut.Children.Add(new TextBlock
            {
                Text = "Sin teléfonos detectados aún.",
                FontSize = 10,
                Foreground = tema.BrushTextoSecundario,
                Margin = new Thickness(4, 2)
            });
            return;
        }

        foreach (var tel in telefonosOrdenados)
        {
            bool esActivo = (telefonoCopiadoActivo == tel && segundosDialOutRestantes > 0);
            // Un número que solo salió de los subtítulos lo escuchó el motor de voz,
            // que puede inventar dígitos: se confirma con la persona antes de marcar.
            bool verificar = !telefonosDeNotas.Contains(string.Concat(tel.Where(char.IsDigit)));
            string etiqueta = $"📞 {tel}" + (verificar ? " · verificar" : "")
                + (esActivo ? $" [⏳ {segundosDialOutRestantes}s]" : "");

            var chip = new Button
            {
                Content = etiqueta,
                FontSize = 10,
                FontWeight = esActivo ? FontWeight.Bold : FontWeight.SemiBold,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(0, 0, 4, 4),
                Background = esActivo ? new SolidColorBrush(Color.Parse("#7A4D00")) : tema.BrushFondoControl,
                Foreground = esActivo ? Brushes.White : tema.BrushTextoControl,
                BorderBrush = esActivo ? tema.BrushBordeSupervision : tema.BrushBorde,
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand)
            };

            string tNumero = tel;
            ToolTip.SetTip(chip, (verificar ? "Escuchado por el motor de voz: confírmalo con la persona antes de marcar.\n" : "")
                + $"Copiar {tel} para Dial-Out (auto-vaciado en 20 s sin Win+V)");
            chip.Click += (_, _) => IniciarCopiaDialOut(tNumero);
            chipsDialOut.Children.Add(chip);
        }

        scrollChips.Offset = new Vector(0, 0);
    }

    void IniciarCopiaDialOut(string numero)
    {
        RegistrarActividad();
        telefonoCopiadoActivo = numero;
        segundosDialOutRestantes = 20;

        Privacidad.CopiarTemporalDialOut(numero, 20, () =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                telefonoCopiadoActivo = null;
                segundosDialOutRestantes = 0;
                timerCuentaRegresivaDialOut?.Stop();
                RefrescarChipsDialOut();
            });
        });

        timerCuentaRegresivaDialOut?.Stop();
        timerCuentaRegresivaDialOut = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timerCuentaRegresivaDialOut.Tick += (_, _) =>
        {
            segundosDialOutRestantes--;
            if (segundosDialOutRestantes <= 0)
            {
                timerCuentaRegresivaDialOut?.Stop();
                telefonoCopiadoActivo = null;
            }
            RefrescarChipsDialOut();
        };
        timerCuentaRegresivaDialOut.Start();

        RefrescarChipsDialOut();
    }

    // Modo Supervisión QA: Borde ámbar visible + apagado automático a los 20 min
    void AlternarModoSupervisionQA()
    {
        RegistrarActividad();
        if (TryGetPlatformHandle()?.Handle is not IntPtr hwnd) return;

        modoSupervisionQA = botonSupervision.IsChecked == true;
        if (modoSupervisionQA)
        {
            Privacidad.PermitirCapturas(hwnd);
            marcoSupervision.BorderThickness = new Thickness(3);
            botonSupervision.Content = "👁️ Supervisión ACTIVA";
            botonSupervision.Background = new SolidColorBrush(Color.Parse("#8C5B00"));
            botonSupervision.Foreground = Brushes.White;

            timerSupervisionQA?.Stop();
            timerSupervisionQA = new DispatcherTimer { Interval = TimeSpan.FromMinutes(20) };
            timerSupervisionQA.Tick += (_, _) =>
            {
                // Auto-apagado de seguridad anti-olvido a los 20 minutos
                botonSupervision.IsChecked = false;
                timerSupervisionQA.Stop();
            };
            timerSupervisionQA.Start();
        }
        else
        {
            Privacidad.OcultarDeCapturas(hwnd);
            marcoSupervision.BorderThickness = new Thickness(0);
            botonSupervision.Content = "🔒 Oculto";
            botonSupervision.Background = tema.BrushFondoControl;
            botonSupervision.Foreground = tema.BrushTextoControl;
            timerSupervisionQA?.Stop();
        }
    }

    void AplicarTemaVisual()
    {
        // 1. Sincronizar ThemeVariant de Avalonia a nivel ventana y aplicación
        var variant = tema.EsModoOscuro ? ThemeVariant.Dark : ThemeVariant.Light;
        RequestedThemeVariant = variant;
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = variant;
        }

        Background = tema.BrushFondoVentana;
        marcoSupervision.BorderBrush = tema.BrushBordeSupervision;

        // Encabezado de marca dinámico
        tituloApp.Foreground = tema.BrushTextoPrincipal;
        badgeVersion.Background = tema.BrushFondoControl;
        badgeVersion.BorderBrush = tema.BrushBorde;
        if (badgeVersion.Child is TextBlock tbBadge)
            tbBadge.Foreground = tema.BrushAcento;
        separadorMarca.Background = tema.BrushBorde;

        // Contenedores de paneles
        if (panelGlosarioContenedor != null)
        {
            panelGlosarioContenedor.Background = tema.BrushFondoPanel;
            panelGlosarioContenedor.BorderBrush = tema.BrushBorde;
        }
        if (panelCallesContenedor != null)
        {
            panelCallesContenedor.Background = tema.BrushFondoPanel;
            panelCallesContenedor.BorderBrush = tema.BrushBorde;
        }

        // Notas
        tituloNotas.Foreground = tema.BrushAcento;
        botonLimpiarNotas.Background = tema.BrushFondoControl;
        botonLimpiarNotas.Foreground = tema.BrushTextoControl;
        botonLimpiarNotas.BorderBrush = tema.BrushBorde;

        notas.Background = tema.BrushFondoPanel;
        notas.Foreground = tema.BrushTextoPrincipal;
        notas.BorderBrush = tema.BrushBorde;
        notas.CaretBrush = tema.BrushTextoPrincipal;

        // Buscadores
        busquedaGlosario.Background = tema.BrushFondoPanel;
        busquedaGlosario.Foreground = tema.BrushTextoPrincipal;
        busquedaGlosario.BorderBrush = tema.BrushBorde;
        busquedaGlosario.CaretBrush = tema.BrushTextoPrincipal;

        busquedaCalles.Background = tema.BrushFondoPanel;
        busquedaCalles.Foreground = tema.BrushTextoPrincipal;
        busquedaCalles.BorderBrush = tema.BrushBorde;
        busquedaCalles.CaretBrush = tema.BrushTextoPrincipal;

        // Subtítulos
        ingles.AplicarTema(tema.BrushFondoBandaIngles, tema.BrushTextoPrincipal, tema.BrushTextoSecundario, tema.BrushCifra);
        espanol.AplicarTema(tema.BrushFondoBandaEspanol, tema.BrushTextoPrincipal, tema.BrushTextoSecundario, tema.BrushCifra);

        // Barra superior
        botonTema.Background = tema.BrushFondoControl;
        botonTema.Foreground = tema.BrushTextoControl;
        botonTema.BorderBrush = tema.BrushBorde;

        if (botonActualizaciones != null)
        {
            botonActualizaciones.Background = tema.BrushFondoControl;
            botonActualizaciones.Foreground = tema.BrushTextoControl;
            botonActualizaciones.BorderBrush = tema.BrushBorde;
        }

        menos.Background = tema.BrushFondoControl;
        menos.Foreground = tema.BrushTextoControl;
        menos.BorderBrush = tema.BrushBorde;

        mas.Background = tema.BrushFondoControl;
        mas.Foreground = tema.BrushTextoControl;
        mas.BorderBrush = tema.BrushBorde;

        encima.Background = tema.BrushFondoControl;
        encima.Foreground = tema.BrushTextoControl;
        encima.BorderBrush = tema.BrushBorde;

        copiar.Background = tema.BrushFondoControl;
        copiar.Foreground = tema.BrushTextoControl;
        copiar.BorderBrush = tema.BrushBorde;

        navegador.Background = tema.BrushFondoControl;
        navegador.Foreground = tema.BrushTextoControl;
        navegador.BorderBrush = tema.BrushBorde;

        modelo.Background = tema.BrushFondoControl;
        modelo.Foreground = tema.BrushTextoControl;
        modelo.BorderBrush = tema.BrushBorde;

        escuchar.Background = tema.BrushFondoControl;
        escuchar.Foreground = tema.BrushTextoControl;
        escuchar.BorderBrush = tema.BrushBorde;

        estado.Foreground = tema.BrushTextoSecundario;

        // Barra Dial-Out
        barraDialOut.Background = tema.BrushFondoPanel;
        tituloDialOut.Foreground = tema.BrushAcento;
        botonLimpiarDialOut.Background = tema.BrushFondoControl;
        botonLimpiarDialOut.Foreground = tema.BrushTextoControl;
        botonLimpiarDialOut.BorderBrush = tema.BrushBorde;

        // Títulos y ayudas de herramientas
        tituloGlosario.Foreground = tema.BrushAcento;
        ayudaGlosario.Foreground = tema.BrushTextoSecundario;
        selectorPais.Background = tema.BrushFondoControl;
        selectorPais.Foreground = tema.BrushTextoControl;
        selectorPais.BorderBrush = tema.BrushBorde;

        cabeceraCalles.Foreground = tema.BrushAcento;
        ayudaCalles.Foreground = tema.BrushTextoSecundario;

        foreach (var s in splitters)
        {
            s.Background = tema.BrushBorde;
        }

        if (!modoSupervisionQA)
        {
            botonSupervision.Background = tema.BrushFondoControl;
            botonSupervision.Foreground = tema.BrushTextoControl;
            botonSupervision.BorderBrush = tema.BrushBorde;
        }

        EjecutarBusquedaGlosario();
        EjecutarBusquedaCalles();
        RefrescarChipsDialOut();
    }

    async Task Iniciar()
    {
        var (nombre, proceso) = Navegadores[navegador.SelectedIndex];
        var p = Process.GetProcessesByName(proceso).OrderBy(x => x.StartTime).FirstOrDefault();
        if (p == null)
        {
            estado.Text = $"{nombre} no está abierto. Ábrelo y presiona Escuchar.";
            return;
        }
        var carpeta = Modelos[modelo.SelectedIndex].Carpeta;
        if (recorrido == null || modeloCargado != carpeta)
        {
            estado.Text = "Cargando el motor…";
            escuchar.IsEnabled = false;
            recorrido?.Dispose();
            recorrido = null;
            modeloCargado = null;
            try { recorrido = await Task.Run(() => new Recorrido(carpeta)); }
            catch (Exception e)
            {
                // Sin esto el error cierra el programa sin aviso. Al cargar no hay
                // nada de una sesión: el mensaje solo nombra archivos del programa.
                escuchar.IsEnabled = true;
                estado.Text = $"No se pudo cargar el motor ({e.GetType().Name}: {e.Message})";
                return;
            }
            recorrido.Cambio += v =>
            {
                RegistrarActividad();
                Dispatcher.UIThread.Post(() =>
                {
                    (v.Idioma == "en" ? ingles : espanol).Mostrar(v);
                    if (!string.IsNullOrWhiteSpace(v.Cerrado))
                    {
                        RegistrarTelefonoDesdeSubtitulo(v.Cerrado);
                    }
                });
            };
            modeloCargado = carpeta;
            escuchar.IsEnabled = true;
        }
        cancelar = new CancellationTokenSource();
        var r = recorrido;
        escucha = Task.Run(() => r.Escuchar(p.Id, cancelar.Token));
        escuchando = nombre;
        escuchar.Content = "Pausar";
        navegador.IsEnabled = modelo.IsEnabled = false;
        _ = escucha.ContinueWith(t =>
        {
            if (t.IsFaulted) Dispatcher.UIThread.Post(async () =>
            {
                await Detener();
                estado.Text = $"Se cortó la captura ({t.Exception!.InnerException?.GetType().Name}). Presiona Escuchar.";
            });
        });
    }

    async Task Detener()
    {
        if (escucha == null) return;
        cancelar!.Cancel();
        try { await escucha; } catch { }
        (escucha, escuchando) = (null, null);
        escuchar.Content = "Escuchar";
        navegador.IsEnabled = modelo.IsEnabled = true;
    }

    async Task FinDeSesion()
    {
        await Detener();
        ingles.Limpiar();
        espanol.Limpiar();
        notas.Text = "";
        notas.IsUndoEnabled = false;
        notas.IsUndoEnabled = true;
        busquedaCalles.Text = "";
        busquedaGlosario.Text = "";
        candidatosCallesActuales.Clear();
        candidatosGlosarioActuales.Clear();
        resultadosCalles.Children.Clear();
        resultadosGlosario.Children.Clear();
        telefonosOrdenados.Clear();
        telefonosDeSubtitulos.Clear();
        telefonosDeNotas.Clear();
        telefonosDescartados.Clear();
        telefonoCopiadoActivo = null;
        Privacidad.CancelarDialOut();
        RefrescarChipsDialOut();

        // Restaurar modo supervisión a cerrado estricto
        if (modoSupervisionQA)
        {
            botonSupervision.IsChecked = false;
        }

        recorrido?.Reiniciar();
        medidor.Reiniciar();
        RegistrarActividad();
        GC.Collect();
        estado.Text = "Sesión borrada. Presiona Escuchar para empezar otra.";
    }

    void AnimarOndaAudio()
    {
        if (escucha == null || recorrido == null)
        {
            for (int i = 0; i < 4; i++)
            {
                if (barrasOnda[i].Height > 4.5)
                    barrasOnda[i].Height = Math.Max(4.0, barrasOnda[i].Height * 0.70);
                else
                    barrasOnda[i].Height = 4.0;
            }
            return;
        }

        float db = recorrido.DbfsActual;
        // dbfs va típicamente de -80 dBFS (silencio) a -10 dBFS (voz alta)
        // Umbral de habla activa: > -48 dBFS
        double intensidad = Math.Clamp((db - (-48.0)) / ((-15.0) - (-48.0)), 0.0, 1.0);

        for (int i = 0; i < 4; i++)
        {
            double target;
            if (intensidad < 0.05)
            {
                target = 4.0;
            }
            else
            {
                double jitter = 0.82 + 0.36 * rnd.NextDouble();
                target = Math.Clamp(4.0 + (intensidad * 18.0 * factoresOnda[i] * jitter), 4.0, 22.0);
            }
            barrasOnda[i].Height = barrasOnda[i].Height * 0.40 + target * 0.60;
        }
    }

    async void Actualizar()
    {
        long inactivoMs = Environment.TickCount64 - Interlocked.Read(ref ultimaActividad);
        if (inactivoMs >= InactividadMaximaMs)
        {
            bool haySesionActiva = escucha != null || (notas.Text?.Length ?? 0) > 0 || (busquedaCalles.Text?.Length ?? 0) > 0 || ingles.TieneFrases || espanol.TieneFrases;
            if (haySesionActiva)
            {
                await FinDeSesion();
                estado.Text = "Sesión borrada automáticamente por inactividad (15 min sin actividad).";
                return;
            }
        }

        medidor.Leer(escucha != null);
        if (escucha == null || recorrido == null) return;
        var m = recorrido.Medir();
        string demora = m.Demora is double d ? $"{d:0.0} s" : "—";
        string perdido = m.BloquesPerdidos > 0 ? $" · audio perdido {m.AudioPerdido:0.0} s ({m.BloquesPerdidos} veces)" : "";
        estado.Text = $"Escuchando {escuchando} · procesador {medidor.Cpu:0} % · RAM {medidor.Ram / (1024 * 1024)} MB · " +
                      $"demora {demora} · atraso {m.Atraso:0.0} s{perdido} · {m.FrasesEn + m.FrasesEs} frases";
        ToolTip.SetTip(copiar, Resumen());

        // Actualizar indicador AGC en los subtítulos
        ingles.ActualizarAgc(recorrido.DbfsActual, recorrido.GananciaActual);
        espanol.ActualizarAgc(recorrido.DbfsActual, recorrido.GananciaActual);
    }

    void MoverLinea(int num, string texto, Banda origen, Banda destino, string nuevoIdioma)
    {
        RegistrarActividad();
        if (recorrido != null && recorrido.EsFraseActiva(num))
        {
            recorrido.ForzarIdioma(nuevoIdioma);
            return;
        }

        origen.Remover(num);
        var textoCifras = Cifras.EnTexto(texto, nuevoIdioma);
        destino.Mostrar(new Vista(num, nuevoIdioma, textoCifras, ""));
    }

    string Resumen()
    {
        var m = recorrido?.Medir();
        string S(double? x) => x is double v ? $"{v:0.00} s" : "—";
        return $"""
            Birdlang: métricas de la sesión (sin texto de la llamada)
            Equipo: {Medidor.Equipo()}
            Motor: Moonshine {modeloCargado ?? "—"}
            Tiempo escuchando: {TimeSpan.FromSeconds(m?.Escuchado ?? 0):h\:mm\:ss}
            Frases: inglés {m?.FrasesEn ?? 0}, español {m?.FrasesEs ?? 0}
            Procesador: promedio {medidor.CpuPromedio:0} %, peor segundo {medidor.CpuPeor:0} %
            RAM: máxima {medidor.RamMax / (1024 * 1024)} MB
            Demora (mediana · 9 de 10 · peor): {S(m?.Demora)} · {S(m?.Demora90)} · {S(m?.DemoraPeor)}
            Atraso máximo: {S(m?.AtrasoMax)}
            Audio perdido: {m?.AudioPerdido ?? 0:0.00} s ({m?.BloquesPerdidos ?? 0} veces)
            Pasada más lenta del motor: {m?.PasadaMasLentaMs ?? 0:0} ms
            """;
    }

    void Letra(double cambio)
    {
        ingles.Tamano = espanol.Tamano = Math.Clamp(ingles.Tamano + cambio, 12, 48);
        notas.FontSize = Math.Clamp(notas.FontSize + cambio, 10, 40);
        busquedaCalles.FontSize = Math.Clamp(busquedaCalles.FontSize + cambio, 10, 30);
        busquedaGlosario.FontSize = Math.Clamp(busquedaGlosario.FontSize + cambio, 10, 30);
    }

    static StackPanel Fila(params Control[] controles)
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        fila.Children.AddRange(controles);
        return fila;
    }

    GridSplitter Separador(AvaloniaProperty posicion, int indice)
    {
        var s = new GridSplitter { Background = tema.BrushBorde };
        s.SetValue(posicion, indice);
        splitters.Add(s);
        return s;
    }
}
