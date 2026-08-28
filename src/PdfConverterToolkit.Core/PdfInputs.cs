namespace PdfConverterToolkit.Core;

/// <summary>
/// Resolve o que o usuario informou — arquivos, pastas ou curingas (<c>*.pdf</c>) — na
/// lista concreta de PDFs a processar. Usado pela linha de comando, pelo dialogo de
/// selecao e pelo arrastar-e-soltar da janela.
/// </summary>
public static class PdfInputs
{
    private const string PdfExtension = ".pdf";

    /// <summary>True se o caminho aponta para um arquivo com extensao .pdf.</summary>
    public static bool IsPdf(string path)
        => path.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Expande cada entrada em PDFs existentes, em ordem alfabetica dentro de cada
    /// entrada e sem repetir o mesmo arquivo.
    /// </summary>
    /// <param name="inputs">Arquivos, pastas ou padroes com curinga.</param>
    /// <param name="recursive">Nas pastas, tambem procurar nas subpastas.</param>
    /// <param name="onMissing">Chamado com a entrada que nao resolveu para nada.</param>
    public static List<string> Expand(
        IEnumerable<string> inputs,
        bool recursive = false,
        Action<string>? onMissing = null)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string input in inputs)
        {
            int before = found.Count;

            foreach (string file in Resolve(input, recursive))
            {
                if (seen.Add(Path.GetFullPath(file)))
                {
                    found.Add(file);
                }
            }

            if (found.Count == before)
            {
                onMissing?.Invoke(input);
            }
        }

        return found;
    }

    private static List<string> Resolve(string input, bool recursive)
    {
        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        try
        {
            if (Directory.Exists(input))
            {
                return Directory.EnumerateFiles(input, "*" + PdfExtension, option).Order().ToList();
            }

            if (input.Contains('*') || input.Contains('?'))
            {
                string directory = Path.GetDirectoryName(input) is { Length: > 0 } d ? d : ".";
                return Directory.EnumerateFiles(directory, Path.GetFileName(input), option)
                    .Where(IsPdf)
                    .Order()
                    .ToList();
            }

            if (IsPdf(input) && File.Exists(input))
            {
                return [input];
            }
        }
        catch (Exception)
        {
            // Caminho inacessivel (permissao, unidade removida): trata como "nao encontrado".
        }

        return [];
    }
}
