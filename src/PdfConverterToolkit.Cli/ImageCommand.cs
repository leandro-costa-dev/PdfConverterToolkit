using PdfConverterToolkit.Core;
using PdfConverterToolkit.Imaging;

namespace PdfConverterToolkit.Cli;

/// <summary>Comando <c>imagem</c>: uma imagem por pagina do PDF.</summary>
internal static class ImageCommand
{
    public static int Run(string[] args, int start)
    {
        var reader = new ArgReader(args, start);
        var options = new ImageExportOptions();
        string? folder = null;
        bool recursive = false;

        while (reader.Next(out string arg))
        {
            switch (arg)
            {
                case "-d" or "--pasta":
                    folder = reader.Value(arg);
                    break;

                case "-r" or "--recursivo":
                    recursive = true;
                    break;

                case "--formato":
                    string wanted = reader.Value(arg);
                    if (!ImageFormats.TryParse(wanted, out ImageOutputFormat format))
                    {
                        throw new ArgumentException($"--formato aceita: {ImageFormats.NamesForHelp}.");
                    }

                    options.Format = format;
                    break;

                case "--largura":
                    options.Width = reader.Number(arg, 0, 20000);
                    break;

                case "--altura":
                    options.Height = reader.Number(arg, 0, 20000);
                    break;

                case "--sem-proporcao":
                    options.KeepAspectRatio = false;
                    break;

                case "--dpi":
                    options.Dpi = reader.Number(arg, 30, 1200);
                    break;

                case "--qualidade":
                    options.Quality = reader.Number(arg, 1, 100);
                    break;

                case "--sem-anotacoes":
                    options.IncludeAnnotations = false;
                    break;

                case "--senha":
                    options.Password = reader.Value(arg);
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

        // Sem --pasta, as imagens ficam ao lado do primeiro PDF: um lote de imagens
        // espalhado pelas pastas de origem seria pior de achar depois.
        string outputDirectory = folder is { Length: > 0 }
            ? folder
            : Path.GetDirectoryName(Path.GetFullPath(pdfs[0]))!;
        Directory.CreateDirectory(outputDirectory);

        var report = new BatchReport();
        int images = PdfImageExporter.ExportBatch(pdfs, outputDirectory, options, report, Program.Progress());

        return Program.Finish(report, $"{images} imagem(ns) gerada(s) em {outputDirectory}");
    }
}
