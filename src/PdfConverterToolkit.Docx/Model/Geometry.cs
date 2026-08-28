using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace PdfConverterToolkit.Docx.Model;

/// <summary>
/// Retangulo em coordenadas de tela (origem no canto superior esquerdo, Y crescendo
/// para baixo), medido em pontos tipograficos (1 pt = 1/72 pol).
/// O PDF usa origem no canto inferior esquerdo; a conversao e feita por <see cref="PageSpace"/>.
/// </summary>
public readonly record struct Rect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public double CenterX => (Left + Right) / 2.0;

    public double CenterY => (Top + Bottom) / 2.0;

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    /// <summary>Fracao da area deste retangulo que esta dentro de <paramref name="other"/>.</summary>
    public double OverlapRatio(Rect other)
    {
        double w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        double h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        if (w <= 0 || h <= 0)
        {
            return 0;
        }

        double area = Width * Height;
        return area <= 0 ? 0 : (w * h) / area;
    }

    public Rect Union(Rect other) => new(
        Math.Min(Left, other.Left),
        Math.Min(Top, other.Top),
        Math.Max(Right, other.Right),
        Math.Max(Bottom, other.Bottom));

    public static Rect Union(IEnumerable<Rect> rects)
    {
        Rect? acc = null;
        foreach (var r in rects)
        {
            acc = acc is null ? r : acc.Value.Union(r);
        }

        return acc ?? new Rect(0, 0, 0, 0);
    }
}

/// <summary>
/// Converte coordenadas do PDF (Y para cima, origem inferior esquerda) para coordenadas
/// de tela (Y para baixo), aplicando tambem a rotacao declarada na pagina (/Rotate).
/// </summary>
public sealed class PageSpace
{
    private readonly double pdfWidth;
    private readonly double pdfHeight;
    private readonly int rotation;
    private readonly double offsetX;
    private readonly double offsetY;

    public PageSpace(Page page)
    {
        // A CropBox e a area realmente visivel; quando ausente, cai na MediaBox.
        PdfRectangle box = page.CropBox.Bounds;
        if (box.Width <= 0 || box.Height <= 0)
        {
            box = page.MediaBox.Bounds;
        }

        offsetX = box.Left;
        offsetY = box.Bottom;
        pdfWidth = box.Width;
        pdfHeight = box.Height;

        rotation = ((page.Rotation.Value % 360) + 360) % 360;
        rotation -= rotation % 90;

        bool swapped = rotation is 90 or 270;
        Width = swapped ? pdfHeight : pdfWidth;
        Height = swapped ? pdfWidth : pdfHeight;
    }

    /// <summary>Largura da pagina ja rotacionada, em pontos.</summary>
    public double Width { get; }

    /// <summary>Altura da pagina ja rotacionada, em pontos.</summary>
    public double Height { get; }

    /// <summary>true quando a pagina e mais larga do que alta.</summary>
    public bool IsLandscape => Width > Height;

    public (double X, double Y) ToScreen(double pdfX, double pdfY)
    {
        double x = pdfX - offsetX;
        double y = pdfY - offsetY;

        return rotation switch
        {
            90 => (y, x),
            180 => (pdfWidth - x, y),
            270 => (pdfHeight - y, pdfWidth - x),
            _ => (x, pdfHeight - y),
        };
    }

    public Rect ToScreen(PdfRectangle rect)
    {
        // Depois da rotacao os cantos podem trocar de papel, entao normalizamos.
        var (x1, y1) = ToScreen(rect.Left, rect.Bottom);
        var (x2, y2) = ToScreen(rect.Right, rect.Top);
        return new Rect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }
}
