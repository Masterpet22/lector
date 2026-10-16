package app.nebulareader.mobile;

import android.content.ContentResolver;
import android.graphics.Bitmap;
import android.graphics.Color;
import android.graphics.pdf.PdfRenderer;
import android.net.Uri;
import android.os.Build;
import android.os.ParcelFileDescriptor;
import android.util.DisplayMetrics;
import android.util.Xml;

import com.github.junrar.Archive;
import com.github.junrar.rarfile.FileHeader;
import com.getcapacitor.JSArray;
import com.getcapacitor.JSObject;

import org.apache.commons.compress.archivers.zip.ZipArchiveEntry;
import org.apache.commons.compress.archivers.zip.ZipFile;
import org.xmlpull.v1.XmlPullParser;

import java.io.ByteArrayOutputStream;
import java.io.Closeable;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.channels.FileChannel;
import java.util.ArrayList;
import java.util.Enumeration;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

interface NativeDocumentSession extends Closeable {
    int getPageCount();
    NativePage readPage(int index) throws Exception;
    JSObject getMetadata();

    static NativeDocumentSession open(ContentResolver resolver, Uri uri, String fileName, DisplayMetrics metrics) throws Exception {
        String lowerName = fileName.toLowerCase(Locale.ROOT);
        if (lowerName.endsWith(".pdf")) return new NativePdfSession(resolver, uri, metrics);
        if (lowerName.endsWith(".cbz") || lowerName.endsWith(".zip")) return new NativeZipSession(resolver, uri);
        if (lowerName.endsWith(".cbr")) return new NativeRarSession(resolver, uri);
        throw new IOException("Formato no compatible con el lector nativo.");
    }
}

final class NativePage {
    final byte[] data;
    final String mimeType;

    NativePage(byte[] data, String mimeType) {
        this.data = data;
        this.mimeType = mimeType;
    }
}

final class NativeZipSession implements NativeDocumentSession {
    private final ParcelFileDescriptor.AutoCloseInputStream input;
    private final ZipFile archive;
    private final List<ZipArchiveEntry> pages = new ArrayList<>();
    private final JSObject metadata;

    NativeZipSession(ContentResolver resolver, Uri uri) throws Exception {
        ParcelFileDescriptor descriptor = requireDescriptor(resolver, uri);
        input = new ParcelFileDescriptor.AutoCloseInputStream(descriptor);
        FileChannel channel = input.getChannel();
        archive = ZipFile.builder()
            .setSeekableByteChannel(channel)
            .setIgnoreLocalFileHeader(true)
            .get();

        ZipArchiveEntry comicInfo = null;
        Enumeration<ZipArchiveEntry> entries = archive.getEntries();
        while (entries.hasMoreElements()) {
            ZipArchiveEntry entry = entries.nextElement();
            if (entry.isDirectory()) continue;
            if (isComicInfo(entry.getName())) comicInfo = entry;
            else if (NativeDocuments.isImage(entry.getName())) pages.add(entry);
        }
        pages.sort((first, second) -> NativeDocuments.compareNaturally(first.getName(), second.getName()));
        if (pages.isEmpty()) throw new IOException("El archivo no contiene imágenes compatibles.");
        if (comicInfo == null) metadata = new JSObject();
        else try (InputStream xml = archive.getInputStream(comicInfo)) { metadata = NativeDocuments.readComicInfo(xml); }
    }

    @Override public int getPageCount() { return pages.size(); }

    @Override public NativePage readPage(int index) throws Exception {
        ZipArchiveEntry entry = pages.get(NativeDocuments.requireIndex(index, pages.size()));
        try (InputStream page = archive.getInputStream(entry)) {
            return new NativePage(NativeDocuments.readAll(page), NativeDocuments.mimeFor(entry.getName()));
        }
    }

    @Override public JSObject getMetadata() { return metadata; }

    @Override public void close() throws IOException {
        archive.close();
        input.close();
    }

    private static boolean isComicInfo(String name) {
        return name.replace('\\', '/').toLowerCase(Locale.ROOT).endsWith("/comicinfo.xml")
            || name.equalsIgnoreCase("ComicInfo.xml");
    }

    private static ParcelFileDescriptor requireDescriptor(ContentResolver resolver, Uri uri) throws IOException {
        ParcelFileDescriptor descriptor = resolver.openFileDescriptor(uri, "r");
        if (descriptor == null) throw new IOException("El proveedor no pudo abrir el documento.");
        return descriptor;
    }
}

