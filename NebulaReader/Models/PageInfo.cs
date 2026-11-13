namespace NebulaReader.Models;

/// <summary>
/// Metadatos básicos de una página individual dentro de un cómic o PDF.
/// </summary>
public class PageInfo
{
    public int PageIndex { get; set; }
    public string EntryName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }

    public string DisplayLabel => $"Pág. {PageIndex + 1}";
}

