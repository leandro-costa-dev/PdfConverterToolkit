namespace PdfConverterToolkit.Docx.Writer;

/// <summary>
/// Conversoes entre a unidade do PDF (ponto tipografico) e as unidades do Office Open XML.
/// </summary>
internal static class Units
{
    /// <summary>1 pt = 20 twips (1/1440 pol).</summary>
    public static int ToTwips(double points) => (int)Math.Round(points * 20.0);

    /// <summary>1 pt = 12700 EMU (1/914400 pol).</summary>
    public static long ToEmu(double points) => (long)Math.Round(points * 12700.0);

    /// <summary>O Word grava o corpo da fonte em meios-pontos.</summary>
    public static string ToHalfPoints(double points) =>
        Math.Clamp((int)Math.Round(points * 2.0), 2, 3276).ToString();
}
