namespace PdfConverterToolkit.App.Controls;

/// <summary>
/// Pecas de interface repetidas nas tres abas. Tudo em TableLayoutPanel, que se comporta
/// bem em qualquer DPI e tamanho de janela.
/// </summary>
internal static class Widgets
{
    /// <summary>Coluna unica que ocupa toda a aba; as linhas sao definidas por quem chama.</summary>
    public static TableLayoutPanel RootLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return layout;
    }

    /// <summary>Linha "Pasta de saida": rotulo + caixa (estica) + botao Procurar.</summary>
    public static Control OutputRow(TextBox box, Button browse, string hint)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        box.Margin = new Padding(3, 6, 3, 3);

        browse.Text = "Procurar…";
        browse.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        browse.Height = 26;
        browse.Margin = new Padding(3, 4, 0, 3);

        layout.Controls.Add(Label("Pasta de saída:", fill: true), 0, 0);
        layout.Controls.Add(box, 1, 0);
        layout.Controls.Add(browse, 2, 0);
        layout.Controls.Add(Hint(hint, autoSize: true), 3, 0);
        return layout;
    }

    /// <summary>Linha da acao: botao principal, botao cancelar e a barra de progresso.</summary>
    public static Control ActionRow(Button action, string label, Button cancel, ProgressBar progress)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 186));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        action.Text = label;
        action.AutoSize = false;
        action.Size = new Size(178, 40);
        action.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        action.Anchor = AnchorStyles.Left;

        cancel.Text = "Cancelar";
        cancel.AutoSize = false;
        cancel.Size = new Size(92, 40);
        cancel.Anchor = AnchorStyles.Left;
        cancel.Enabled = false;

        progress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        progress.Height = 26;
        progress.Margin = new Padding(6, 3, 3, 3);

        layout.Controls.Add(action, 0, 0);
        layout.Controls.Add(cancel, 1, 0);
        layout.Controls.Add(progress, 2, 0);
        return layout;
    }

    /// <summary>Caixa de texto somente-leitura onde o resultado de cada arquivo e registrado.</summary>
    public static Control LogBox(TextBox log)
    {
        log.Dock = DockStyle.Fill;
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.BackColor = SystemColors.Window;
        log.Font = new Font("Consolas", 8.5f);

        return new GroupBox
        {
            Text = "Resultado",
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Controls = { log },
        };
    }

    public static Label Label(string text, bool fill = false) => new()
    {
        Text = text,
        AutoSize = !fill,
        Dock = fill ? DockStyle.Fill : DockStyle.None,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    /// <summary>Rotulo posicionado em coordenadas absolutas dentro de um GroupBox.</summary>
    public static Label LabelAt(string text, int x, int y) => new()
    {
        Text = text,
        Location = new Point(x, y),
        AutoSize = true,
    };

    /// <summary>Texto de apoio, em cinza.</summary>
    public static Label Hint(string text, bool autoSize = false) => new()
    {
        Text = text,
        AutoSize = autoSize,
        ForeColor = SystemColors.GrayText,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 7, 3, 3),
    };

    /// <summary>Texto de apoio ocupando uma area fixa dentro de um GroupBox.</summary>
    public static Label HintAt(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Location = new Point(x, y),
        Size = new Size(width, height),
        ForeColor = SystemColors.GrayText,
    };

    public static NumericUpDown Numeric(int min, int max, int value, int x, int y)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            Size = new Size(80, 24),
            ThousandsSeparator = false,
            Location = new Point(x, y),
        };
        return numeric;
    }

    public static CheckBox Check(string text, int x, int y, bool checkedState, string? tip = null)
    {
        var box = new CheckBox
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Checked = checkedState,
        };

        if (tip is not null)
        {
            new ToolTip().SetToolTip(box, tip);
        }

        return box;
    }

    public static RadioButton Radio(string text, int x, int y, bool checkedState, string? tip = null)
    {
        var radio = new RadioButton
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Checked = checkedState,
        };

        if (tip is not null)
        {
            new ToolTip().SetToolTip(radio, tip);
        }

        return radio;
    }

    /// <summary>Altura real do deslizador de qualidade, ja contando a faixa de marcacoes.</summary>
    public const int QualityHeight = 45;

    /// <summary>
    /// Deslizador de qualidade JPEG com o valor exibido ao lado. <c>AutoSize</c> desligado de
    /// proposito: ligado, o TrackBar ignora a altura pedida e cresce sozinho, passando por cima
    /// do que estiver logo abaixo dele.
    /// </summary>
    public static TrackBar Quality(int value, int x, int y, int width, Label valueLabel)
    {
        var track = new TrackBar
        {
            AutoSize = false,
            Location = new Point(x, y),
            Size = new Size(width, QualityHeight),
            Minimum = 1,
            Maximum = 100,
            TickFrequency = 10,
            Value = value,
        };

        valueLabel.Text = value.ToString();
        track.ValueChanged += (_, _) => valueLabel.Text = track.Value.ToString();
        return track;
    }
}
