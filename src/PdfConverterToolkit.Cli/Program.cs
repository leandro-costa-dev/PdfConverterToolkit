using PdfConverterToolkit.Core;

namespace PdfConverterToolkit.Cli;

/// <summary>
/// Linha de comando do PdfConverterToolkit: <c>word</c>, <c>imagem</c> e <c>compactar</c>.
/// Sem comando explicito, assume <c>word</c> — e o que a maioria das chamadas quer.
/// </summary>
internal static class Program
{
    /// <summary>0 = tudo certo · 1 = sem argumentos · 2 = erro de uso · 3 = nenhum PDF · 4 = alguma falha.</summary>
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        if (args[0] is "-h" or "--help" or "/?" or "ajuda")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        int start = 1;

        if (command is not ("word" or "docx" or "imagem" or "imagens" or "compactar"))
        {
            // Compatibilidade com o pdf2docx: "pdfconv arquivo.pdf" converte para Word.
            command = "word";
            start = 0;
        }

        try
        {
            return command switch
            {
                "imagem" or "imagens" => ImageCommand.Run(args, start),
                "compactar" => CompressCommand.Run(args, start),
                _ => WordCommand.Run(args, start),
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Erro: {ex.Message}");
            Console.Error.WriteLine("Use --help para ver as opcoes.");
            return 2;
        }
    }

    /// <summary>Expande as entradas em PDFs; lista vazia significa "nada a fazer".</summary>
    public static List<string> Resolve(ArgReader parsed, bool recursive)
    {
        parsed.RequireInputs();

        var pdfs = PdfInputs.Expand(
            parsed.Inputs,
            recursive,
            missing => Console.Error.WriteLine($"Aviso: nao encontrei {missing}"));

        if (pdfs.Count == 0)
        {
            Console.Error.WriteLine("Nenhum PDF encontrado com os caminhos informados.");
        }

        return pdfs;
    }

    /// <summary>Andamento numa unica linha, reescrita a cada pagina.</summary>
    public static IProgress<BatchProgress> Progress() => new Progress<BatchProgress>(p =>
        Console.Write($"\r  {p.Message}".PadRight(78)[..78]));

    /// <summary>Imprime o relatorio do lote e devolve o codigo de saida.</summary>
    public static int Finish(BatchReport report, string heading)
    {
        Console.Write('\r');
        Console.WriteLine(heading.PadRight(78));

        foreach (string line in report.Summaries)
        {
            Console.WriteLine($"  {line}");
        }

        if (report.Errors.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"{report.Errors.Count} erro(s):");
            foreach (string line in report.Errors)
            {
                Console.Error.WriteLine($"  {line}");
            }

            return 4;
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            pdfconv — conversor de PDF: para Word (.docx), para imagens, ou compactado.

            Uso:
              pdfconv <comando> <arquivo.pdf | pasta | curinga> [mais entradas...] [opcoes]

            Comandos:
              word        PDF -> Word (.docx). E o comando assumido quando nenhum e informado.
              imagem      PDF -> uma imagem por pagina (JPG, PNG, BMP, GIF, TIFF).
              compactar   Reduz o tamanho do PDF rasterizando as paginas.

            Opcoes de todos os comandos:
              -d, --pasta <pasta>          Pasta de saida (padrao: a mesma do PDF).
              -r, --recursivo              Nas pastas informadas, incluir subpastas.
                  --senha <senha>          Senha do PDF protegido.
                  --dpi <n>                Resolucao das paginas rasterizadas.
                  --qualidade <1-100>      Qualidade JPEG.
                  --sobrescrever           Regravar por cima de um arquivo existente.
              -h, --help                   Mostra esta ajuda.

            Opcoes de "word":
              -o, --saida <arquivo.docx>   Nome do arquivo de saida (so com um PDF, ou com
                                           --arquivo-unico).
                  --modo <modo>            fiel (padrao) | texto | imagem | imagem-texto.
                  --arquivo-unico          Juntar todos os PDFs num unico .docx, na ordem
                                           informada. Nao vale no modo fiel.
                  --sem-tabelas            Nao reconstruir tabelas.
                  --sem-tabelas-sem-borda  Reconstruir apenas tabelas com linhas visiveis.
                  --sem-imagens            Nao extrair imagens.
                  --sem-titulos            Nao marcar titulos (Titulo 1..3).
                  --sem-links              Nao preservar hiperlinks.
                  --sem-raster             Deixar em branco as paginas sem camada de texto.
                  --limpar-cabecalhos      Remover cabecalhos/rodapes repetidos.
                  --linhas-fixas           Manter as quebras de linha do PDF.

              Modos: "fiel" reconstroi layout, tabelas e imagens (padrao). "texto" extrai so
              o texto corrido. "imagem" poe cada pagina como figura. "imagem-texto" empilha
              a figura e o texto da pagina.

            Opcoes de "imagem":
                  --formato <fmt>          jpg | png | bmp | gif | tif (padrao jpg).
                  --largura <px>           Largura em pixels (0 = pelo DPI).
                  --altura <px>            Altura em pixels (0 = pelo DPI).
                  --sem-proporcao          Nao preservar a proporcao ao redimensionar.
                  --sem-anotacoes          Nao renderizar anotacoes/assinaturas.

            Opcoes de "compactar":
                  --manter-maiores         Gravar mesmo quando o resultado nao reduzir.

            Exemplos:
              pdfconv edital.pdf
              pdfconv word "C:\\Editais\\*.pdf" -d "C:\\Editais\\Word" --limpar-cabecalhos
              pdfconv word contrato.pdf --modo imagem-texto --dpi 200
              pdfconv word "C:\\Anexos" --modo imagem --arquivo-unico -o dossie.docx
              pdfconv imagem contrato.pdf --formato png --largura 1600
              pdfconv compactar "C:\\Digitalizados" -r --dpi 100 --qualidade 50
            """);
    }
}
