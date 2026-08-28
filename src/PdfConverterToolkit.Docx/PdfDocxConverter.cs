using PdfConverterToolkit.Core;
using PdfConverterToolkit.Docx.Analysis;
using PdfConverterToolkit.Docx.Model;
using PdfConverterToolkit.Docx.Writer;
using UglyToad.PdfPig;

namespace PdfConverterToolkit.Docx;

/// <summary>
/// Converte um PDF em um documento do Word (.docx) preservando a estrutura:
/// texto editavel com a formatacao original, tabelas de verdade, imagens e o
/// tamanho/orientacao de cada pagina.
/// </summary>
public static class PdfDocxConverter
{
    /// <summary>Converte um arquivo PDF em .docx.</summary>
    /// <param name="pdfPath">Caminho do PDF de entrada.</param>
    /// <param name="docxPath">Caminho do .docx a ser gravado (sobrescreve se existir).</param>
    /// <param name="options">Ajustes da conversao; null usa os padroes.</param>
    /// <param name="progress">Recebe o andamento pagina a pagina.</param>
    /// <param name="cancellationToken">Permite cancelar entre paginas.</param>
    public static ConversionResult Convert(
        string pdfPath,
        string docxPath,
        ConversionOptions? options = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        byte[] bytes = File.ReadAllBytes(pdfPath);
        return Convert(bytes, docxPath, options, progress, cancellationToken);
    }

    /// <summary>Converte um PDF ja carregado em memoria.</summary>
    public static ConversionResult Convert(
        byte[] pdfBytes,
        string docxPath,
        ConversionOptions? options = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);

        options ??= new ConversionOptions();
        var result = new ConversionResult { OutputPath = docxPath };

        var parsing = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
            ClipPaths = false,
        };

        if (!string.IsNullOrEmpty(options.Password))
        {
            parsing.Password = options.Password;
        }

        var layouts = new List<PageLayout>();

        using (var pdf = PdfDocument.Open(pdfBytes, parsing))
        {
            var analyzer = new PageAnalyzer(options);
            int total = pdf.NumberOfPages;
            result.PageCount = total;

            for (int number = 1; number <= total; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ConversionProgress(number, total, "Analisando"));

                try
                {
                    var layout = analyzer.Analyze(pdf.GetPage(number), pdfBytes);
                    layouts.Add(layout);

                    if (layout.IsRasterFallback)
                    {
                        result.RasterizedPageCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"Pagina {number}: {ex.Message}");
                }
            }
        }

        if (options.RemoveRepeatedHeadersFooters)
        {
            RepeatedBlockFilter.Remove(layouts);
        }

        result.TextCharacterCount = CountCharacters(layouts);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(docxPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var builder = new DocxBuilder(docxPath, options.ReflowText))
        {
            for (int i = 0; i < layouts.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ConversionProgress(i + 1, layouts.Count, "Gravando"));
                builder.AddPage(layouts[i], isLast: i == layouts.Count - 1);
            }

            builder.Save();
            result.ParagraphCount = builder.ParagraphCount;
            result.TableCount = builder.TableCount;
            result.ImageCount = builder.ImageCount;
        }

        if (result.TextCharacterCount == 0 && result.RasterizedPageCount == 0)
        {
            result.Warnings.Add(
                "O PDF nao tem camada de texto. Use um OCR antes de converter, ou ligue a rasterizacao das paginas.");
        }

        return result;
    }

    private static int CountCharacters(List<PageLayout> layouts)
    {
        int count = 0;
        foreach (var layout in layouts)
        {
            foreach (var block in layout.Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph:
                        count += paragraph.Text.Count(c => !char.IsWhiteSpace(c));
                        break;

                    case TableBlock table:
                        foreach (var cell in DistinctCells(table))
                        {
                            foreach (var paragraph in cell.Content)
                            {
                                count += paragraph.Text.Count(c => !char.IsWhiteSpace(c));
                            }
                        }

                        break;
                }
            }
        }

        return count;
    }

    internal static IEnumerable<TableCell> DistinctCells(TableBlock table)
    {
        var seen = new HashSet<TableCell>();
        for (int r = 0; r < table.RowCount; r++)
        {
            for (int c = 0; c < table.ColumnCount; c++)
            {
                var cell = table.Cells[r, c];
                if (cell is not null && seen.Add(cell))
                {
                    yield return cell;
                }
            }
        }
    }
}
