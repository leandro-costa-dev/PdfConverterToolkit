using PdfConverterToolkit.Docx.Model;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Reconstroi tabelas a partir das divisorias vetoriais: acha as grades conexas,
/// deriva as bordas de linhas/colunas e resolve as celulas mescladas pela ausencia
/// de divisoria entre celulas vizinhas.
/// </summary>
internal static class TableBuilder
{
    /// <summary>Tolerancia, em pontos, para dois tracos serem considerados a mesma divisoria.</summary>
    private const double EdgeTolerance = 2.5;

    /// <summary>Fracao do lado da celula que precisa estar coberta para a borda existir.</summary>
    private const double BorderCoverage = 0.65;

    private const int MaxColumns = 64;
    private const int MaxRows = 400;

    /// <param name="rulings">Tracos vetoriais da pagina.</param>
    /// <param name="pageHeightPt">
    /// Altura da pagina: uma grade de celula unica so vale como tarja de titulo se for
    /// baixa — uma moldura em volta da pagina inteira nao e tabela.
    /// </param>
    public static List<TableBlock> Build(Rulings rulings, double pageHeightPt)
    {
        var tables = new List<TableBlock>();
        foreach (var (horizontal, vertical) in FindGrids(rulings))
        {
            var table = BuildTable(horizontal, vertical, rulings.Fills, pageHeightPt);
            if (table is not null)
            {
                tables.Add(table);
            }
        }

        // Descarta grades contidas em outra maior (bordas duplicadas, molduras).
        return tables
            .Where(t => !tables.Any(other => !ReferenceEquals(other, t)
                                             && t.Bounds.OverlapRatio(other.Bounds) > 0.9
                                             && Area(other) > Area(t)))
            .OrderBy(t => t.Bounds.Top)
            .ToList();

        static double Area(TableBlock t) => t.Bounds.Width * t.Bounds.Height;
    }

    /// <summary>Agrupa tracos que se cruzam; cada grupo conexo e uma tabela candidata.</summary>
    private static List<(List<HLine> H, List<VLine> V)> FindGrids(Rulings rulings)
    {
        int hCount = rulings.Horizontal.Count;
        int total = hCount + rulings.Vertical.Count;
        var parent = new int[total];
        for (int i = 0; i < total; i++)
        {
            parent[i] = i;
        }

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        for (int hi = 0; hi < hCount; hi++)
        {
            HLine h = rulings.Horizontal[hi];
            for (int vi = 0; vi < rulings.Vertical.Count; vi++)
            {
                VLine v = rulings.Vertical[vi];
                bool touches = v.X >= h.X1 - EdgeTolerance && v.X <= h.X2 + EdgeTolerance
                               && h.Y >= v.Y1 - EdgeTolerance && h.Y <= v.Y2 + EdgeTolerance;
                if (touches)
                {
                    Union(hi, hCount + vi);
                }
            }
        }

        var groups = new Dictionary<int, (List<HLine> H, List<VLine> V)>();
        for (int i = 0; i < total; i++)
        {
            int root = Find(i);
            if (!groups.TryGetValue(root, out var g))
            {
                g = ([], []);
                groups[root] = g;
            }

            if (i < hCount)
            {
                g.H.Add(rulings.Horizontal[i]);
            }
            else
            {
                g.V.Add(rulings.Vertical[i - hCount]);
            }
        }

        return groups.Values.Where(g => g.H.Count >= 2 && g.V.Count >= 2).ToList();
    }

