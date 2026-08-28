using PDFtoImage;

namespace PdfConverterToolkit.Core;

/// <summary>Contagem de paginas — usada para dimensionar a barra de progresso do lote.</summary>
public static class PdfPageCounter
{
    /// <summary>Numero de paginas do PDF em memoria; 0 se nao for possivel ler.</summary>
    public static int Count(byte[] pdfBytes, string? password = null)
    {
        try
        {
            return Conversion.GetPageCount(pdfBytes, password);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Conta as paginas de cada PDF da lista. Arquivos ilegiveis entram com 0 pagina e o
    /// motivo vai para <paramref name="report"/>.
    /// </summary>
    /// <returns>Vetor paralelo a <paramref name="pdfPaths"/> com a contagem de cada um.</returns>
    public static int[] CountAll(
        IReadOnlyList<string> pdfPaths,
        BatchReport report,
        out int totalPages,
        string? password = null)
    {
        var counts = new int[pdfPaths.Count];
        totalPages = 0;

        for (int i = 0; i < pdfPaths.Count; i++)
        {
            try
            {
                counts[i] = Conversion.GetPageCount(File.ReadAllBytes(pdfPaths[i]), password);
                totalPages += counts[i];
            }
            catch (Exception ex)
            {
                report.AddError(Path.GetFileName(pdfPaths[i]), ex);
                counts[i] = 0;
            }
        }

        return counts;
    }
}
