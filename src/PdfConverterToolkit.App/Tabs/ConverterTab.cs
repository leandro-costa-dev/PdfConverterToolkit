using PdfConverterToolkit.App.Controls;
using PdfConverterToolkit.Core;

namespace PdfConverterToolkit.App.Tabs;

/// <summary>
/// O esqueleto comum das tres abas: fila de PDFs, grupo de opcoes, pasta de saida, botao de
/// acao com cancelamento, progresso e registro do resultado. Cada aba concreta so precisa
/// montar suas opcoes e dizer o que fazer com o lote.
///
/// O parametro <typeparamref name="TOptions"/> separa as duas threads: os controles sao
/// lidos em <see cref="ReadOptions"/>, na thread da interface, e o lote recebe so o objeto
/// pronto — nao ha como tocar num controle de dentro da thread de fundo.
/// </summary>
internal abstract class ConverterTab<TOptions> : UserControl
{
    private readonly TextBox outputFolder = new();
    private readonly Button browse = new();
    private readonly Button action = new();
    private readonly Button cancel = new();
    private readonly ProgressBar progress = new();
    private readonly Label status = new();
    private readonly TextBox log = new();

    private Control? optionsGroup;
    private CancellationTokenSource? cancellation;

    protected ConverterTab()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(8);
    }

    /// <summary>A fila de PDFs desta aba.</summary>
    protected FileQueue Queue { get; } = new();

    /// <summary>Texto do botao principal.</summary>
    protected abstract string ActionLabel { get; }

    /// <summary>Altura reservada ao grupo de opcoes.</summary>
    protected abstract int OptionsHeight { get; }

    /// <summary>Monta o grupo de opcoes especifico da aba.</summary>
    protected abstract Control BuildOptions();

    /// <summary>
    /// Le os controles da aba e devolve as opcoes do lote. Chamado na thread da interface,
    /// antes de o lote comecar — e o unico lugar onde os controles podem ser lidos.
    /// </summary>
    protected abstract TOptions ReadOptions();

    /// <summary>
    /// Executa o lote. Roda em thread de fundo: use so <paramref name="options"/> e
    /// <paramref name="context"/>, nunca os controles. Devolve a primeira linha do
    /// relatorio (o "titulo" do resultado).
    /// </summary>
    protected abstract string RunBatch(TOptions options, BatchContext context);

    /// <summary>
    /// Chamado ao entrar e sair do estado "ocupado". As abas sobrescrevem para restaurar
    /// regras proprias de habilitacao (por exemplo, qualidade so no JPEG).
    /// </summary>
    protected virtual void OnBusyChanged(bool busy)
    {
    }

    /// <summary>
    /// Ultima palavra sobre a pasta de saida. O padrao respeita o que o usuario escolheu —
    /// vazio significa "ao lado de cada PDF"; abas que precisam de um destino unico
    /// sobrescrevem para escolher um.
    /// </summary>
    protected virtual string ResolveOutputDirectory(string chosen, string[] pdfs) => chosen;

    /// <summary>Tudo o que o lote precisa saber, ja validado.</summary>
    /// <param name="OutputDirectory">Pasta escolhida; vazio significa "ao lado de cada PDF".</param>
    protected sealed record BatchContext(
        string[] Pdfs,
        string OutputDirectory,
        BatchReport Report,
        IProgress<BatchProgress> Progress,
        CancellationToken Token);

    /// <summary>
    /// Monta a aba. Chamado pelo construtor da aba concreta, depois que os controles dela
    /// existem — <see cref="BuildOptions"/> depende disso.
    /// </summary>
    protected void Build()
    {
        var root = Widgets.RootLayout();
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));                // 0: fila
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, OptionsHeight));    // 1: opcoes
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));               // 2: pasta de saida
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));               // 3: acao + progresso
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));               // 4: situacao
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));                // 5: resultado

        optionsGroup = BuildOptions();

        status.Text = "Pronto.";
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;

        root.Controls.Add(Queue, 0, 0);
        root.Controls.Add(optionsGroup, 0, 1);
        root.Controls.Add(Widgets.OutputRow(outputFolder, browse, "(vazio = ao lado do PDF)"), 0, 2);
        root.Controls.Add(Widgets.ActionRow(action, ActionLabel, cancel, progress), 0, 3);
        root.Controls.Add(status, 0, 4);
        root.Controls.Add(Widgets.LogBox(log), 0, 5);

        Controls.Add(root);

        Queue.FilesAdded += (_, _) =>
        {
            if (outputFolder.Text.Trim().Length == 0)
            {
                outputFolder.Text = Queue.FirstFolder;
            }
        };

        browse.Click += (_, _) => BrowseOutput();
        action.Click += async (_, _) => await RunAsync();
        cancel.Click += (_, _) => cancellation?.Cancel();
        Queue.EnableDropOn(this);
    }

    private void BrowseOutput()
    {
        using var dialog = new FolderBrowserDialog { Description = "Escolha a pasta de saída" };
        string current = outputFolder.Text.Trim();
        if (current.Length > 0 && Directory.Exists(current))
        {
            dialog.SelectedPath = current;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            outputFolder.Text = dialog.SelectedPath;
        }
    }

    private async Task RunAsync()
    {
        string[] pdfs = Queue.Files;
        if (pdfs.Length == 0)
        {
            MessageBox.Show(this, "Adicione ao menos um arquivo PDF.", "Nada a converter",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string outputDirectory = ResolveOutputDirectory(outputFolder.Text.Trim(), pdfs);
        if (outputDirectory.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não foi possível criar/acessar a pasta de saída:\n{ex.Message}",
                    "Pasta inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // Ultima leitura de controles antes do lote: daqui para baixo, nada mais toca na
        // interface fora da thread dela.
        TOptions options = ReadOptions();

        var report = new BatchReport();
        var reporter = new Progress<BatchProgress>(p =>
        {
            progress.Maximum = Math.Max(1, p.Total);
            progress.Value = Math.Clamp(p.Done, 0, progress.Maximum);
            status.Text = p.Message;
        });

        cancellation = new CancellationTokenSource();
        SetBusy(true);
        log.Clear();
        progress.Value = 0;
        status.Text = "Iniciando…";

        string heading;
        try
        {
            var context = new BatchContext(pdfs, outputDirectory, report, reporter, cancellation.Token);
            heading = await Task.Run(() => RunBatch(options, context), cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            heading = "Cancelado — o que já havia sido gravado continua no lugar.";
        }
        catch (Exception ex)
        {
            report.AddError($"Falha geral: {ex.Message}");
            heading = "Interrompido por falha.";
        }
        finally
        {
            SetBusy(false);
            cancellation.Dispose();
            cancellation = null;
        }

        bool alongside = outputDirectory.Length == 0;
        log.Text = report.Compose(
            alongside ? heading + " (ao lado de cada PDF)" : heading,
            alongside ? null : outputDirectory);
        status.Text = $"Concluído: {report.Produced} arquivo(s), {report.Errors.Count} erro(s).";

        if (report.Errors.Count > 0)
        {
            MessageBox.Show(this, $"{report.Errors.Count} arquivo(s) com erro. Veja o painel Resultado.",
                "Concluído com erros", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy)
    {
        Queue.Enabled = !busy;
        action.Enabled = !busy;
        browse.Enabled = !busy;
        outputFolder.Enabled = !busy;
        cancel.Enabled = busy;

        if (optionsGroup is not null)
        {
            optionsGroup.Enabled = !busy;
        }

        UseWaitCursor = busy;
        OnBusyChanged(busy);
    }
}
