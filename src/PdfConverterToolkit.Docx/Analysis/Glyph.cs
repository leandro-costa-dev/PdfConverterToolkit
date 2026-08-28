using PdfConverterToolkit.Docx.Model;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Um caractere do PDF ja convertido para coordenadas de tela e com o estilo resolvido.
/// E a unidade minima usada para remontar linhas, paragrafos e celulas.
/// </summary>
internal sealed class Glyph
{
    public required string Text { get; init; }

    /// <summary>Retangulo do desenho da letra (define o tamanho visual da linha).</summary>
    public required Rect Bounds { get; init; }

    /// <summary>X onde a letra comeca sobre a linha de base.</summary>
    public required double AdvanceStart { get; init; }

    /// <summary>
    /// X onde a proxima letra deveria comecar (avanco da fonte). E daqui que sai o vao
    /// real entre duas letras: a caixa do desenho e mais estreita que o avanco, e usa-la
    /// faria surgir espacos onde nao existem — entre dois algarismos, por exemplo.
    /// </summary>
    public required double AdvanceEnd { get; init; }

    /// <summary>Y da linha de base em coordenadas de tela.</summary>
    public required double BaseLine { get; init; }

    /// <summary>Corpo da fonte em pontos.</summary>
    public required double FontSize { get; init; }

    public required TextStyle Style { get; init; }

    public string? Uri { get; set; }

    /// <summary>Largura tipica de um espaco nesta fonte, em pontos.</summary>
    public double SpaceWidth => Math.Max(FontSize * 0.25, 0.5);
}
