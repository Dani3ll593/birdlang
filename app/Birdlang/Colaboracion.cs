// El aviso «Colabora con el proyecto»: pide a los voluntarios el bloque de
// «📋 Métricas» de cualquier sesión (solo datos técnicos de rendimiento, sin
// texto de la llamada). Solo en la versión Estándar (la que trae el
// actualizador). Birdlang no abre el formulario: copia el enlace y el
// intérprete lo pega en su navegador.
//
// Lo único que se escribe en disco es «No volver a mostrar», en
// preferencias.txt junto al programa (README §4): al borrar la carpeta no
// queda rastro, y la versión para empresas no escribe nada.
static class Colaboracion
{
    public const string Formulario = "https://forms.gle/omQEu6ceUL2ZWpoX6";

    const string NoMostrar = "colaborar=no";
    static string Archivo => Nativos.Ruta("preferencias.txt");

    // Uno de cada 4 inicios, al azar: así no hace falta guardar un contador.
    public static bool Mostrar() => Actualizacion.Disponible && Random.Shared.Next(4) == 0 && !Apagado();

    static bool Apagado()
    {
        try { return File.Exists(Archivo) && File.ReadAllLines(Archivo).Contains(NoMostrar); }
        catch { return false; }
    }

    // Si la carpeta es de solo lectura, el aviso se oculta igual en esta sesión.
    public static void NoVolverAMostrar()
    {
        try { File.WriteAllText(Archivo, NoMostrar + Environment.NewLine); }
        catch { }
    }
}
