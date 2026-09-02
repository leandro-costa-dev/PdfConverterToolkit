using PdfConverterToolkit.Core;
using PdfConverterToolkit.Docx.Model;
using PdfConverterToolkit.Docx.Writer;

namespace PdfConverterToolkit.Docx;

/// <summary>Como o PDF vira .docx.</summary>
public enum WordMode
{
    /// <summary>
    /// Layout fiel: texto editavel com a formatacao original, tabelas de verdade, imagens
    /// no lugar e uma secao por pagina. E o modo completo (<see cref="PdfDocxConverter"/>).
    /// </summary>
    Faithful,

    /// <summary>Somente o texto corrido do PDF, sem layout. Nao funciona em PDF digitalizado.</summary>
    Text,

    /// <summary>Cada pagina vira uma imagem dentro do Word: identico ao original, sem texto editavel.</summary>
    Image,

    /// <summary>Por pagina: a imagem fiel e, abaixo, o texto editavel daquela pagina.</summary>
    Hybrid,
}

/// <summary>Ajustes da conversao para Word, incluindo os do modo fiel.</summary>
public sealed class WordOptions
{
    public WordMode Mode { get; set; } = WordMode.Faithful;

    /// <summary>Ajustes usados pelo modo fiel (tabelas, imagens, titulos, links…).</summary>
    public ConversionOptions Conversion { get; init; } = new();

    /// <summary>Resolucao das paginas rasterizadas (modos Imagem, Hibrido e fallback do fiel).</summary>
    public int Dpi
    {
        get => Conversion.RasterDpi;
        set => Conversion.RasterDpi = value;
    }

    /// <summary>Qualidade JPEG (1-100) das paginas rasterizadas.</summary>
    public int Quality
    {
        get => Conversion.RasterQuality;
        set => Conversion.RasterQuality = value;
    }

    /// <summary>Senha do PDF, quando protegido.</summary>
    public string? Password
    {
        get => Conversion.Password;
        set => Conversion.Password = value;
    }

    /// <summary>
    /// Quantas etapas de progresso cada pagina custa: o modo fiel passa duas vezes por
    /// pagina (analisar e gravar), os demais uma so.
    /// </summary>
    public int StepsPerPage => Mode == WordMode.Faithful ? 2 : 1;
}

/// <summary>O que saiu da conversao de um arquivo.</summary>
public sealed class WordFileResult
{
    public required string OutputPath { get; init; }

    public required WordMode Mode { get; init; }

    public int PageCount { get; set; }

    public int TextCharacterCount { get; set; }

    /// <summary>Detalhes do motor fiel; null nos modos rapidos.</summary>
    public ConversionResult? Details { get; set; }

    public List<string> Warnings { get; } = [];

    /// <summary>Uma linha descrevendo o resultado, para o relatorio do lote.</summary>
    public string Summary(string name) => Mode switch
    {
        WordMode.Faithful =>
            $"{name}: {PageCount} pagina(s), {Details?.ParagraphCount ?? 0} paragrafo(s), " +
            $"{Details?.TableCount ?? 0} tabela(s), {Details?.ImageCount ?? 0} imagem(ns), " +
            $"{TextCharacterCount:N0} caractere(s) → {FileSize.OfFile(OutputPath)}.",
        WordMode.Text => $"{name}: {TextCharacterCount:N0} caractere(s) → .docx editavel.",
        WordMode.Image => $"{name}: {PageCount} pagina(s) → {FileSize.OfFile(OutputPath)} (imagem).",
        _ => $"{name}: {PageCount} pagina(s) → {FileSize.OfFile(OutputPath)} (imagem + texto).",
    };
}

/// <summary>
/// O PDF nao tem camada de texto, entao o modo Texto nao tem o que extrair. E o caso
/// tipico de documento digitalizado sem OCR.
/// </summary>
public sealed class MissingTextLayerException(string fileName)
    : Exception($"{fileName}: sem camada de texto (PDF digitalizado?) — use \"Pagina como imagem\" ou \"Imagem + texto\".");

