namespace PdfConverterToolkit.Cli;

/// <summary>
/// Percorre os argumentos separando opcoes (<c>--isso</c>) de entradas (arquivos, pastas,
/// curingas). Cada comando le as opcoes que conhece; o que sobrar com <c>-</c> na frente e
/// erro de uso.
/// </summary>
internal sealed class ArgReader(string[] args, int start)
{
    private int index = start;

    /// <summary>Arquivos, pastas e curingas informados, na ordem.</summary>
    public List<string> Inputs { get; } = [];

    /// <summary>Avanca para o proximo argumento; false quando terminou.</summary>
    public bool Next(out string arg)
    {
        if (index >= args.Length)
        {
            arg = string.Empty;
            return false;
        }

        arg = args[index++];
        return true;
    }

    /// <summary>Le o valor que acompanha a opcao atual.</summary>
    public string Value(string option)
    {
        if (index >= args.Length)
        {
            throw new ArgumentException($"a opcao {option} exige um valor.");
        }

        return args[index++];
    }

    /// <summary>Le um numero dentro da faixa aceita pela opcao.</summary>
    public int Number(string option, int min, int max)
    {
        string text = Value(option);
        if (!int.TryParse(text, out int number) || number < min || number > max)
        {
            throw new ArgumentException($"a opcao {option} espera um numero entre {min} e {max}.");
        }

        return number;
    }

    /// <summary>Registra o argumento como entrada, ou recusa se parecer uma opcao.</summary>
    public void AddInput(string arg)
    {
        if (arg.StartsWith('-'))
        {
            throw new ArgumentException($"opcao desconhecida: {arg}");
        }

        Inputs.Add(arg);
    }

    /// <summary>Falha quando nenhuma entrada foi informada.</summary>
    public void RequireInputs()
    {
        if (Inputs.Count == 0)
        {
            throw new ArgumentException("informe ao menos um PDF de entrada.");
        }
    }
}
