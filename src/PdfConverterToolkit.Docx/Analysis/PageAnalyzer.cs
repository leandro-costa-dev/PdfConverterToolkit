using PdfConverterToolkit.Docx.Model;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Transforma uma pagina do PDF no modelo de layout: paragrafos, tabelas e imagens
/// posicionados, na ordem em que devem aparecer no documento.
/// </summary>
internal sealed class PageAnalyzer(ConversionOptions options)
{
    public PageLayout Analyze(Page page, byte[] pdfBytes)
    {
        var space = new PageSpace(page);
        var pageBounds = new Rect(0, 0, space.Width, space.Height);

        var glyphs = ExtractGlyphs(page, space);
        bool hasText = glyphs.Count(g => !string.IsNullOrWhiteSpace(g.Text)) >= 5;

        var layout = new PageLayout
        {
            Number = page.Number,
            WidthPt = space.Width,
            HeightPt = space.Height,
        };

        if (!hasText)
        {
            if (options.RasterizeTextlessPages)
            {
                var raster = PageRasterizer.Render(
                    pdfBytes, page.Number - 1, pageBounds, options.RasterDpi, options.RasterQuality, options.Password);
                if (raster is not null)
                {
                    layout.Blocks.Add(raster);
                    layout.IsRasterFallback = true;
                    return Finish(layout, pageBounds);
                }
            }

            if (options.ExtractImages)
            {
                layout.Blocks.AddRange(ExtractImages(page, space, pdfBytes, pageHasText: false));
            }

            return Finish(layout, pageBounds);
        }

        double bodyFontSize = MostCommonFontSize(glyphs);
        var remaining = new List<Glyph>(glyphs);
        var blocks = new List<LayoutBlock>();

        if (options.DetectTables)
        {
            var rulings = RulingExtractor.Extract(page, space);
            foreach (var table in TableBuilder.Build(rulings, space.Height))
            {
                if (FillTable(table, remaining, bodyFontSize))
                {
                    blocks.Add(table);
                }
            }
        }

        if (options.DetectBorderlessTables)
        {
            foreach (var table in BorderlessTableBuilder.Detect(remaining))
            {
                if (FillTable(table, remaining, bodyFontSize))
                {
                    blocks.Add(table);
                }
            }
        }

        if (options.ExtractImages)
        {
            blocks.AddRange(ExtractImages(page, space, pdfBytes, pageHasText: true, glyphs));
        }

        // O que sobrou e texto corrido: quebras horizontais muito largas viram linhas separadas.
        var flowLines = TextLineBuilder.Build(remaining, splitGapPt: Math.Max(60.0, space.Width * 0.3));
        var flowRegion = flowLines.Count > 0
            ? Rect.Union(flowLines.Select(l => l.Bounds))
            : pageBounds;

        blocks.AddRange(ParagraphBuilder.Build(flowLines, flowRegion, bodyFontSize, options.DetectHeadings));

        var ordered = SortIntoReadingOrder(blocks);
        ApplySpacing(ordered);
        layout.Blocks.AddRange(ordered);
        return Finish(layout, pageBounds);
    }

    /// <summary>
    /// Recalcula o espaco antes de cada paragrafo olhando o bloco imediatamente anterior
    /// na ordem de leitura — inclusive tabelas e imagens, que o construtor de paragrafos
    /// nao enxerga.
    /// </summary>
    private static void ApplySpacing(List<LayoutBlock> ordered)
    {
        LayoutBlock? previous = null;
        foreach (var block in ordered)
        {
            if (block is ParagraphBlock paragraph)
            {
                paragraph.SpaceBeforePt = previous is null ? 0 : ParagraphBuilder.SpaceBetween(previous, paragraph);
            }

            previous = block;
        }
    }

    /// <summary>
    /// Extrai as imagens e, para as que o leitor nao decodifica (JPEG2000, mascaras
    /// exoticas), recorta a regiao correspondente da pagina rasterizada.
    /// </summary>
    private List<ImageBlock> ExtractImages(
        Page page,
        PageSpace space,
        byte[] pdfBytes,
        bool pageHasText,
        IReadOnlyList<Glyph>? glyphs = null)
    {
        var (images, undecodable) = ImageExtractor.Extract(page, space, pageHasText);

        if (glyphs is { Count: > 0 })
        {
            // Rodapes e cabecalhos as vezes vem duas vezes: como texto e como figura.
            // Manter as duas duplicaria a linha no Word.
            images.RemoveAll(i => IsCoveredByText(i.Bounds, glyphs));
            undecodable.RemoveAll(r => IsCoveredByText(r, glyphs));
        }

        if (undecodable.Count == 0)
        {
            return images;
        }

        using var bitmap = PageRasterizer.RenderPage(pdfBytes, page.Number - 1, options.RasterDpi, options.Password);
        if (bitmap is null)
        {
            return images;
        }

        double pointsToPixels = bitmap.Width / Math.Max(space.Width, 1);
        foreach (var region in undecodable)
        {
            var cropped = PageRasterizer.Crop(bitmap, region, pointsToPixels);
            if (cropped is not null)
            {
                images.Add(cropped);
            }
        }

        return images;
    }

