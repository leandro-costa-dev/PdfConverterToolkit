using PdfConverterToolkit.App.Controls;
using PdfConverterToolkit.Core;
using PdfConverterToolkit.Docx;

namespace PdfConverterToolkit.App.Tabs;

/// <summary>
/// O que a aba entrega ao lote. Juntar os PDFs num arquivo so e escolher o nome dele sao
/// decisoes do lote, nao da conversao — por isso ficam aqui e nao em <see cref="WordOptions"/>.
/// </summary>
/// <param name="Word">Modo e ajustes da conversao.</param>
/// <param name="SingleFile">Gravar um unico .docx com todos os PDFs da fila.</param>
/// <param name="SingleFileName">Nome desse .docx unico, sem pasta.</param>
internal sealed record WordBatchOptions(WordOptions Word, bool SingleFile, string SingleFileName);

/// <summary>
/// Aba PDF → Word. Reune os quatro modos num so lugar: o motor de layout fiel e os tres
/// modos rapidos (texto, pagina como imagem, imagem + texto). As opcoes de reconstrucao
/// valem apenas para o modo fiel; DPI e qualidade, apenas para os modos que rasterizam.
/// Nos tres modos rapidos o lote pode sair num unico .docx.
/// </summary>
internal sealed class WordTab : ConverterTab<WordBatchOptions>
{
    private readonly RadioButton modeFaithful = Widgets.Radio(
        "Layout fiel", 12, 24, true,
        "Texto editavel com a formatacao original, tabelas de verdade, imagens no lugar e uma secao por pagina.");

    private readonly RadioButton modeText = Widgets.Radio(
        "Texto", 12, 48, false,
        "So o texto corrido, editavel e pesquisavel. Nao preserva o layout nem funciona em PDF digitalizado.");

    private readonly RadioButton modeImage = Widgets.Radio(
        "Página como imagem", 12, 72, false,
        "Cada pagina vira uma figura: identico ao original, com assinaturas, mas sem texto editavel.");

    private readonly RadioButton modeHybrid = Widgets.Radio(
        "Imagem + texto", 12, 96, false,
        "Por pagina: a figura fiel e, abaixo, o texto editavel daquela pagina.");

    private readonly CheckBox tables = Widgets.Check("Reconstruir tabelas", 200, 24, true,
        "Remonta as tabelas a partir das linhas do PDF.");

    private readonly CheckBox borderlessTables = Widgets.Check("Tabelas sem borda", 200, 48, true,
        "Tambem reconhece colunas alinhadas sem linhas visiveis.");

    private readonly CheckBox images = Widgets.Check("Extrair imagens", 200, 72, true,
        "Insere as figuras do PDF na posicao original.");

    private readonly CheckBox headings = Widgets.Check("Marcar títulos", 200, 96, true,
        "Aplica Titulo 1..3 para o painel de navegacao do Word.");

    private readonly CheckBox links = Widgets.Check("Manter hiperlinks", 380, 24, true,
        "Preserva os enderecos clicaveis.");

    private readonly CheckBox rasterize = Widgets.Check("Páginas sem texto viram imagem", 380, 48, true,
        "Paginas digitalizadas entram como figura, em vez de sair em branco.");

    private readonly CheckBox cleanHeaders = Widgets.Check("Remover cabeçalho/rodapé repetido", 380, 72, false,
        "Tira do corpo o que se repete em quase todas as paginas.");

    private readonly CheckBox reflow = Widgets.Check("Texto corrido (reflui ao editar)", 380, 96, true,
        "Desmarcado, cada linha do PDF vira uma linha fixa.");

    private readonly CheckBox singleFile = Widgets.Check("Arquivo único (um só .docx)", 12, 122, false,
        "Junta todos os PDFs da fila num unico .docx, na ordem da fila. Nao vale no Layout fiel.");

    private readonly TextBox singleFileName = new() { Location = new Point(310, 118), Size = new Size(290, 24) };

    private readonly NumericUpDown dpi = Widgets.Numeric(36, 600, 150, 700, 22);
    private readonly Label qualityValue = Widgets.LabelAt("80", 810, 60);
    private readonly TrackBar quality;
    private readonly TextBox password = new() { Location = new Point(700, 106), Size = new Size(180, 24), UseSystemPasswordChar = true };

    public WordTab()
    {
        quality = Widgets.Quality(80, 700, 54, 110, qualityValue);
        Build();
        Queue.FilesAdded += (_, _) => SuggestSingleFileName();
        UpdateEnabled();
    }

    protected override string ActionLabel => "Converter para Word";

    protected override int OptionsHeight => 178;

    /// <summary>True quando o lote deve sair num unico .docx.</summary>
    private bool MergeRequested => singleFile.Checked && !modeFaithful.Checked;

