using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Privacidad.SinVolcados();
        AppBuilder.Configure<App>().UseWin32().UseSkia().UseHarfBuzz().StartWithClassicDesktopLifetime(args);
    }
}

sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime escritorio) escritorio.MainWindow = new Ventana();
        base.OnFrameworkInitializationCompleted();
    }
}
