using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>
/// Fábrica para detectar el formato real de un cómic o PDF e instanciar su lector unificado.
/// </summary>
public interface IBookSourceFactory
{
    Task<IBookSource> CreateAsync(string filePathOrUri, CancellationToken ct = default);
    Task<BookFormat> DetectFormatAsync(string filePathOrUri, CancellationToken ct = default);
    BookFormat DetectFormat(ReadOnlySpan<byte> headerBytes, string? fileNameOrExtension = null);
    Task<BookMetadata> ExtractMetadataAsync(string filePathOrUri, CancellationToken ct = default);
}

