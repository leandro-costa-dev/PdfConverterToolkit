using PdfConverterToolkit.App.Tabs;

namespace PdfConverterToolkit.App;

/// <summary>
/// Janela unica do PdfConverterToolkit, com uma aba por conversao: PDF → Word,
/// PDF → Imagem e Compactar PDF. A interface e montada em codigo — nao ha designer.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly TabControl tabs = new();

    public MainForm()
    {
        Text = "PdfConverterToolkit — Word · Imagem · Compactar";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 720);
        Size = new Size(1000, 760);
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(8);

        TrySetIcon();
        BuildTabs();
    }

    private void BuildTabs()
    {
        tabs.Dock = DockStyle.Fill;

        tabs.TabPages.Add(Page("PDF → Word", new WordTab()));
        tabs.TabPages.Add(Page("PDF → Imagem", new ImageTab()));
        tabs.TabPages.Add(Page("Compactar PDF", new CompressTab()));

        Controls.Add(tabs);
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage
        {
            Text = title,
            UseVisualStyleBackColor = true,
            Padding = new Padding(8),
        };

        page.Controls.Add(content);
        return page;
    }

    /// <summary>
    /// Define o icone da janela. Preferencia: o .ico embutido como recurso (multi-resolucao,
    /// o Windows escolhe o tamanho ideal). Reserva: o icone embutido no proprio .exe.
    /// </summary>
    private void TrySetIcon()
    {
        try
        {
            var assembly = typeof(MainForm).Assembly;
            string? resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("pdf_filetype.ico", StringComparison.OrdinalIgnoreCase));

            if (resource is not null)
            {
                using Stream? stream = assembly.GetManifestResourceStream(resource);
                if (stream is not null)
                {
                    Icon = new Icon(stream);
                    return;
                }
            }

            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Icon = Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch (Exception)
        {
            // Sem icone disponivel: mantem o padrao do Windows.
        }
    }
}