    /// <summary>
    /// Diz se a regiao da imagem ja esta coberta pelo texto extraido — sinal de que a
    /// figura e so uma copia rasterizada do que ja sera escrito como texto.
    /// </summary>
    private static bool IsCoveredByText(Rect bounds, IReadOnlyList<Glyph> glyphs)
    {
        double area = bounds.Width * bounds.Height;
        if (area <= 0)
        {
            return false;
        }

        double covered = 0;
        foreach (var glyph in glyphs)
        {
            if (string.IsNullOrWhiteSpace(glyph.Text))
            {
                continue;
            }

            double width = Math.Min(bounds.Right, glyph.Bounds.Right) - Math.Max(bounds.Left, glyph.Bounds.Left);
            double height = Math.Min(bounds.Bottom, glyph.Bounds.Bottom) - Math.Max(bounds.Top, glyph.Bounds.Top);
            if (width > 0 && height > 0)
            {
                covered += width * height;
            }
        }

        return covered >= area * 0.15;
    }

    /// <summary>Fecha a pagina calculando a area realmente ocupada (vira a margem no .docx).</summary>
    private static PageLayout Finish(PageLayout layout, Rect pageBounds)
    {
        layout.ContentBounds = layout.Blocks.Count > 0
            ? Rect.Union(layout.Blocks.Select(b => b.Bounds))
            : pageBounds;

        return layout;
    }

    /// <summary>Le as letras da pagina e resolve fonte, corpo, cor e hiperlink de cada uma.</summary>
    private List<Glyph> ExtractGlyphs(Page page, PageSpace space)
    {
        var glyphs = new List<Glyph>();

        IReadOnlyList<Letter> letters;
        try
        {
            letters = page.Letters;
        }
        catch (Exception)
        {
            return glyphs;
        }

        var links = ReadHyperlinks(page, space);
        var styleCache = new Dictionary<(string?, int, bool, bool, string), TextStyle>();

        foreach (var letter in letters)
        {
            if (string.IsNullOrEmpty(letter.Value))
            {
                continue;
            }

            // Texto invisivel (modo "Neither") e a camada de OCR — vale a pena manter.
            if (letter.Value.Length == 1 && char.IsControl(letter.Value[0]))
            {
                continue;
            }

            Rect bounds = space.ToScreen(letter.BoundingBox);
            var (startX, baseY) = space.ToScreen(letter.StartBaseLine.X, letter.StartBaseLine.Y);
            var (endX, _) = space.ToScreen(letter.EndBaseLine.X, letter.EndBaseLine.Y);

            // Depois da rotacao o inicio pode cair a direita do fim; normalizamos.
            double advanceStart = Math.Min(startX, endX);
            double advanceEnd = Math.Max(startX, endX);
            if (advanceEnd - advanceStart < 0.01)
            {
                advanceStart = bounds.Left;
                advanceEnd = bounds.Right;
            }

            double size = letter.PointSize > 0.1 ? letter.PointSize : Math.Max(bounds.Height / 0.7, 1.0);
            var (family, nameBold, nameItalic) = FontMapper.Resolve(letter.FontName);
            bool bold = nameBold || letter.FontDetails?.IsBold == true;
            bool italic = nameItalic || letter.FontDetails?.IsItalic == true;
            string color = ColorHex.From(letter.Color);

            var key = (family, (int)Math.Round(size * 2), bold, italic, color);
            if (!styleCache.TryGetValue(key, out var style))
            {
                style = new TextStyle(family, Math.Round(size * 2) / 2.0, bold, italic, color);
                styleCache[key] = style;
            }

            // Espacos com largura zero nao ajudam e atrapalham o calculo dos vaos.
            if (string.IsNullOrWhiteSpace(letter.Value) && bounds.Width <= 0.01)
            {
                continue;
            }

            var glyph = new Glyph
            {
                Text = letter.Value,
                Bounds = bounds,
                AdvanceStart = advanceStart,
                AdvanceEnd = advanceEnd,
                BaseLine = baseY,
                FontSize = style.SizePt,
                Style = style,
            };

            if (links.Count > 0)
            {
                foreach (var (area, uri) in links)
                {
                    if (area.Contains(bounds.CenterX, bounds.CenterY))
                    {
                        glyph.Uri = uri;
                        break;
                    }
                }
            }

            glyphs.Add(glyph);
        }

        return glyphs;
    }

