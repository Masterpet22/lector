namespace NebulaReader.Models;

/// <summary>Campos editables del catálogo local para un documento.</summary>
public sealed record DocumentMetadataEdit(
    string Title,
    string? SeriesName,
    string? Author,
    DateTime? PublicationDate,
    string? Publisher,
    string? Language,
    string? Description,
    string? Tags);
