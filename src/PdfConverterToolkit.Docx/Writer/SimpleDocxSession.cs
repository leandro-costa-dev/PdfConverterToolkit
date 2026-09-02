using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfConverterToolkit.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using PIG = UglyToad.PdfPig;

namespace PdfConverterToolkit.Docx.Writer;

/// <summary>
/// Um .docx aberto que recebe os PDFs um depois do outro, nos modos rapidos que nao tentam
/// reconstruir o layout: so texto, so imagem da pagina, ou os dois empilhados. Converter um
/// arquivo sozinho e o caso de um <c>Append</c> unico; o "arquivo unico" do lote chama
/// <c>Append</c> uma vez por PDF.
///
/// O que precisa valer para o documento inteiro, e nao para cada PDF, mora aqui: o contador
/// de ids das figuras e a promocao da secao final.
///
/// Isolado num arquivo proprio para que os namespaces do OpenXML (que tem tipos como
/// Font/Text) nao conflitem com o WinForms.
/// </summary>
internal sealed class SimpleDocxSession : IDisposable
{
    private const double EmuPerInch = 914400.0;

    // A4 retrato em twips (1 pol = 1440 twips): 210 x 297 mm.
    private const long A4WidthTwips = 11906;
    private const long A4HeightTwips = 16838;
    private const long MarginTwips = 360;                  // 0,25 pol de margem em cada lado
    private const double EmuPerTwip = EmuPerInch / 1440.0; // 635 EMU por twip

    private readonly WordprocessingDocument document;
    private readonly MainDocumentPart main;
    private readonly Body body;

    /// <summary>Ids das figuras: tem de ser unicos no documento inteiro, nao por PDF.</summary>
    private uint drawingId = 1;

    /// <summary>Paragrafo que carrega a secao mais recente; null se ainda nao houver nenhuma.</summary>
    private Paragraph? sectionOwner;

    private bool hasContent;

    public SimpleDocxSession(string outPath)
    {
        document = WordprocessingDocument.Create(outPath, WordprocessingDocumentType.Document);
        main = document.AddMainDocumentPart();
        main.Document = new Document();
        body = main.Document.AppendChild(new Body());
    }

    /// <summary>True enquanto nada foi acrescentado — nao ha documento que valha gravar.</summary>
    public bool IsEmpty => !hasContent;

    /// <summary>
    /// Modo TEXTO: extrai o texto do PDF (PdfPig) como paragrafos editaveis.
    /// O texto e montado a parte e so entra no documento se houver algum caractere, para que
    /// um PDF digitalizado no meio do lote nao deixe folhas em branco no arquivo unico.
    /// </summary>
    /// <returns>
    /// Quantidade de caracteres (nao-espaco) extraidos — 0 indica PDF sem camada de texto
    /// (provavelmente digitalizado) e, nesse caso, nada foi acrescentado.
    /// </returns>
    public int AppendText(
        byte[] pdfBytes,
        string? password = null,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        using var pig = OpenPdf(pdfBytes, password);

        var texts = new List<string>(pig.NumberOfPages);
        int chars = 0;
        int n = 0;

        foreach (var page in pig.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            n++;

            string text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
            chars += text.Count(c => !char.IsWhiteSpace(c));
            texts.Add(text);

            onPageDone?.Invoke(n);
        }

        if (chars == 0)
        {
            return 0;
        }

        StartNewDocument();

        for (int i = 0; i < texts.Count; i++)
        {
            if (i > 0)
            {
                body.AppendChild(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
            }

            AppendTextParagraphs(texts[i]);
        }

        hasContent = true;
        return chars;
    }

    /// <summary>
    /// Modo IMAGEM: rasteriza cada pagina (PDFium) e insere como imagem no .docx (1 por pagina).
    /// Cada pagina vira uma folha A4 (retrato ou paisagem conforme a orientacao do original),
    /// com a imagem ajustada para caber na area util. Preserva anotacoes/assinaturas.
    /// </summary>
    /// <returns>Quantas paginas foram acrescentadas.</returns>
    public int AppendImages(
        byte[] pdfBytes,
        int dpi,
        int quality,
        string? password = null,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        int pages = PdfPageCounter.Count(pdfBytes, password);

        for (int pg = 0; pg < pages; pg++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var raster = PdfRasterizer.RenderJpeg(pdfBytes, pg, dpi, quality, password);
            bool landscape = raster.Width > raster.Height;

            // Cada pagina e sua propria secao (para ter orientacao propria) e fecha essa secao
            // dentro do proprio paragrafo; a ultima do documento sobe para o corpo em Save().
            var paragraph = new Paragraph(
                new ParagraphProperties(BuildSection(landscape)),
                BuildImageRun(raster, dpi, landscape));

            body.AppendChild(paragraph);
            sectionOwner = paragraph;

            hasContent = true;
            onPageDone?.Invoke(pg + 1);
        }

        return pages;
    }

    /// <summary>
    /// Modo HIBRIDO: por pagina, insere a imagem fiel seguida do texto real editavel.
    /// Fidelidade visual total (a imagem) + texto selecionavel/pesquisavel logo abaixo.
    /// Cada pagina vira uma folha A4 na orientacao do original. O .docx nao suporta camada
    /// de texto invisivel sobreposta, entao imagem e texto ficam empilhados por pagina.
    /// </summary>
    /// <returns>Quantas paginas foram acrescentadas.</returns>
    public int AppendHybrid(
        byte[] pdfBytes,
        int dpi,
        int quality,
        string? password = null,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        using var pig = OpenPdf(pdfBytes, password);

        int pages = pig.NumberOfPages;
        for (int pg = 1; pg <= pages; pg++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var raster = PdfRasterizer.RenderJpeg(pdfBytes, pg - 1, dpi, quality, password);
            bool landscape = raster.Width > raster.Height;
            body.AppendChild(new Paragraph(BuildImageRun(raster, dpi, landscape)));

            AppendTextParagraphs(ContentOrderTextExtractor.GetText(pig.GetPage(pg)) ?? string.Empty);

            // Fecha a secao desta pagina (define orientacao e forca a proxima pagina).
            var closing = new Paragraph(new ParagraphProperties(BuildSection(landscape)));
            body.AppendChild(closing);
            sectionOwner = closing;

            hasContent = true;
            onPageDone?.Invoke(pg);
        }

        return pages;
    }

    public void Save()
    {
        PromoteLastSection();
        main.Document.Save();
    }

    public void Dispose()
    {
        document.Dispose();
    }

    /// <summary>
    /// O esquema exige que a secao da ultima pagina more no corpo, e nao dentro de um
    /// paragrafo. Enquanto se escreve nao se sabe qual sera a ultima, entao toda pagina fecha
    /// a secao no paragrafo dela e aqui a derradeira e promovida — o paragrafo que ficar vazio
    /// sai junto.
    /// </summary>
    private void PromoteLastSection()
    {
        if (sectionOwner is null
            || sectionOwner.ParagraphProperties is not { } properties
            || properties.GetFirstChild<SectionProperties>() is not { } section)
        {
            return;
        }

        section.Remove();

        if (!properties.HasChildren)
        {
            properties.Remove();

            if (!sectionOwner.HasChildren)
            {
                sectionOwner.Remove();
            }
        }

        body.AppendChild(section);
        sectionOwner = null;
    }

    /// <summary>
    /// No modo texto nao ha secao por pagina, entao o PDF seguinte precisa de uma quebra
    /// explicita para nao continuar na mesma folha em que o anterior parou.
    /// </summary>
    private void StartNewDocument()
    {
        if (hasContent)
        {
            body.AppendChild(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
        }
    }

    /// <summary>Abre o PDF com leitura tolerante — os mesmos ajustes do motor fiel.</summary>
    private static PIG.PdfDocument OpenPdf(byte[] pdfBytes, string? password)
    {
        var parsing = new PIG.ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true,
            ClipPaths = false,
        };

        if (!string.IsNullOrEmpty(password))
        {
            parsing.Password = password;
        }

        return PIG.PdfDocument.Open(pdfBytes, parsing);
    }

    /// <summary>Acrescenta o texto como paragrafos (uma linha por paragrafo).</summary>
    private void AppendTextParagraphs(string text)
    {
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var run = new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            body.AppendChild(new Paragraph(run));
        }
    }

