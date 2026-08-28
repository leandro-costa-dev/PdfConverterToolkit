using PdfConverterToolkit.Docx.Model;
using UglyToad.PdfPig.Content;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Extrai as imagens embutidas na pagina, ja convertidas para PNG/JPEG e posicionadas
/// em coordenadas de tela.
/// </summary>
internal static class ImageExtractor
{
    /// <summary>Menor lado aceito, em pontos: abaixo disso costuma ser enfeite de fundo.</summary>
    private const double MinSizePt = 4.0;

    /// <summary>
    /// Le as imagens da pagina.
    /// </summary>
    /// <returns>
    /// As imagens decodificadas e, separadamente, as regioes das imagens que o leitor
    /// nao conseguiu decodificar — elas serao recortadas da pagina rasterizada.
    /// </returns>
    public static (List<ImageBlock> Images, List<Rect> Undecodable) Extract(Page page, PageSpace space, bool pageHasText)
    {
        var blocks = new List<ImageBlock>();
        var undecodable = new List<Rect>();

        IEnumerable<IPdfImage> images;
        try
        {
            images = page.GetImages();
        }
        catch (Exception)
        {
            return (blocks, undecodable);
        }

        double pageArea = Math.Max(space.Width * space.Height, 1);

        foreach (var image in images)
        {
            Rect bounds;
            try
            {
                bounds = space.ToScreen(image.BoundingBox);
            }
            catch (Exception)
            {
                continue;
            }

            if (bounds.Width < MinSizePt || bounds.Height < MinSizePt)
            {
                continue;
            }

            // Digitalizacao com camada de texto por cima: a imagem e so o fundo, o texto ja basta.
            if (pageHasText && (bounds.Width * bounds.Height) / pageArea > 0.8)
            {
                continue;
            }

            if (!TryGetBytes(image, out byte[] data, out ImageFormat format))
            {
                undecodable.Add(bounds);
                continue;
            }

            blocks.Add(new ImageBlock
            {
                Bounds = bounds,
                Data = data,
                Format = format,
            });
        }

        return (blocks, undecodable);
    }

    private static bool TryGetBytes(IPdfImage image, out byte[] data, out ImageFormat format)
    {
        data = [];
        format = ImageFormat.Png;

        try
        {
            // O JPEG original e reaproveitado sem recodificar quando existe.
            if (image.TryGetBytesAsMemory(out var raw) && IsJpeg(raw.Span))
            {
                data = raw.ToArray();
                format = ImageFormat.Jpeg;
                return true;
            }
        }
        catch (Exception)
        {
            // Segue para a conversao em PNG.
        }

        try
        {
            if (image.TryGetPng(out byte[]? png) && png is { Length: > 0 })
            {
                data = png;
                format = ImageFormat.Png;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }

        return false;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
