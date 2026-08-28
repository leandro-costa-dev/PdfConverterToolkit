using PdfConverterToolkit.Docx.Model;

namespace PdfConverterToolkit.Docx.Analysis;

/// <summary>
/// Reconhece tabelas que o PDF desenha sem nenhuma linha: blocos em que varias linhas
/// seguidas tem o mesmo numero de "colunas" e as colunas comecam sempre no mesmo X.
/// O resultado e uma tabela sem bordas — no Word o texto continua alinhado como no original.
/// </summary>
internal static class BorderlessTableBuilder
{
    /// <summary>Minimo de linhas alinhadas para aceitar o bloco como tabela.</summary>
    private const int MinRows = 3;

    /// <summary>Desalinhamento tolerado entre o inicio de uma coluna e outra, em pontos.</summary>
    private const double ColumnTolerance = 6.0;

    public static List<TableBlock> Detect(IReadOnlyList<Glyph> glyphs)
    {
        var rows = BuildRows(glyphs).Where(r => r.Count >= 2).ToList();
        var tables = new List<TableBlock>();

        int i = 0;
        while (i < rows.Count)
        {
            int j = i + 1;
            while (j < rows.Count && IsAligned(rows[j - 1], rows[j]))
            {
                j++;
            }

            if (j - i >= MinRows)
            {
                var table = BuildTable(rows.GetRange(i, j - i));
                if (table is not null)
                {
                    tables.Add(table);
                }
            }

            i = j > i + 1 ? j : i + 1;
        }

        return tables;
    }

    /// <summary>Uma linha da pagina ja quebrada nos seus trechos separados por vaos largos.</summary>
    private static List<List<Rect>> BuildRows(IReadOnlyList<Glyph> glyphs)
    {
        var rows = new List<List<Rect>>();
        var ordered = glyphs.OrderBy(g => g.BaseLine).ThenBy(g => g.Bounds.Left).ToList();

        var current = new List<Glyph>();
        double reference = double.NaN;

        void Close()
        {
            if (current.Count > 0)
            {
                rows.Add(Segment(current));
                current = [];
            }
        }

        foreach (var g in ordered)
        {
            if (double.IsNaN(reference) || Math.Abs(g.BaseLine - reference) > Math.Max(1.0, 0.4 * g.FontSize))
            {
                Close();
                reference = g.BaseLine;
            }

            current.Add(g);
        }

        Close();
        return rows;
    }

    /// <summary>Quebra uma linha nos vaos horizontais largos: cada pedaco vira uma celula.</summary>
    private static List<Rect> Segment(List<Glyph> line)
    {
        line.Sort(static (a, b) => a.AdvanceStart.CompareTo(b.AdvanceStart));

        var segments = new List<Rect>();
        Rect current = line[0].Bounds;

        for (int i = 1; i < line.Count; i++)
        {
            double gap = line[i].AdvanceStart - line[i - 1].AdvanceEnd;
            double threshold = Math.Max(2.5 * line[i].SpaceWidth, 8.0);
            if (gap > threshold)
            {
                segments.Add(current);
                current = line[i].Bounds;
            }
            else
            {
                current = current.Union(line[i].Bounds);
            }
        }

        segments.Add(current);
        return segments;
    }

    private static bool IsAligned(List<Rect> a, List<Rect> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        // Linhas muito distantes verticalmente nao pertencem ao mesmo bloco.
        double gap = b[0].Top - a[0].Bottom;
        if (gap > 2.5 * Math.Max(a[0].Height, 1.0))
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (Math.Abs(a[i].Left - b[i].Left) > ColumnTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static TableBlock? BuildTable(List<List<Rect>> rows)
    {
        int cols = rows[0].Count;
        if (cols < 2)
        {
            return null;
        }

        // Divisorias verticais no meio do vao entre uma coluna e a seguinte.
        var colEdges = new List<double>(cols + 1);
        colEdges.Add(rows.Min(r => r[0].Left) - 2.0);
        for (int c = 1; c < cols; c++)
        {
            double previousRight = rows.Max(r => r[c - 1].Right);
            double currentLeft = rows.Min(r => r[c].Left);
            colEdges.Add((previousRight + currentLeft) / 2.0);
        }

        colEdges.Add(rows.Max(r => r[cols - 1].Right) + 2.0);

        var rowEdges = new List<double>(rows.Count + 1);
        rowEdges.Add(rows[0].Min(s => s.Top) - 1.0);
        for (int r = 1; r < rows.Count; r++)
        {
            double previousBottom = rows[r - 1].Max(s => s.Bottom);
            double currentTop = rows[r].Min(s => s.Top);
            rowEdges.Add((previousBottom + currentTop) / 2.0);
        }

        rowEdges.Add(rows[^1].Max(s => s.Bottom) + 1.0);

        var cells = new TableCell[rows.Count, cols];
        for (int r = 0; r < rows.Count; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                cells[r, c] = new TableCell
                {
                    Row = r,
                    Column = c,
                    Bounds = new Rect(colEdges[c], rowEdges[r], colEdges[c + 1], rowEdges[r + 1]),
                };
            }
        }

        return new TableBlock
        {
            Bounds = new Rect(colEdges[0], rowEdges[0], colEdges[^1], rowEdges[^1]),
            ColumnEdges = colEdges,
            RowEdges = rowEdges,
            Cells = cells,
        };
    }
}
