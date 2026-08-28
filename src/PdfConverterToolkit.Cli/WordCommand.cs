using PdfConverterToolkit.Core;
using PdfConverterToolkit.Docx;

namespace PdfConverterToolkit.Cli;

/// <summary>Comando <c>word</c>: PDF -&gt; .docx, nos quatro modos.</summary>
internal static class WordCommand
{
    public static int Run(string[] args, int start)
    {
        var reader = new ArgReader(args, start);
        var options = new WordOptions();
        string? output = null;
        string? folder = null;
        bool overwrite = false;
        bool recursive = false;

        while (reader.Next(out string arg))
        {
            switch (arg)
            {
                case "-o" or "--saida":
                    output = reader.Value(arg);
                    break;

                case "-d" or "--pasta":
                    folder = reader.Value(arg);
                    break;

                case "-r" or "--recursivo":
                    recursive = true;
                    break;

                case "--modo":
                    options.Mode = ParseMode(reader.Value(arg));
                    break;

                case "--senha":
                    options.Password = reader.Value(arg);
                    break;

                case "--dpi":
                    options.Dpi = reader.Number(arg, 36, 600);
                    break;

                case "--qualidade":
                    options.Quality = reader.Number(arg, 1, 100);
                    break;

                case "--sem-tabelas":
                    options.Conversion.DetectTables = false;
                    options.Conversion.DetectBorderlessTables = false;
                    break;

                case "--sem-tabelas-sem-borda":
                    options.Conversion.DetectBorderlessTables = false;
                    break;

                case "--sem-imagens":
                    options.Conversion.ExtractImages = false;
                    break;

                case "--sem-titulos":
                    options.Conversion.DetectHeadings = false;
                    break;

                case "--sem-links":
                    options.Conversion.KeepHyperlinks = false;
                    break;

                case "--sem-raster":
                    options.Conversion.RasterizeTextlessPages = false;
                    break;

                case "--limpar-cabecalhos":
                    options.Conversion.RemoveRepeatedHeadersFooters = true;
                    break;

                case "--linhas-fixas":
                    options.Conversion.ReflowText = false;
                    break;

                case "--sobrescrever":
                    overwrite = true;
                    break;

                default:
                    reader.AddInput(arg);
                    break;
            }
        }

        var pdfs = Program.Resolve(reader, recursive);
        if (pdfs.Count == 0)
        {
            return 3;
        }

        if (folder is { Length: > 0 })
        {
            Directory.CreateDirectory(folder);
        }

        var report = new BatchReport();

        if (output is { Length: > 0 })
        {
            if (pdfs.Count > 1)
            {
                throw new ArgumentException("--saida vale para um PDF de entrada; para vários use --pasta.");
            }

            ConvertOne(pdfs[0], output, options, report, overwrite);
        }
        else
        {
            WordConverter.ConvertBatch(pdfs, folder, options, report, Program.Progress(), overwrite);
        }

        return Program.Finish(report, $"{report.Produced} arquivo(s) .docx gerado(s).");
    }

    /// <summary>Caminho com nome de saida escolhido a dedo (<c>--saida</c>).</summary>
    private static void ConvertOne(string pdfPath, string docxPath, WordOptions options, BatchReport report, bool overwrite)
    {
        string name = Path.GetFileNameWithoutExtension(pdfPath);
        string target = overwrite ? docxPath : OutputPath.EnsureUnique(docxPath);
        var progress = Program.Progress();
        int pages = Math.Max(1, PdfPageCounter.Count(File.ReadAllBytes(pdfPath), options.Password));
        int steps = pages * options.StepsPerPage;

        try
        {
            var result = WordConverter.Convert(
                pdfPath,
                target,
                options,
                new Progress<ConversionProgress>(p => progress.Report(new BatchProgress(
                    p.Stage == "Gravando" ? pages + p.Page : p.Page,
                    steps,
                    $"{p.Stage} {name} — pagina {p.Page}/{p.TotalPages}…"))));

            report.Produced++;
            report.AddSummary(result.Summary(name));

            foreach (string warning in result.Warnings)
            {
                report.AddSummary($"{name}: aviso — {warning}");
            }
        }
        catch (MissingTextLayerException ex)
        {
            report.AddError(ex.Message);
        }
        catch (Exception ex)
        {
            report.AddError(name, ex);
        }
    }

    private static WordMode ParseMode(string text) => text.Trim().ToLowerInvariant() switch
    {
        "fiel" or "layout" => WordMode.Faithful,
        "texto" => WordMode.Text,
        "imagem" => WordMode.Image,
        "imagem-texto" or "hibrido" or "fidelidade" => WordMode.Hybrid,
        _ => throw new ArgumentException("--modo aceita: fiel, texto, imagem ou imagem-texto."),
    };
}
