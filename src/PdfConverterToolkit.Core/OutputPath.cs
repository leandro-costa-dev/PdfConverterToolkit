namespace PdfConverterToolkit.Core;

/// <summary>Utilitarios para escolher o nome do arquivo de saida.</summary>
public static class OutputPath
{
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
