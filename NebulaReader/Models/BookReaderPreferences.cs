using SQLite;

namespace NebulaReader.Models;

/// <summary>Configuración del visor personalizada para un documento.</summary>
[Table("BookReaderPreferences")]
public sealed class BookReaderPreferences
{
    [PrimaryKey]
    public string BookId { get; set; } = string.Empty;
    public ReaderLayoutMode LayoutMode { get; set; }
    public PageFitMode PageFitMode { get; set; }
    public bool RightToLeftManga { get; set; }
    public ReaderVisualFilter VisualFilter { get; set; }
}
