using NebulaReader.Models;

namespace NebulaReader.Services;

/// <summary>Procesa un listado conocido sin perder los resultados previos ante fallos o cancelación.</summary>
public static class FolderImportRunner
{
    public static async Task<FolderImportResult> RunAsync(
        IReadOnlyList<string> files,
        Func<string, CancellationToken, Task<bool>> importOneAsync,
        Func<int> pendingCovers,
        IProgress<FolderImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(importOneAsync);
        ArgumentNullException.ThrowIfNull(pendingCovers);
        var added = 0;
        var existing = 0;
        var failures = new List<ImportFailure>();
        var processed = 0;
        progress?.Report(new FolderImportProgress(0, files.Count, 0, 0, 0, null, pendingCovers()));

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (await importOneAsync(file, ct).ConfigureAwait(false)) added++;
                else existing++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { failures.Add(new ImportFailure(file, ex.Message)); }

            processed++;
            progress?.Report(new FolderImportProgress(processed, files.Count, added, existing,
                failures.Count, file, pendingCovers()));
        }

        return new FolderImportResult(processed, files.Count, added, existing,
            failures.Count, ct.IsCancellationRequested, failures);
    }
}