    protected override Control BuildOptions()
    {
        var group = new GroupBox { Text = "Modo e opções", Dock = DockStyle.Fill };

        group.Controls.AddRange(
        [
            modeFaithful, modeText, modeImage, modeHybrid,
            tables, borderlessTables, images, headings,
            links, rasterize, cleanHeaders, reflow,
            singleFile, Widgets.LabelAt("Nome do arquivo:", 200, 124), singleFileName,
            Widgets.LabelAt("DPI:", 640, 26), dpi,
            Widgets.LabelAt("Qualidade:", 620, 60), quality, qualityValue,
            Widgets.LabelAt("Senha:", 640, 110), password,
        ]);

        foreach (RadioButton mode in new[] { modeFaithful, modeText, modeImage, modeHybrid })
        {
            mode.CheckedChanged += (_, _) => UpdateEnabled();
        }

        tables.CheckedChanged += (_, _) => UpdateEnabled();
        singleFile.CheckedChanged += (_, _) => UpdateEnabled();
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
    /// Um arquivo unificado nao pode ficar "ao lado de cada PDF": sem pasta escolhida, ele vai
    /// para a pasta do primeiro PDF (o mesmo criterio da aba de imagens).
    /// </summary>
    protected override string ResolveOutputDirectory(string chosen, string[] pdfs)
        => chosen.Length > 0 || !MergeRequested
            ? chosen
            : Path.GetDirectoryName(Path.GetFullPath(pdfs[0]))!;

    protected override string RunBatch(WordBatchOptions options, BatchContext context)
    {
        if (options.SingleFile)
        {
            WordConverter.ConvertMerged(
                context.Pdfs,
                Path.Combine(context.OutputDirectory, options.SingleFileName),
                options.Word,
                context.Report,
                context.Progress,
                overwrite: false,
                context.Token);

            return $"{context.Report.Produced} arquivo .docx unificado gerado.";
        }

        int produced = WordConverter.ConvertBatch(
            context.Pdfs,
            context.OutputDirectory,
            options.Word,
            context.Report,
            context.Progress,
            overwrite: false,
            context.Token);

        return $"{produced} arquivo(s) .docx gerado(s).";
    }

    protected override WordBatchOptions ReadOptions()
    {
        var options = new WordOptions
        {
            Mode = SelectedMode(),
            Dpi = (int)dpi.Value,
            Quality = quality.Value,
            Password = password.Text.Length > 0 ? password.Text : null,
        };

        options.Conversion.DetectTables = tables.Checked;
        options.Conversion.DetectBorderlessTables = tables.Checked && borderlessTables.Checked;
        options.Conversion.ExtractImages = images.Checked;
        options.Conversion.DetectHeadings = headings.Checked;
        options.Conversion.KeepHyperlinks = links.Checked;
        options.Conversion.RasterizeTextlessPages = rasterize.Checked;
        options.Conversion.RemoveRepeatedHeadersFooters = cleanHeaders.Checked;
        options.Conversion.ReflowText = reflow.Checked;
        return new WordBatchOptions(options, MergeRequested, ResolveSingleFileName());
    }

    /// <summary>Sugere o nome do arquivo unico na primeira vez que a fila recebe PDFs.</summary>
    private void SuggestSingleFileName()
    {
        string[] pdfs = Queue.Files;
        if (pdfs.Length > 0 && singleFileName.Text.Trim().Length == 0)
        {
            singleFileName.Text = WordConverter.SuggestMergedName(pdfs[0]);
        }
    }

    /// <summary>
    /// O nome digitado, saneado. A leitura acontece depois de o usuario clicar em Converter e
    /// nao ha como abortar o lote daqui, entao um nome impossivel e corrigido em vez de recusado.
    /// </summary>
    private string ResolveSingleFileName()
    {
        string name = OutputPath.SafeFileName(singleFileName.Text, ".docx");
        if (name.Length > 0)
        {
            return name;
        }

        string[] pdfs = Queue.Files;
        return pdfs.Length > 0 ? WordConverter.SuggestMergedName(pdfs[0]) : "PDFs_unificados.docx";
    }

    private WordMode SelectedMode()
        => modeText.Checked ? WordMode.Text
         : modeImage.Checked ? WordMode.Image
         : modeHybrid.Checked ? WordMode.Hybrid
         : WordMode.Faithful;

    /// <summary>
    /// As opcoes de reconstrucao so existem no modo fiel; DPI e qualidade so valem onde a
    /// pagina e rasterizada (fiel usa como reserva para paginas sem texto).
    /// </summary>
    private void UpdateEnabled()
    {
        bool faithful = modeFaithful.Checked;
        bool raster = faithful || modeImage.Checked || modeHybrid.Checked;

        foreach (CheckBox box in new[] { tables, images, headings, links, rasterize, cleanHeaders, reflow })
        {
            box.Enabled = faithful;
        }

        borderlessTables.Enabled = faithful && tables.Checked;

        dpi.Enabled = raster;
        quality.Enabled = raster;
        qualityValue.Enabled = raster;

        // O arquivo unico vale nos tres modos rapidos; o fiel tem motor proprio, que grava um
        // .docx por PDF. A caixa fica cinza sem ser desmarcada, para nao perder a intencao de
        // quem so passou pelo modo fiel — ReadOptions a ignora enquanto ele estiver escolhido.
        singleFile.Enabled = !faithful;
        singleFileName.Enabled = !faithful && singleFile.Checked;
    }
}
