using PdfConverterToolkit.App.Controls;
using PdfConverterToolkit.Imaging;

namespace PdfConverterToolkit.App.Tabs;

/// <summary>
/// Aba Compactar PDF: rasteriza as paginas e remonta um PDF menor, gravando
/// <c>nome_compactado.pdf</c>.
/// </summary>
internal sealed class CompressTab : ConverterTab<CompressionOptions>
{
    private readonly NumericUpDown dpi = Widgets.Numeric(50, 600, 120, 130, 28);
    private readonly Label qualityValue = Widgets.LabelAt("60", 636, 32);
    private readonly TrackBar quality;
    private readonly CheckBox skipIfLarger = Widgets.Check("Pular se não reduzir o tamanho", 12, 72, true,
        "Descarta o resultado quando ele fica maior que o original.");

    private readonly TextBox password = new() { Location = new Point(718, 28), Size = new Size(150, 24), UseSystemPasswordChar = true };

    public CompressTab()
    {
        quality = Widgets.Quality(60, 380, 26, 250, qualityValue);
        Build();
    }

    protected override string ActionLabel => "Compactar PDF";

    protected override int OptionsHeight => 160;

    protected override Control BuildOptions()
    {
        var group = new GroupBox { Text = "Opções de compactação", Dock = DockStyle.Fill };

        group.Controls.AddRange(
        [
            Widgets.LabelAt("Resolução (DPI):", 12, 32), dpi,
            Widgets.LabelAt("Qualidade (JPEG):", 260, 32), quality, qualityValue,
            skipIfLarger,
            Widgets.LabelAt("Senha:", 668, 32), password,
            Widgets.HintAt(
                "As páginas são rasterizadas (o texto vira imagem) e remontadas num novo PDF. Ideal para " +
                "PDFs digitalizados / com muitas imagens. PDFs só de texto tendem a aumentar de tamanho — " +
                "nesse caso o arquivo é pulado. Menor DPI e menor qualidade = arquivo menor.",
                12, 100, 820, 48),
        ]);

        return group;
    }

    protected override CompressionOptions ReadOptions() => new()
    {
        Dpi = (int)dpi.Value,
        Quality = quality.Value,
        SkipIfLarger = skipIfLarger.Checked,
        Password = password.Text.Length > 0 ? password.Text : null,
    };

    protected override string RunBatch(CompressionOptions options, BatchContext context)
    {
        int produced = PdfCompressor.CompressBatch(
            context.Pdfs, context.OutputDirectory, options, context.Report, context.Progress, context.Token);

        return $"{produced} PDF(s) compactado(s).";
    }
}
