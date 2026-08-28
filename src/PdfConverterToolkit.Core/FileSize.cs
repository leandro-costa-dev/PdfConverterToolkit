namespace PdfConverterToolkit.Core;

/// <summary>Formatacao de tamanho de arquivo para as mensagens de resultado.</summary>
public static class FileSize
{
    /// <summary>Tamanho em KB com separador de milhar (ex.: "1.024 KB").</summary>
    public static string Kb(long bytes) => $"{bytes / 1024:N0} KB";

    /// <summary>Tamanho do arquivo em KB; "?" se o arquivo nao existir mais.</summary>
    public static string OfFile(string path)
    {
        try
        {
            return Kb(new FileInfo(path).Length);
        }
        catch (Exception)
        {
            return "? KB";
        }
    }
}