/// <summary>
/// Ponto de entrada unico para PDF -&gt; Word: escolhe entre o motor fiel e os modos
/// rapidos, e roda o lote reportando andamento e acumulando o relatorio.
/// </summary>
public static class WordConverter
{
    /// <summary>Converte um PDF em .docx no modo pedido.</summary>
    /// <param name="pdfPath">PDF de entrada.</param>
    /// <param name="docxPath">Arquivo .docx a gravar (sobrescreve se existir).</param>
    /// <param name="options">Modo e ajustes.</param>
    /// <param name="progress">Andamento pagina a pagina.</param>
    /// <param name="cancellationToken">Cancela entre paginas.</param>
    /// <exception cref="MissingTextLayerException">
    /// Modo Texto num PDF sem texto extraivel; o arquivo vazio nao e deixado no disco.
    /// </exception>
    public static WordFileResult Convert(
        string pdfPath,
        string docxPath,
        WordOptions options,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);
        ArgumentNullException.ThrowIfNull(options);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(docxPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (options.Mode == WordMode.Faithful)
        {
            var conversion = PdfDocxConverter.Convert(pdfPath, docxPath, options.Conversion, progress, cancellationToken);
            var faithful = new WordFileResult
            {
                OutputPath = docxPath,
                Mode = WordMode.Faithful,
                PageCount = conversion.PageCount,
                TextCharacterCount = conversion.TextCharacterCount,
                Details = conversion,
            };
            faithful.Warnings.AddRange(conversion.Warnings);
            return faithful;
        }

        byte[] bytes = File.ReadAllBytes(pdfPath);
        int pages = PdfPageCounter.Count(bytes, options.Password);
        string stage = options.Mode == WordMode.Text ? "Extraindo" : "Convertendo";
        void OnPage(int page) => progress?.Report(new ConversionProgress(page, pages, stage));

        var result = new WordFileResult
        {
            OutputPath = docxPath,
            Mode = options.Mode,
            PageCount = pages,
        };

        try
        {
            switch (options.Mode)
            {
                case WordMode.Image:
                    SimpleDocxWriter.FromImages(
                        bytes, docxPath, options.Dpi, options.Quality, options.Password, OnPage, cancellationToken);
                    break;

                case WordMode.Hybrid:
                    SimpleDocxWriter.FromHybrid(
                        bytes, docxPath, options.Dpi, options.Quality, options.Password, OnPage, cancellationToken);
                    break;

                default:
                    result.TextCharacterCount = SimpleDocxWriter.FromText(
                        bytes, docxPath, options.Password, OnPage, cancellationToken);

                    if (result.TextCharacterCount == 0)
                    {
                        Delete(docxPath);
                        throw new MissingTextLayerException(Path.GetFileName(pdfPath));
                    }

                    break;
            }
        }
        catch (Exception)
        {
            // Um .docx parcial nao serve para nada: sai do disco antes de propagar o erro.
            Delete(docxPath);
            throw;
        }

        return result;
    }

