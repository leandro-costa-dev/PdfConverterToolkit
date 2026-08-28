using PdfConverterToolkit.Core;
using PdfConverterToolkit.Imaging;

namespace PdfConverterToolkit.Cli;

/// <summary>Comando <c>compactar</c>: rasteriza as paginas e remonta um PDF menor.</summary>
internal static class CompressCommand
{
    public static int Run(string[] args, int start)
    {
        var reader = new ArgReader(args, start);
        var options = new CompressionOptions();
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

                case "--dpi":
                    options.Dpi = reader.Number(arg, 50, 600);
                    break;

                case "--qualidade":
                    options.Quality = reader.Number(arg, 1, 100);
                    break;

                case "--manter-maiores":
                    options.SkipIfLarger = false;
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

        if (folder is { Length: > 0 })
        {
            Directory.CreateDirectory(folder);
        }

        var report = new BatchReport();
        int produced = PdfCompressor.CompressBatch(pdfs, folder, options, report, Program.Progress());

        return Program.Finish(report, $"{produced} PDF(s) compactado(s).");
    }
}
