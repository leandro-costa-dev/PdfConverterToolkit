using System.Text;

namespace PdfConverterToolkit.Core;

/// <summary>
/// Acumula o que aconteceu num lote — arquivos gerados, resumo de cada um e erros —
/// e monta a mensagem final. Um erro num arquivo nunca interrompe os demais.
/// </summary>
public sealed class BatchReport
{
    private const int MaxSummariesShown = 20;
    private const int MaxErrorsShown = 15;

    /// <summary>Uma linha por arquivo processado com sucesso (ou pulado).</summary>
    public List<string> Summaries { get; } = [];

    /// <summary>Uma linha por falha, no formato "arquivo: motivo".</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Quantos arquivos de saida foram efetivamente gravados.</summary>
    public int Produced { get; set; }

    public void AddSummary(string line) => Summaries.Add(line);

    public void AddError(string line) => Errors.Add(line);

    public void AddError(string file, Exception ex) => Errors.Add($"{file}: {ex.Message}");

    /// <summary>Mensagem completa para exibir ao usuario ao fim do lote.</summary>
    public string Compose(string heading, string? outputDirectory = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading);

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            sb.AppendLine(outputDirectory);
        }

        AppendList(sb, Summaries, MaxSummariesShown, header: null);
        AppendList(sb, Errors, MaxErrorsShown, header: $"{Errors.Count} erro(s):");
        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, List<string> lines, int max, string? header)
    {
        if (lines.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        if (header is not null)
        {
            sb.AppendLine(header);
        }

        foreach (string line in lines.Take(max))
        {
            sb.AppendLine("• " + line);
        }

        if (lines.Count > max)
        {
            sb.AppendLine($"… e mais {lines.Count - max}.");
        }
    }
}
