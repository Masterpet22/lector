namespace NebulaReader.Models;

public sealed record ImportFailure(string FilePath, string Error);

public sealed record FolderImportProgress(
    int Processed, int Total, int Added, int Existing, int Failed,
    string? CurrentFile, int PendingCovers);

public sealed record FolderImportResult(
    int Processed, int Total, int Added, int Existing, int Failed,
    bool Cancelled, IReadOnlyList<ImportFailure> Failures);
