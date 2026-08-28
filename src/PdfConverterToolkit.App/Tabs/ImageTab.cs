using PdfConverterToolkit.App.Controls;
using PdfConverterToolkit.Imaging;

namespace PdfConverterToolkit.App.Tabs;

/// <summary>
/// Aba PDF → Imagem: uma imagem por pagina, em JPG, PNG, BMP, GIF ou TIFF, dimensionada
/// por pixels ou por DPI.
/// </summary>
internal sealed class ImageTab : ConverterTab<ImageExportOptions>
{
    private readonly ComboBox format = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Location = new Point(110, 26),
        Size = new Size(160, 24),
    };

    private readonly NumericUpDown width = Widgets.Numeric(0, 20000, 1240, 110, 62);
    private readonly NumericUpDown height = Widgets.Numeric(0, 20000, 0, 390, 62);
    private readonly CheckBox aspect = Widgets.Check("Manter proporção", 540, 64, true);
    private readonly NumericUpDown dpi = Widgets.Numeric(30, 1200, 150, 130, 98);
    private readonly CheckBox annotations = Widgets.Check("Incluir anotações e assinaturas", 300, 100, true,
        "Renderiza anotacoes e campos de formulario (assinaturas digitais, carimbos gov.br, tinta).");

    private readonly Label qualityValue = Widgets.LabelAt("90", 396, 138);
    private readonly TrackBar quality;
    private readonly TextBox password = new() { Location = new Point(560, 134), Size = new Size(160, 24), UseSystemPasswordChar = true };

    public ImageTab()
    {
        quality = Widgets.Quality(90, 130, 128, 260, qualityValue);
        Build();
        UpdateEnabled();
    }

    protected override string ActionLabel => "Converter";

    protected override int OptionsHeight => 216;

    protected override Control BuildOptions()
    {
        var group = new GroupBox { Text = "Opções de saída", Dock = DockStyle.Fill };

        format.Items.AddRange(ImageFormats.All.Select(f => (object)f.Label).ToArray());
        format.SelectedIndex = 0;
        format.SelectedIndexChanged += (_, _) => UpdateEnabled();

        group.Controls.AddRange(
        [
            Widgets.LabelAt("Formato:", 12, 30), format,
            Widgets.LabelAt("Largura (px):", 12, 66), width,
            Widgets.LabelAt("Altura (px):", 300, 66), height,
            aspect,
            Widgets.LabelAt("DPI (se L/A = 0):", 12, 102), dpi,
            annotations,
            Widgets.LabelAt("Qualidade (JPEG):", 12, 138), quality, qualityValue,
            Widgets.LabelAt("Senha:", 500, 138), password,
            Widgets.HintAt(
                "Deixe Largura e Altura em 0 para dimensionar pelo DPI. Com \"Manter proporção\", " +
                "informe apenas uma das dimensões. Cada página vira um arquivo: nome_p001, nome_p002…",
                12, 174, 820, 32),
        ]);

        return group;
    }

    protected override void OnBusyChanged(bool busy)
    {
        if (!busy)
        {
            UpdateEnabled();
        }
    }

    /// <summary>
    /// Um lote de imagens espalhado pelas pastas de origem seria pior de achar depois: sem
    /// pasta escolhida, tudo vai para a pasta do primeiro PDF.
    /// </summary>
    protected override string ResolveOutputDirectory(string chosen, string[] pdfs)
        => chosen.Length > 0 ? chosen : Path.GetDirectoryName(Path.GetFullPath(pdfs[0]))!;

    protected override ImageExportOptions ReadOptions() => new()
    {
        Format = SelectedFormat(),
        Width = (int)width.Value,
        Height = (int)height.Value,
        KeepAspectRatio = aspect.Checked,
        Dpi = (int)dpi.Value,
        Quality = quality.Value,
        IncludeAnnotations = annotations.Checked,
        Password = password.Text.Length > 0 ? password.Text : null,
    };

    protected override string RunBatch(ImageExportOptions options, BatchContext context)
    {
        int images = PdfImageExporter.ExportBatch(
            context.Pdfs, context.OutputDirectory, options, context.Report, context.Progress, context.Token);

        return $"{images} imagem(ns) gerada(s).";
    }

    private ImageOutputFormat SelectedFormat()
        => ImageFormats.All[Math.Max(0, format.SelectedIndex)].Format;

    /// <summary>Somente o JPEG tem controle de qualidade.</summary>
    private void UpdateEnabled()
    {
        bool jpeg = ImageFormats.Of(SelectedFormat()).SupportsQuality;
        quality.Enabled = jpeg;
        qualityValue.Enabled = jpeg;
    }
}