    /// <summary>Secao A4 (retrato ou paisagem) com margens de 0,25 pol.</summary>
    private static SectionProperties BuildSection(bool landscape)
    {
        uint w = (uint)(landscape ? A4HeightTwips : A4WidthTwips);
        uint h = (uint)(landscape ? A4WidthTwips : A4HeightTwips);
        return new SectionProperties(
            new PageSize()
            {
                Width = w,
                Height = h,
                Orient = landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait,
            },
            new PageMargin()
            {
                Top = (int)MarginTwips,
                Right = (uint)MarginTwips,
                Bottom = (int)MarginTwips,
                Left = (uint)MarginTwips,
                Header = 0U,
                Footer = 0U,
                Gutter = 0U,
            });
    }

    /// <summary>Area util (folha menos as margens) em EMU, conforme a orientacao.</summary>
    private static (long W, long H) PrintableEmu(bool landscape)
    {
        long pageW = landscape ? A4HeightTwips : A4WidthTwips;
        long pageH = landscape ? A4WidthTwips : A4HeightTwips;
        return ((long)((pageW - 2 * MarginTwips) * EmuPerTwip),
                (long)((pageH - 2 * MarginTwips) * EmuPerTwip));
    }

    /// <summary>Cria o Run com a imagem escalada para caber na area util (proporcao preservada).</summary>
    private Run BuildImageRun(PdfRasterizer.RasterPage raster, int dpi, bool landscape)
    {
        ImagePart imagePart = main.AddImagePart(ImagePartType.Jpeg);
        using (var ms = new MemoryStream(raster.Data, 0, raster.Data.Length, writable: false, publiclyVisible: true))
        {
            ms.Position = 0;
            imagePart.FeedData(ms);
        }

        string relId = main.GetIdOfPart(imagePart);

        long emuW = (long)(raster.Width / (double)dpi * EmuPerInch);
        long emuH = (long)(raster.Height / (double)dpi * EmuPerInch);

        var (maxW, maxH) = PrintableEmu(landscape);
        double scale = Math.Min(maxW / (double)emuW, maxH / (double)emuH);
        emuW = Math.Max(1, (long)Math.Round(emuW * scale));
        emuH = Math.Max(1, (long)Math.Round(emuH * scale));

        uint id = drawingId++;

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent() { Cx = emuW, Cy = emuH },
                new DW.EffectExtent() { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties() { Id = id, Name = $"Pagina{id}" },
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties() { Id = id, Name = $"img{id}.jpg" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip() { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset() { X = 0L, Y = 0L },
                                    new A.Extents() { Cx = emuW, Cy = emuH }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            )
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });

        return new Run(drawing);
    }
}