    private List<(Rect Area, string Uri)> ReadHyperlinks(Page page, PageSpace space)
    {
        var links = new List<(Rect, string)>();
        if (!options.KeepHyperlinks)
        {
            return links;
        }

        try
        {
            foreach (var link in page.GetHyperlinks())
            {
                if (!string.IsNullOrWhiteSpace(link.Uri))
                {
                    links.Add((space.ToScreen(link.Bounds), link.Uri));
                }
            }
        }
        catch (Exception)
        {
            // Anotacoes malformadas nao devem derrubar a conversao.
        }

        return links;
    }

    /// <summary>
    /// Move para dentro da tabela os caracteres que caem em cada celula.
    /// Retorna false quando a "tabela" nao contem texto nenhum (moldura decorativa).
    /// </summary>
    private bool FillTable(TableBlock table, List<Glyph> available, double bodyFontSize)
    {
        var inside = available.Where(g => table.Bounds.Contains(g.Bounds.CenterX, g.Bounds.CenterY)).ToList();
        if (inside.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<TableCell>();
        for (int r = 0; r < table.RowCount; r++)
        {
            for (int c = 0; c < table.ColumnCount; c++)
            {
                var cell = table.Cells[r, c];
                if (cell is null || !seen.Add(cell))
                {
                    continue;
                }

                var cellGlyphs = inside
                    .Where(g => cell.Bounds.Contains(g.Bounds.CenterX, g.Bounds.CenterY))
                    .ToList();
                if (cellGlyphs.Count == 0)
                {
                    continue;
                }

                var lines = TextLineBuilder.Build(cellGlyphs, splitGapPt: double.MaxValue, maxSpaces: 1);
                var paragraphs = ParagraphBuilder.Build(lines, cell.Bounds, bodyFontSize, detectHeadings: false);
                if (paragraphs.Count == 0)
                {
                    continue;
                }

                // O espaco acima do primeiro paragrafo viraria altura extra na linha da
                // tabela; a posicao vertical do texto e reproduzida pelo alinhamento.
                paragraphs[0].SpaceBeforePt = 0;
                cell.VerticalAlignment = InferVerticalAlignment(cell.Bounds, Rect.Union(lines.Select(l => l.Bounds)));
                cell.Content.AddRange(paragraphs);
            }
        }

        available.RemoveAll(g => table.Bounds.Contains(g.Bounds.CenterX, g.Bounds.CenterY));
        return true;
    }

    /// <summary>Deduz o alinhamento vertical comparando as folgas acima e abaixo do texto.</summary>
    private static CellVerticalAlignment InferVerticalAlignment(Rect cell, Rect content)
    {
        double above = content.Top - cell.Top;
        double below = cell.Bottom - content.Bottom;
        double tolerance = Math.Max(2.0, content.Height * 0.35);

        if (Math.Abs(above - below) <= tolerance)
        {
            return CellVerticalAlignment.Center;
        }

        return above < below ? CellVerticalAlignment.Top : CellVerticalAlignment.Bottom;
    }

    /// <summary>Corpo de fonte mais frequente — a referencia para identificar titulos.</summary>
    private static double MostCommonFontSize(List<Glyph> glyphs)
    {
        var histogram = new Dictionary<double, int>();
        foreach (var g in glyphs)
        {
            if (string.IsNullOrWhiteSpace(g.Text))
            {
                continue;
            }

            histogram[g.FontSize] = histogram.GetValueOrDefault(g.FontSize) + 1;
        }

        return histogram.Count == 0 ? 11.0 : histogram.MaxBy(kv => kv.Value).Key;
    }

    /// <summary>Ordena de cima para baixo; blocos na mesma faixa vertical vao da esquerda para a direita.</summary>
    private static List<LayoutBlock> SortIntoReadingOrder(List<LayoutBlock> blocks) =>
        blocks
            .OrderBy(b => Math.Round(b.Bounds.Top / 3.0))
            .ThenBy(b => b.Bounds.Left)
            .ToList();
}
