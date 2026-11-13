namespace NebulaReader.Models;

/// <summary>
/// Formatos de archivo compatibles con Nebula Reader.
/// </summary>
public enum BookFormat
{
    Unknown = 0,
    Cbz = 1,
    Cbr = 2,
    Pdf = 3
}

/// <summary>
/// Estado de lectura de un libro o cómic en la biblioteca.
/// </summary>
public enum ReadingStatus
{
    Unread = 0,
    Reading = 1,
    Completed = 2
}

