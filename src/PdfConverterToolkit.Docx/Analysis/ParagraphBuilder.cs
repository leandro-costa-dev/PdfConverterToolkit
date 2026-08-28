using System.Text.RegularExpressions;
using PdfConverterToolkit.Docx.Model;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Junta linhas soltas em paragrafos e deduz o que o PDF nao guarda: alinhamento,
/// recuos, espacamento e nivel de titulo.
/// </summary>
internal static partial class ParagraphBuilder
{
    [GeneratedRegex(@"^\s*(\d+(\.\d+)*[\.\)\-]?|[a-zA-Z][\.\)]|[ivxIVX]+[\.\)]|[•▪●◦‣·\-–—§])\s+")]
    private static partial Regex ListMarkerRegex();

    /// <summary>
    /// Monta os paragrafos de uma regiao.
    /// </summary>
    /// <param name="lines">Linhas ja ordenadas de cima para baixo.</param>
    /// <param name="region">Area util onde as linhas vivem (pagina ou celula).</param>
    /// <param name="bodyFontSize">Corpo de fonte predominante do documento, para achar titulos.</param>
    /// <param name="detectHeadings">Se deve marcar titulos.</param>
    public static List<ParagraphBlock> Build(
        IReadOnlyList<TextLineBox> lines,
        Rect region,
        double bodyFontSize,
        bool detectHeadings)
    {
        var paragraphs = new List<ParagraphBlock>();
        if (lines.Count == 0)
        {
            return paragraphs;
        }

        // A margem direita real do texto: usar a borda da regiao exagera em celulas estreitas.
        double textRight = lines.Max(l => l.Bounds.Right);
        double textLeft = lines.Min(l => l.Bounds.Left);

        ParagraphBlock? current = null;
        TextLineBox? previous = null;

        foreach (var line in lines)
        {
            if (line.IsEmpty)
            {
                continue;
            }

            if (current is null || previous is null || !BelongsToSame(previous, line, textRight))
            {
                current = new ParagraphBlock();
                paragraphs.Add(current);
            }

            current.Lines.Add(line);
            previous = line;
        }

        ParagraphBlock? previousParagraph = null;
        foreach (var paragraph in paragraphs)
        {
            Finish(paragraph, textLeft, textRight, bodyFontSize, detectHeadings);
            paragraph.SpaceBeforePt = previousParagraph is null
                ? Math.Max(0, paragraph.Bounds.Top - region.Top)
                : SpaceBetween(previousParagraph, paragraph);
            previousParagraph = paragraph;
        }

        return paragraphs;
    }

    /// <summary>
    /// Espaco a acrescentar antes de um paragrafo. Mede-se de linha de base a linha de
    /// base e desconta-se a entrelinha que o Word ja aplica — senao cada paragrafo novo
    /// ganharia uma folga que nao existe no PDF.
    /// </summary>
    public static double SpaceBetween(LayoutBlock previous, ParagraphBlock current)
    {
        if (previous is ParagraphBlock paragraph)
        {
            double leading = Math.Max(current.NaturalLeadingPt, paragraph.NaturalLeadingPt);
            return Math.Max(0, current.FirstBaseLine - paragraph.LastBaseLine - leading);
        }

        return Math.Max(0, current.Bounds.Top - previous.Bounds.Bottom);
    }

    /// <summary>Decide se a linha continua o paragrafo anterior ou comeca outro.</summary>
    private static bool BelongsToSame(TextLineBox previous, TextLineBox current, double textRight)
    {
        double size = Math.Max(previous.FontSize, current.FontSize);

        // Corpos de fonte muito diferentes indicam mudanca de bloco (titulo x texto).
        if (Math.Min(previous.FontSize, current.FontSize) < 0.75 * size)
        {
            return false;
        }

        // Espaco vertical maior que uma entrelinha normal fecha o paragrafo.
        double gap = current.Bounds.Top - previous.Bounds.Bottom;
        if (gap > 0.75 * size || gap < -0.8 * size)
        {
            return false;
        }

        // Linha anterior curta = fim de paragrafo (nao foi ate a margem direita).
        if (previous.Bounds.Right < textRight - 2.0 * size)
        {
            return false;
        }

        // Marcador de lista/numeracao sempre inicia um novo paragrafo.
        return !ListMarkerRegex().IsMatch(current.Text);
    }

