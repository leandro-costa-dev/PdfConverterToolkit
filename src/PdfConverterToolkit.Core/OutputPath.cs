using System.Text;

namespace PdfConverterToolkit.Core;

/// <summary>Utilitarios para escolher o nome do arquivo de saida.</summary>
public static class OutputPath
{
    /// <summary>
    /// Transforma o que o usuario digitou num nome de arquivo utilizavel: sem pasta, sem
    /// caracteres proibidos (trocados por "_") e com a extensao pedida. Um caminho colado por
    /// engano perde a pasta e fica so o nome.
    /// </summary>
    /// <param name="extension">Extensao com ponto, por exemplo ".docx".</param>
    /// <returns>
    /// O nome saneado, ou string vazia quando nao sobrou nada aproveitavel — quem chama
    /// decide o padrao nesse caso.
    /// </returns>
    public static string SafeFileName(string? name, string extension)
    {
        string trimmed = (name ?? string.Empty).Trim();

        // Uma pasta digitada por engano nao deve mudar o destino: fica so o ultimo trecho.
        int separator = trimmed.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (separator >= 0)
        {
            trimmed = trimmed[(separator + 1)..];
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var safe = new StringBuilder(trimmed.Length);
        foreach (char c in trimmed)
        {
            safe.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string result = safe.ToString().Trim();
        if (result.Length == 0 || result.All(c => c == '.'))
        {
            return string.Empty;
        }

        return result.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? result : result + extension;
    }

    /// <summary>
    /// Devolve o proprio caminho se ele estiver livre; senao acrescenta " (1)", " (2)"…
    /// ate achar um nome que ainda nao exista.
    /// </summary>
    public static string EnsureUnique(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name} ({Guid.NewGuid():N}){extension}");
    }
}
