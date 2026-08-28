using PdfConverterToolkit.Core;
using PdfConverterToolkit.Docx.Model;
using SkiaSharp;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Traduz o rasterizador compartilhado (<see cref="PdfRasterizer"/>) para os blocos do
/// modelo de layout. Serve para dois casos: paginas sem camada de texto (digitalizadas) e
/// imagens embutidas que o leitor de PDF nao consegue decodificar — nesses casos
/// recortamos a regiao da pagina ja renderizada.
/// </summary>
internal static class PageRasterizer
{
    /// <summary>A pagina inteira como um unico bloco de imagem JPEG.</summary>
    public static ImageBlock? Render(byte[] pdfBytes, int pageIndex, Rect pageBounds, int dpi, int quality, string? password)
    {
        using SKBitmap? bitmap = RenderPage(pdfBytes, pageIndex, dpi, password);
        if (bitmap is null)
        {
            return null;
        }

        byte[]? jpeg = PdfRasterizer.TryEncodeJpeg(bitmap, quality);
        return jpeg is null ? null : new ImageBlock
        {
            Bounds = pageBounds,
            Data = jpeg,
            Format = ImageFormat.Jpeg,
        };
    }

    /// <summary>Renderiza a pagina inteira; o chamador e dono do bitmap.</summary>
    public static SKBitmap? RenderPage(byte[] pdfBytes, int pageIndex, int dpi, string? password)
        => PdfRasterizer.TryRender(pdfBytes, pageIndex, PdfRasterizer.ByDpi(dpi), password);

    /// <summary>Recorta uma regiao (em pontos) do bitmap da pagina e devolve um PNG.</summary>
    public static ImageBlock? Crop(SKBitmap page, Rect region, double pointsToPixels)
    {
        byte[]? png = PdfRasterizer.TryCropPng(
            page,
            (int)Math.Floor(region.Left * pointsToPixels),
            (int)Math.Floor(region.Top * pointsToPixels),
            (int)Math.Ceiling(region.Right * pointsToPixels),
            (int)Math.Ceiling(region.Bottom * pointsToPixels));

        return png is null ? null : new ImageBlock
        {
            Bounds = region,
            Data = png,
            Format = ImageFormat.Png,
        };
    }
}