final class NativeRarSession implements NativeDocumentSession {
    private final ParcelFileDescriptor.AutoCloseInputStream input;
    private final Archive archive;
    private final List<FileHeader> pages = new ArrayList<>();
    private final JSObject metadata;

    NativeRarSession(ContentResolver resolver, Uri uri) throws Exception {
        ParcelFileDescriptor descriptor = resolver.openFileDescriptor(uri, "r");
        if (descriptor == null) throw new IOException("El proveedor no pudo abrir el documento.");
        input = new ParcelFileDescriptor.AutoCloseInputStream(descriptor);
        archive = new Archive(input);
        FileHeader comicInfo = null;
        for (FileHeader header : archive.getFileHeaders()) {
            if (header.isDirectory()) continue;
            String name = header.getFileName();
            if (name == null) continue;
            if (name.replace('\\', '/').toLowerCase(Locale.ROOT).endsWith("comicinfo.xml")) comicInfo = header;
            else if (NativeDocuments.isImage(name)) pages.add(header);
        }
        pages.sort((first, second) -> NativeDocuments.compareNaturally(first.getFileName(), second.getFileName()));
        if (pages.isEmpty()) throw new IOException("El CBR no contiene imágenes compatibles.");
        if (comicInfo == null) metadata = new JSObject();
        else try (InputStream xml = archive.getInputStream(comicInfo)) { metadata = NativeDocuments.readComicInfo(xml); }
    }

    @Override public int getPageCount() { return pages.size(); }

    @Override public NativePage readPage(int index) throws Exception {
        FileHeader header = pages.get(NativeDocuments.requireIndex(index, pages.size()));
        try (InputStream page = archive.getInputStream(header)) {
            return new NativePage(NativeDocuments.readAll(page), NativeDocuments.mimeFor(header.getFileName()));
        }
    }

    @Override public JSObject getMetadata() { return metadata; }

    @Override public void close() throws IOException {
        archive.close();
        input.close();
    }
}

final class NativePdfSession implements NativeDocumentSession {
    private final ParcelFileDescriptor descriptor;
    private final PdfRenderer renderer;
    private final DisplayMetrics metrics;

    NativePdfSession(ContentResolver resolver, Uri uri, DisplayMetrics metrics) throws IOException {
        descriptor = resolver.openFileDescriptor(uri, "r");
        if (descriptor == null) throw new IOException("El proveedor no pudo abrir el PDF.");
        renderer = new PdfRenderer(descriptor);
        this.metrics = metrics;
        if (renderer.getPageCount() == 0) throw new IOException("El PDF no contiene páginas.");
    }

    @Override public int getPageCount() { return renderer.getPageCount(); }

    @Override public NativePage readPage(int index) throws Exception {
        int safeIndex = NativeDocuments.requireIndex(index, renderer.getPageCount());
        try (PdfRenderer.Page page = renderer.openPage(safeIndex)) {
            float density = Math.max(1.5f, Math.min(metrics.density, 2f) * 1.5f);
            float scale = Math.min(density, 2800f / Math.max(page.getWidth(), page.getHeight()));
            int width = Math.max(1, Math.round(page.getWidth() * scale));
            int height = Math.max(1, Math.round(page.getHeight() * scale));
            Bitmap bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
            bitmap.eraseColor(Color.WHITE);
            page.render(bitmap, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY);
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            Bitmap.CompressFormat format = Build.VERSION.SDK_INT >= Build.VERSION_CODES.R
                ? Bitmap.CompressFormat.WEBP_LOSSY
                : Bitmap.CompressFormat.WEBP;
            if (!bitmap.compress(format, 92, output)) {
                bitmap.recycle();
                throw new IOException("No se pudo renderizar la página PDF.");
            }
            bitmap.recycle();
            return new NativePage(output.toByteArray(), "image/webp");
        }
    }

    @Override public JSObject getMetadata() { return new JSObject(); }

    @Override public void close() throws IOException {
        renderer.close();
        descriptor.close();
    }
}

final class NativeDocuments {
    private NativeDocuments() {}

    static boolean isImage(String name) {
        String normalized = name.replace('\\', '/');
        String lower = normalized.toLowerCase(Locale.ROOT);
        String leaf = normalized.substring(normalized.lastIndexOf('/') + 1);
        return !normalized.contains("/__MACOSX/")
            && !leaf.startsWith("._")
            && (lower.endsWith(".avif") || lower.endsWith(".gif") || lower.endsWith(".jpg")
                || lower.endsWith(".jpeg") || lower.endsWith(".png") || lower.endsWith(".webp"));
    }