    /// <summary>
    /// Converte varios PDFs. Uma falha em um arquivo nao interrompe os demais: vai para o
    /// relatorio e o lote segue.
    /// </summary>
    /// <param name="outputDirectory">Pasta de saida; vazio grava ao lado de cada PDF.</param>
    /// <param name="overwrite">Regrava por cima; sem isso o existente ganha sufixo " (1)".</param>
    /// <returns>Quantos arquivos .docx foram gravados.</returns>
    public static int ConvertBatch(
        IReadOnlyList<string> pdfPaths,
        string? outputDirectory,
        WordOptions options,
        BatchReport report,
        IProgress<BatchProgress>? progress = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfPaths);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);

        int[] pageCounts = PdfPageCounter.CountAll(pdfPaths, report, out int totalPages, options.Password);
        int steps = options.StepsPerPage;
        int totalSteps = Math.Max(1, totalPages * steps);
        int done = 0;

        for (int i = 0; i < pdfPaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string pdfPath = pdfPaths[i];
            string name = Path.GetFileNameWithoutExtension(pdfPath);
            int pages = pageCounts[i];
            if (pages <= 0)
            {
                continue;
            }

            string folder = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.GetDirectoryName(Path.GetFullPath(pdfPath))!
                : outputDirectory;
            string docxPath = Path.Combine(folder, name + ".docx");
            if (!overwrite)
            {
                docxPath = OutputPath.EnsureUnique(docxPath);
            }

            int fileBase = done;
            var fileProgress = new Progress<ConversionProgress>(p =>
            {
                // No modo fiel a pagina passa duas vezes: analisar e gravar.
                int step = p.Stage == "Gravando" ? pages + p.Page : p.Page;
                progress?.Report(new BatchProgress(
                    fileBase + Math.Min(step, pages * steps),
                    totalSteps,
                    $"{p.Stage} {name} — pagina {p.Page}/{p.TotalPages}…"));
            });

            try
            {
                var result = Convert(pdfPath, docxPath, options, fileProgress, cancellationToken);
                report.Produced++;
                report.AddSummary(result.Summary(name));

                foreach (string warning in result.Warnings)
                {
                    report.AddSummary($"{name}: aviso — {warning}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MissingTextLayerException ex)
            {
                report.AddError(ex.Message);
            }
            catch (Exception ex)
            {
                report.AddError(name, ex);
            }

            done += pages * steps;
        }

        progress?.Report(new BatchProgress(totalSteps, totalSteps, "Finalizando…"));
        return report.Produced;
    }

    /// <summary>Nome sugerido para o .docx unico: o nome do primeiro PDF + "_unificado".</summary>
    public static string SuggestMergedName(string firstPdfPath)
        => Path.GetFileNameWithoutExtension(firstPdfPath) + "_unificado.docx";

    /// <summary>
    /// Converte varios PDFs num unico .docx, na ordem da lista. Vale nos modos rapidos
    /// (Texto, Imagem e Imagem + texto); o modo fiel tem motor proprio e nao e suportado.
    /// Uma falha em um arquivo nao interrompe os demais: vai para o relatorio e o lote segue.
    /// </summary>
    /// <param name="docxPath">Arquivo unico a gravar.</param>
    /// <param name="overwrite">Regrava por cima; sem isso o existente ganha sufixo " (1)".</param>
    /// <returns>Quantos arquivos .docx foram gravados (0 ou 1).</returns>
    /// <exception cref="ArgumentException">Modo fiel, que nao sabe acrescentar a um documento aberto.</exception>
    /// <exception cref="OperationCanceledException">
    /// Cancelado no meio; o .docx com os PDFs ja acrescentados fica gravado.
    /// </exception>
    public static int ConvertMerged(
        IReadOnlyList<string> pdfPaths,
        string docxPath,
        WordOptions options,
        BatchReport report,
        IProgress<BatchProgress>? progress = null,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(docxPath);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);

        if (options.Mode == WordMode.Faithful)
        {
            throw new ArgumentException(
                "O arquivo unico nao vale no modo Layout fiel; use Texto, Pagina como imagem ou Imagem + texto.");
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(docxPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string target = overwrite ? docxPath : OutputPath.EnsureUnique(docxPath);
        string mergedName = Path.GetFileName(target);

        int[] pageCounts = PdfPageCounter.CountAll(pdfPaths, report, out int totalPages, options.Password);
        int totalSteps = Math.Max(1, totalPages);
        string stage = options.Mode == WordMode.Text ? "Extraindo" : "Convertendo";

        int merged = 0;
        int done = 0;
        bool saved = false;
        bool cancelled = false;

        try
        {
            using var session = new SimpleDocxSession(target);

            for (int i = 0; i < pdfPaths.Count; i++)
            {
                string pdfPath = pdfPaths[i];
                string name = Path.GetFileNameWithoutExtension(pdfPath);
                int pages = pageCounts[i];
                if (pages <= 0)
                {
                    continue;
                }

                int fileBase = done;
                void OnPage(int page) => progress?.Report(new BatchProgress(
                    fileBase + Math.Min(page, pages),
                    totalSteps,
                    $"{stage} {name} — pagina {page}/{pages}…"));

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Append(session, pdfPath, options, OnPage, cancellationToken);
                    merged++;
                    report.AddSummary($"{name}: {pages} pagina(s) acrescentada(s).");
                }
                catch (OperationCanceledException)
                {
                    // O que ja foi acrescentado e valido: para o lote e grava o que existe.
                    cancelled = true;
                    break;
                }
                catch (MissingTextLayerException ex)
                {
                    report.AddError(ex.Message);
                }
                catch (Exception ex)
                {
                    report.AddError(name, ex);
                }

                done += pages;
            }

            if (!session.IsEmpty)
            {
                session.Save();
                saved = true;
            }
        }
        catch (Exception)
        {
            // Um .docx aberto e nunca gravado nao serve para nada: sai do disco antes de propagar.
            Delete(target);
            throw;
        }

        if (!saved)
        {
            Delete(target);
            report.AddError($"{mergedName}: nenhum PDF pôde ser acrescentado — arquivo único não gerado.");
            return report.Produced;
        }

        report.Produced++;
        report.AddSummary($"{mergedName}: {merged} PDF(s) unificado(s) → {FileSize.OfFile(target)}.");
        progress?.Report(new BatchProgress(totalSteps, totalSteps, "Finalizando…"));

        if (cancelled)
        {
            report.AddSummary($"{mergedName}: aviso — cancelado, o arquivo tem so os {merged} primeiro(s) PDF(s).");
            throw new OperationCanceledException(cancellationToken);
        }

        return report.Produced;
    }

    /// <summary>Acrescenta um PDF a sessao, no modo pedido.</summary>
    /// <exception cref="MissingTextLayerException">Modo Texto num PDF sem texto extraivel.</exception>
    private static void Append(
        SimpleDocxSession session,
        string pdfPath,
        WordOptions options,
        Action<int> onPage,
        CancellationToken cancellationToken)
    {
        byte[] bytes = File.ReadAllBytes(pdfPath);

        switch (options.Mode)
        {
            case WordMode.Image:
                session.AppendImages(
                    bytes, options.Dpi, options.Quality, options.Password, onPage, cancellationToken);
                break;

            case WordMode.Hybrid:
                session.AppendHybrid(
                    bytes, options.Dpi, options.Quality, options.Password, onPage, cancellationToken);
                break;

            default:
                if (session.AppendText(bytes, options.Password, onPage, cancellationToken) == 0)
                {
                    throw new MissingTextLayerException(Path.GetFileName(pdfPath));
                }

                break;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // O arquivo pode estar aberto em outro programa: nao ha o que fazer aqui.
        }
    }
}
