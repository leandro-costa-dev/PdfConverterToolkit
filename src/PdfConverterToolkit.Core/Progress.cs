namespace PdfConverterToolkit.Core;

/// <summary>
/// Andamento dentro de um arquivo: pagina <paramref name="Page"/> de
/// <paramref name="TotalPages"/>, na etapa <paramref name="Stage"/>.
/// </summary>
public readonly record struct ConversionProgress(int Page, int TotalPages, string Stage);

/// <summary>
/// Andamento de um lote: <paramref name="Done"/> de <paramref name="Total"/> paginas
/// concluidas, com a mensagem que descreve o que esta acontecendo.
/// </summary>
public readonly record struct BatchProgress(int Done, int Total, string Message);
