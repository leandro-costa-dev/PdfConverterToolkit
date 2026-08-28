using System.Text.RegularExpressions;
using PdfConverterToolkit.Docx.Model;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Encontra os paragrafos que aparecem no topo ou no rodape de quase todas as paginas
/// — timbre, titulo corrente, "Pagina 3 de 12" — para poder retira-los do corpo.
/// </summary>
internal static partial class RepeatedBlockFilter
{
    /// <summary>Faixa do topo/rodape, como fracao da altura da pagina.</summary>
    private const double BandRatio = 0.09;

    /// <summary>Fracao das paginas em que o texto precisa aparecer para contar como repetido.</summary>
    private const double MinFrequency = 0.6;

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    public static void Remove(List<PageLayout> pages)
    {
        if (pages.Count < 3)
        {
            return;
        }

        var frequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in pages)
        {
            // Um mesmo texto repetido na propria pagina conta uma vez so.
            foreach (string key in CandidateKeys(page).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                frequency[key] = frequency.GetValueOrDefault(key) + 1;
            }
        }

        int threshold = (int)Math.Ceiling(pages.Count * MinFrequency);
        var repeated = frequency.Where(kv => kv.Value >= threshold).Select(kv => kv.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (repeated.Count == 0)
        {
            return;
        }

        foreach (var page in pages)
        {
            page.Blocks.RemoveAll(block =>
                block is ParagraphBlock paragraph
                && IsInBand(page, paragraph)
                && repeated.Contains(Normalize(paragraph.Text)));
        }
    }

    private static IEnumerable<string> CandidateKeys(PageLayout page) =>
        page.Blocks
            .OfType<ParagraphBlock>()
            .Where(p => IsInBand(page, p))
            .Select(p => Normalize(p.Text))
            .Where(k => k.Length > 0);

    private static bool IsInBand(PageLayout page, ParagraphBlock paragraph)
    {
        double band = page.HeightPt * BandRatio;
        return paragraph.Bounds.Bottom <= band || paragraph.Bounds.Top >= page.HeightPt - band;
    }

    /// <summary>Numeros viram "#" para que "Pagina 1" e "Pagina 2" contem como o mesmo rodape.</summary>
    private static string Normalize(string text) =>
        DigitsRegex().Replace(text.Trim(), "#");
}
