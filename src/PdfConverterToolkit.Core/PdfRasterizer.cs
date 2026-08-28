using PDFtoImage;
using SkiaSharp;

namespace PdfConverterToolkit.Core;

/// <summary>
/// Desenha paginas de PDF com o PDFium (via PDFtoImage) e codifica o resultado. E o unico
/// ponto do projeto que conversa com o rasterizador: a exportacao de imagens, a compactacao
/// e os modos "pagina como imagem" do Word passam todos por aqui.
/// </summary>
public static class PdfRasterizer
{
    /// <summary>Uma pagina rasterizada: bytes ja codificados e o tamanho em pixels.</summary>
    public readonly record struct RasterPage(byte[] Data, int Width, int Height);

    /// <summary>Renderiza dimensionando pelo DPI.</summary>
    /// <param name="withAnnotations">
    /// Inclui anotacoes e campos de formulario — assinaturas digitais, carimbos gov.br,
    /// tinta — que o PDFium ignora por padrao.
    /// </param>
    public static RenderOptions ByDpi(int dpi, bool withAnnotations = true)
        => new(
            Dpi: dpi,
            WithAnnotations: withAnnotations,
            WithFormFill: withAnnotations,
            BackgroundColor: SKColors.White);

    /// <summary>
    /// Renderiza num tamanho em pixels. Largura e altura em 0 caem no dimensionamento por
    /// DPI; com <paramref name="keepAspectRatio"/> basta informar uma das duas.
    /// </summary>
    public static RenderOptions BySize(int width, int height, bool keepAspectRatio, int dpi, bool withAnnotations = true)
    {
        int? w = width > 0 ? width : null;
        int? h = height > 0 ? height : null;

        if (w is null && h is null)
        {
            return ByDpi(dpi, withAnnotations);
        }

        return new RenderOptions(
            Width: w,
            Height: h,
            WithAspectRatio: keepAspectRatio,
            WithAnnotations: withAnnotations,
            WithFormFill: withAnnotations,
            BackgroundColor: SKColors.White);
    }

    /// <summary>
    /// Renderiza a pagina (indice iniciando em 0). O chamador e dono do bitmap devolvido.
    /// </summary>
    public static SKBitmap Render(byte[] pdfBytes, int pageIndex, RenderOptions options, string? password = null)
        => Conversion.ToImage(pdfBytes, page: (Index)pageIndex, password: password, options: options);

    /// <summary>Como <see cref="Render"/>, mas devolve null em vez de lancar.</summary>
    public static SKBitmap? TryRender(byte[] pdfBytes, int pageIndex, RenderOptions options, string? password = null)
    {
        try
        {
            return Render(pdfBytes, pageIndex, options, password);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Renderiza a pagina no DPI pedido e devolve o JPEG correspondente.</summary>
    public static RasterPage RenderJpeg(byte[] pdfBytes, int pageIndex, int dpi, int quality, string? password = null)
    {
        using SKBitmap bitmap = Render(pdfBytes, pageIndex, ByDpi(dpi), password);
        return new RasterPage(EncodeJpeg(bitmap, quality), bitmap.Width, bitmap.Height);
    }

    /// <summary>Codifica em JPEG; a qualidade e limitada a 1..100.</summary>
    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
        => Encode(bitmap, SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));

    /// <summary>Codifica em PNG (sem perdas).</summary>
    public static byte[] EncodePng(SKBitmap bitmap)
        => Encode(bitmap, SKEncodedImageFormat.Png, 100);

    /// <summary>Como <see cref="EncodeJpeg"/>, mas devolve null em vez de lancar.</summary>
    public static byte[]? TryEncodeJpeg(SKBitmap bitmap, int quality)
    {
        try
        {
            return EncodeJpeg(bitmap, quality);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Recorta a regiao (em pixels) do bitmap de uma pagina e devolve um PNG. Null quando a
    /// regiao cai fora da pagina ou o recorte falha.
    /// </summary>
    public static byte[]? TryCropPng(SKBitmap page, int left, int top, int right, int bottom)
    {
        var rect = SKRectI.Intersect(
            new SKRectI(left, top, right, bottom),
            new SKRectI(0, 0, page.Width, page.Height));

        if (rect.Width < 1 || rect.Height < 1)
        {
            return null;
        }

        try
        {
            using var subset = new SKBitmap(rect.Width, rect.Height);
            return page.ExtractSubset(subset, rect) ? EncodePng(subset) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, quality);
        return data.ToArray();
    }
}
