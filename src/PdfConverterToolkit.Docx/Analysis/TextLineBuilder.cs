using System.Text;
using System.Text.RegularExpressions;
using PdfConverterToolkit.Docx.Model;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Agrupa caracteres em linhas (pela linha de base) e, dentro de cada linha, em trechos
/// com o mesmo estilo — reinserindo os espacos que o PDF nao guarda.
/// </summary>
internal static partial class TextLineBuilder
{
    [GeneratedRegex(@" {2,}")]
    private static partial Regex CollapseRegex();

    /// <summary>
    /// Monta as linhas de texto de um conjunto de caracteres.
    /// </summary>
    /// <param name="glyphs">Caracteres da regiao (pagina inteira ou uma celula).</param>
    /// <param name="splitGapPt">
    /// Espaco horizontal, em pontos, a partir do qual a linha e quebrada em duas
    /// (colunas lado a lado). Use <see cref="double.MaxValue"/> para nunca quebrar.
    /// </param>
    /// <param name="maxSpaces">
    /// Quantos espacos, no maximo, um vao pode gerar. Dentro de celulas vale 1: uma
    /// sequencia longa de espacos so faria o texto quebrar de linha sem necessidade.
    /// </param>
    public static List<TextLineBox> Build(IReadOnlyList<Glyph> glyphs, double splitGapPt, int maxSpaces = 12)
    {
        var lines = new List<TextLineBox>();
        if (glyphs.Count == 0)
        {
            return lines;
        }

        foreach (var cluster in ClusterByBaseLine(glyphs))
        {
            cluster.Sort(static (a, b) => a.AdvanceStart.CompareTo(b.AdvanceStart));

            foreach (var segment in SplitOnWideGaps(cluster, splitGapPt))
            {
                var line = BuildLine(segment, maxSpaces);
                if (line is not null)
                {
                    lines.Add(line);
                }
            }
        }

        lines.Sort(static (a, b) =>
        {
            int byTop = a.Bounds.Top.CompareTo(b.Bounds.Top);
            return byTop != 0 ? byTop : a.Bounds.Left.CompareTo(b.Bounds.Left);
        });

        return lines;
    }

    /// <summary>Agrupa caracteres cuja linha de base esta praticamente na mesma altura.</summary>
    private static List<List<Glyph>> ClusterByBaseLine(IReadOnlyList<Glyph> glyphs)
    {
        var ordered = glyphs.OrderBy(g => g.BaseLine).ThenBy(g => g.Bounds.Left).ToList();
        var clusters = new List<List<Glyph>>();

        List<Glyph>? current = null;
        double reference = 0;
        double referenceSize = 0;

        foreach (var g in ordered)
        {
            double tolerance = Math.Max(1.0, 0.4 * Math.Max(g.FontSize, referenceSize));
            if (current is null || Math.Abs(g.BaseLine - reference) > tolerance)
            {
                current = [];
                clusters.Add(current);
                reference = g.BaseLine;
                referenceSize = g.FontSize;
            }
            else
            {
                // A referencia acompanha a linha para tolerar leves variacoes de base.
                reference = (reference * current.Count + g.BaseLine) / (current.Count + 1);
                referenceSize = Math.Max(referenceSize, g.FontSize);
            }

            current.Add(g);
        }

        return clusters;
    }

    /// <summary>Quebra a linha quando ha um vao horizontal grande demais (colunas distintas).</summary>
    private static IEnumerable<List<Glyph>> SplitOnWideGaps(List<Glyph> line, double splitGapPt)
    {
        var segment = new List<Glyph> { line[0] };
        for (int i = 1; i < line.Count; i++)
        {
            double gap = line[i].AdvanceStart - line[i - 1].AdvanceEnd;
            if (gap > splitGapPt)
            {
                yield return segment;
                segment = [];
            }

            segment.Add(line[i]);
        }

        yield return segment;
    }

