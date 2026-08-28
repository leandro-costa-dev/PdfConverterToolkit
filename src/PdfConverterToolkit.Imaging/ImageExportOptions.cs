using GdiFormat = System.Drawing.Imaging.ImageFormat;

namespace PdfConverterToolkit.Imaging;

/// <summary>Formatos de imagem que a exportacao sabe gravar.</summary>
public enum ImageOutputFormat
{
    Jpeg,
    Png,
    Bmp,
    Gif,
    Tiff,
}

/// <summary>
/// A tabela de formatos — rotulo para a janela, nome para a linha de comando, extensao do
/// arquivo e o codificador usado. JPEG e PNG saem direto do Skia; BMP, GIF e TIFF passam
/// pelo GDI+, que e quem sabe grava-los.
/// </summary>
public static class ImageFormats
{
    /// <summary>Um formato de saida e como cada camada o identifica.</summary>
    /// <param name="Label">Rotulo exibido na janela.</param>
    /// <param name="Extension">Extensao gravada, com ponto.</param>
    /// <param name="Names">Nomes aceitos na linha de comando.</param>
    /// <param name="Gdi">Codificador do GDI+; null quando o Skia da conta.</param>
    public sealed record Descriptor(
        ImageOutputFormat Format,
        string Label,
        string Extension,
        string[] Names,
        GdiFormat? Gdi)
    {
        /// <summary>Somente o JPEG tem controle de qualidade.</summary>
        public bool SupportsQuality => Format == ImageOutputFormat.Jpeg;
    }

    private static readonly Descriptor[] Table =
    [
        new(ImageOutputFormat.Jpeg, "JPEG (.jpg)", ".jpg", ["jpg", "jpeg"], null),
        new(ImageOutputFormat.Png, "PNG (.png)", ".png", ["png"], null),
        new(ImageOutputFormat.Bmp, "BMP (.bmp)", ".bmp", ["bmp"], GdiFormat.Bmp),
        new(ImageOutputFormat.Gif, "GIF (.gif)", ".gif", ["gif"], GdiFormat.Gif),
        new(ImageOutputFormat.Tiff, "TIFF (.tif)", ".tif", ["tif", "tiff"], GdiFormat.Tiff),
    ];

    /// <summary>Todos os formatos, na ordem em que aparecem na janela.</summary>
    public static IReadOnlyList<Descriptor> All => Table;

    public static Descriptor Of(ImageOutputFormat format)
        => Table.First(d => d.Format == format);

    /// <summary>Interpreta o nome do formato vindo da linha de comando ("jpg", "png"…).</summary>
    public static bool TryParse(string text, out ImageOutputFormat format)
    {
        string wanted = text.Trim().TrimStart('.');
        foreach (var descriptor in Table)
        {
            if (descriptor.Names.Contains(wanted, StringComparer.OrdinalIgnoreCase))
            {
                format = descriptor.Format;
                return true;
            }
        }

        format = ImageOutputFormat.Jpeg;
        return false;
    }

    /// <summary>Os nomes aceitos, para a mensagem de ajuda.</summary>
    public static string NamesForHelp => string.Join(" | ", Table.Select(d => d.Names[0]));
}

/// <summary>Ajustes da exportacao de paginas de PDF em imagens.</summary>
public sealed class ImageExportOptions
{
    public ImageOutputFormat Format { get; set; } = ImageOutputFormat.Jpeg;

    /// <summary>Largura em pixels; 0 dimensiona pelo <see cref="Dpi"/>.</summary>
    public int Width { get; set; }

    /// <summary>Altura em pixels; 0 dimensiona pelo <see cref="Dpi"/>.</summary>
    public int Height { get; set; }

    /// <summary>Preserva a proporcao: basta informar largura ou altura.</summary>
    public bool KeepAspectRatio { get; set; } = true;

    /// <summary>Resolucao usada quando largura e altura ficam em 0.</summary>
    public int Dpi { get; set; } = 150;

    /// <summary>Qualidade JPEG (1-100); ignorada nos outros formatos.</summary>
    public int Quality { get; set; } = 90;

    /// <summary>
    /// Renderiza anotacoes e campos de formulario — assinaturas digitais, carimbos gov.br,
    /// tinta —, que o PDFium ignora por padrao.
    /// </summary>
    public bool IncludeAnnotations { get; set; } = true;

    /// <summary>Senha do PDF, quando protegido.</summary>
    public string? Password { get; set; }
}

/// <summary>Ajustes da compactacao de PDF.</summary>
public sealed class CompressionOptions
{
    /// <summary>Resolucao das paginas rasterizadas: menor DPI, arquivo menor.</summary>
    public int Dpi { get; set; } = 120;

    /// <summary>Qualidade JPEG (1-100) das paginas rasterizadas.</summary>
    public int Quality { get; set; } = 60;

    /// <summary>
    /// Descarta o resultado quando ele fica maior que o original — o que acontece em PDFs
    /// so de texto, onde rasterizar nao compensa.
    /// </summary>
    public bool SkipIfLarger { get; set; } = true;

    /// <summary>Senha do PDF, quando protegido.</summary>
    public string? Password { get; set; }
}
