namespace PdfConverterToolkit.Docx.Writer;

/// <summary>
/// Os tres modos rapidos de PDF -&gt; .docx, que nao tentam reconstruir o layout:
/// so texto, so imagem da pagina, ou os dois empilhados. Para texto editavel com a
/// formatacao original use o motor fiel (<see cref="PdfDocxConverter"/>).
///
/// Cada modo e um arquivo com um PDF so — o caso de uma unica chamada de <c>Append</c> em
/// <see cref="SimpleDocxSession"/>, que e onde a escrita mora de verdade. Varios PDFs num
/// unico .docx passam pela sessao direto (<see cref="WordConverter.ConvertMerged"/>).
/// </summary>
public static class SimpleDocxWriter
{
    /// <summary>
    /// Modo TEXTO: extrai o texto do PDF (PdfPig) para um .docx editavel.
    /// Retorna a quantidade de caracteres (nao-espaco) extraidos — 0 indica PDF sem
    /// camada de texto (provavelmente digitalizado).
    /// </summary>
    public static int FromText(
        byte[] pdfBytes,
        string outPath,
        string? password = null,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        using var session = new SimpleDocxSession(outPath);
        int chars = session.AppendText(pdfBytes, password, onPageDone, cancellationToken);
        session.Save();
        return chars;
    }

    /// <summary>
    /// Modo IMAGEM: rasteriza cada pagina (PDFium) e insere como imagem no .docx (1 por pagina).
    /// Cada pagina vira uma folha A4 (retrato ou paisagem conforme a orientacao do original),
    /// com a imagem ajustada para caber na area util. Preserva anotacoes/assinaturas.
    /// </summary>
    public static void FromImages(
        byte[] pdfBytes,
        string outPath,
        int dpi,
        int quality,
        string? password = null,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        using var session = new SimpleDocxSession(outPath);
        session.AppendImages(pdfBytes, dpi, quality, password, onPageDone, cancellationToken);
        session.Save();
    }

    /// <summary>
    /// Modo HIBRIDO: por pagina, insere a imagem fiel seguida do texto real editavel.
    /// Fidelidade visual total (a imagem) + texto selecionavel/pesquisavel logo abaixo.
    /// Cada pagina vira uma folha A4 na orientacao do original. O .docx nao suporta camada
    /// de texto invisivel sobreposta, entao imagem e texto ficam empilhados por pagina.
    /// </summary>
    public static void FromHybrid(
        byte[] pdfBytes,
        string outPath,
        int dpi,
        int quality,
        string? password = null,
        Action<int>? onPageDone = null,
        CancellationToken cancellationToken = default)
    {
        using var session = new SimpleDocxSession(outPath);
        session.AppendHybrid(pdfBytes, dpi, quality, password, onPageDone, cancellationToken);
        session.Save();
    }
}
