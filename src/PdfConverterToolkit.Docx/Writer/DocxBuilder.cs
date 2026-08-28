using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfConverterToolkit.Docx.Model;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WordCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;

namespace PdfConverterToolkit.Docx.Writer;

/// <summary>
/// Escreve o arquivo .docx a partir do modelo de layout: uma secao por pagina,
/// com o tamanho e as margens do original, paragrafos formatados, tabelas com
/// bordas/mesclagens e as imagens no lugar em que estavam.
/// </summary>
internal sealed class DocxBuilder : IDisposable
{
    private readonly WordprocessingDocument document;
    private readonly MainDocumentPart mainPart;
    private readonly Body body;
    private readonly Dictionary<string, string> hyperlinkIds = new(StringComparer.Ordinal);
    private readonly bool reflowText;
    private uint drawingId = 1;

    public DocxBuilder(string outputPath, bool reflowText = true)
    {
        this.reflowText = reflowText;
        document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document();
        body = mainPart.Document.AppendChild(new Body());
        AddStyles();
    }

    public int ParagraphCount { get; private set; }

    public int TableCount { get; private set; }

    public int ImageCount { get; private set; }

    /// <summary>Escreve uma pagina inteira; a ultima pagina nao leva quebra de secao propria.</summary>
    public void AddPage(PageLayout page, bool isLast)
    {
        double marginLeft = page.ContentBounds.Left;

        foreach (var block in page.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock paragraph:
                    body.AppendChild(BuildParagraph(paragraph, marginLeft, keepLineBreaks: !reflowText));
                    ParagraphCount++;
                    break;

                case TableBlock table:
                    body.AppendChild(BuildTable(table, marginLeft));
                    // O Word exige um paragrafo depois de cada tabela.
                    body.AppendChild(Spacer());
                    TableCount++;
                    break;

                case ImageBlock image:
                    body.AppendChild(BuildImageParagraph(image, marginLeft));
                    ImageCount++;
                    break;
            }
        }

