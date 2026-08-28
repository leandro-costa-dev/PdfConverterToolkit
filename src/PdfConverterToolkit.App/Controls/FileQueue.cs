using PdfConverterToolkit.Core;

namespace PdfConverterToolkit.App.Controls;

/// <summary>
/// A fila de PDFs de uma aba: a lista, os botoes de adicionar/remover/limpar e o
/// arrastar-e-soltar (de arquivos ou de pastas). As tres abas usam este mesmo controle.
/// </summary>
internal sealed class FileQueue : UserControl
{
    private readonly ListBox list = new();
    private readonly Button add = new();
    private readonly Button remove = new();
    private readonly Button clear = new();
    private readonly CheckBox subfolders = new();

    public FileQueue()
    {
        Dock = DockStyle.Fill;
        Controls.Add(BuildGroup());
        WireEvents();
    }

    /// <summary>Disparado quando entram arquivos novos na fila.</summary>
    public event EventHandler? FilesAdded;

    /// <summary>Os PDFs da fila, na ordem em que foram adicionados.</summary>
    public string[] Files => list.Items.Cast<string>().ToArray();

    /// <summary>Pasta do primeiro PDF da fila; vazio se a fila estiver vazia.</summary>
    public string FirstFolder => list.Items.Count > 0
        ? Path.GetDirectoryName(Path.GetFullPath((string)list.Items[0]!)) ?? string.Empty
        : string.Empty;

    /// <summary>
    /// Acrescenta PDFs a fila. Pastas sao expandidas (respeitando "incluir subpastas") e
    /// os repetidos sao ignorados.
    /// </summary>
    public void Add(IEnumerable<string> paths)
    {
        var pdfs = PdfInputs.Expand(paths, subfolders.Checked);
        int added = 0;

        foreach (string pdf in pdfs)
        {
            if (!list.Items.Contains(pdf))
            {
                list.Items.Add(pdf);
                added++;
            }
        }

        if (added > 0)
        {
            FilesAdded?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Aceita arquivos arrastados tambem sobre <paramref name="zone"/> (a aba inteira).</summary>
    public void EnableDropOn(Control zone)
    {
        zone.AllowDrop = true;
        zone.DragEnter += OnDragEnter;
        zone.DragDrop += OnDragDrop;
    }

    private Control BuildGroup()
    {
        var group = new GroupBox
        {
            Text = "Arquivos PDF  (arraste e solte arquivos ou pastas aqui)",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 8),
        };

        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));

        list.Dock = DockStyle.Fill;
        list.HorizontalScrollbar = true;
        list.IntegralHeight = false;
        list.SelectionMode = SelectionMode.MultiExtended;

        add.Text = "Adicionar PDFs…";
        remove.Text = "Remover";
        clear.Text = "Limpar tudo";

        var column = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8, 0, 0, 0),
        };

        foreach (Button button in new[] { add, remove, clear })
        {
            button.AutoSize = false;
            button.Size = new Size(132, 30);
            button.Margin = new Padding(0, 0, 0, 6);
            column.Controls.Add(button);
        }

        subfolders.Text = "Incluir subpastas";
        subfolders.AutoSize = true;
        subfolders.Margin = new Padding(0, 4, 0, 0);
        column.Controls.Add(subfolders);

        inner.Controls.Add(list, 0, 0);
        inner.Controls.Add(column, 1, 0);
        group.Controls.Add(inner);
        return group;
    }

    private void WireEvents()
    {
        add.Click += (_, _) => Pick();
        remove.Click += (_, _) => RemoveSelected();
        clear.Click += (_, _) => list.Items.Clear();

        EnableDropOn(this);
        EnableDropOn(list);
    }

    private void Pick()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Selecione um ou mais arquivos PDF",
            Filter = "Arquivos PDF (*.pdf)|*.pdf|Todos os arquivos (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Add(dialog.FileNames);
        }
    }

    private void RemoveSelected()
    {
        for (int i = list.SelectedIndices.Count - 1; i >= 0; i--)
        {
            list.Items.RemoveAt(list.SelectedIndices[i]);
        }
    }

    private static void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = HasDroppableContent(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            Add(paths);
        }
    }

    /// <summary>True se o que esta sendo arrastado tem ao menos um PDF ou uma pasta.</summary>
    private static bool HasDroppableContent(IDataObject? data)
        => data?.GetData(DataFormats.FileDrop) is string[] paths
           && paths.Any(p => Directory.Exists(p) || PdfInputs.IsPdf(p));
}
