using NebulaReader.Models;

namespace NebulaReader.Services;

public interface ILibraryBackupService
{
    Task<int> ExportAsync(Stream output, CancellationToken ct = default);
    Task<BackupImportResult> ImportAsync(Stream input, CancellationToken ct = default);
}
