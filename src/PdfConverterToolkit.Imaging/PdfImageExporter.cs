using System.Drawing;
using PdfConverterToolkit.Core;
using SkiaSharp;

namespace PdfConverterToolkit.Imaging;

/// <summary>
/// Exporta as paginas de um PDF como arquivos de imagem, um por pagina
/// (<c>nome_p001.jpg</c>, <c>nome_p002.jpg</c>…).
/// </summary>
public static class PdfImageExporter
{
    /// <summary>
    /// Exporta as paginas de um PDF ja em memoria. Um erro numa pagina nao interrompe as
    /// demais: vai para o relatorio e a exportacao continua.
    /// </summary>
    /// <param name="baseName">Nome do PDF sem extensao, usado no nome das imagens.</param>
    /// <param name="onPageDone">Chamado com o numero da pagina antes de converte-la.</param>
    /// <returns>Os caminhos das imagens gravadas.</returns>
    public static List<string> Export(
        byte[] pdfBytes,
        string baseName,
        string outputDirectory,
        ImageExportOptions options,
        BatchReport report,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfBytes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);

        var descriptor = ImageFormats.Of(options.Format);
        var render = PdfRasterizer.BySize(
            options.Width, options.Height, options.KeepAspectRatio, options.Dpi, options.IncludeAnnotations);

        int pages = PdfPageCounter.Count(pdfBytes, options.Password);
        var written = new List<string>(pages);

        Directory.CreateDirectory(outputDirectory);

        for (int page = 0; page < pages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onPageDone?.Invoke(page + 1);

            try
            {
                using SKBitmap bitmap = PdfRasterizer.Render(pdfBytes, page, render, options.Password);

                string path = OutputPath.EnsureUnique(
                    Path.Combine(outputDirectory, $"{baseName}_p{page + 1:D3}{descriptor.Extension}"));

                Save(bitmap, path, descriptor, options.Quality);
                written.Add(path);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                report.AddError($"{baseName} pag. {page + 1}: {ex.Message}");
            }
        }

        return written;
    }

    /// <summary>
    /// Exporta varios PDFs para a mesma pasta, reportando o andamento em paginas.
    /// </summary>
    /// <returns>Quantas imagens foram geradas no total.</returns>
    public static int ExportBatch(
        IReadOnlyList<string> pdfPaths,
        string outputDirectory,
        ImageExportOptions options,
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
        int generated = 0;

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

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(pdfPath);
            }
            catch (Exception ex)
            {
                report.AddError(Path.GetFileName(pdfPath), ex);
                done += pages;
                continue;
            }

            int fileBase = done;
            var images = Export(
                bytes,
                baseName,
                outputDirectory,
                options,
                report,
                page => progress?.Report(new BatchProgress(
                    fileBase + page - 1, totalSteps, $"Convertendo {baseName} — pagina {page}/{pages}…")),
                cancellationToken);

            if (images.Count > 0)
            {
                report.Produced++;
                report.AddSummary($"{baseName}: {images.Count} imagem(ns) em {ImageFormats.Of(options.Format).Extension}.");
            }

            generated += images.Count;
            done += pages;
        }

        progress?.Report(new BatchProgress(totalSteps, totalSteps, "Finalizando…"));
        return generated;
    }

    /// <summary>
    /// Grava o bitmap no formato pedido. JPEG e PNG saem codificados pelo proprio Skia;
    /// BMP, GIF e TIFF passam pelo GDI+, que e quem sabe grava-los (o GIF e paletizado em
    /// 256 cores automaticamente).
    /// </summary>
    private static void Save(SKBitmap bitmap, string path, ImageFormats.Descriptor descriptor, int quality)
    {
        if (descriptor.Gdi is null)
        {
            byte[] data = descriptor.Format == ImageOutputFormat.Jpeg
                ? PdfRasterizer.EncodeJpeg(bitmap, quality)
                : PdfRasterizer.EncodePng(bitmap);

            File.WriteAllBytes(path, data);
            return;
        }

        using Bitmap gdi = ToGdi(bitmap);
        gdi.Save(path, descriptor.Gdi);
    }

    /// <summary>Converte um SKBitmap (SkiaSharp) para System.Drawing.Bitmap independente.</summary>
    private static Bitmap ToGdi(SKBitmap bitmap)
    {
        using var ms = new MemoryStream(PdfRasterizer.EncodePng(bitmap));
        using var decoded = new Bitmap(ms);
        return new Bitmap(decoded);
    }
}
