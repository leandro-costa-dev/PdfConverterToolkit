using System.Text;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Traduz o nome da fonte como aparece no PDF ("BCDEEE+Arial-BoldMT", "TimesNewRomanPS-ItalicMT")
/// para um nome de familia que o Word reconheca ("Arial", "Times New Roman").
/// </summary>
internal static class FontMapper
{
    private static readonly Dictionary<string, string> KnownFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["arial"] = "Arial",
        ["arialmt"] = "Arial",
        ["arialnarrow"] = "Arial Narrow",
        ["arialblack"] = "Arial Black",
        ["arialunicodems"] = "Arial Unicode MS",
        ["helvetica"] = "Arial",
        ["helveticaneue"] = "Arial",
        ["times"] = "Times New Roman",
        ["timesnewroman"] = "Times New Roman",
        ["timesnewromanps"] = "Times New Roman",
        ["timesnewromanpsmt"] = "Times New Roman",
        ["timesroman"] = "Times New Roman",
        ["couriernew"] = "Courier New",
        ["courier"] = "Courier New",
        ["couriernewps"] = "Courier New",
        ["calibri"] = "Calibri",
        ["cambria"] = "Cambria",
        ["candara"] = "Candara",
        ["consolas"] = "Consolas",
        ["constantia"] = "Constantia",
        ["corbel"] = "Corbel",
        ["garamond"] = "Garamond",
        ["georgia"] = "Georgia",
        ["verdana"] = "Verdana",
        ["tahoma"] = "Tahoma",
        ["trebuchetms"] = "Trebuchet MS",
        ["segoeui"] = "Segoe UI",
        ["symbol"] = "Symbol",
        ["zapfdingbats"] = "Wingdings",
        ["wingdings"] = "Wingdings",
        ["wingdings2"] = "Wingdings 2",
        ["wingdings3"] = "Wingdings 3",
        ["liberationserif"] = "Times New Roman",
        ["liberationsans"] = "Arial",
        ["liberationmono"] = "Courier New",
        ["nimbusromno9l"] = "Times New Roman",
        ["nimbussanl"] = "Arial",
        ["dejavusans"] = "DejaVu Sans",
        ["dejavuserif"] = "DejaVu Serif",
        ["carlito"] = "Calibri",
        ["caladea"] = "Cambria",
    };

    // Sufixos de estilo que o PDF anexa ao nome da familia.
    private static readonly string[] StyleSuffixes =
    [
        "bolditalic", "boldoblique", "italicbold", "semibolditalic", "extrabolditalic",
        "bold", "italic", "oblique", "regular", "roman", "book", "medium", "light",
        "semibold", "extrabold", "black", "heavy", "condensed", "narrow", "mt", "ps", "std", "pro",
    ];

    /// <summary>Descobre a familia, e se o proprio nome ja indica negrito/italico.</summary>
    public static (string Family, bool Bold, bool Italic) Resolve(string? pdfFontName)
    {
        if (string.IsNullOrWhiteSpace(pdfFontName))
        {
            return ("Calibri", false, false);
        }

        string name = pdfFontName.Trim();

        // Prefixo de subconjunto: seis letras maiusculas + '+'.
        int plus = name.IndexOf('+');
        if (plus == 6)
        {
            name = name[7..];
        }

        string lower = name.ToLowerInvariant();
        bool bold = lower.Contains("bold") || lower.Contains("black") || lower.Contains("heavy") || lower.Contains(",b");
        bool italic = lower.Contains("italic") || lower.Contains("oblique") || lower.Contains(",i");

        // Remove separadores e sufixos de estilo para chegar na familia.
        string core = new(name.Where(char.IsLetterOrDigit).ToArray());
        string coreLower = core.ToLowerInvariant();

        if (KnownFamilies.TryGetValue(coreLower, out var direct))
        {
            return (direct, bold, italic);
        }

        bool trimmed = true;
        while (trimmed && coreLower.Length > 0)
        {
            trimmed = false;
            foreach (var suffix in StyleSuffixes)
            {
                if (coreLower.Length > suffix.Length && coreLower.EndsWith(suffix, StringComparison.Ordinal))
                {
                    coreLower = coreLower[..^suffix.Length];
                    core = core[..coreLower.Length];
                    trimmed = true;
                    break;
                }
            }
        }

        if (KnownFamilies.TryGetValue(coreLower, out var mapped))
        {
            return (mapped, bold, italic);
        }

        return (Prettify(core), bold, italic);
    }

    /// <summary>"MinhaFonteCorporativa" -&gt; "Minha Fonte Corporativa".</summary>
    private static string Prettify(string core)
    {
        if (core.Length == 0)
        {
            return "Calibri";
        }

        var sb = new StringBuilder(core.Length + 6);
        for (int i = 0; i < core.Length; i++)
        {
            char c = core[i];
            bool boundary = i > 0
                && char.IsUpper(c)
                && (char.IsLower(core[i - 1]) || (i + 1 < core.Length && char.IsLower(core[i + 1])));
            if (boundary)
            {
                sb.Append(' ');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
