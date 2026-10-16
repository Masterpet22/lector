package app.nebulareader.mobile;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.provider.DocumentsContract;
import android.util.Base64;

import androidx.activity.result.ActivityResult;

import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.ActivityCallback;
import com.getcapacitor.annotation.CapacitorPlugin;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.Locale;
import java.util.ArrayDeque;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

@CapacitorPlugin(name = "DirectoryAccess")
public class DirectoryAccessPlugin extends Plugin {
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final Map<String, InputStream> openFiles = new ConcurrentHashMap<>();
    private final Map<String, NativeDocumentSession> documentSessions = new ConcurrentHashMap<>();

    @PluginMethod
    public void pickDirectory(PluginCall call) {
        Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT_TREE);
        intent.addFlags(
            Intent.FLAG_GRANT_READ_URI_PERMISSION
                | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION
        );
        startActivityForResult(call, intent, "directoryPicked");
    }

    @ActivityCallback
    private void directoryPicked(PluginCall call, ActivityResult result) {
        if (call == null) return;
        if (result.getResultCode() != Activity.RESULT_OK || result.getData() == null || result.getData().getData() == null) {
            call.resolve(new JSObject().put("cancelled", true));
            return;
        }

        Uri treeUri = result.getData().getData();
        int permissionFlags = result.getData().getFlags() & Intent.FLAG_GRANT_READ_URI_PERMISSION;
        try {
            getContext().getContentResolver().takePersistableUriPermission(treeUri, permissionFlags);
            String name = getDirectoryName(treeUri);
            call.resolve(new JSObject()
                .put("cancelled", false)
                .put("uri", treeUri.toString())
                .put("name", name));
        } catch (Exception error) {
            call.reject("No se pudo conservar el acceso a la carpeta seleccionada.", error);
        }
    }

    @PluginMethod
    public void listFiles(PluginCall call) {
        String uriValue = call.getString("uri");
        if (uriValue == null) {
            call.reject("Falta la URI de la carpeta.");
            return;
        }

        executor.execute(() -> {
            try {
                JSArray files = listComicFiles(requireTreeUri(uriValue));
                call.resolve(new JSObject().put("files", files));
            } catch (Exception error) {
                call.reject("No se pudo leer el contenido de la carpeta.", error);
            }
        });
    }

    @PluginMethod
    public void readFile(PluginCall call) {
        String uriValue = call.getString("uri");
        if (uriValue == null) {
            call.reject("Falta la URI del archivo.");
            return;
        }

        executor.execute(() -> {
            try {
                byte[] content = readContent(Uri.parse(uriValue));
                call.resolve(new JSObject().put("data", Base64.encodeToString(content, Base64.NO_WRAP)));
            } catch (Exception error) {
                call.reject("No se pudo leer el documento seleccionado.", error);
            }
        });
    }

    @PluginMethod
    public void openFile(PluginCall call) {
        String uriValue = call.getString("uri");
        if (uriValue == null) {
            call.reject("Falta la URI del archivo.");
            return;
        }

        executor.execute(() -> {
            try {
                InputStream input = getContext().getContentResolver().openInputStream(Uri.parse(uriValue));
                if (input == null) throw new IOException("El proveedor de archivos no pudo abrir el documento.");
                String sessionId = UUID.randomUUID().toString();
                openFiles.put(sessionId, input);
                call.resolve(new JSObject().put("sessionId", sessionId));
            } catch (Exception error) {
                call.reject("No se pudo abrir el documento seleccionado.", error);
            }
        });
    }

    @PluginMethod
    public void readFileChunk(PluginCall call) {
        String sessionId = call.getString("sessionId");
        int requestedBytes = call.getInt("maxBytes", 512 * 1024);
        if (sessionId == null) {
            call.reject("Falta la sesión del archivo.");
            return;
        }

        executor.execute(() -> {
            InputStream input = openFiles.get(sessionId);
            if (input == null) {
                call.reject("La sesión del archivo ya no está disponible.");
                return;
            }
            try {
                int chunkSize = Math.max(16 * 1024, Math.min(requestedBytes, 1024 * 1024));
                byte[] buffer = new byte[chunkSize];
                int count = input.read(buffer);
                if (count < 0) {
                    closeSession(sessionId);
                    call.resolve(new JSObject().put("data", "").put("done", true));
                    return;
                }
                call.resolve(new JSObject()
                    .put("data", Base64.encodeToString(buffer, 0, count, Base64.NO_WRAP))
                    .put("done", false));
            } catch (Exception error) {
                closeSession(sessionId);
                call.reject("No se pudo leer el documento seleccionado.", error);
            }
        });
    }

    @PluginMethod
    public void closeFile(PluginCall call) {
        String sessionId = call.getString("sessionId");
        if (sessionId != null) closeSession(sessionId);
        call.resolve();
    }

    @PluginMethod
    public void inspectDocument(PluginCall call) {
        String uriValue = call.getString("uri");
        String fileName = call.getString("name");
        if (uriValue == null || fileName == null) {
            call.reject("Faltan los datos del documento.");
            return;
        }

        executor.execute(() -> {
            try (NativeDocumentSession session = openDocumentSession(uriValue, fileName)) {
                NativePage cover = session.readPage(0);
                call.resolve(new JSObject()
                    .put("pageCount", session.getPageCount())
                    .put("coverData", Base64.encodeToString(cover.data, Base64.NO_WRAP))
                    .put("coverMimeType", cover.mimeType)
                    .put("metadata", session.getMetadata()));
            } catch (Exception error) {
                call.reject("No se pudo inspeccionar el documento con el lector nativo.", error);
            }
        });
    }

    @PluginMethod
    public void openDocument(PluginCall call) {
        String uriValue = call.getString("uri");
        String fileName = call.getString("name");
        if (uriValue == null || fileName == null) {
            call.reject("Faltan los datos del documento.");
            return;
        }

        executor.execute(() -> {
            NativeDocumentSession session = null;
            try {
                session = openDocumentSession(uriValue, fileName);
                String sessionId = UUID.randomUUID().toString();
                documentSessions.put(sessionId, session);
                call.resolve(new JSObject()
                    .put("sessionId", sessionId)
                    .put("pageCount", session.getPageCount())
                    .put("metadata", session.getMetadata()));
            } catch (Exception error) {
                if (session != null) try { session.close(); } catch (IOException ignored) {}
                call.reject("No se pudo abrir el documento con el lector nativo.", error);
            }
        });
    }

    @PluginMethod
    public void readDocumentPage(PluginCall call) {
        String sessionId = call.getString("sessionId");
        Integer pageIndex = call.getInt("index");
        if (sessionId == null || pageIndex == null) {
            call.reject("Faltan los datos de la página.");
            return;
        }

        executor.execute(() -> {
            NativeDocumentSession session = documentSessions.get(sessionId);
            if (session == null) {
                call.reject("La sesión del documento ya no está disponible.");
                return;
            }
            try {
                NativePage page = session.readPage(pageIndex);
                call.resolve(new JSObject()
                    .put("data", Base64.encodeToString(page.data, Base64.NO_WRAP))
                    .put("mimeType", page.mimeType));
            } catch (Exception error) {
                call.reject("No se pudo extraer la página solicitada.", error);
            }
        });
    }

    @PluginMethod
    public void closeDocument(PluginCall call) {
        String sessionId = call.getString("sessionId");
        if (sessionId == null) {
            call.resolve();
            return;
        }
        executor.execute(() -> {
            closeDocumentSession(sessionId);
            call.resolve();
        });
    }

    private NativeDocumentSession openDocumentSession(String uriValue, String fileName) throws Exception {
        return NativeDocumentSession.open(
            getContext().getContentResolver(),
            Uri.parse(uriValue),
            fileName,
            getContext().getResources().getDisplayMetrics()
        );
    }

    private Uri requireTreeUri(String value) {
        Uri uri = Uri.parse(value);
        if (!DocumentsContract.isTreeUri(uri)) {
            throw new IllegalArgumentException("La URI seleccionada no corresponde a una carpeta.");
        }
        return uri;
    }

    private String getDirectoryName(Uri treeUri) throws IOException {
        String documentId = DocumentsContract.getTreeDocumentId(treeUri);
        Uri documentUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, documentId);
        try (Cursor cursor = getContext().getContentResolver().query(
            documentUri,
            new String[] { DocumentsContract.Document.COLUMN_DISPLAY_NAME },
            null,
            null,
            null
        )) {
            if (cursor == null || !cursor.moveToFirst()) throw new IOException("No se pudo obtener el nombre de la carpeta.");
            String name = cursor.getString(0);
            if (name == null || name.isEmpty()) throw new IOException("El proveedor no devolvió el nombre de la carpeta.");
            return name;
        }
    }

    private JSArray listComicFiles(Uri treeUri) throws IOException {
        ContentResolver resolver = getContext().getContentResolver();
        String rootId = DocumentsContract.getTreeDocumentId(treeUri);
        ArrayDeque<DirectoryEntry> pending = new ArrayDeque<>();
        JSArray files = new JSArray();
        pending.push(new DirectoryEntry(rootId, ""));

        while (!pending.isEmpty()) {
            DirectoryEntry directory = pending.pop();
            Uri childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(treeUri, directory.documentId);
            String[] columns = {
                DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                DocumentsContract.Document.COLUMN_DISPLAY_NAME,
                DocumentsContract.Document.COLUMN_MIME_TYPE,
                DocumentsContract.Document.COLUMN_SIZE,
                DocumentsContract.Document.COLUMN_LAST_MODIFIED
            };
            try (Cursor cursor = resolver.query(childrenUri, columns, null, null, null)) {
                if (cursor == null) throw new IOException("El proveedor de archivos no devolvió resultados.");
                int idColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DOCUMENT_ID);
                int nameColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_DISPLAY_NAME);
                int mimeColumn = cursor.getColumnIndexOrThrow(DocumentsContract.Document.COLUMN_MIME_TYPE);
                int sizeColumn = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_SIZE);
                int modifiedColumn = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_LAST_MODIFIED);

                while (cursor.moveToNext()) {
                    String documentId = cursor.getString(idColumn);
                    String name = cursor.getString(nameColumn);
                    String mimeType = cursor.getString(mimeColumn);
                    if (name == null || documentId == null) continue;
                    String relativePath = directory.relativePath.isEmpty()
                        ? name
                        : directory.relativePath + "/" + name;

                    if (DocumentsContract.Document.MIME_TYPE_DIR.equals(mimeType)) {
                        pending.push(new DirectoryEntry(documentId, relativePath));
                    } else if (isComicFile(name)) {
                        Uri fileUri = DocumentsContract.buildDocumentUriUsingTree(treeUri, documentId);
                        files.put(new JSObject()
                            .put("name", name)
                            .put("relativePath", relativePath)
                            .put("uri", fileUri.toString())
                            .put("size", sizeColumn < 0 || cursor.isNull(sizeColumn) ? 0 : cursor.getLong(sizeColumn))
                            .put("lastModified", modifiedColumn < 0 || cursor.isNull(modifiedColumn) ? 0 : cursor.getLong(modifiedColumn)));
                    }
                }
            }
        }
        return files;
    }

    private boolean isComicFile(String name) {
        String lowerName = name.toLowerCase(Locale.ROOT);
        return lowerName.endsWith(".cbz")
            || lowerName.endsWith(".cbr")
            || lowerName.endsWith(".zip")
            || lowerName.endsWith(".pdf");
    }

    private byte[] readContent(Uri uri) throws IOException {
        ContentResolver resolver = getContext().getContentResolver();
        try (InputStream input = resolver.openInputStream(uri);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            if (input == null) throw new IOException("El proveedor de archivos no pudo abrir el documento.");
            byte[] buffer = new byte[64 * 1024];
            int count;
            while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
            return output.toByteArray();
        }
    }

    private void closeSession(String sessionId) {
        InputStream input = openFiles.remove(sessionId);
        if (input == null) return;
        try {
            input.close();
        } catch (IOException ignored) {}
    }

    private void closeDocumentSession(String sessionId) {
        NativeDocumentSession session = documentSessions.remove(sessionId);
        if (session == null) return;
        try {
            session.close();
        } catch (IOException ignored) {}
    }

    @Override
    protected void handleOnDestroy() {
        for (String sessionId : openFiles.keySet()) closeSession(sessionId);
        for (String sessionId : documentSessions.keySet()) closeDocumentSession(sessionId);
        executor.shutdownNow();
        super.handleOnDestroy();
    }

    private static final class DirectoryEntry {
        private final String documentId;
        private final String relativePath;

        private DirectoryEntry(String documentId, String relativePath) {
            this.documentId = documentId;
            this.relativePath = relativePath;
        }
    }
}
