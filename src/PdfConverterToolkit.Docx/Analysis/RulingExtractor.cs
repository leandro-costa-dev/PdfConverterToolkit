using PdfConverterToolkit.Docx.Model;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>Segmento horizontal (uma divisoria de linha da tabela), em coordenadas de tela.</summary>
internal readonly record struct HLine(double Y, double X1, double X2)
{
    public double Length => X2 - X1;
}

/// <summary>Segmento vertical (uma divisoria de coluna da tabela), em coordenadas de tela.</summary>
internal readonly record struct VLine(double X, double Y1, double Y2)
{
    public double Length => Y2 - Y1;
}

/// <summary>Retangulo preenchido, candidato a cor de fundo de celula.</summary>
internal sealed record FilledArea(Rect Bounds, string Hex);

/// <summary>Todos os tracos vetoriais uteis de uma pagina.</summary>
internal sealed class Rulings
{
    public List<HLine> Horizontal { get; } = [];

    public List<VLine> Vertical { get; } = [];

    public List<FilledArea> Fills { get; } = [];
}

/// <summary>
/// Le os caminhos vetoriais da pagina e separa o que serve para remontar tabelas:
/// linhas horizontais, linhas verticais e areas preenchidas (fundo das celulas).
/// Linhas desenhadas como retangulos muito finos — pratica comum — tambem sao reconhecidas.
/// </summary>
internal static class RulingExtractor
{
    /// <summary>Espessura maxima, em pontos, para um retangulo ser considerado um traco.</summary>
    private const double MaxRuleThickness = 3.5;

    /// <summary>Comprimento minimo, em pontos, para o traco valer como divisoria.</summary>
    private const double MinRuleLength = 5.0;

    /// <summary>Desvio maximo, em pontos, para um segmento ainda contar como reto.</summary>
    private const double StraightTolerance = 1.2;

    public static Rulings Extract(Page page, PageSpace space)
    {
        var result = new Rulings();

        IReadOnlyList<PdfPath> paths;
        try
        {
            paths = page.Paths;
        }
        catch (Exception)
        {
            // Alguns PDFs tem fluxos de conteudo defeituosos; sem vetores seguimos so com texto.
            return result;
        }

        foreach (var path in paths)
        {
            if (path.IsClipping)
            {
                continue;
            }

            foreach (var subpath in path)
            {
                if (path.IsStroked)
                {
                    AddStrokedSegments(result, subpath, space);
                }

                if (path.IsFilled)
                {
                    AddFilledShape(result, subpath, space, path.FillColor);
                }
            }
        }

        Merge(result);
        return result;
    }

    private static void AddStrokedSegments(Rulings result, PdfSubpath subpath, PageSpace space)
    {
        foreach (var command in subpath.Commands)
        {
            if (command is not PdfSubpath.Line line)
            {
                continue;
            }

            var (x1, y1) = space.ToScreen(line.From.X, line.From.Y);
            var (x2, y2) = space.ToScreen(line.To.X, line.To.Y);
            AddSegment(result, x1, y1, x2, y2);
        }
    }

    private static void AddSegment(Rulings result, double x1, double y1, double x2, double y2)
    {
        double dx = Math.Abs(x2 - x1);
        double dy = Math.Abs(y2 - y1);

        if (dy <= StraightTolerance && dx >= MinRuleLength)
        {
            result.Horizontal.Add(new HLine((y1 + y2) / 2, Math.Min(x1, x2), Math.Max(x1, x2)));
        }
        else if (dx <= StraightTolerance && dy >= MinRuleLength)
        {
            result.Vertical.Add(new VLine((x1 + x2) / 2, Math.Min(y1, y2), Math.Max(y1, y2)));
        }
    }

    private static void AddFilledShape(Rulings result, PdfSubpath subpath, PageSpace space, IColor? color)
    {
        PdfRectangle? box = subpath.GetBoundingRectangle();
        if (box is null)
        {
            return;
        }

        Rect r = space.ToScreen(box.Value);

        // Retangulo fino = traco desenhado como preenchimento.
        if (r.Height <= MaxRuleThickness && r.Width >= MinRuleLength)
        {
            result.Horizontal.Add(new HLine(r.CenterY, r.Left, r.Right));
            return;
        }

        if (r.Width <= MaxRuleThickness && r.Height >= MinRuleLength)
        {
            result.Vertical.Add(new VLine(r.CenterX, r.Top, r.Bottom));
            return;
        }

        string hex = ColorHex.From(color);
        if (hex is not "FFFFFF" && r.Width > 4 && r.Height > 4)
        {
            result.Fills.Add(new FilledArea(r, hex));
        }
    }

    /// <summary>Funde tracos colineares e sobrepostos, que o PDF costuma emitir em pedacos.</summary>
    private static void Merge(Rulings rulings)
    {
        rulings.Horizontal.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X1.CompareTo(b.X1));
        var horizontal = new List<HLine>();
        foreach (var h in rulings.Horizontal)
        {
            if (horizontal.Count > 0)
            {
                var last = horizontal[^1];
                if (Math.Abs(last.Y - h.Y) <= StraightTolerance && h.X1 <= last.X2 + 2.0)
                {
                    horizontal[^1] = new HLine((last.Y + h.Y) / 2, last.X1, Math.Max(last.X2, h.X2));
                    continue;
                }
            }

            horizontal.Add(h);
        }

        rulings.Vertical.Sort(static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y1.CompareTo(b.Y1));
        var vertical = new List<VLine>();
        foreach (var v in rulings.Vertical)
        {
            if (vertical.Count > 0)
            {
                var last = vertical[^1];
                if (Math.Abs(last.X - v.X) <= StraightTolerance && v.Y1 <= last.Y2 + 2.0)
                {
                    vertical[^1] = new VLine((last.X + v.X) / 2, last.Y1, Math.Max(last.Y2, v.Y2));
                    continue;
                }
            }

            vertical.Add(v);
        }

        rulings.Horizontal.Clear();
        rulings.Horizontal.AddRange(horizontal);
        rulings.Vertical.Clear();
        rulings.Vertical.AddRange(vertical);
    }
}

/// <summary>Converte cores do PdfPig para o formato RRGGBB usado no Office Open XML.</summary>
internal static class ColorHex
{
    public static string From(IColor? color)
    {
        if (color is null)
        {
            return "000000";
        }

        try
        {
            var (r, g, b) = color.ToRGBValues();
            return $"{Channel(r):X2}{Channel(g):X2}{Channel(b):X2}";
        }
        catch (Exception)
        {
            return "000000";
        }
    }

    private static int Channel(double value) => Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