    static String mimeFor(String name) {
        String lower = name.toLowerCase(Locale.ROOT);
        if (lower.endsWith(".jpg") || lower.endsWith(".jpeg")) return "image/jpeg";
        if (lower.endsWith(".png")) return "image/png";
        if (lower.endsWith(".webp")) return "image/webp";
        if (lower.endsWith(".gif")) return "image/gif";
        if (lower.endsWith(".avif")) return "image/avif";
        return "application/octet-stream";
    }

    static int requireIndex(int index, int size) {
        if (index < 0 || index >= size) throw new IndexOutOfBoundsException("Página fuera de rango.");
        return index;
    }

    static byte[] readAll(InputStream input) throws IOException {
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
        return output.toByteArray();
    }

    static int compareNaturally(String first, String second) {
        int firstIndex = 0;
        int secondIndex = 0;
        while (firstIndex < first.length() && secondIndex < second.length()) {
            char firstChar = Character.toLowerCase(first.charAt(firstIndex));
            char secondChar = Character.toLowerCase(second.charAt(secondIndex));
            if (Character.isDigit(firstChar) && Character.isDigit(secondChar)) {
                int firstEnd = firstIndex;
                int secondEnd = secondIndex;
                while (firstEnd < first.length() && Character.isDigit(first.charAt(firstEnd))) firstEnd++;
                while (secondEnd < second.length() && Character.isDigit(second.charAt(secondEnd))) secondEnd++;
                String firstNumber = first.substring(firstIndex, firstEnd).replaceFirst("^0+(?!$)", "");
                String secondNumber = second.substring(secondIndex, secondEnd).replaceFirst("^0+(?!$)", "");
                if (firstNumber.length() != secondNumber.length()) return Integer.compare(firstNumber.length(), secondNumber.length());
                int comparison = firstNumber.compareTo(secondNumber);
                if (comparison != 0) return comparison;
                firstIndex = firstEnd;
                secondIndex = secondEnd;
                continue;
            }
            if (firstChar != secondChar) return Character.compare(firstChar, secondChar);
            firstIndex++;
            secondIndex++;
        }
        return Integer.compare(first.length(), second.length());
    }

    static JSObject readComicInfo(InputStream input) throws Exception {
        XmlPullParser parser = Xml.newPullParser();
        parser.setInput(input, "UTF-8");
        JSObject metadata = new JSObject();
        List<String> creators = new ArrayList<>();
        Set<String> tags = new LinkedHashSet<>();
        String currentTag = "";
        int event = parser.getEventType();
        while (event != XmlPullParser.END_DOCUMENT) {
            if (event == XmlPullParser.START_TAG) currentTag = parser.getName();
            else if (event == XmlPullParser.TEXT) {
                String value = parser.getText().trim();
                if (!value.isEmpty()) {
                    if (currentTag.equalsIgnoreCase("Title")) metadata.put("title", value);
                    else if (currentTag.equalsIgnoreCase("Series")) metadata.put("series", value);
                    else if (currentTag.equalsIgnoreCase("Number")) metadata.put("number", value);
                    else if (currentTag.equalsIgnoreCase("Publisher")) metadata.put("publisher", value);
                    else if (currentTag.equalsIgnoreCase("Year")) metadata.put("year", value);
                    else if (currentTag.equalsIgnoreCase("Summary")) metadata.put("summary", value);
                    else if (currentTag.equalsIgnoreCase("Writer") || currentTag.equalsIgnoreCase("Penciller") || currentTag.equalsIgnoreCase("Inker")) creators.add(value);
                    else if (currentTag.equalsIgnoreCase("Genre") || currentTag.equalsIgnoreCase("Tags")) {
                        for (String tag : value.split(",")) if (!tag.trim().isEmpty()) tags.add(tag.trim());
                    }
                }
            } else if (event == XmlPullParser.END_TAG) currentTag = "";
            event = parser.next();
        }
        if (!creators.isEmpty()) metadata.put("author", String.join(", ", new LinkedHashSet<>(creators)));
        if (!tags.isEmpty()) metadata.put("tags", new JSArray(new ArrayList<>(tags)));
        return metadata;
    }
}