        var section = BuildSection(page);
        if (isLast)
        {
            body.AppendChild(section);
        }
        else
        {
            // Secoes intermediarias moram no ultimo paragrafo da pagina.
            body.AppendChild(new Paragraph(new ParagraphProperties(EmptyRunSize(), section)));
        }
    }

    public void Save()
    {
        mainPart.Document.Save();
    }

    public void Dispose()
    {
        document.Dispose();
    }

    /// <summary>
    /// Estilos base: sem o espacamento automatico do Word, o texto cai exatamente
    /// onde o PDF o colocava. Heading1..3 existem so para alimentar o painel de navegacao.
    /// </summary>
    private void AddStyles()
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(
                    new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
                        new FontSize { Val = "22" })),
                new ParagraphPropertiesDefault(
                    new ParagraphPropertiesBaseStyle(
                        new SpacingBetweenLines
                        {
                            After = "0",
                            Before = "0",
                            Line = "240",
                            LineRule = LineSpacingRuleValues.Auto,
                        }))),
            new Style(
                new StyleName { Val = "Normal" },
                new PrimaryStyle())
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true,
            });

        for (int level = 1; level <= 6; level++)
        {
            styles.AppendChild(new Style(
                new StyleName { Val = $"heading {level}" },
                new BasedOn { Val = "Normal" },
                new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new OutlineLevel { Val = level - 1 }))
            {
                Type = StyleValues.Paragraph,
                StyleId = $"Heading{level}",
            });
        }

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static SectionProperties BuildSection(PageLayout page)
    {
        double width = page.WidthPt;
        double height = page.HeightPt;

        double left = Math.Clamp(page.ContentBounds.Left, 0, width / 2);
        double top = Math.Clamp(page.ContentBounds.Top, 0, height / 2);
        double right = Math.Clamp(width - page.ContentBounds.Right, 0, width / 2);
        double bottom = Math.Clamp(height - page.ContentBounds.Bottom, 0, height / 2);

        return new SectionProperties(
            new PageSize
            {
                Width = (uint)Units.ToTwips(width),
                Height = (uint)Units.ToTwips(height),
                Orient = page.IsLandscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait,
            },
            new PageMargin
            {
                Left = (uint)Units.ToTwips(left),
                Right = (uint)Units.ToTwips(right),
                Top = Units.ToTwips(top),
                Bottom = Units.ToTwips(bottom),
                Header = 0U,
                Footer = 0U,
                Gutter = 0U,
            });
    }

    /// <summary>Paragrafo minusculo: fecha secoes e separa tabelas sem abrir espaco visivel.</summary>
    private static Paragraph Spacer() =>
        new(new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "0", Line = "20", LineRule = LineSpacingRuleValues.Exact },
            EmptyRunSize()));

    private static ParagraphMarkRunProperties EmptyRunSize() =>
        new(new FontSize { Val = "2" }, new FontSizeComplexScript { Val = "2" });

    /// <param name="keepLineBreaks">
    /// true mantem as quebras de linha do PDF (o bloco ocupa exatamente a mesma altura);
    /// false junta as linhas em texto corrido, que o Word reflui ao editar.
    /// </param>
    private Paragraph BuildParagraph(ParagraphBlock block, double marginLeft, bool keepLineBreaks)
    {
        var paragraph = new Paragraph(BuildParagraphProperties(block, marginLeft, keepLineBreaks));

        for (int i = 0; i < block.Lines.Count; i++)
        {
            var line = block.Lines[i];

            if (i > 0 && keepLineBreaks)
            {
                paragraph.AppendChild(new Run(new Break()));
            }

            // Sem quebra fixa, as linhas voltam a ser texto corrido separado por espaco.
            bool needsSpace = i > 0 && !keepLineBreaks && !EndsWithHyphen(block.Lines[i - 1].Text);
            bool first = true;

            foreach (var token in line.Tokens)
            {
                string text = token.Text;
                if (first && needsSpace)
                {
                    text = " " + text.TrimStart();
                    first = false;
                }

                if (text.Length == 0)
                {
                    continue;
                }

                AppendToken(paragraph, text, token);
            }
        }

        return paragraph;
    }

    private static bool EndsWithHyphen(string text)
    {
        string trimmed = text.TrimEnd();
        return trimmed.EndsWith('-') || trimmed.EndsWith('­');
    }

    private void AppendToken(OpenXmlElement parent, string text, TextToken token)
    {
        var run = new Run(
            BuildRunProperties(token.Style),
            new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        if (token.Uri is { Length: > 0 } uri && TryGetHyperlinkId(uri, out string id))
        {
            parent.AppendChild(new Hyperlink(run) { Id = id });
        }
        else
        {
            parent.AppendChild(run);
        }
    }

    /// <summary>
    /// Monta o &lt;w:rPr&gt;. A ordem dos filhos e imposta pelo esquema do Office Open XML:
    /// rFonts, b, bCs, i, iCs, color, sz, szCs.
    /// </summary>
    private static RunProperties BuildRunProperties(TextStyle style)
    {
        var properties = new RunProperties(
            new RunFonts
            {
                Ascii = style.FontFamily,
                HighAnsi = style.FontFamily,
                ComplexScript = style.FontFamily,
            });

        if (style.Bold)
        {
            properties.AppendChild(new Bold());
            properties.AppendChild(new BoldComplexScript());
        }

        if (style.Italic)
        {
            properties.AppendChild(new Italic());
            properties.AppendChild(new ItalicComplexScript());
        }

        if (style.ColorHex is not "000000")
        {
            properties.AppendChild(new Color { Val = style.ColorHex });
        }

        properties.AppendChild(new FontSize { Val = Units.ToHalfPoints(style.SizePt) });
        properties.AppendChild(new FontSizeComplexScript { Val = Units.ToHalfPoints(style.SizePt) });

        return properties;
    }

    private static ParagraphProperties BuildParagraphProperties(
        ParagraphBlock block,
        double marginLeft,
        bool keepLineBreaks)
    {
        // A ordem dos filhos de <w:pPr> tambem e imposta pelo esquema: pStyle, spacing, ind, jc.
        var properties = new ParagraphProperties();

        if (block.HeadingLevel is >= 1 and <= 6)
        {
            properties.AppendChild(new ParagraphStyleId { Val = $"Heading{block.HeadingLevel}" });
        }

        var spacing = new SpacingBetweenLines
        {
            Before = Math.Clamp(Units.ToTwips(block.SpaceBeforePt), 0, 2400).ToString(),
            After = "0",
        };

        if (block.LineSpacingPt > 1)
        {
            spacing.Line = Units.ToTwips(block.LineSpacingPt).ToString();
            spacing.LineRule = LineSpacingRuleValues.Exact;
        }

        properties.AppendChild(spacing);

        // O recuo e medido a partir da margem esquerda (da pagina ou da celula).
        // Com texto centralizado ou a direita o recuo so atrapalharia o alinhamento.
        bool indentable = block.Alignment is BlockAlignment.Left or BlockAlignment.Justify;
        double indent = indentable ? Math.Max(0, block.BodyLeftPt - marginLeft) : 0;
        if (indent > 1 || (indentable && Math.Abs(block.FirstLineIndentPt) > 1))
        {
            var indentation = new Indentation { Left = Units.ToTwips(indent).ToString() };
            if (block.FirstLineIndentPt > 1)
            {
                indentation.FirstLine = Units.ToTwips(block.FirstLineIndentPt).ToString();
            }
            else if (block.FirstLineIndentPt < -1)
            {
                indentation.Hanging = Units.ToTwips(-block.FirstLineIndentPt).ToString();
            }

            properties.AppendChild(indentation);
        }

        properties.AppendChild(new Justification
        {
            Val = block.Alignment switch
            {
                BlockAlignment.Center => JustificationValues.Center,
                BlockAlignment.Right => JustificationValues.Right,

                // Com quebras fixas o Word esticaria tambem a linha antes de cada quebra,
                // abrindo buracos no meio das palavras.
                BlockAlignment.Justify when !keepLineBreaks => JustificationValues.Both,
                _ => JustificationValues.Left,
            },
        });

        return properties;
    }

    private Table BuildTable(TableBlock block, double marginLeft)
    {
        var widths = new List<int>(block.ColumnCount);
        for (int c = 0; c < block.ColumnCount; c++)
        {
            widths.Add(Math.Max(1, Units.ToTwips(block.ColumnEdges[c + 1] - block.ColumnEdges[c])));
        }

        var grid = new TableGrid();
        foreach (int width in widths)
        {
            grid.AppendChild(new GridColumn { Width = width.ToString() });
        }

        // Ordem exigida pelo esquema: tblW, tblInd, tblLayout, tblCellMar.
        var properties = new TableProperties(
            new TableWidth { Width = widths.Sum().ToString(), Type = TableWidthUnitValues.Dxa },
            new TableIndentation
            {
                Width = Math.Max(0, Units.ToTwips(block.Bounds.Left - marginLeft)),
                Type = TableWidthUnitValues.Dxa,
            },
            new TableLayout { Type = TableLayoutValues.Fixed },
            // Sem folga interna: o recuo de cada paragrafo ja reproduz o afastamento
            // que o texto tinha da borda da celula no PDF.
            new TableCellMarginDefault(
                new TableCellLeftMargin { Width = 0, Type = TableWidthValues.Dxa },
                new TableCellRightMargin { Width = 0, Type = TableWidthValues.Dxa }));

        var table = new Table(properties, grid);

        for (int r = 0; r < block.RowCount; r++)
        {
            var row = new TableRow(new TableRowProperties(new TableRowHeight
            {
                Val = (uint)Math.Max(1, Units.ToTwips(block.RowEdges[r + 1] - block.RowEdges[r])),
                HeightType = HeightRuleValues.AtLeast,
            }));

            int c = 0;
            while (c < block.ColumnCount)
            {
                var cell = block.Cells[r, c];
                if (cell is null)
                {
                    row.AppendChild(BuildEmptyCell(widths[c]));
                    c++;
                    continue;
                }

                int span = Math.Max(1, Math.Min(cell.ColumnSpan, block.ColumnCount - c));
                int width = 0;
                for (int i = 0; i < span; i++)
                {
                    width += widths[c + i];
                }

                row.AppendChild(BuildCell(cell, width, span, isOriginRow: cell.Row == r));
                c += span;
            }

            table.AppendChild(row);
        }

        return table;
    }

    private WordCell BuildCell(Model.TableCell cell, int widthTwips, int span, bool isOriginRow)
    {
        var properties = new TableCellProperties(
            new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa });

        if (span > 1)
        {
            properties.AppendChild(new GridSpan { Val = span });
        }

        if (cell.RowSpan > 1)
        {
            properties.AppendChild(new VerticalMerge
            {
                Val = isOriginRow ? MergedCellValues.Restart : MergedCellValues.Continue,
            });
        }

        properties.AppendChild(new TableCellBorders(
            Border<TopBorder>(cell.BorderTop),
            Border<LeftBorder>(cell.BorderLeft),
            Border<BottomBorder>(cell.BorderBottom),
            Border<RightBorder>(cell.BorderRight)));

        if (cell.ShadingHex is { Length: 6 } fill)
        {
            properties.AppendChild(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = fill,
            });
        }

        if (cell.VerticalAlignment is not CellVerticalAlignment.Top)
        {
            properties.AppendChild(new TableCellVerticalAlignment
            {
                Val = cell.VerticalAlignment is CellVerticalAlignment.Center
                    ? TableVerticalAlignmentValues.Center
                    : TableVerticalAlignmentValues.Bottom,
            });
        }

        var tableCell = new WordCell(properties);

        if (isOriginRow && cell.Content.Count > 0)
        {
            foreach (var paragraph in cell.Content)
            {
                // Dentro da celula o recuo e medido a partir da propria celula, e as
                // quebras do PDF sao mantidas: a largura da celula nao permite outro corte.
                tableCell.AppendChild(BuildParagraph(paragraph, cell.Bounds.Left, keepLineBreaks: true));
            }
        }
        else
        {
            tableCell.AppendChild(new Paragraph(new ParagraphProperties(EmptyRunSize())));
        }

        return tableCell;
    }

    private static WordCell BuildEmptyCell(int widthTwips) =>
        new(
            new TableCellProperties(new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa }),
            new Paragraph(new ParagraphProperties(EmptyRunSize())));

    private static T Border<T>(bool visible)
        where T : BorderType, new() =>
        new()
        {
            Val = visible ? BorderValues.Single : BorderValues.None,
            Size = visible ? 4U : 0U,
            Color = "000000",
            Space = 0U,
        };

    private Paragraph BuildImageParagraph(ImageBlock image, double marginLeft)
    {
        // Ordem exigida pelo esquema: spacing, ind, jc.
        var properties = new ParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });

        double indent = Math.Max(0, image.Bounds.Left - marginLeft);
        if (indent > 1)
        {
            properties.AppendChild(new Indentation { Left = Units.ToTwips(indent).ToString() });
        }

        properties.AppendChild(new Justification { Val = JustificationValues.Left });

        return new Paragraph(properties, BuildImageRun(image));
    }

    private Run BuildImageRun(ImageBlock image)
    {
        var partType = image.Format == ImageFormat.Jpeg ? ImagePartType.Jpeg : ImagePartType.Png;
        ImagePart part = mainPart.AddImagePart(partType);
        using (var stream = new MemoryStream(image.Data, writable: false))
        {
            part.FeedData(stream);
        }

        string relationshipId = mainPart.GetIdOfPart(part);
        long width = Math.Max(1, Units.ToEmu(image.Bounds.Width));
        long height = Math.Max(1, Units.ToEmu(image.Bounds.Height));
        uint id = drawingId++;

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = width, Cy = height },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = $"Imagem {id}" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = id, Name = $"imagem{id}" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = width, Cy = height }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });

        return new Run(drawing);
    }

    private bool TryGetHyperlinkId(string uri, out string id)
    {
        if (hyperlinkIds.TryGetValue(uri, out string? existing))
        {
            id = existing;
            return true;
        }

        try
        {
            var relationship = mainPart.AddHyperlinkRelationship(new Uri(uri, UriKind.Absolute), true);
            hyperlinkIds[uri] = relationship.Id;
            id = relationship.Id;
            return true;
        }
        catch (Exception)
        {
            // URI relativa ou invalida: o texto continua la, so nao fica clicavel.
            id = string.Empty;
            return false;
        }
    }
}