    /// <summary>Converte os caracteres ordenados de uma linha em trechos estilizados.</summary>
    private static TextLineBox? BuildLine(List<Glyph> glyphs, int maxSpaces)
    {
        var tokens = new List<TextToken>();
        var sb = new StringBuilder();
        TextStyle? style = null;
        string? uri = null;
        Glyph? previous = null;

        void Flush()
        {
            if (sb.Length > 0 && style is not null)
            {
                tokens.Add(new TextToken { Text = sb.ToString(), Style = style, Uri = uri });
            }

            sb.Clear();
        }

        foreach (var g in glyphs)
        {
            if (string.IsNullOrEmpty(g.Text))
            {
                continue;
            }

            string spaces = previous is null ? string.Empty : SpacesBetween(previous, g, maxSpaces);

            if (style is null || !style.Equals(g.Style) || !string.Equals(uri, g.Uri, StringComparison.Ordinal))
            {
                // Os espacos ficam no trecho anterior, para nao herdarem o novo estilo.
                if (spaces.Length > 0 && style is not null)
                {
                    sb.Append(spaces);
                    spaces = string.Empty;
                }

                Flush();
                style = g.Style;
                uri = g.Uri;
            }

            sb.Append(spaces);
            sb.Append(g.Text);
            previous = g;
        }

        Flush();
        Normalize(tokens, maxSpaces);

        if (tokens.Count == 0)
        {
            return null;
        }

        var bounds = Rect.Union(glyphs.Select(g => g.Bounds));
        double baseLine = glyphs.Sum(g => g.BaseLine * Math.Max(g.Bounds.Width, 0.1))
                          / glyphs.Sum(g => Math.Max(g.Bounds.Width, 0.1));

        return new TextLineBox
        {
            Tokens = tokens,
            Bounds = bounds,
            BaseLine = baseLine,
            FontSize = glyphs.Max(g => g.FontSize),
        };
    }

    /// <summary>
    /// Limpa os trechos da linha: tira o espaco das pontas (o recuo ja posiciona a linha)
    /// e, quando o destino nao comporta sequencias longas — dentro de celulas —, reduz
    /// cada sequencia de espacos a um so, para o texto nao quebrar de linha a toa.
    /// </summary>
    private static void Normalize(List<TextToken> tokens, int maxSpaces)
    {
        if (tokens.Count == 0)
        {
            return;
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            string text = tokens[i].Text;

            if (maxSpaces <= 1)
            {
                text = CollapseRegex().Replace(text, " ");
            }

            if (i == 0)
            {
                text = text.TrimStart();
            }

            if (i == tokens.Count - 1)
            {
                text = text.TrimEnd();
            }

            tokens[i] = new TextToken { Text = text, Style = tokens[i].Style, Uri = tokens[i].Uri };
        }

        tokens.RemoveAll(t => t.Text.Length == 0);
    }

    /// <summary>Decide quantos espacos representam o vao entre dois caracteres vizinhos.</summary>
    private static string SpacesBetween(Glyph previous, Glyph current, int maxSpaces)
    {
        // O proprio PDF as vezes emite o caractere de espaco; nesse caso nao inventamos outro.
        if (previous.Text is " " || current.Text is " ")
        {
            return string.Empty;
        }

        // O vao e medido entre avancos, nao entre desenhos: assim o espacamento natural
        // da fonte nao vira espaco de verdade.
        double gap = current.AdvanceStart - previous.AdvanceEnd;
        double spaceWidth = Math.Max(previous.SpaceWidth, current.SpaceWidth);

        if (gap < spaceWidth * 0.4)
        {
            return string.Empty;
        }

        if (maxSpaces <= 1 || gap < spaceWidth * 1.8)
        {
            return " ";
        }

        int count = (int)Math.Round(gap / spaceWidth);
        return new string(' ', Math.Clamp(count, 2, maxSpaces));
    }
}