    private static TableBlock? BuildTable(
        List<HLine> horizontal,
        List<VLine> vertical,
        List<FilledArea> fills,
        double pageHeightPt)
    {
        var rowEdges = Cluster(horizontal.Select(h => h.Y));
        var colEdges = Cluster(vertical.Select(v => v.X));

        // Uma tabela que atravessa a quebra de pagina comeca (ou termina) sem traco
        // horizontal: os tracos verticais e que mostram onde a grade realmente vai.
        Extend(rowEdges, vertical.Min(v => v.Y1), vertical.Max(v => v.Y2));
        Extend(colEdges, horizontal.Min(h => h.X1), horizontal.Max(h => h.X2));

        int rows = rowEdges.Count - 1;
        int cols = colEdges.Count - 1;
        if (rows < 1 || cols < 1 || cols > MaxColumns || rows > MaxRows)
        {
            return null;
        }

        var bounds = new Rect(colEdges[0], rowEdges[0], colEdges[^1], rowEdges[^1]);
        if (bounds.Width < 20 || bounds.Height < 8)
        {
            return null;
        }

        // Uma unica celula so passa se for uma tarja baixa (titulo de secao com fundo).
        if (rows * cols == 1 && bounds.Height > pageHeightPt * 0.2)
        {
            return null;
        }

        var cells = new TableCell[rows, cols];
        var claimed = new bool[rows, cols];

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (claimed[r, c])
                {
                    continue;
                }

                int colSpan = 1;
                while (c + colSpan < cols
                       && !HasVerticalBorder(vertical, colEdges[c + colSpan], rowEdges[r], rowEdges[r + 1]))
                {
                    colSpan++;
                }

                int rowSpan = 1;
                while (r + rowSpan < rows
                       && !HasHorizontalBorder(horizontal, rowEdges[r + rowSpan], colEdges[c], colEdges[c + colSpan]))
                {
                    rowSpan++;
                }

                var cellBounds = new Rect(colEdges[c], rowEdges[r], colEdges[c + colSpan], rowEdges[r + rowSpan]);
                var cell = new TableCell
                {
                    Row = r,
                    Column = c,
                    RowSpan = rowSpan,
                    ColumnSpan = colSpan,
                    Bounds = cellBounds,
                    BorderTop = HasHorizontalBorder(horizontal, cellBounds.Top, cellBounds.Left, cellBounds.Right),
                    BorderBottom = HasHorizontalBorder(horizontal, cellBounds.Bottom, cellBounds.Left, cellBounds.Right),
                    BorderLeft = HasVerticalBorder(vertical, cellBounds.Left, cellBounds.Top, cellBounds.Bottom),
                    BorderRight = HasVerticalBorder(vertical, cellBounds.Right, cellBounds.Top, cellBounds.Bottom),
                    ShadingHex = FindShading(fills, cellBounds),
                };

                for (int rr = r; rr < r + rowSpan; rr++)
                {
                    for (int cc = c; cc < c + colSpan; cc++)
                    {
                        cells[rr, cc] = cell;
                        claimed[rr, cc] = true;
                    }
                }
            }
        }

        return new TableBlock
        {
            Bounds = bounds,
            RowEdges = rowEdges,
            ColumnEdges = colEdges,
            Cells = cells,
        };
    }

    /// <summary>Estica a grade ate onde os tracos perpendiculares alcancam.</summary>
    private static void Extend(List<double> edges, double start, double end)
    {
        const double MinExtension = 3.0;

        if (edges.Count == 0)
        {
            return;
        }

        if (edges[0] - start > MinExtension)
        {
            edges.Insert(0, start);
        }

        if (end - edges[^1] > MinExtension)
        {
            edges.Add(end);
        }
    }

    /// <summary>Reduz coordenadas proximas a um unico valor (a divisoria de verdade).</summary>
    private static List<double> Cluster(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var result = new List<double>();
        int i = 0;
        while (i < sorted.Count)
        {
            double sum = sorted[i];
            int count = 1;
            int j = i + 1;
            while (j < sorted.Count && sorted[j] - sorted[i] <= EdgeTolerance)
            {
                sum += sorted[j];
                count++;
                j++;
            }

            result.Add(sum / count);
            i = j;
        }

        return result;
    }

    private static bool HasHorizontalBorder(List<HLine> lines, double y, double x1, double x2)
    {
        double needed = (x2 - x1) * BorderCoverage;
        if (needed <= 0)
        {
            return false;
        }

        var intervals = lines
            .Where(l => Math.Abs(l.Y - y) <= EdgeTolerance)
            .Select(l => (Math.Max(l.X1, x1), Math.Min(l.X2, x2)));

        return CoveredLength(intervals) >= needed;
    }

    private static bool HasVerticalBorder(List<VLine> lines, double x, double y1, double y2)
    {
        double needed = (y2 - y1) * BorderCoverage;
        if (needed <= 0)
        {
            return false;
        }

        var intervals = lines
            .Where(l => Math.Abs(l.X - x) <= EdgeTolerance)
            .Select(l => (Math.Max(l.Y1, y1), Math.Min(l.Y2, y2)));

        return CoveredLength(intervals) >= needed;
    }

    /// <summary>Comprimento total coberto por intervalos, contando sobreposicoes uma vez so.</summary>
    private static double CoveredLength(IEnumerable<(double Start, double End)> intervals)
    {
        var list = intervals.Where(i => i.End > i.Start).OrderBy(i => i.Start).ToList();
        double total = 0;
        double cursor = double.NegativeInfinity;

        foreach (var (start, end) in list)
        {
            double from = Math.Max(start, cursor);
            if (end > from)
            {
                total += end - from;
                cursor = end;
            }
        }

        return total;
    }

    private static string? FindShading(List<FilledArea> fills, Rect cell)
    {
        foreach (var fill in fills)
        {
            if (cell.OverlapRatio(fill.Bounds) >= 0.7 && fill.Hex is not "FFFFFF")
            {
                return fill.Hex;
            }
        }

        return null;
    }
}
