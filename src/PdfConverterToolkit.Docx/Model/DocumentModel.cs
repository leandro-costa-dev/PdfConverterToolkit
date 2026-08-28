namespace PdfConverterToolkit.Docx.Model;

/// <summary>Aparencia de um trecho de texto (o que vira um &lt;w:rPr&gt; no .docx).</summary>
public sealed record TextStyle(string FontFamily, double SizePt, bool Bold, bool Italic, string ColorHex)
{
    public static readonly TextStyle Default = new("Calibri", 11, false, false, "000000");
}

/// <summary>Trecho continuo de texto com a mesma aparencia (vira um &lt;w:r&gt;).</summary>
public sealed class TextToken
{
    public required string Text { get; init; }

    public required TextStyle Style { get; init; }

    /// <summary>Destino do hiperlink quando o trecho esta dentro de uma area clicavel.</summary>
    public string? Uri { get; init; }
}

/// <summary>Uma linha fisica de texto da pagina.</summary>
public sealed class TextLineBox
{
    public required List<TextToken> Tokens { get; init; }

    public required Rect Bounds { get; init; }

    /// <summary>Y da linha de base, em coordenadas de tela.</summary>
    public required double BaseLine { get; init; }

    /// <summary>Maior corpo de fonte encontrado na linha, em pontos.</summary>
    public required double FontSize { get; init; }

    public string Text => string.Concat(Tokens.Select(t => t.Text));

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}

public enum BlockAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>Elemento posicionado da pagina (paragrafo, tabela ou imagem).</summary>
public abstract class LayoutBlock
{
    public Rect Bounds { get; set; }
}

/// <summary>Paragrafo: uma ou mais linhas consecutivas que pertencem ao mesmo bloco de texto.</summary>
public sealed class ParagraphBlock : LayoutBlock
{
    public List<TextLineBox> Lines { get; } = [];

    public BlockAlignment Alignment { get; set; } = BlockAlignment.Left;

    /// <summary>Posicao X do corpo do paragrafo na pagina, em pontos.</summary>
    public double BodyLeftPt { get; set; }

    /// <summary>Recuo extra (ou negativo) aplicado apenas a primeira linha, em pontos.</summary>
    public double FirstLineIndentPt { get; set; }

    /// <summary>Espaco vertical livre acima do paragrafo, em pontos.</summary>
    public double SpaceBeforePt { get; set; }

    /// <summary>Distancia media entre linhas de base dentro do paragrafo, em pontos.</summary>
    public double LineSpacingPt { get; set; }

    /// <summary>Nivel de titulo (1..6) quando o paragrafo parece um cabecalho; 0 = corpo.</summary>
    public int HeadingLevel { get; set; }

    public string Text => string.Join(" ", Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0));

    /// <summary>Linha de base da primeira linha do paragrafo.</summary>
    public double FirstBaseLine => Lines.Count > 0 ? Lines[0].BaseLine : Bounds.Top;

    /// <summary>Linha de base da ultima linha do paragrafo.</summary>
    public double LastBaseLine => Lines.Count > 0 ? Lines[^1].BaseLine : Bounds.Bottom;

    /// <summary>
    /// Entrelinha natural do paragrafo: e o que o Word ja aplica sozinho entre duas
    /// linhas, e por isso precisa ser descontado do espaco antes do paragrafo.
    /// </summary>
    public double NaturalLeadingPt => LineSpacingPt > 1
        ? LineSpacingPt
        : Lines.Count > 0 ? Lines.Max(l => l.FontSize) * 1.15 : 0;
}

public enum CellVerticalAlignment
{
    Top,
    Center,
    Bottom,
}

/// <summary>Celula de uma tabela detectada.</summary>
public sealed class TableCell
{
    public required int Row { get; init; }

    public required int Column { get; init; }

    public int RowSpan { get; set; } = 1;

    public int ColumnSpan { get; set; } = 1;

    public Rect Bounds { get; set; }

    public List<ParagraphBlock> Content { get; } = [];

    /// <summary>Onde o texto se apoia dentro da celula, deduzido da posicao original.</summary>
    public CellVerticalAlignment VerticalAlignment { get; set; } = CellVerticalAlignment.Top;

    public bool BorderTop { get; set; }

    public bool BorderBottom { get; set; }

    public bool BorderLeft { get; set; }

    public bool BorderRight { get; set; }

    /// <summary>Cor de fundo em RRGGBB, ou null quando a celula nao tem preenchimento.</summary>
    public string? ShadingHex { get; set; }
}

/// <summary>Tabela reconstruida a partir das linhas vetoriais (ou do alinhamento das colunas).</summary>
public sealed class TableBlock : LayoutBlock
{
    /// <summary>Coordenadas X das divisorias verticais, da esquerda para a direita.</summary>
    public required IReadOnlyList<double> ColumnEdges { get; init; }

    /// <summary>Coordenadas Y das divisorias horizontais, de cima para baixo.</summary>
    public required IReadOnlyList<double> RowEdges { get; init; }

    public required TableCell[,] Cells { get; init; }

    public int RowCount => RowEdges.Count - 1;

    public int ColumnCount => ColumnEdges.Count - 1;
}

public enum ImageFormat
{
    Png,
    Jpeg,
}

/// <summary>Imagem extraida (ou pagina inteira rasterizada, no modo de fallback).</summary>
public sealed class ImageBlock : LayoutBlock
{
    public required byte[] Data { get; init; }

    public required ImageFormat Format { get; init; }
}

/// <summary>Pagina ja analisada, pronta para virar uma secao do .docx.</summary>
public sealed class PageLayout
{
    public required int Number { get; init; }

    /// <summary>Largura da folha em pontos (ja rotacionada).</summary>
    public required double WidthPt { get; init; }

    /// <summary>Altura da folha em pontos (ja rotacionada).</summary>
    public required double HeightPt { get; init; }

    public Rect ContentBounds { get; set; }

    public List<LayoutBlock> Blocks { get; } = [];

    /// <summary>true quando a pagina nao tinha texto e foi rasterizada como imagem.</summary>
    public bool IsRasterFallback { get; set; }

    public bool IsLandscape => WidthPt > HeightPt;
}
