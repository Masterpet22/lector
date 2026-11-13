using Android.App;
using Android.Content;
using Android.Provider;
using AndroidUri = Android.Net.Uri;
using AppContext = Android.App.Application;

namespace NebulaReader.Services;

public partial class FilePickerService
{
    public const int OpenDocumentTreeRequestCode = 9401;
    private static readonly object FolderPickSync = new();
    private static TaskCompletionSource<string?>? _pendingFolderPickTcs;

    public static bool TryCompleteFolderPickResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != OpenDocumentTreeRequestCode)
        {
            return false;
        }

        TaskCompletionSource<string?>? tcs;
        lock (FolderPickSync)
        {
            tcs = _pendingFolderPickTcs;
            _pendingFolderPickTcs = null;
        }

        if (tcs == null)
        {
            return true;
        }

        if (resultCode != Result.Ok || data?.Data is not AndroidUri treeUri)
        {
            tcs.TrySetResult(null);
            return true;
        }

        try
        {
            var activity = Platform.CurrentActivity;
            var resolver = activity?.ContentResolver ?? AppContext.Context.ContentResolver;
            resolver?.TakePersistableUriPermission(
                treeUri,
                ActivityFlags.GrantReadUriPermission);
        }
        catch
        {
            // Algunos proveedores SAF no admiten permisos persistentes; el permiso temporal sigue activo en la sesión.
        }

        tcs.TrySetResult(treeUri.ToString());
        return true;
    }

    private async partial Task<string?> PickFolderPlatformAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        TaskCompletionSource<string?> tcs;
        lock (FolderPickSync)
        {
            _pendingFolderPickTcs?.TrySetCanceled();
            _pendingFolderPickTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = _pendingFolderPickTcs;
        }

        using var registration = ct.Register(() =>
        {
            lock (FolderPickSync)
            {
                if (ReferenceEquals(_pendingFolderPickTcs, tcs))
                {
                    _pendingFolderPickTcs = null;
                }
            }
            tcs.TrySetCanceled(ct);
        });

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var activity = Platform.CurrentActivity
                ?? throw new InvalidOperationException("No hay una Activity activa para mostrar el selector de carpetas.");

            var intent = new Intent(Intent.ActionOpenDocumentTree);
            intent.AddFlags(
                ActivityFlags.GrantReadUriPermission |
                ActivityFlags.GrantPersistableUriPermission |
                ActivityFlags.GrantPrefixUriPermission);

            activity.StartActivityForResult(intent, OpenDocumentTreeRequestCode);
        }).ConfigureAwait(false);

        return await tcs.Task.ConfigureAwait(false);
    }

    private partial Task<IReadOnlyList<string>> EnumerateBookFilesPlatformAsync(string folderPathOrUri, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (!folderPathOrUri.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(folderPathOrUri))
                {
                    return Array.Empty<string>();
                }

                return Directory
                    .EnumerateFiles(folderPathOrUri, "*.*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true
                    })
                    .Where(path => IsSupportedBookFileName(Path.GetFileName(path)))
                    .OrderBy(path => path, NaturalSortComparer.Instance)
                    .ToArray();
            }

            var treeUri = AndroidUri.Parse(folderPathOrUri);
            var resolver = AppContext.Context.ContentResolver;
            if (treeUri == null || resolver == null)
            {
                return Array.Empty<string>();
            }

            var rootDocId = DocumentsContract.GetTreeDocumentId(treeUri);
            if (string.IsNullOrWhiteSpace(rootDocId))
            {
                return Array.Empty<string>();
            }

            var results = new List<string>();
            var pendingFolders = new Queue<string>();
            pendingFolders.Enqueue(rootDocId);

            string[] projection =
            [
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName,
                DocumentsContract.Document.ColumnMimeType
            ];

            while (pendingFolders.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var currentDocId = pendingFolders.Dequeue();
                var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, currentDocId);
                if (childrenUri == null) continue;

                using var cursor = resolver.Query(childrenUri, projection, null, null, null);
                if (cursor == null) continue;

                var idIdx = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
                var nameIdx = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
                var mimeIdx = cursor.GetColumnIndex(DocumentsContract.Document.ColumnMimeType);

                while (cursor.MoveToNext())
                {
                    ct.ThrowIfCancellationRequested();
                    if (idIdx < 0 || nameIdx < 0 || mimeIdx < 0) continue;

                    var docId = cursor.GetString(idIdx);
                    var displayName = cursor.GetString(nameIdx);
                    var mimeType = cursor.GetString(mimeIdx);
                    if (string.IsNullOrWhiteSpace(docId) || string.IsNullOrWhiteSpace(displayName) || displayName.StartsWith('.'))
                    {
                        continue;
                    }

                    if (string.Equals(mimeType, DocumentsContract.Document.MimeTypeDir, StringComparison.OrdinalIgnoreCase))
                    {
                        pendingFolders.Enqueue(docId);
                    }
                    else if (IsSupportedBookFileName(displayName))
                    {
                        var docUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, docId);
                        if (docUri != null)
                        {
                            results.Add(docUri.ToString()!);
                        }
                    }
                }
            }

            results.Sort(NaturalSortComparer.Instance);
            return results;
        }, ct);
    }
}