    private static void Finish(
        ParagraphBlock paragraph,
        double textLeft,
        double textRight,
        double bodyFontSize,
        bool detectHeadings)
    {
        var lines = paragraph.Lines;
        paragraph.Bounds = Rect.Union(lines.Select(l => l.Bounds));

        double width = Math.Max(textRight - textLeft, 1.0);
        double left = paragraph.Bounds.Left;
        double right = paragraph.Bounds.Right;
        double leftGap = left - textLeft;
        double rightGap = textRight - right;
        double size = lines.Max(l => l.FontSize);

        if (lines.Count > 1)
        {
            double sum = 0;
            for (int i = 1; i < lines.Count; i++)
            {
                sum += lines[i].BaseLine - lines[i - 1].BaseLine;
            }

            paragraph.LineSpacingPt = sum / (lines.Count - 1);
        }

        // O recuo usa a menor esquerda entre as linhas seguintes; a primeira pode ter recuo proprio.
        double bodyLeft = lines.Count > 1 ? lines.Skip(1).Min(l => l.Bounds.Left) : left;
        paragraph.BodyLeftPt = bodyLeft;
        paragraph.FirstLineIndentPt = lines.Count > 1 ? lines[0].Bounds.Left - bodyLeft : 0;

        paragraph.Alignment = InferAlignment(paragraph, leftGap, rightGap, width, size, textRight);

        if (detectHeadings)
        {
            paragraph.HeadingLevel = InferHeadingLevel(paragraph, size, bodyFontSize);
        }
    }

    private static BlockAlignment InferAlignment(
        ParagraphBlock paragraph,
        double leftGap,
        double rightGap,
        double width,
        double size,
        double textRight)
    {
        double tolerance = Math.Max(0.02 * width, 0.5 * size);

        bool centered = leftGap > tolerance && rightGap > tolerance && Math.Abs(leftGap - rightGap) <= tolerance;
        if (centered)
        {
            return BlockAlignment.Center;
        }

        if (rightGap <= tolerance && leftGap > 4 * tolerance)
        {
            return BlockAlignment.Right;
        }

        // Justificado: todas as linhas, menos a ultima, encostam na margem direita.
        if (paragraph.Lines.Count > 1)
        {
            bool allFull = paragraph.Lines
                .Take(paragraph.Lines.Count - 1)
                .All(l => l.Bounds.Right >= textRight - 0.6 * size);
            if (allFull)
            {
                return BlockAlignment.Justify;
            }
        }

        return BlockAlignment.Left;
    }

    private static int InferHeadingLevel(ParagraphBlock paragraph, double size, double bodyFontSize)
    {
        string text = paragraph.Text.Trim();
        if (text.Length is 0 or > 160 || paragraph.Lines.Count > 3)
        {
            return 0;
        }

        if (text.EndsWith('.') && text.Length > 60)
        {
            return 0;
        }

        bool bold = paragraph.Lines
            .SelectMany(l => l.Tokens)
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .All(t => t.Style.Bold);

        double ratio = bodyFontSize > 0 ? size / bodyFontSize : 1.0;

        if (ratio >= 1.55)
        {
            return 1;
        }

        if (ratio >= 1.3)
        {
            return 2;
        }

        if (ratio >= 1.12 && bold)
        {
            return 3;
        }

        // Titulo numerado em negrito, no mesmo corpo do texto ("3.1. DO OBJETO").
        bool numbered = ListMarkerRegex().IsMatch(text) && char.IsDigit(text.TrimStart()[0]);
        if (bold && paragraph.Lines.Count <= 2 && (numbered || IsUpperCaseTitle(text)))
        {
            return 3;
        }

        return 0;
    }

    private static bool IsUpperCaseTitle(string text)
    {
        int letters = text.Count(char.IsLetter);
        if (letters < 3)
        {
            return false;
        }

        int upper = text.Count(c => char.IsLetter(c) && char.IsUpper(c));
        return upper >= letters * 0.9;
    }
}
