namespace PdfConverterToolkit.App;

internal static class Program
{
    /// <summary>Ponto de entrada da aplicacao.</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
