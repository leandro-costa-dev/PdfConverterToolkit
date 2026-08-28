using PdfConverterToolkit.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfConverterToolkit.Imaging;

/// <summary>
/// Reduz o tamanho de um PDF rasterizando cada pagina (PDFium) e remontando um PDF novo
/// (PDFsharp) so com essas imagens.
///
/// Como o texto vira imagem, o resultado perde selecao e busca: compensa em PDFs
/// digitalizados ou cheios de imagens, e tende a aumentar o arquivo em PDFs so de texto —
/// por isso existe <see cref="CompressionOptions.SkipIfLarger"/>. Assinaturas e anotacoes
/// continuam visiveis, porque entram desenhadas na imagem.
/// </summary>
public static class PdfCompressor
{
    /// <summary>O que aconteceu com um arquivo.</summary>
    /// <param name="OriginalBytes">Tamanho do PDF de entrada.</param>
    /// <param name="CompressedBytes">Tamanho do PDF gerado.</param>
    /// <param name="Skipped">True quando o resultado foi descartado por nao reduzir nada.</param>
    public readonly record struct CompressionResult(long OriginalBytes, long CompressedBytes, bool Skipped)
    {
        /// <summary>Reducao alcancada, em porcentagem do tamanho original.</summary>
        public double ReductionPercent =>
            OriginalBytes <= 0 ? 0 : 100.0 * (1 - (double)CompressedBytes / OriginalBytes);
    }

    /// <summary>
    /// Compacta um PDF. Se o resultado nao reduzir e <see cref="CompressionOptions.SkipIfLarger"/>
    /// estiver ligado, o arquivo gerado e apagado e o retorno vem com <c>Skipped</c>.
    /// </summary>
    /// <param name="onPageDone">Chamado com o numero da pagina antes de rasteriza-la.</param>
    public static CompressionResult Compress(
        string pdfPath,
        string outputPath,
        CompressionOptions options,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        byte[] bytes = File.ReadAllBytes(pdfPath);
        int pages = PdfPageCounter.Count(bytes, options.Password);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            Rebuild(bytes, pages, outputPath, options, onPageDone, cancellationToken);
        }
        catch (Exception)
        {
            Delete(outputPath);
            throw;
        }

        long before = new FileInfo(pdfPath).Length;
        long after = new FileInfo(outputPath).Length;

        if (options.SkipIfLarger && after >= before)
        {
            Delete(outputPath);
            return new CompressionResult(before, after, Skipped: true);
        }

        return new CompressionResult(before, after, Skipped: false);
    }

    /// <summary>
    /// Compacta varios PDFs para a mesma pasta (ou ao lado de cada um, se a pasta vier
    /// vazia), gerando <c>nome_compactado.pdf</c>.
    /// </summary>
    /// <returns>Quantos PDFs compactados foram efetivamente gravados.</returns>
    public static int CompressBatch(
        IReadOnlyList<string> pdfPaths,
        string? outputDirectory,
        CompressionOptions options,
        BatchReport report,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfPaths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);

        int[] pageCounts = PdfPageCounter.CountAll(pdfPaths, report, out int totalPages, options.Password);
        int totalSteps = Math.Max(1, totalPages);
        int done = 0;

        for (int i = 0; i < pdfPaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string pdfPath = pdfPaths[i];
            string baseName = Path.GetFileNameWithoutExtension(pdfPath);
            int pages = pageCounts[i];
            if (pages <= 0)
            {
                continue;
            }

            string folder = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(pdfPath))!
                : outputDirectory;
            string outputPath = OutputPath.EnsureUnique(Path.Combine(folder, $"{baseName}_compactado.pdf"));

            int fileBase = done;

            try
            {
                var result = Compress(
                    pdfPath,
                    outputPath,
                    options,
                    page => progress?.Report(new BatchProgress(
                        fileBase + page - 1, totalSteps, $"Compactando {baseName} — pagina {page}/{pages}…")),
                    cancellationToken);

                if (result.Skipped)
                {
                    report.AddSummary(
                        $"{baseName}: pulado — resultado ({FileSize.Kb(result.CompressedBytes)}) " +
                        $"≥ original ({FileSize.Kb(result.OriginalBytes)}).");
                }
                else
                {
                    report.Produced++;
                    report.AddSummary(
                        $"{baseName}: {FileSize.Kb(result.OriginalBytes)} → {FileSize.Kb(result.CompressedBytes)} " +
                        $"({result.ReductionPercent:0.0}% de reducao).");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.AddError(baseName, ex);
            }

            done += pages;
        }

        progress?.Report(new BatchProgress(totalSteps, totalSteps, "Finalizando…"));
        return report.Produced;
    }

    /// <summary>Monta o PDF novo: uma pagina por imagem, do tamanho da pagina original.</summary>
    private static void Rebuild(
        byte[] pdfBytes,
        int pages,
        string outputPath,
        CompressionOptions options,
        Action<int>? onPageDone,
        CancellationToken cancellationToken)
    {
        using var document = new PdfDocument();

        // O PDFsharp le os streams das imagens so na hora de gravar: eles precisam ficar
        // vivos ate o Save().
        var pending = new List<IDisposable>();
        try
        {
            for (int page = 0; page < pages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onPageDone?.Invoke(page + 1);

                var raster = PdfRasterizer.RenderJpeg(pdfBytes, page, options.Dpi, options.Quality, options.Password);

                PdfPage pdfPage = document.AddPage();
                pdfPage.Width = XUnit.FromPoint(raster.Width / (double)options.Dpi * 72.0);
                pdfPage.Height = XUnit.FromPoint(raster.Height / (double)options.Dpi * 72.0);

                // O PDFsharp le o stream via GetBuffer(): precisa de MemoryStream com buffer exposto.
                var ms = new MemoryStream();
                ms.Write(raster.Data, 0, raster.Data.Length);
                ms.Position = 0;
                XImage image = XImage.FromStream(ms);
                pending.Add(ms);
                pending.Add(image);

                using XGraphics gfx = XGraphics.FromPdfPage(pdfPage);
                gfx.DrawImage(image, 0, 0, pdfPage.Width.Point, pdfPage.Height.Point);
            }

            document.Save(outputPath);
        }
        finally
        {
            foreach (IDisposable item in pending)
            {
                item.Dispose();
            }
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // O arquivo pode estar aberto em outro programa: nao ha o que fazer aqui.
        }
    }
}
