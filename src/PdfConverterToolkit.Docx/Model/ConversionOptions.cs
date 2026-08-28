namespace PdfConverterToolkit.Docx.Model;

/// <summary>Ajustes da conversao PDF -&gt; DOCX.</summary>
public sealed class ConversionOptions
{
    /// <summary>Reconstroi tabelas a partir das linhas vetoriais do PDF.</summary>
    public bool DetectTables { get; set; } = true;

    /// <summary>Tambem reconhece tabelas "sem grade", pelo alinhamento das colunas.</summary>
    public bool DetectBorderlessTables { get; set; } = true;

    /// <summary>Extrai as imagens embutidas e as posiciona no texto.</summary>
    public bool ExtractImages { get; set; } = true;

    /// <summary>Marca titulos (Heading 1..3) quando o paragrafo tem cara de cabecalho.</summary>
    public bool DetectHeadings { get; set; } = true;

    /// <summary>Preserva hiperlinks clicaveis.</summary>
    public bool KeepHyperlinks { get; set; } = true;

    /// <summary>
    /// Paginas sem camada de texto (digitalizadas) viram uma imagem da pagina inteira,
    /// em vez de sairem em branco.
    /// </summary>
    public bool RasterizeTextlessPages { get; set; } = true;

    /// <summary>Resolucao usada ao rasterizar paginas sem texto.</summary>
    public int RasterDpi { get; set; } = 150;

    /// <summary>Qualidade JPEG (1-100) das paginas rasterizadas.</summary>
    public int RasterQuality { get; set; } = 80;

    /// <summary>
    /// Junta as linhas de um mesmo paragrafo em texto corrido, que o Word reflui ao editar.
    /// Desligado, cada linha do PDF vira uma linha fixa — o layout fica identico, mas o
    /// texto nao se reorganiza sozinho. Dentro de tabelas as linhas sao sempre preservadas,
    /// porque a largura da celula nao deixa margem para o Word quebrar de outro jeito.
    /// </summary>
    public bool ReflowText { get; set; } = true;

    /// <summary>
    /// Remove do corpo os cabecalhos/rodapes que se repetem em quase todas as paginas
    /// (numero de pagina, timbre em texto). Desligado: tudo e mantido onde estava.
    /// </summary>
    public bool RemoveRepeatedHeadersFooters { get; set; }

    /// <summary>Senha do PDF, quando protegido.</summary>
    public string? Password { get; set; }
}


/// <summary>Resumo do que foi produzido.</summary>
public sealed class ConversionResult
{
    public required string OutputPath { get; init; }

    public int PageCount { get; set; }

    public int ParagraphCount { get; set; }

    public int TableCount { get; set; }

    public int ImageCount { get; set; }

    public int RasterizedPageCount { get; set; }

    /// <summary>Caracteres de texto real extraidos (0 = PDF sem camada de texto).</summary>
    public int TextCharacterCount { get; set; }

    public List<string> Warnings { get; } = [];
}
